using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace ModHearth.UI
{
    public class DockingManager<TControl, TWindow> 
        where TControl : UserControl 
        where TWindow : Window
    {
        private readonly Window _parentWindow;
        private readonly Grid _mainGrid;
        private readonly int _splitterColumnIndex;
        private readonly int _contentColumnIndex;
        private readonly Control _splitterControl;
        private readonly ContentControl _dockHostControl;
        
        private readonly Func<TControl> _controlCreator;
        private readonly Func<TControl, TWindow> _windowCreator;
        
        private readonly double _defaultWidth;
        private readonly double _minWidth;
        private readonly double _maxWidth;
        private readonly double _splitterWidth;

        private TControl? _sharedControl;
        private TWindow? _floatingWindow;
        private bool _isDocked = true;
        private bool _isExpanded;

        private bool _isDraggingSplitter;
        private Point _splitterStartPoint;
        private double _initialContentWidth;
        private double _initialParentWidth;

        public event EventHandler? DockStateChanged;
        public event EventHandler? Closed;

        public TControl? SharedControl => _sharedControl;
        public TWindow? FloatingWindow => _floatingWindow;
        public bool IsDocked => _isDocked;
        public bool IsOpen => _isExpanded || _floatingWindow != null;

        public DockingManager(
            Window parentWindow,
            Grid mainGrid,
            int splitterColumnIndex,
            int contentColumnIndex,
            Control splitterControl,
            ContentControl dockHostControl,
            Func<TControl> controlCreator,
            Func<TControl, TWindow> windowCreator,
            double defaultWidth,
            double minWidth,
            double maxWidth,
            double splitterWidth = 7)
        {
            _parentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
            _mainGrid = mainGrid ?? throw new ArgumentNullException(nameof(mainGrid));
            _splitterColumnIndex = splitterColumnIndex;
            _contentColumnIndex = contentColumnIndex;
            _splitterControl = splitterControl ?? throw new ArgumentNullException(nameof(splitterControl));
            _dockHostControl = dockHostControl ?? throw new ArgumentNullException(nameof(dockHostControl));
            _controlCreator = controlCreator ?? throw new ArgumentNullException(nameof(controlCreator));
            _windowCreator = windowCreator ?? throw new ArgumentNullException(nameof(windowCreator));
            _defaultWidth = defaultWidth;
            _minWidth = minWidth;
            _maxWidth = maxWidth;
            _splitterWidth = splitterWidth;

            RegisterSplitterEvents();
        }

        public void Open()
        {
            EnsureControlCreated();

            if (_isDocked)
            {
                ExpandPanel();
                _dockHostControl.Content = _sharedControl;
                _dockHostControl.IsVisible = true;
                RefreshStyles();
            }
            else
            {
                ShowFloatingWindow();
            }
        }

        public void Close()
        {
            CollapsePanel();
            _dockHostControl.Content = null;
            _dockHostControl.IsVisible = false;

            if (_floatingWindow != null)
            {
                var win = _floatingWindow;
                _floatingWindow = null;
                win.Content = null;
                win.Close();
            }
            
            RefreshStyles();
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleDock()
        {
            _isDocked = !_isDocked;
            
            if (_isDocked)
            {
                if (_floatingWindow != null)
                {
                    _floatingWindow.Content = null;
                    _floatingWindow.Close();
                    _floatingWindow = null;
                }
                ExpandPanel();
                _dockHostControl.Content = _sharedControl;
                _dockHostControl.IsVisible = true;
                RefreshStyles();
            }
            else
            {
                CollapsePanel();
                _dockHostControl.Content = null;
                _dockHostControl.IsVisible = false;
                ShowFloatingWindow();
            }
            
            DockStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EnsureControlCreated()
        {
            if (_sharedControl == null)
            {
                _sharedControl = _controlCreator();
            }
        }

        private void ShowFloatingWindow()
        {
            CollapsePanel();
            _dockHostControl.Content = null;
            _dockHostControl.IsVisible = false;

            if (_floatingWindow == null)
            {
                _floatingWindow = _windowCreator(_sharedControl!);
                _floatingWindow.Closed += (sender, args) =>
                {
                    var win = _floatingWindow;
                    _floatingWindow = null;
                    if (win != null)
                    {
                        win.Content = null;
                    }
                    if (!_isDocked)
                    {
                        _isDocked = true;
                        ExpandPanel();
                        _dockHostControl.Content = _sharedControl;
                        _dockHostControl.IsVisible = true;
                        RefreshStyles();
                        DockStateChanged?.Invoke(this, EventArgs.Empty);
                    }
                };
            }
            _floatingWindow.Show(_parentWindow);
            RefreshStyles();
        }

        private void ExpandPanel()
        {
            if (_isExpanded) return;
            
            _parentWindow.Width += _defaultWidth + _splitterWidth;
            _parentWindow.MinWidth += _defaultWidth + _splitterWidth;
            
            if (_mainGrid != null && _mainGrid.ColumnDefinitions.Count > _contentColumnIndex)
            {
                _mainGrid.ColumnDefinitions[_splitterColumnIndex].Width = new GridLength(_splitterWidth, GridUnitType.Pixel);
                _mainGrid.ColumnDefinitions[_contentColumnIndex].Width = new GridLength(_defaultWidth, GridUnitType.Pixel);
            }
            
            _splitterControl.IsVisible = true;
            _isExpanded = true;
        }

        private void CollapsePanel()
        {
            if (!_isExpanded) return;
            
            _parentWindow.Width -= _defaultWidth + _splitterWidth;
            _parentWindow.MinWidth -= _defaultWidth + _splitterWidth;
            
            if (_mainGrid != null && _mainGrid.ColumnDefinitions.Count > _contentColumnIndex)
            {
                _mainGrid.ColumnDefinitions[_splitterColumnIndex].Width = new GridLength(0, GridUnitType.Pixel);
                _mainGrid.ColumnDefinitions[_contentColumnIndex].Width = new GridLength(0, GridUnitType.Pixel);
            }
            
            _splitterControl.IsVisible = false;
            _isExpanded = false;
        }

        private void RefreshStyles()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Style.instance != null)
                {
                    if (_sharedControl != null)
                    {
                        WindowThemeManager.ApplyToVisual(_sharedControl, Style.instance);
                    }
                    WindowThemeManager.ApplyToOpenWindows();
                }
            }, DispatcherPriority.Loaded);
        }

        private void RegisterSplitterEvents()
        {
            if (_splitterControl == null) return;

            _splitterControl.PointerPressed += (sender, e) =>
            {
                var props = e.GetCurrentPoint(_parentWindow).Properties;
                if (props.IsLeftButtonPressed)
                {
                    _isDraggingSplitter = true;
                    _splitterStartPoint = e.GetPosition(_parentWindow);
                    if (_mainGrid != null && _mainGrid.ColumnDefinitions.Count > _contentColumnIndex)
                    {
                        _initialContentWidth = _mainGrid.ColumnDefinitions[_contentColumnIndex].Width.Value;
                    }
                    _initialParentWidth = _parentWindow.Width;
                    e.Pointer.Capture(_splitterControl);
                    e.Handled = true;
                }
            };

            _splitterControl.PointerMoved += (sender, e) =>
            {
                if (!_isDraggingSplitter) return;
                var currentPoint = e.GetPosition(_parentWindow);
                double deltaX = currentPoint.X - _splitterStartPoint.X;
                double newWidth = Math.Clamp(_initialContentWidth - deltaX, _minWidth, _maxWidth);

                if (_mainGrid != null && _mainGrid.ColumnDefinitions.Count > _contentColumnIndex)
                {
                    _mainGrid.ColumnDefinitions[_contentColumnIndex].Width = new GridLength(newWidth, GridUnitType.Pixel);
                }
                _parentWindow.Width = _initialParentWidth + (newWidth - _initialContentWidth);

                e.Handled = true;
            };

            _splitterControl.PointerReleased += (sender, e) =>
            {
                if (!_isDraggingSplitter) return;
                _isDraggingSplitter = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            };
        }
    }
}
