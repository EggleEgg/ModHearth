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
    public partial class DockingManager<TControl, TWindow> : IDisposable
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

        private static double GetAvailablePrimary(DockSide side, PixelRect workingArea, PixelPoint parentScreenPos, double scale) => side switch
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
                double availablePrimary = DockingManager<TControl, TWindow>.GetAvailablePrimary(side, workingArea, parentScreenPos, scale);

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

            double availablePrimary = DockingManager<TControl, TWindow>.GetAvailablePrimary(side, workingArea, parentScreenPos, scale);

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
            CloseCore();
        }

        /// <summary>
        /// Shared close body. Kept separate from <see cref="Close"/> so <see cref="Dispose(bool)"/>
        /// can tear down before setting the disposed flag, and so the disposed guard cannot turn the
        /// final close into a no-op.
        /// </summary>
        private void CloseCore()
        {
            CollapsePanel();

            ClearAllDockHosts();
            DetachDragTracker();
            DetachFloatingWindow(closeWindow: true);

            HidePreview();
            Closed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Detaches and disposes the drag tracker. Safe to call when no tracker exists, so every
        /// teardown path (close, dock, float-window closed, dispose) can share it.
        /// </summary>
        private void DetachDragTracker()
        {
            if (_dragTracker == null)
                return;

            _dragTracker.DragFinished -= OnDragFinished;
            _dragTracker.Dispose();
            _dragTracker = null;
        }

        /// <summary>
        /// Detaches the floating window and always removes both pointer handlers. Closing the window
        /// raises <see cref="Window.Closed"/>, which calls this again; the null field check keeps that
        /// re-entrant path a no-op.
        /// </summary>
        private void DetachFloatingWindow(bool closeWindow)
        {
            if (FloatingWindow == null)
                return;

            TWindow win = FloatingWindow;
            FloatingWindow = null;

            win.PositionChanged -= OnFloatingWindowPositionChanged;
            if (_pointerReleasedHandler != null)
                win.RemoveHandler(InputElement.PointerReleasedEvent, _pointerReleasedHandler);
            if (_pointerCaptureLostHandler != null)
                win.RemoveHandler(InputElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
            win.Content = null;

            if (closeWindow)
                win.Close();
        }

        private void ClearAllDockHosts()
        {
            foreach (DockingTarget target in _sideTargets.Values)
                ClearDockHost(target);
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
                DetachDragTracker();
                DetachFloatingWindow(closeWindow: true);

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
            ClearAllDockHosts();

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
                    DetachDragTracker();

                    if (FloatingWindow == null)
                        return;

                    DetachFloatingWindow(closeWindow: false);
                    HidePreview();
                };
            }

            FloatingWindow.Show(_parentWindow);
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

            SharedControl?.Opacity = 0;

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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
