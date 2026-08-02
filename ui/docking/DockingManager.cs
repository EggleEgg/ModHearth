using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ModHearth.UI
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
    }

    /// <summary>
    /// Scalable logic for docking sub-windows into MainWindow on any side (Left, Right, Top, Bottom)
    /// with overflow prevention against host screen bounds.
    /// </summary>
    public class DockingManager<TControl, TWindow> : IDisposable
        where TControl : UserControl
        where TWindow : Window
    {
        private readonly Window _parentWindow;
        private readonly IReadOnlyDictionary<DockSide, DockingTarget> _sideTargets;
        private readonly DockSide _defaultSide;
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

        private TControl? _sharedControl;
        private TWindow? _floatingWindow;
        private bool _isDocked;
        private bool _isExpanded;
        private bool _isOverDockTarget;
        private IWindowDragTracker? _dragTracker;

        private DockSide _activeSide;
        private DockSide? _hoverSide;
        private double _expandedSize;
        private double _preExpandParentPrimary;

        private bool _isDraggingSplitter;
        private Point _splitterStartPoint;
        private double _initialContentSize;
        private double _initialParentSize;
        private bool _isDisposed;

        private readonly Dictionary<Control, EventHandler<PointerPressedEventArgs>> _splitterPressedHandlers = new();
        private readonly Dictionary<Control, EventHandler<PointerEventArgs>> _splitterMovedHandlers = new();
        private readonly Dictionary<Control, EventHandler<PointerReleasedEventArgs>> _splitterReleasedHandlers = new();

        private EventHandler<PointerReleasedEventArgs>? _pointerReleasedHandler;
        private EventHandler<PointerCaptureLostEventArgs>? _pointerCaptureLostHandler;

        public event EventHandler? DockStateChanged;
        public event EventHandler? Closed;

        public TControl? SharedControl => _sharedControl;
        public TWindow? FloatingWindow => _floatingWindow;
        public bool IsDocked => _isDocked;
        public bool IsOpen => _isExpanded || _floatingWindow != null;
        public DockSide ActiveSide => _activeSide;
        public DockSide DefaultSide => _defaultSide;

        private DockingTarget ActiveTarget => _sideTargets[_activeSide];

        public DockingManager(
            Window parentWindow,
            IReadOnlyDictionary<DockSide, DockingTarget> sideTargets,
            DockSide defaultSide,
            Func<TControl> controlCreator,
            Func<TControl, TWindow> windowCreator,
            double defaultSize,
            double minSize,
            double maxSize,
            double splitterSize = 7,
            bool initialDocked = false,
            Action<DockSide>? onSideAcquired = null,
            Func<DockSide, double>? proportionLoader = null,
            Action<DockSide, double>? proportionSaver = null,
            Func<DockSide>? sideLoader = null,
            Action<DockSide>? sideSaver = null)
        {
            _parentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
            _sideTargets = sideTargets ?? throw new ArgumentNullException(nameof(sideTargets));
            if (!_sideTargets.ContainsKey(defaultSide))
                throw new ArgumentException($"Default side '{defaultSide}' is not present in sideTargets.", nameof(defaultSide));

            _defaultSide = defaultSide;
            _sideLoader = sideLoader;
            _sideSaver = sideSaver;
            _activeSide = _sideLoader?.Invoke() ?? defaultSide;
            if (!_sideTargets.ContainsKey(_activeSide))
                _activeSide = defaultSide;

            _controlCreator = controlCreator ?? throw new ArgumentNullException(nameof(controlCreator));
            _windowCreator = windowCreator ?? throw new ArgumentNullException(nameof(windowCreator));
            _defaultSize = defaultSize;
            _minSize = minSize;
            _maxSize = maxSize;
            _splitterSize = splitterSize;
            _onSideAcquired = onSideAcquired;
            _proportionLoader = proportionLoader;
            _proportionSaver = proportionSaver;
            _isDocked = initialDocked && CanDockOnSide(_activeSide);

            RegisterSplitterEvents();
        }

        private static bool IsHorizontal(DockSide side) =>
            side is DockSide.Left or DockSide.Right;

        private bool TryGetScreenLayout(out PixelRect workingArea, out PixelPoint parentScreenPos, out double scale)
        {
            workingArea = default;
            parentScreenPos = default;
            scale = _parentWindow.DesktopScaling;

            try
            {
                var screen = _parentWindow.Screens?.ScreenFromWindow(_parentWindow) ?? _parentWindow.Screens?.Primary;
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

        private double GetParentMinPrimary(DockSide side) =>
            IsHorizontal(side) ? _parentWindow.MinWidth : _parentWindow.MinHeight;

        private double GetAvailablePrimary(DockSide side, PixelRect workingArea, PixelPoint parentScreenPos, double scale)
        {
            return side switch
            {
                DockSide.Right or DockSide.Left =>
                    (workingArea.X + workingArea.Width - parentScreenPos.X) / scale,
                DockSide.Bottom or DockSide.Top =>
                    (workingArea.Y + workingArea.Height - parentScreenPos.Y) / scale,
                _ => double.MaxValue
            };
        }

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

            _activeSide = _sideLoader?.Invoke() ?? _defaultSide;
            if (!_sideTargets.ContainsKey(_activeSide))
                _activeSide = _defaultSide;

            if (_isDocked && CanDockOnSide(_activeSide))
            {
                ShowDockedContent();
            }
            else
            {
                if (_isDocked)
                    _isDocked = false;

                ShowFloatingWindow();
            }
        }

        public void Close()
        {
            if (_isDisposed) return;
            CollapsePanel();

            foreach (var target in _sideTargets.Values)
                ClearDockHost(target);

            if (_dragTracker != null)
            {
                _dragTracker.DragFinished -= OnDragFinished;
                _dragTracker.Dispose();
                _dragTracker = null;
            }

            if (_floatingWindow != null)
            {
                var win = _floatingWindow;
                _floatingWindow = null;
                win.PositionChanged -= OnFloatingWindowPositionChanged;
                if (_pointerReleasedHandler != null) win.RemoveHandler(InputElement.PointerReleasedEvent, _pointerReleasedHandler);
                if (_pointerCaptureLostHandler != null) win.RemoveHandler(InputElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
                win.Content = null;
                win.Close();
            }

            HidePreview();
            WindowThemeManager.ApplyToOpenWindows();
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleDock()
        {
            SetDocked(!_isDocked);
        }

        public void SetDocked(bool docked)
        {
            if (_isDisposed || _isDocked == docked) return;

            if (docked)
            {
                _activeSide = _defaultSide;
                if (!CanDockOnSide(_activeSide))
                    return;
            }

            _isDocked = docked;

            if (!IsOpen)
            {
                DockStateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_isDocked)
            {
                if (_dragTracker != null)
                {
                    _dragTracker.DragFinished -= OnDragFinished;
                    _dragTracker.Dispose();
                    _dragTracker = null;
                }

                if (_floatingWindow != null)
                {
                    _floatingWindow.PositionChanged -= OnFloatingWindowPositionChanged;
                    _floatingWindow.Content = null;
                    _floatingWindow.Close();
                    _floatingWindow = null;
                }

                if (IsOpen || _sharedControl != null)
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
            if (_sharedControl == null)
                _sharedControl = _controlCreator();
        }

        private void ShowFloatingWindow()
        {
            CollapsePanel();
            foreach (var target in _sideTargets.Values)
                ClearDockHost(target);

            if (_floatingWindow == null)
            {
                _floatingWindow = _windowCreator(_sharedControl!);
                _floatingWindow.PositionChanged += OnFloatingWindowPositionChanged;

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

                _floatingWindow.AddHandler(InputElement.PointerReleasedEvent, _pointerReleasedHandler, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
                _floatingWindow.AddHandler(InputElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);

                _dragTracker = WindowDragTracker.Create(_floatingWindow);
                _dragTracker.DragFinished += OnDragFinished;

                _floatingWindow.Closed += (_, _) =>
                {
                    if (_dragTracker != null)
                    {
                        _dragTracker.DragFinished -= OnDragFinished;
                        _dragTracker.Dispose();
                        _dragTracker = null;
                    }

                    if (_floatingWindow == null) return;
                    var win = _floatingWindow;
                    _floatingWindow = null;
                    win.PositionChanged -= OnFloatingWindowPositionChanged;
                    if (_pointerReleasedHandler != null) win.RemoveHandler(InputElement.PointerReleasedEvent, _pointerReleasedHandler);
                    if (_pointerCaptureLostHandler != null) win.RemoveHandler(InputElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
                    win.Content = null;
                    HidePreview();
                };
            }

            _floatingWindow.Show(_parentWindow);
        }

        private void OnDragFinished(object? sender, EventArgs e)
        {
            if (_isOverDockTarget)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isOverDockTarget && !_isDocked && !_isDisposed && _floatingWindow != null)
                        Dock();
                });
            }
        }

        private void OnFloatingWindowPositionChanged(object? sender, PixelPointEventArgs e)
        {
            if (_floatingWindow == null || _isDocked || _isDisposed) return;

            try
            {
                if (!_parentWindow.IsVisible)
                    return;

                PixelPoint parentTopLeft = _parentWindow.PointToScreen(new Point(0, 0));
                double scale = _parentWindow.DesktopScaling;
                double parentWidthPx = _parentWindow.ClientSize.Width * scale;
                double parentHeightPx = _parentWindow.ClientSize.Height * scale;

                PixelPoint floatingPos = _floatingWindow.Position;
                double floatingScale = _floatingWindow.DesktopScaling;
                PixelPoint floatingCenter = new PixelPoint(
                    floatingPos.X + (int)(_floatingWindow.ClientSize.Width * floatingScale / 2),
                    floatingPos.Y + (int)(_floatingWindow.ClientSize.Height * floatingScale / 2)
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

            foreach (var (side, target) in _sideTargets)
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

            if (snapThreshold <= 0 || snapThreshold > 500)
                snapThreshold = 250;

            return snapThreshold;
        }

        private static bool IsOverSide(
            DockSide side,
            PixelPoint floatingCenter,
            PixelPoint parentTopLeft,
            double parentWidthPx,
            double parentHeightPx,
            int snapThreshold)
        {
            return side switch
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
        }

        // Vacates this manager's docked side without immediately creating a floating window. Used when another DockingManager is claiming the same side.
        internal void ForceUndockForSideHandoff()
        {
            if (_isDisposed || !_isDocked) return;

            CollapsePanel();
            ClearDockHost(ActiveTarget);
            _isDocked = false;
            DockStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static double GetDistanceToSideEdge(
            DockSide side,
            PixelPoint floatingCenter,
            PixelPoint parentTopLeft,
            double parentWidthPx,
            double parentHeightPx)
        {
            return side switch
            {
                DockSide.Right => Math.Abs(floatingCenter.X - (parentTopLeft.X + parentWidthPx)),
                DockSide.Left => Math.Abs(floatingCenter.X - parentTopLeft.X),
                DockSide.Bottom => Math.Abs(floatingCenter.Y - (parentTopLeft.Y + parentHeightPx)),
                DockSide.Top => Math.Abs(floatingCenter.Y - parentTopLeft.Y),
                _ => double.MaxValue
            };
        }

        private void UpdatePreviewVisibility(DockSide? side)
        {
            foreach (var (dockSide, target) in _sideTargets)
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
                    var border = target.PreviewBorder;
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
            foreach (var target in _sideTargets.Values)
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

            if (_isDocked && _activeSide == side)
                return;

            HidePreview();

            if (_dragTracker != null)
            {
                _dragTracker.DragFinished -= OnDragFinished;
                _dragTracker.Dispose();
                _dragTracker = null;
            }

            if (_floatingWindow != null)
            {
                var win = _floatingWindow;
                _floatingWindow = null;
                win.PositionChanged -= OnFloatingWindowPositionChanged;
                win.Content = null;
                win.Close();
            }

            if (_isDocked && _activeSide != side)
            {
                var previousTarget = _sideTargets[_activeSide];
                CollapsePanel();
                ClearDockHost(previousTarget);
            }

            _activeSide = side;
            _isDocked = true;
            _sideSaver?.Invoke(side);
            ShowDockedContent();
            DockStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearDockHost(DockingTarget target)
        {
            if (target.DockHostControl.Content == _sharedControl)
            {
                target.DockHostControl.Content = null;
                target.DockHostControl.IsVisible = false;
            }
        }

        private void ExpandPanel()
        {
            if (_isExpanded || _isDisposed) return;

            if (!TryComputeExpandedLayout(_activeSide, out double childSize, out double newParentPrimary))
                return;

            var target = ActiveTarget;
            _expandedSize = childSize;
            double minIncrease = childSize + _splitterSize;
            _preExpandParentPrimary = IsHorizontal(_activeSide) ? _parentWindow.Width : _parentWindow.Height;

            if (IsHorizontal(_activeSide))
            {
                _parentWindow.MinWidth += minIncrease;
                _parentWindow.Width = newParentPrimary;

                if (target.MainGrid != null && target.MainGrid.ColumnDefinitions.Count > target.ContentIndex)
                {
                    target.MainGrid.ColumnDefinitions[target.SplitterIndex].Width = new GridLength(_splitterSize, GridUnitType.Pixel);
                    target.MainGrid.ColumnDefinitions[target.ContentIndex].Width = new GridLength(childSize, GridUnitType.Pixel);
                }
            }
            else
            {
                _parentWindow.MinHeight += minIncrease;
                _parentWindow.Height = newParentPrimary;

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

            var target = ActiveTarget;
            double collapseAmount = _expandedSize + _splitterSize;

            if (IsHorizontal(_activeSide))
            {
                _parentWindow.MinWidth -= collapseAmount;
                _parentWindow.Width = _preExpandParentPrimary;

                if (target.MainGrid != null && target.MainGrid.ColumnDefinitions.Count > target.ContentIndex)
                {
                    target.MainGrid.ColumnDefinitions[target.SplitterIndex].Width = new GridLength(0, GridUnitType.Pixel);
                    target.MainGrid.ColumnDefinitions[target.ContentIndex].Width = new GridLength(0, GridUnitType.Pixel);
                }
            }
            else
            {
                _parentWindow.MinHeight -= collapseAmount;
                _parentWindow.Height = _preExpandParentPrimary;

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

        private void ShowDockedContent()
        {
            if (_isDisposed) return;

            if (!CanDockOnSide(_activeSide))
            {
                _isDocked = false;
                ShowFloatingWindow();
                return;
            }

            _onSideAcquired?.Invoke(_activeSide);
            ExpandPanel();
            if (!_isExpanded)
            {
                _isDocked = false;
                ShowFloatingWindow();
                return;
            }

            if (_sharedControl != null)
                _sharedControl.Opacity = 0;

            DockingTarget target = ActiveTarget;
            if (_sharedControl?.Parent is ContentControl oldContentControl)
            {
                oldContentControl.Content = null;
            }
            else if (_sharedControl?.Parent is Decorator oldDecorator)
            {
                oldDecorator.Child = null;
            }

            target.DockHostControl.Content = _sharedControl;
            target.DockHostControl.IsVisible = true;

            Dispatcher.UIThread.Post(() =>
            {
                if (_isDisposed || _sharedControl == null) return;

                if (Style.instance != null)
                {
                    WindowThemeManager.ApplyToVisual(_sharedControl, Style.instance);
                    WindowThemeManager.ApplyToOpenWindows();
                }

                _sharedControl.Opacity = 1;
            }, DispatcherPriority.Loaded);
        }

        private void RegisterSplitterEvents()
        {
            foreach (var target in _sideTargets.Values)
            {
                if (target.SplitterControl == null)
                    continue;

                var splitter = target.SplitterControl;

                EventHandler<PointerPressedEventArgs> pressedHandler = (sender, e) =>
                {
                    if (_isDisposed || !_isExpanded || _activeSide != target.Side) return;
                    var props = e.GetCurrentPoint(_parentWindow).Properties;
                    if (!props.IsLeftButtonPressed)
                        return;

                    _isDraggingSplitter = true;
                    _splitterStartPoint = e.GetPosition(_parentWindow);

                    if (IsHorizontal(_activeSide))
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
                };

                EventHandler<PointerEventArgs> movedHandler = (sender, e) =>
                {
                    if (!_isDraggingSplitter || _isDisposed || _activeSide != target.Side) return;
                    var currentPoint = e.GetPosition(_parentWindow);

                    if (_activeSide == DockSide.Right)
                    {
                        double deltaX = currentPoint.X - _splitterStartPoint.X;
                        double newSize = Math.Clamp(_initialContentSize - deltaX, _minSize, _maxSize);
                        if (target.MainGrid != null && target.MainGrid.ColumnDefinitions.Count > target.ContentIndex)
                            target.MainGrid.ColumnDefinitions[target.ContentIndex].Width = new GridLength(newSize, GridUnitType.Pixel);
                        _parentWindow.Width = _initialParentSize + (newSize - _initialContentSize);
                        _expandedSize = newSize;
                    }
                    else if (_activeSide == DockSide.Left)
                    {
                        double deltaX = currentPoint.X - _splitterStartPoint.X;
                        double newSize = Math.Clamp(_initialContentSize - deltaX, _minSize, _maxSize);
                        if (target.MainGrid != null && target.MainGrid.ColumnDefinitions.Count > target.ContentIndex)
                            target.MainGrid.ColumnDefinitions[target.ContentIndex].Width = new GridLength(newSize, GridUnitType.Pixel);
                        _parentWindow.Width = _initialParentSize + (newSize - _initialContentSize);
                        _expandedSize = newSize;
                    }
                    else if (_activeSide == DockSide.Bottom)
                    {
                        double deltaY = currentPoint.Y - _splitterStartPoint.Y;
                        double newSize = Math.Clamp(_initialContentSize - deltaY, _minSize, _maxSize);
                        if (target.MainGrid != null && target.MainGrid.RowDefinitions.Count > target.ContentIndex)
                            target.MainGrid.RowDefinitions[target.ContentIndex].Height = new GridLength(newSize, GridUnitType.Pixel);
                        _parentWindow.Height = _initialParentSize + (newSize - _initialContentSize);
                        _expandedSize = newSize;
                    }
                    else if (_activeSide == DockSide.Top)
                    {
                        double deltaY = currentPoint.Y - _splitterStartPoint.Y;
                        double newSize = Math.Clamp(_initialContentSize + deltaY, _minSize, _maxSize);
                        if (target.MainGrid != null && target.MainGrid.RowDefinitions.Count > target.ContentIndex)
                            target.MainGrid.RowDefinitions[target.ContentIndex].Height = new GridLength(newSize, GridUnitType.Pixel);
                        _parentWindow.Height = _initialParentSize + (newSize - _initialContentSize);
                        _expandedSize = newSize;
                    }

                    e.Handled = true;
                };

                EventHandler<PointerReleasedEventArgs> releasedHandler = (sender, e) =>
                {
                    if (!_isDraggingSplitter || _isDisposed) return;
                    _isDraggingSplitter = false;
                    e.Pointer.Capture(null);

                    double parentPrimary = IsHorizontal(_activeSide) ? _parentWindow.Width : _parentWindow.Height;
                    if (parentPrimary > 0)
                    {
                        double proportion = _expandedSize / parentPrimary;
                        proportion = Math.Clamp(proportion, 0.01, 0.99);
                        _proportionSaver?.Invoke(_activeSide, proportion);
                    }

                    e.Handled = true;
                };

                splitter.PointerPressed += pressedHandler;
                splitter.PointerMoved += movedHandler;
                splitter.PointerReleased += releasedHandler;

                _splitterPressedHandlers[splitter] = pressedHandler;
                _splitterMovedHandlers[splitter] = movedHandler;
                _splitterReleasedHandlers[splitter] = releasedHandler;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            foreach (var (splitter, handler) in _splitterPressedHandlers)
                splitter.PointerPressed -= handler;
            foreach (var (splitter, handler) in _splitterMovedHandlers)
                splitter.PointerMoved -= handler;
            foreach (var (splitter, handler) in _splitterReleasedHandlers)
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

            (_sharedControl as IDisposable)?.Dispose();
            _sharedControl = null;

            Close();
        }
    }
}
