using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ModHearth.UI.ModList
{
    /// <summary>
    /// A generic controller that manages selection state for a list, allowing for context menu interactions and mass selection
    /// </summary>
    public sealed class ListSelectionController<T> where T : class, ISelectableItem
    {
        private bool suppressSelectionHandling;
        private bool suppressSelectionForScrollbar;
        private List<T>? suppressSelectionSnapshot;
        private IList? suppressSelectionList;
        private List<T>? contextSelectionSnapshot;
        private IList? contextSelectionList;

        public void RegisterList(DataGrid list)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        }
        public void RegisterList(ListBox list)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        }

        public bool HandleSelectionChanged(ListBox list)
        {
            if (suppressSelectionHandling)
                return true;

            return TryRestoreScrollbarSelection(list);
        }
        public bool HandleSelectionChanged(DataGrid list)
        {
            if (suppressSelectionHandling)
                return true;

            return TryRestoreScrollbarSelection(list);
        }
        public bool TryRestoreScrollbarSelection(ListBox list)
        {
            if (list?.SelectedItems is not { } selectedItems)
                return false;

            return TryRestoreScrollbarSelectionCore(selectedItems, selection => RestoreListSelection(list, selection));
        }

        public bool TryRestoreScrollbarSelection(DataGrid grid)
        {
            if (grid?.SelectedItems is not { } selectedItems)
                return false;

            return TryRestoreScrollbarSelectionCore(selectedItems, selection => RestoreListSelection(grid, selection));
        }

        private bool TryRestoreScrollbarSelectionCore(IList selectedItems, Action<List<T>> restore)
        {
            if (!suppressSelectionForScrollbar || suppressSelectionList != selectedItems)
                return false;

            suppressSelectionForScrollbar = false;
            suppressSelectionList = null;

            if (suppressSelectionSnapshot != null)
                restore(suppressSelectionSnapshot);

            suppressSelectionSnapshot = null;
            return true;
        }

        public bool TryRestoreContextSelection(ListBox list, T item)
        {
            if (list?.SelectedItems is not { } selectedItems)
                return false;

            return TryRestoreContextSelectionCore(selectedItems, item, selection => RestoreListSelection(list, selection));
        }

        public bool TryRestoreContextSelection(DataGrid grid, T item)
        {
            if (grid?.SelectedItems is not { } selectedItems)
                return false;

            return TryRestoreContextSelectionCore(selectedItems, item, selection => RestoreListSelection(grid, selection));
        }

        private bool TryRestoreContextSelectionCore(IList selectedItems, T item, Action<List<T>> restore)
        {
            bool restored = false;
            if (contextSelectionSnapshot != null &&
                contextSelectionList == selectedItems &&
                contextSelectionSnapshot.Contains(item))
            {
                restore(contextSelectionSnapshot);
                restored = true;
            }

            contextSelectionSnapshot = null;
            contextSelectionList = null;
            return restored;
        }

        public void RestoreListSelection(ListBox list, IEnumerable<T> selection)
        {
            if (list.SelectedItems == null)
                return;

            List<T> target = selection as List<T> ?? [.. selection];
            if (SelectionAlreadyMatches(list.SelectedItems, target))
                return;

            HashSet<T> previous = [.. list.SelectedItems.Cast<T>()];
            HashSet<T> targetSet = [.. target];
            suppressSelectionHandling = true;
            try
            {
                list.SelectedItems.Clear();
                foreach (T item in target)
                    _ = list.SelectedItems.Add(item);

                // Restoring a drag selection used to rescan the full list. Only items that were
                // selected before or after the restore can have changed selection state.
                UpdateSelectionState(previous, targetSet);
            }
            finally
            {
                suppressSelectionHandling = false;
            }
        }

        public void RestoreListSelection(DataGrid grid, IEnumerable<T> selection)
        {
            if (grid.SelectedItems == null)
                return;

            List<T> target = selection as List<T> ?? [.. selection];
            if (SelectionAlreadyMatches(grid.SelectedItems, target))
                return;

            suppressSelectionHandling = true;
            grid.SelectedItems.Clear();
            foreach (T item in target)
                _ = grid.SelectedItems.Add(item);
            UpdateSelectionState(grid);
            suppressSelectionHandling = false;
        }

        private static bool SelectionAlreadyMatches(IList currentSelectedItems, List<T> target)
        {
            if (currentSelectedItems.Count != target.Count)
                return false;

            foreach (T item in target)
            {
                if (!currentSelectedItems.Contains(item))
                    return false;
            }
            return true;
        }

        private static void UpdateSelectionState(IEnumerable<T> previous, HashSet<T> selected)
        {
            HashSet<T> changedItems = [.. previous];
            changedItems.UnionWith(selected);

            foreach (T item in changedItems)
                item.IsSelected = selected.Contains(item);
        }

        public void UpdateSelectionState(DataGrid list)
        {
            if (list == null) return;

            UpdateSelectionStateCore(list.ItemsSource as IEnumerable<T>, list.SelectedItems);
        }
        public void UpdateSelectionState(ListBox list)
        {
            if (list == null) return;

            UpdateSelectionStateCore(list.ItemsSource as IEnumerable<T>, list.SelectedItems);
        }
        public void UpdateSelectionStateCore(IEnumerable<T>? items, IList? selectedItems)
        {


            if (items == null)
            {

                return;
            }

            HashSet<T> selected = selectedItems?.Cast<T>().ToHashSet() ?? [];

            foreach (T item in items)
            {
                bool isCurrentlySelected = selected.Contains(item);
                if (item.IsSelected != isCurrentlySelected)
                {
                    item.IsSelected = isCurrentlySelected;
                }
            }


        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            suppressSelectionForScrollbar = false;
            suppressSelectionList = null;
            suppressSelectionSnapshot = null;

            switch (sender)
            {
                case ListBox list:
                    {
                        PointerPoint point = e.GetCurrentPoint(list);
                        CaptureContextSelectionSnapshot(list, point);

                        if (IsPointerOverScrollBar(list, point.Position))
                        {
                            CaptureScrollbarSelectionSnapshot(list, point);
                            return;
                        }

                        if (point.Properties.IsRightButtonPressed && TryOpenContextMenu(list, e, point))
                            return;

                        return;
                    }

                case DataGrid grid:
                    {
                        PointerPoint point = e.GetCurrentPoint(grid);
                        CaptureContextSelectionSnapshot(grid, point);

                        if (IsPointerOverScrollBar(grid, point.Position))
                        {
                            CaptureScrollbarSelectionSnapshot(grid, point);
                            return;
                        }

                        if (point.Properties.IsRightButtonPressed && TryOpenContextMenu(grid, e, point))
                            return;
                        break;
                    }
            }
        }

        private void CaptureScrollbarSelectionSnapshot(ListBox list, PointerPoint point)
            => CaptureScrollbarSelectionSnapshot(list.SelectedItems, point);

        private void CaptureScrollbarSelectionSnapshot(DataGrid grid, PointerPoint point)
            => CaptureScrollbarSelectionSnapshot(grid.SelectedItems, point);

        private void CaptureScrollbarSelectionSnapshot(IList? selectedItems, PointerPoint point)
        {
            if (!point.Properties.IsLeftButtonPressed)
                return;

            suppressSelectionForScrollbar = true;
            suppressSelectionList = selectedItems;
            suppressSelectionSnapshot = selectedItems?.Cast<T>().ToList() ?? [];
        }

        private void CaptureContextSelectionSnapshot(ListBox list, PointerPoint point)
            => CaptureContextSelectionSnapshot(list.SelectedItems, point, () => GetItemAtPoint<ListBoxItem>(list, point.Position));

        private void CaptureContextSelectionSnapshot(DataGrid grid, PointerPoint point)
            => CaptureContextSelectionSnapshot(grid.SelectedItems, point, () => GetItemAtPoint<DataGridRow>(grid, point.Position));

        private void CaptureContextSelectionSnapshot(IList? selectedItems, PointerPoint point, Func<T?> resolveHit)
        {
            contextSelectionSnapshot = null;
            contextSelectionList = null;

            if (!point.Properties.IsRightButtonPressed)
                return;

            T? hit = resolveHit();
            if (hit == null)
                return;

            List<T> selected = selectedItems?.Cast<T>().ToList() ?? [];
            if (selected.Count > 1 && selected.Contains(hit))
            {
                contextSelectionSnapshot = selected;
                contextSelectionList = selectedItems;
            }
        }

        private static T? GetItemAtPoint<TContainer>(IInputElement root, Point point) where TContainer : Control
        {
            IInputElement? element = root.InputHitTest(point);
            Control? control = element as Control;
            TContainer? container = control?.FindAncestorOfType<TContainer>();
            return container?.DataContext as T;
        }

        private static bool IsPointerOverScrollBar(IInputElement root, Point point)
        {
            IInputElement? element = root.InputHitTest(point);
            if (element is not Control control)
                return false;

            return control switch
            {
                ScrollBar => true,
                _ => control.FindAncestorOfType<ScrollBar>() != null,
            };
        }

        private static bool TryFindContextMenu(IInputElement root, Point point, out ContextMenu? menu, out Control? target)
        {
            menu = null;
            target = null;

            IInputElement? element = root.InputHitTest(point);
            Control? control = element as Control;
            Visual? current = control;

            while (current != null)
            {
                if (current is Control currentControl && currentControl.ContextMenu != null)
                {
                    menu = currentControl.ContextMenu;
                    target = currentControl;
                    return true;
                }

                current = current.GetVisualParent();
            }

            return false;
        }

        private static void EnsureItemSelected(IList? selectedItems, T hit)
        {
            if (selectedItems == null)
                return;

            if (selectedItems.Count == 0 || !selectedItems.Contains(hit))
            {
                selectedItems.Clear();
                _ = selectedItems.Add(hit);
            }
        }

        private bool TryOpenContextMenu(ListBox list, PointerPressedEventArgs e, PointerPoint point)
            => TryOpenContextMenu(
                list,
                list.SelectedItems,
                point,
                e,
                GetItemAtPoint<ListBoxItem>(list, point.Position),
                () => UpdateSelectionState(list));

        private bool TryOpenContextMenu(DataGrid grid, PointerPressedEventArgs e, PointerPoint point)
            => TryOpenContextMenu(
                grid,
                grid.SelectedItems,
                point,
                e,
                GetItemAtPoint<DataGridRow>(grid, point.Position),
                () => UpdateSelectionState(grid));

        private bool TryOpenContextMenu(
            IInputElement root,
            IList? selectedItems,
            PointerPoint point,
            PointerPressedEventArgs e,
            T? hit,
            Action updateSelectionState)
        {
            if (hit == null)
                return false;

            if (!TryFindContextMenu(root, point.Position, out _, out _))
                return false;

            EnsureItemSelected(selectedItems, hit);
            updateSelectionState();
            e.Handled = true;
            return true;
        }
    }
}
