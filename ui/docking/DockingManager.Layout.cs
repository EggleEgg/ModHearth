using Avalonia.Controls;
using ModHearth.Utils.Logging;

namespace ModHearth.UI.Docking
{
    public partial class DockingManager<TControl, TWindow> : IDisposable
        where TControl : UserControl
        where TWindow : Window
    {
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

            DetachDragTracker();
            DetachFloatingWindow(closeWindow: true);

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

        private void SetSafeMinWidth(double value) => TrySetWindowMetric(() => _parentWindow.MinWidth = Math.Max(0, value));

        private void SetSafeMinHeight(double value) => TrySetWindowMetric(() => _parentWindow.MinHeight = Math.Max(0, value));

        private void SetSafeWidth(double value) => TrySetWindowMetric(() => _parentWindow.Width = Math.Max(_parentWindow.MinWidth, value));

        private void SetSafeHeight(double value) => TrySetWindowMetric(() => _parentWindow.Height = Math.Max(_parentWindow.MinHeight, value));

        /// <summary>
        /// Applies a window-metric change, ignoring the platform exceptions Avalonia can throw while
        /// a window is being shown/resized. Logged at dev level so failures are still diagnosable.
        /// </summary>
        private static void TrySetWindowMetric(Action apply)
        {
            try
            {
                apply();
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Ignored window metric change: {ex.Message}");
            }
        }
    }
}
