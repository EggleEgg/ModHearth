using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ModHearth.UI.Docking
{
    public partial class DockingManager<TControl, TWindow> : IDisposable
        where TControl : UserControl
        where TWindow : Window
    {
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
                        border?.IsVisible = true;
                    });
                }
            }
        }

        private void HidePreview()
        {
            foreach (DockingTarget target in _sideTargets.Values)
            {
                target.PreviewBorder?.IsVisible = false;
            }

            _isOverDockTarget = false;
            _hoverSide = null;
        }
    }
}
