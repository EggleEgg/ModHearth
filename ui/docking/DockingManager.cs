using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using ModHearth.UI.Theme;

namespace ModHearth.UI.Docking
{
    public enum DockSide
    {
        Left,
        Right,
        //TODO Fully implement this side
        Top,
        Bottom
    }

    /// <summary>
    /// Configuration for where content can be docked
    /// </summary>
    public class DockingTarget
    {
        public Grid MainGrid { get; set; } = null!;
        public DockSide Side { get; set; } = DockSide.Right;
        public int SplitterIndex { get; set; }
        public int ContentIndex { get; set; }
        public Control SplitterControl { get; set; } = null!;
        public ContentControl DockHostControl { get; set; } = null!;
        public Border? PreviewBorder { get; set; }
        public Border? DockHostBorder { get; set; }
    }

    /// <summary>
    /// Configuration options for DockingManager
    /// </summary>
    public class DockingManagerOptions<TControl, TWindow>
        where TControl : UserControl
        where TWindow : Window
    {
        public IReadOnlyDictionary<DockSide, DockingTarget> SideTargets { get; set; } = null!;
        public DockSide DefaultSide { get; set; } = DockSide.Right;
        public Func<TControl> ControlCreator { get; set; } = null!;
        public Func<TControl, TWindow> WindowCreator { get; set; } = null!;
        public double DefaultSize { get; set; }
        public double MinSize { get; set; }
        public double MaxSize { get; set; }
        public double SplitterSize { get; set; } = 7;
        public bool InitialDocked { get; set; }
        public Action<DockSide>? OnSideAcquired { get; set; }
        public Func<DockSide, double>? ProportionLoader { get; set; }
        public Action<DockSide, double>? ProportionSaver { get; set; }
        public Func<DockSide>? SideLoader { get; set; }
        public Action<DockSide>? SideSaver { get; set; }
    }

    /// <summary>
    /// Scalable logic for docking sub-windows into MainWindow on any side (Left, Right, Top, Bottom)
    /// with overflow prevention against host screen bounds.
    /// </summary>
    public class DockingManager<TControl, TWindow> : IDisposable
        where TControl : UserControl
        where TWindow : Window
    {
        private const string pressedName = "pressed";
        private readonly Window _parentWindow;
        private readonly IReadOnlyDictionary<DockSide, DockingTarget> _sideTargets;
        private readonly Action<DockSide>? _onSideAcquired;
        private readonly Func<DockSide, double>? _proportionLoader;
        private readonly Action<DockSide, double>? _proportionSaver;
        private readonly Func<DockSide>? _sideLoader;
        private readonly Action<DockSide>? _sideSaver;

        private readonly Func<TControl> _controlCreator;
        private readonly Func<TControl, TWindow> _windowCreator;

        private readonly double _defaultSize;
        private readonly double _minSize;
        private readonly double _maxSize;
        private readonly double _splitterSize;
        private bool _isExpanded;
        private bool _isOverDockTarget;
        private IWindowDragTracker? _dragTracker;
        private DockSide? _hoverSide;
        private double _expandedSize;
        private double _preExpandParentPrimary;
        private double _preExpandMinWidth;
        private double _preExpandMinHeight;

        private bool _isDraggingSplitter;
        private Point _splitterStartPoint;
        private double _initialContentSize;
        private double _initialParentSize;
        private bool _splitterResizePending;
        private double _pendingSplitterSize;
        private bool _isDisposed;

        private readonly Dictionary<Control, EventHandler<PointerPressedEventArgs>> _splitterPressedHandlers = [];
        private readonly Dictionary<Control, EventHandler<PointerEventArgs>> _splitterMovedHandlers = [];
        private readonly Dictionary<Control, EventHandler<PointerReleasedEventArgs>> _splitterReleasedHandlers = [];

        private EventHandler<PointerReleasedEventArgs>? _pointerReleasedHandler;
        private EventHandler<PointerCaptureLostEventArgs>? _pointerCaptureLostHandler;

        public event EventHandler? DockStateChanged;
        public event EventHandler? Closed;

        public TControl? SharedControl { get; private set; }
        public TWindow? FloatingWindow { get; private set; }
        public bool IsDocked { get; private set; }
        public bool IsOpen => _isExpanded || FloatingWindow != null;
        public DockSide ActiveSide { get; private set; }
        public DockSide DefaultSide { get; }

        private DockingTarget ActiveTarget => _sideTargets[ActiveSide];

        public DockingManager(
            Window parentWindow,
            DockingManagerOptions<TControl, TWindow> options)
        {
            _parentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
            DockingManagerOptions<TControl, TWindow> opts = options ?? throw new ArgumentNullException(nameof(options));
            _sideTargets = opts.SideTargets ?? throw new ArgumentNullException(nameof(options), "SideTargets cannot be null.");
            if (!_sideTargets.ContainsKey(opts.DefaultSide))
                throw new ArgumentException($"Default side '{opts.DefaultSide}' is not present in sideTargets.", nameof(options));

            DefaultSide = opts.DefaultSide;
            _sideLoader = opts.SideLoader;
            _sideSaver = opts.SideSaver;
            ActiveSide = _sideLoader?.Invoke() ?? DefaultSide;
            if (!_sideTargets.ContainsKey(ActiveSide))
                ActiveSide = DefaultSide;

            _controlCreator = opts.ControlCreator ?? throw new ArgumentNullException(nameof(options), "ControlCreator cannot be null.");
            _windowCreator = opts.WindowCreator ?? throw new ArgumentNullException(nameof(options), "WindowCreator cannot be null.");
            _defaultSize = opts.DefaultSize;
            _minSize = opts.MinSize;
            _maxSize = opts.MaxSize;
            _splitterSize = opts.SplitterSize;
            _onSideAcquired = opts.OnSideAcquired;
            _proportionLoader = opts.ProportionLoader;
            _proportionSaver = opts.ProportionSaver;
            IsDocked = opts.InitialDocked && CanDockOnSide(ActiveSide);

            RegisterSplitterEvents();
        }

        private static bool IsHorizontal(DockSide side) => side is DockSide.Left or DockSide.Right;

        private bool TryGetScreenLayout(out PixelRect workingArea, out PixelPoint parentScreenPos, out double scale)
        {
            workingArea = default;
            parentScreenPos = default;
            scale = _parentWindow.DesktopScaling;

            try
            {
                Screen? screen = _parentWindow.Screens?.ScreenFromWindow(_parentWindow) ?? _parentWindow.Screens?.Primary;
                if (screen == null)
                    return false;

                workingArea = screen.WorkingArea;
                parentScreenPos = _parentWindow.PointToScreen(new Point(0, 0));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private double GetParentMinPrimary(DockSide side) => IsHorizontal(side) ? _parentWindow.MinWidth : _parentWindow.MinHeight;

        private double GetAvailablePrimary(DockSide side, PixelRect workingArea, PixelPoint parentScreenPos, double scale) => side switch
        {
            DockSide.Right or DockSide.Left =>
                (workingArea.X + workingArea.Width - parentScreenPos.X) / scale,
            DockSide.Bottom or DockSide.Top =>
                (workingArea.Y + workingArea.Height - parentScreenPos.Y) / scale,
            _ => double.MaxValue
        };

        private bool FitsCrossAxis(DockSide side, PixelRect workingArea, PixelPoint parentScreenPos, double scale)
        {
            if (IsHorizontal(side))
            {
                double parentMinHeightPx = _parentWindow.MinHeight * scale;
                return parentScreenPos.Y >= workingArea.Y &&
                       parentScreenPos.Y + parentMinHeightPx <= workingArea.Y + workingArea.Height;
            }

            double parentMinWidthPx = _parentWindow.MinWidth * scale;
            return parentScreenPos.X >= workingArea.X &&
                   parentScreenPos.X + parentMinWidthPx <= workingArea.X + workingArea.Width;
        }

        private bool CanDockOnSide(DockSide side)
        {
            if (!_sideTargets.ContainsKey(side))
                return false;

            try
            {
                if (!TryGetScreenLayout(out PixelRect workingArea, out PixelPoint parentScreenPos, out double scale))
                    return true;

                double parentMinPrimary = GetParentMinPrimary(side);
                double requiredPrimary = parentMinPrimary + _minSize + _splitterSize;
                double availablePrimary = GetAvailablePrimary(side, workingArea, parentScreenPos, scale);

                if (requiredPrimary > availablePrimary)
                    return false;

                return FitsCrossAxis(side, workingArea, parentScreenPos, scale);
            }
            catch
            {
                // Fallback if screen metrics cannot be retrieved
                return true;
            }
        }

        private bool TryComputeExpandedLayout(DockSide side, out double childSize, out double newParentPrimary)
        {
            childSize = _defaultSize;
            double currentPrimary = IsHorizontal(side) ? _parentWindow.Width : _parentWindow.Height;
            double savedProportion = _proportionLoader?.Invoke(side) ?? 0.0;
            if (savedProportion > 0 && savedProportion < 1 && currentPrimary > 0)
            {
                childSize = Math.Clamp(savedProportion * currentPrimary, _minSize, _maxSize);
            }
            double parentMinPrimary = GetParentMinPrimary(side);

            if (!TryGetScreenLayout(out PixelRect workingArea, out PixelPoint parentScreenPos, out double scale))
            {
                newParentPrimary = currentPrimary + childSize + _splitterSize;
                return true;
            }

            double availablePrimary = GetAvailablePrimary(side, workingArea, parentScreenPos, scale);

            newParentPrimary = currentPrimary + childSize + _splitterSize;

            if (newParentPrimary > availablePrimary)
            {
                childSize = Math.Clamp(availablePrimary - currentPrimary - _splitterSize, _minSize, _defaultSize);
                newParentPrimary = currentPrimary + childSize + _splitterSize;
            }

            if (newParentPrimary > availablePrimary)
            {
                childSize = _minSize;
                newParentPrimary = availablePrimary;
                double parentCore = newParentPrimary - childSize - _splitterSize;
                if (parentCore < parentMinPrimary)
                    return false;
            }

            return true;
        }

        public void Open()
        {
            if (_isDisposed) return;
            EnsureControlCreated();

            ActiveSide = _sideLoader?.Invoke() ?? DefaultSide;
            if (!_sideTargets.ContainsKey(ActiveSide))
                ActiveSide = DefaultSide;

            if (IsDocked && CanDockOnSide(ActiveSide))
            {
                ShowDockedContent();
            }
            else
            {
                if (IsDocked)
                    IsDocked = false;

                ShowFloatingWindow();
            }
        }

        public void Close()
        {
            if (_isDisposed) return;
            CollapsePanel();

            foreach (DockingTarget target in _sideTargets.Values)
                ClearDockHost(target);

            if (_dragTracker != null)
            {
                _dragTracker.DragFinished -= OnDragFinished;
                _dragTracker.Dispose();
                _dragTracker = null;
            }

            if (FloatingWindow != null)
            {
                TWindow win = FloatingWindow;
                FloatingWindow = null;
                win.PositionChanged -= OnFloatingWindowPositionChanged;
                if (_pointerReleasedHandler != null) win.RemoveHandler(InputElement.PointerReleasedEvent, _pointerReleasedHandler);
                if (_pointerCaptureLostHandler != null) win.RemoveHandler(InputElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
                win.Content = null;
                win.Close();
            }

            HidePreview();
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleDock() => SetDocked(!IsDocked);

        public void SetDocked(bool docked)
        {
            if (_isDisposed || IsDocked == docked) return;

            if (docked)
            {
                ActiveSide = DefaultSide;
                if (!CanDockOnSide(ActiveSide))
                    return;
            }

            IsDocked = docked;

            if (!IsOpen)
            {
                DockStateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (IsDocked)
            {
                if (_dragTracker != null)
                {
                    _dragTracker.DragFinished -= OnDragFinished;
                    _dragTracker.Dispose();
                    _dragTracker = null;
                }

                if (FloatingWindow != null)
                {
                    FloatingWindow.PositionChanged -= OnFloatingWindowPositionChanged;
                    FloatingWindow.Content = null;
                    FloatingWindow.Close();
                    FloatingWindow = null;
                }

                if (IsOpen || SharedControl != null)
                    ShowDockedContent();
            }
            else
            {
                CollapsePanel();
                ClearDockHost(ActiveTarget);
                ShowFloatingWindow();
            }

            DockStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EnsureControlCreated()
        {
            SharedControl ??= _controlCreator();
        }

        private void ShowFloatingWindow()
        {
            CollapsePanel();
            foreach (DockingTarget target in _sideTargets.Values)
                ClearDockHost(target);

            if (FloatingWindow == null)
            {
                FloatingWindow = _windowCreator(SharedControl!);
                FloatingWindow.PositionChanged += OnFloatingWindowPositionChanged;

                _pointerReleasedHandler = (_, _) =>
                {
                    if (_isOverDockTarget)
                        Dock();
                };

                _pointerCaptureLostHandler = (_, _) =>
                {
                    if (_isOverDockTarget)
                        Dock();
                };

                FloatingWindow.AddHandler(InputElement.PointerReleasedEvent, _pointerReleasedHandler, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
                FloatingWindow.AddHandler(InputElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);

                _dragTracker = WindowDragTracker.Create(FloatingWindow);
                _dragTracker.DragFinished += OnDragFinished;

                FloatingWindow.Closed += (_, _) =>
                {
                    if (_dragTracker != null)
                    {
                        _dragTracker.DragFinished -= OnDragFinished;
                        _dragTracker.Dispose();
                        _dragTracker = null;
                    }

                    if (FloatingWindow == null) return;
                    TWindow win = FloatingWindow;
                    FloatingWindow = null;
                    win.PositionChanged -= OnFloatingWindowPositionChanged;
                    if (_pointerReleasedHandler != null) win.RemoveHandler(InputElement.PointerReleasedEvent, _pointerReleasedHandler);
                    if (_pointerCaptureLostHandler != null) win.RemoveHandler(InputElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
                    win.Content = null;
                    HidePreview();
                };
            }

            FloatingWindow.Show(_parentWindow);
        }

        private void OnDragFinished(object? sender, EventArgs e)
        {
            if (_isOverDockTarget)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isOverDockTarget && !IsDocked && !_isDisposed && FloatingWindow != null)
                        Dock();
                });
            }
        }

        private void OnFloatingWindowPositionChanged(object? sender, PixelPointEventArgs e)
        {
            if (FloatingWindow == null || IsDocked || _isDisposed) return;

            try
            {
                if (!_parentWindow.IsVisible)
                    return;

                PixelPoint parentTopLeft = _parentWindow.PointToScreen(new Point(0, 0));
                double scale = _parentWindow.DesktopScaling;
                double parentWidthPx = _parentWindow.ClientSize.Width * scale;
                double parentHeightPx = _parentWindow.ClientSize.Height * scale;

                PixelPoint floatingPos = FloatingWindow.Position;
                double floatingScale = FloatingWindow.DesktopScaling;
                PixelPoint floatingCenter = new(
                    floatingPos.X + (int)(FloatingWindow.ClientSize.Width * floatingScale / 2),
                    floatingPos.Y + (int)(FloatingWindow.ClientSize.Height * floatingScale / 2)
                );

                _hoverSide = DetectHoverSide(floatingCenter, parentTopLeft, parentWidthPx, parentHeightPx);
                _isOverDockTarget = _hoverSide.HasValue;
                UpdatePreviewVisibility(_hoverSide);
            }
            catch
            {
                // Ignore transient coordinate exceptions
            }
        }

        private DockSide? DetectHoverSide(
            PixelPoint floatingCenter,
            PixelPoint parentTopLeft,
            double parentWidthPx,
            double parentHeightPx)
        {
            DockSide? bestSide = null;
            double bestDistance = double.MaxValue;

            foreach ((DockSide side, DockingTarget? target) in _sideTargets)
            {
                if (!CanDockOnSide(side))
                    continue;

                int snapThreshold = GetSnapThreshold(target);
                if (!IsOverSide(side, floatingCenter, parentTopLeft, parentWidthPx, parentHeightPx, snapThreshold))
                    continue;

                double distance = GetDistanceToSideEdge(side, floatingCenter, parentTopLeft, parentWidthPx, parentHeightPx);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSide = side;
                }
            }

            return bestSide;
        }

        private static int GetSnapThreshold(DockingTarget target)
        {
            if (target.PreviewBorder == null)
                return 250;

            int snapThreshold = (int)(target.PreviewBorder.Width > 0
                ? target.PreviewBorder.Width
                : target.PreviewBorder.Height);

            return snapThreshold is <= 0 or > 500 ? 250 : snapThreshold;
        }

        private static bool IsOverSide(
            DockSide side,
            PixelPoint floatingCenter,
            PixelPoint parentTopLeft,
            double parentWidthPx,
            double parentHeightPx,
            int snapThreshold) => side switch
            {
                DockSide.Right =>
                    floatingCenter.X >= parentTopLeft.X + (int)parentWidthPx - snapThreshold &&
                    floatingCenter.X <= parentTopLeft.X + (int)parentWidthPx &&
                    floatingCenter.Y >= parentTopLeft.Y &&
                    floatingCenter.Y <= parentTopLeft.Y + (int)parentHeightPx,
                DockSide.Left =>
                    floatingCenter.X >= parentTopLeft.X &&
                    floatingCenter.X <= parentTopLeft.X + snapThreshold &&
                    floatingCenter.Y >= parentTopLeft.Y &&
                    floatingCenter.Y <= parentTopLeft.Y + (int)parentHeightPx,
                DockSide.Bottom =>
                    floatingCenter.X >= parentTopLeft.X &&
                    floatingCenter.X <= parentTopLeft.X + (int)parentWidthPx &&
                    floatingCenter.Y >= parentTopLeft.Y + (int)parentHeightPx - snapThreshold &&
                    floatingCenter.Y <= parentTopLeft.Y + (int)parentHeightPx,
                DockSide.Top =>
                    floatingCenter.X >= parentTopLeft.X &&
                    floatingCenter.X <= parentTopLeft.X + (int)parentWidthPx &&
                    floatingCenter.Y >= parentTopLeft.Y &&
                    floatingCenter.Y <= parentTopLeft.Y + snapThreshold,
                _ => false
            };

        // Vacates this manager's docked side without immediately creating a floating window. Used when another DockingManager is claiming the same side.

        internal void ForceUndockForSideHandoff()
        {
            if (_isDisposed || !IsDocked) return;

            CollapsePanel();
            ClearDockHost(ActiveTarget);
            IsDocked = false;
            DockStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static double GetDistanceToSideEdge(
            DockSide side,
            PixelPoint floatingCenter,
            PixelPoint parentTopLeft,
            double parentWidthPx,
            double parentHeightPx) => side switch
            {
                DockSide.Right => Math.Abs(floatingCenter.X - (parentTopLeft.X + parentWidthPx)),
                DockSide.Left => Math.Abs(floatingCenter.X - parentTopLeft.X),
                DockSide.Bottom => Math.Abs(floatingCenter.Y - (parentTopLeft.Y + parentHeightPx)),
                DockSide.Top => Math.Abs(floatingCenter.Y - parentTopLeft.Y),
                _ => double.MaxValue
            };

        private void UpdatePreviewVisibility(DockSide? side)
        {
            foreach ((DockSide dockSide, DockingTarget? target) in _sideTargets)
            {
                if (target.PreviewBorder == null)
                    continue;

                bool show = side.HasValue && dockSide == side.Value;
                if (!show)
                {
                    target.PreviewBorder.IsVisible = false;
                }
                else
                {
                    Border border = target.PreviewBorder;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (border != null)
                            border.IsVisible = true;
                    });
                }
            }
        }

        private void HidePreview()
        {
            foreach (DockingTarget target in _sideTargets.Values)
            {
                if (target.PreviewBorder != null)
                    target.PreviewBorder.IsVisible = false;
            }

            _isOverDockTarget = false;
            _hoverSide = null;
        }

        public void Dock()
        {
            if (_isDisposed) return;

            // _hoverSide is only non-null while a floating window is actively being dragged over a valid target
            if (_hoverSide == null)
                return;

            DockSide side = _hoverSide.Value;
            if (!CanDockOnSide(side))
                return;

            if (IsDocked && ActiveSide == side)
                return;

            HidePreview();

            if (_dragTracker != null)
            {
                _dragTracker.DragFinished -= OnDragFinished;
                _dragTracker.Dispose();
                _dragTracker = null;
            }

            if (FloatingWindow != null)
            {
                TWindow win = FloatingWindow;
                FloatingWindow = null;
                win.PositionChanged -= OnFloatingWindowPositionChanged;
                win.Content = null;
                win.Close();
            }

            if (IsDocked && ActiveSide != side)
            {
                DockingTarget previousTarget = _sideTargets[ActiveSide];
                CollapsePanel();
                ClearDockHost(previousTarget);
            }

            ActiveSide = side;
            IsDocked = true;
            _sideSaver?.Invoke(side);
            ShowDockedContent();
            DockStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearDockHost(DockingTarget target)
        {
            if (target.DockHostControl.Content == SharedControl)
            {
                target.DockHostControl.Content = null;
                target.DockHostControl.IsVisible = false;
            }
        }

        private void ExpandPanel()
        {
            if (_isExpanded || _isDisposed) return;

            if (!TryComputeExpandedLayout(ActiveSide, out double childSize, out double newParentPrimary))
                return;

            DockingTarget target = ActiveTarget;
            _expandedSize = childSize;
            double minIncrease = childSize + _splitterSize;
            _preExpandParentPrimary = IsHorizontal(ActiveSide) ? _parentWindow.Width : _parentWindow.Height;
            _preExpandMinWidth = _parentWindow.MinWidth;
            _preExpandMinHeight = _parentWindow.MinHeight;

            if (IsHorizontal(ActiveSide))
            {
                SetSafeMinWidth(_preExpandMinWidth + minIncrease);
                SetSafeWidth(newParentPrimary);

                if (target.MainGrid != null && target.MainGrid.ColumnDefinitions.Count > target.ContentIndex)
                {
                    target.MainGrid.ColumnDefinitions[target.SplitterIndex].Width = new GridLength(_splitterSize, GridUnitType.Pixel);
                    target.MainGrid.ColumnDefinitions[target.ContentIndex].Width = new GridLength(childSize, GridUnitType.Pixel);
                }
            }
            else
            {
                SetSafeMinHeight(_preExpandMinHeight + minIncrease);
                SetSafeHeight(newParentPrimary);

                if (target.MainGrid != null && target.MainGrid.RowDefinitions.Count > target.ContentIndex)
                {
                    target.MainGrid.RowDefinitions[target.SplitterIndex].Height = new GridLength(_splitterSize, GridUnitType.Pixel);
                    target.MainGrid.RowDefinitions[target.ContentIndex].Height = new GridLength(childSize, GridUnitType.Pixel);
                }
            }

            target.SplitterControl.IsVisible = true;
            _isExpanded = true;
        }

        private void CollapsePanel()
        {
            if (!_isExpanded || _isDisposed) return;

            DockingTarget target = ActiveTarget;
            double collapseAmount = _expandedSize + _splitterSize;

            if (IsHorizontal(ActiveSide))
            {
                double newMinWidth = Math.Max(0, _parentWindow.MinWidth - collapseAmount);
                if (_preExpandMinWidth > 0 && newMinWidth < _preExpandMinWidth)
                {
                    newMinWidth = _preExpandMinWidth;
                }
                SetSafeMinWidth(newMinWidth);
                SetSafeWidth(_preExpandParentPrimary);

                if (target.MainGrid != null && target.MainGrid.ColumnDefinitions.Count > target.ContentIndex)
                {
                    target.MainGrid.ColumnDefinitions[target.SplitterIndex].Width = new GridLength(0, GridUnitType.Pixel);
                    target.MainGrid.ColumnDefinitions[target.ContentIndex].Width = new GridLength(0, GridUnitType.Pixel);
                }
            }
            else
            {
                double newMinHeight = Math.Max(0, _parentWindow.MinHeight - collapseAmount);
                if (_preExpandMinHeight > 0 && newMinHeight < _preExpandMinHeight)
                {
                    newMinHeight = _preExpandMinHeight;
                }
                SetSafeMinHeight(newMinHeight);
                SetSafeHeight(_preExpandParentPrimary);

                if (target.MainGrid != null && target.MainGrid.RowDefinitions.Count > target.ContentIndex)
                {
                    target.MainGrid.RowDefinitions[target.SplitterIndex].Height = new GridLength(0, GridUnitType.Pixel);
                    target.MainGrid.RowDefinitions[target.ContentIndex].Height = new GridLength(0, GridUnitType.Pixel);
                }
            }

            target.SplitterControl.IsVisible = false;
            _isExpanded = false;
            _expandedSize = 0;
        }

        private void SetSafeMinWidth(double value)
        {
            try
            {
                _parentWindow.MinWidth = Math.Max(0, value);
            }
            catch
            {
                // Safeguard against invalid MinWidth values
            }
        }

        private void SetSafeMinHeight(double value)
        {
            try
            {
                _parentWindow.MinHeight = Math.Max(0, value);
            }
            catch
            {
                // Safeguard against invalid MinHeight values
            }
        }

        private void SetSafeWidth(double value)
        {
            try
            {
                _parentWindow.Width = Math.Max(_parentWindow.MinWidth, value);
            }
            catch
            {
                // Safeguard against invalid Width values
            }
        }

        private void SetSafeHeight(double value)
        {
            try
            {
                _parentWindow.Height = Math.Max(_parentWindow.MinHeight, value);
            }
            catch
            {
                // Safeguard against invalid Height values
            }
        }

        private void ShowDockedContent()
        {
            if (_isDisposed) return;

            if (!CanDockOnSide(ActiveSide))
            {
                IsDocked = false;
                ShowFloatingWindow();
                return;
            }

            _onSideAcquired?.Invoke(ActiveSide);
            ExpandPanel();
            if (!_isExpanded)
            {
                IsDocked = false;
                ShowFloatingWindow();
                return;
            }

            if (SharedControl != null)
                SharedControl.Opacity = 0;

            DockingTarget target = ActiveTarget;
            switch (SharedControl?.Parent)
            {
                case ContentControl oldContentControl:
                    oldContentControl.Content = null;
                    break;
                case Decorator oldDecorator:
                    oldDecorator.Child = null;
                    break;

            }

            target.DockHostControl.Content = SharedControl;
            target.DockHostControl.IsVisible = true;

            Dispatcher.UIThread.Post(() =>
            {
                if (_isDisposed || SharedControl == null) return;

                if (Style.instance != null)
                {
                    WindowThemeManager.ApplyToVisual(SharedControl, Style.instance);
                }

                SharedControl.Opacity = 1;
            }, DispatcherPriority.Loaded);
        }

        private void RegisterSplitterEvents()
        {
            foreach (DockingTarget target in _sideTargets.Values)
            {
                if (target.SplitterControl == null)
                    continue;

                Control splitter = target.SplitterControl;

                void pressedHandler(object? sender, PointerPressedEventArgs e)
                {
                    if (_isDisposed || !_isExpanded || ActiveSide != target.Side) return;
                    PointerPointProperties props = e.GetCurrentPoint(_parentWindow).Properties;
                    if (!props.IsLeftButtonPressed)
                        return;

                    _isDraggingSplitter = true;
                    _splitterStartPoint = e.GetPosition(_parentWindow);
                    target.DockHostBorder?.Classes.Add(pressedName);

                    if (IsHorizontal(ActiveSide))
                    {
                        if (target.MainGrid != null && target.MainGrid.ColumnDefinitions.Count > target.ContentIndex)
                            _initialContentSize = target.MainGrid.ColumnDefinitions[target.ContentIndex].Width.Value;
                        _initialParentSize = _parentWindow.Width;
                    }
                    else
                    {
                        if (target.MainGrid != null && target.MainGrid.RowDefinitions.Count > target.ContentIndex)
                            _initialContentSize = target.MainGrid.RowDefinitions[target.ContentIndex].Height.Value;
                        _initialParentSize = _parentWindow.Height;
                    }

                    e.Pointer.Capture(splitter);
                    e.Handled = true;
                }

                void movedHandler(object? sender, PointerEventArgs e)
                {
                    if (!_isDraggingSplitter || _isDisposed || ActiveSide != target.Side) return;

                    _pendingSplitterSize = ComputeClampedSize(e.GetPosition(_parentWindow));

                    if (!_splitterResizePending)
                    {
                        _splitterResizePending = true;
                        Dispatcher.UIThread.Post(() =>
                        {
                            _splitterResizePending = false;
                            if (_isDraggingSplitter && !_isDisposed)
                                ApplySplitterSize(target, _pendingSplitterSize);
                        }, DispatcherPriority.Render);
                    }

                    e.Handled = true;
                }

                void releasedHandler(object? sender, PointerReleasedEventArgs e)
                {
                    _ = (target.DockHostBorder?.Classes.Remove(pressedName));
                    if (!_isDraggingSplitter || _isDisposed) return;
                    _isDraggingSplitter = false;
                    _splitterResizePending = false;
                    e.Pointer.Capture(null);

                    double parentPrimary = IsHorizontal(ActiveSide) ? _parentWindow.Width : _parentWindow.Height;
                    if (parentPrimary > 0)
                    {
                        double proportion = _expandedSize / parentPrimary;
                        proportion = Math.Clamp(proportion, 0.01, 0.99);
                        _proportionSaver?.Invoke(ActiveSide, proportion);
                    }

                    e.Handled = true;
                }

                splitter.PointerCaptureLost += (sender, e) =>
                {
                    _ = (target.DockHostBorder?.Classes.Remove(pressedName));
                    _isDraggingSplitter = false;
                    _splitterResizePending = false;
                };

                if (target.DockHostBorder != null)
                {
                    target.DockHostBorder.PointerPressed += (s, e) => target.DockHostBorder.Classes.Add(pressedName);
                    target.DockHostBorder.PointerReleased += (s, e) => target.DockHostBorder.Classes.Remove(pressedName);
                    target.DockHostBorder.PointerCaptureLost += (s, e) => target.DockHostBorder.Classes.Remove(pressedName);
                }

                splitter.PointerPressed += pressedHandler;
                splitter.PointerMoved += movedHandler;
                splitter.PointerReleased += releasedHandler;

                _splitterPressedHandlers[splitter] = pressedHandler;
                _splitterMovedHandlers[splitter] = movedHandler;
                _splitterReleasedHandlers[splitter] = releasedHandler;
            }
        }

        private double ComputeClampedSize(Point currentPoint)
        {
            switch (ActiveSide)
            {
                case DockSide.Right:
                    {
                        double deltaX = currentPoint.X - _splitterStartPoint.X;
                        return Math.Clamp(_initialContentSize - deltaX, _minSize, _maxSize);
                    }
                case DockSide.Left:
                    {
                        double deltaX = currentPoint.X - _splitterStartPoint.X;
                        return Math.Clamp(_initialContentSize + deltaX, _minSize, _maxSize);
                    }
                case DockSide.Bottom:
                    {
                        double deltaY = currentPoint.Y - _splitterStartPoint.Y;
                        return Math.Clamp(_initialContentSize - deltaY, _minSize, _maxSize);
                    }
                case DockSide.Top:
                    {
                        double deltaY = currentPoint.Y - _splitterStartPoint.Y;
                        return Math.Clamp(_initialContentSize + deltaY, _minSize, _maxSize);
                    }
                default:
                    return _initialContentSize;
            }
        }

        private void ApplySplitterSize(DockingTarget target, double newSize)
        {
            switch (ActiveSide)
            {
                case DockSide.Right:
                case DockSide.Left:
                    {
                        if (target.MainGrid != null && target.MainGrid.ColumnDefinitions.Count > target.ContentIndex)
                            target.MainGrid.ColumnDefinitions[target.ContentIndex].Width = new GridLength(newSize, GridUnitType.Pixel);
                        SetSafeWidth(_initialParentSize + (newSize - _initialContentSize));
                        _expandedSize = newSize;
                        break;
                    }
                case DockSide.Bottom:
                case DockSide.Top:
                    {
                        if (target.MainGrid != null && target.MainGrid.RowDefinitions.Count > target.ContentIndex)
                            target.MainGrid.RowDefinitions[target.ContentIndex].Height = new GridLength(newSize, GridUnitType.Pixel);
                        SetSafeHeight(_initialParentSize + (newSize - _initialContentSize));
                        _expandedSize = newSize;
                        break;
                    }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (disposing)
            {
                foreach ((Control? splitter, EventHandler<PointerPressedEventArgs>? handler) in _splitterPressedHandlers)
                    splitter.PointerPressed -= handler;
                foreach ((Control? splitter, EventHandler<PointerEventArgs>? handler) in _splitterMovedHandlers)
                    splitter.PointerMoved -= handler;
                foreach ((Control? splitter, EventHandler<PointerReleasedEventArgs>? handler) in _splitterReleasedHandlers)
                    splitter.PointerReleased -= handler;

                _splitterPressedHandlers.Clear();
                _splitterMovedHandlers.Clear();
                _splitterReleasedHandlers.Clear();

                if (_dragTracker != null)
                {
                    _dragTracker.DragFinished -= OnDragFinished;
                    _dragTracker.Dispose();
                    _dragTracker = null;
                }

                (SharedControl as IDisposable)?.Dispose();
                SharedControl = null;

                Close();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
