using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace ModHearth.UI.Docking
{
    public partial class DockingManager<TControl, TWindow> : IDisposable
        where TControl : UserControl
        where TWindow : Window
    {
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

                // Tear the panel down before the disposed flag is set: CloseCore is the same path
                // used by Close(), so the floating window and its handlers cannot be left alive.
                CloseCore();

                (SharedControl as IDisposable)?.Dispose();
                SharedControl = null;
            }

            _isDisposed = true;
        }
    }
}
