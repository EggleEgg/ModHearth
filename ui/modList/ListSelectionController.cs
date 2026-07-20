using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI;

public sealed class ListSelectionController<T> where T : class, ISelectableItem
{
    private bool suppressSelectionHandling;
    private bool suppressSelectionForScrollbar;
    private List<T>? suppressSelectionSnapshot;
    private System.Collections.IList? suppressSelectionList;
    private List<T>? contextSelectionSnapshot;
    private System.Collections.IList? contextSelectionList;

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
        if (list?.SelectedItems == null || !suppressSelectionForScrollbar || suppressSelectionList != list.SelectedItems)
            return false;

        suppressSelectionForScrollbar = false;
        suppressSelectionList = null;

        if (suppressSelectionSnapshot != null)
            RestoreListSelection(list, suppressSelectionSnapshot);

        suppressSelectionSnapshot = null;
        return true;
    }

    public bool TryRestoreScrollbarSelection(DataGrid grid)
    {
        if (grid?.SelectedItems == null || !suppressSelectionForScrollbar || suppressSelectionList != grid.SelectedItems)
            return false;

        suppressSelectionForScrollbar = false;
        suppressSelectionList = null;

        if (suppressSelectionSnapshot != null)
            RestoreListSelection(grid, suppressSelectionSnapshot);

        suppressSelectionSnapshot = null;
        return true;
    }

    public bool TryRestoreContextSelection(ListBox list, T item)
    {
        if (list?.SelectedItems == null)
            return false;

        bool restored = false;
        if (contextSelectionSnapshot != null &&
            contextSelectionList == list.SelectedItems &&
            contextSelectionSnapshot.Contains(item))
        {
            RestoreListSelection(list, contextSelectionSnapshot);
            restored = true;
        }

        contextSelectionSnapshot = null;
        contextSelectionList = null;
        return restored;
    }

    public bool TryRestoreContextSelection(DataGrid grid, T item)
    {
        if (grid?.SelectedItems == null)
            return false;

        bool restored = false;
        if (contextSelectionSnapshot != null &&
            contextSelectionList == grid.SelectedItems &&
            contextSelectionSnapshot.Contains(item))
        {
            RestoreListSelection(grid, contextSelectionSnapshot);
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

        suppressSelectionHandling = true;
        list.SelectedItems.Clear();
        foreach (T item in selection)
            list.SelectedItems.Add(item);
        UpdateSelectionState(list);
        suppressSelectionHandling = false;
    }

    public void RestoreListSelection(DataGrid grid, IEnumerable<T> selection)
    {
        if (grid.SelectedItems == null)
            return;

        suppressSelectionHandling = true;
        grid.SelectedItems.Clear();
        foreach (T item in selection)
            grid.SelectedItems.Add(item);
        UpdateSelectionState(grid);
        suppressSelectionHandling = false;
    }

    public void UpdateSelectionState(DataGrid list)
    {
        if (list == null) return;

        UpdateSelectionStateCore(list.Name, list.ItemsSource as IEnumerable<T>, list.SelectedItems);
    }
    public void UpdateSelectionState(ListBox list)
    {
        if (list == null) return;

        UpdateSelectionStateCore(list.Name, list.ItemsSource as IEnumerable<T>, list.SelectedItems);
    }
    public void UpdateSelectionStateCore(string? listName, IEnumerable<T>? items, System.Collections.IList? selectedItems)
    {


        if (items == null)
        {

            return;
        }

        HashSet<T> selected = selectedItems?.Cast<T>().ToHashSet() ?? new HashSet<T>();

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

        if (sender is ListBox list)
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

        if (sender is DataGrid grid)
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
        }
    }

    private void CaptureScrollbarSelectionSnapshot(ListBox list, PointerPoint point)
    {
        if (!point.Properties.IsLeftButtonPressed)
            return;

        suppressSelectionForScrollbar = true;
        suppressSelectionList = list.SelectedItems;
        suppressSelectionSnapshot = list.SelectedItems?.Cast<T>().ToList()
            ?? new List<T>();
    }

    private void CaptureScrollbarSelectionSnapshot(DataGrid grid, PointerPoint point)
    {
        if (!point.Properties.IsLeftButtonPressed)
            return;

        suppressSelectionForScrollbar = true;
        suppressSelectionList = grid.SelectedItems;
        suppressSelectionSnapshot = grid.SelectedItems?.Cast<T>().ToList()
            ?? new List<T>();
    }


    private void CaptureContextSelectionSnapshot(ListBox list, PointerPoint point)
    {
        contextSelectionSnapshot = null;
        contextSelectionList = null;

        if (!point.Properties.IsRightButtonPressed)
            return;

        T? hit = GetItemAtPoint(list, point.Position);
        if (hit == null)
            return;

        List<T> selected = list.SelectedItems?.Cast<T>().ToList() ?? new List<T>();
        if (selected.Count > 1 && selected.Contains(hit))
        {
            contextSelectionSnapshot = selected;
            contextSelectionList = list.SelectedItems;
        }
    }

    private void CaptureContextSelectionSnapshot(DataGrid grid, PointerPoint point)
    {
        contextSelectionSnapshot = null;
        contextSelectionList = null;

        if (!point.Properties.IsRightButtonPressed)
            return;

        T? hit = GetItemAtPoint(grid, point.Position);
        if (hit == null)
            return;

        List<T> selected = grid.SelectedItems?.Cast<T>().ToList() ?? new List<T>();
        if (selected.Count > 1 && selected.Contains(hit))
        {
            contextSelectionSnapshot = selected;
            contextSelectionList = grid.SelectedItems;
        }
    }

    private static T? GetItemAtPoint(ListBox list, Point point)
    {
        IInputElement? element = list.InputHitTest(point) as IInputElement;
        Control? control = element as Control;
        ListBoxItem? item = control?.FindAncestorOfType<ListBoxItem>();
        return item?.DataContext as T;
    }

    private static T? GetItemAtPoint(DataGrid grid, Point point)
    {
        IInputElement? element = grid.InputHitTest(point) as IInputElement;
        Control? control = element as Control;
        DataGridRow? row = control?.FindAncestorOfType<DataGridRow>();
        return row?.DataContext as T;
    }

    private static bool IsPointerOverScrollBar(IInputElement root, Point point)
    {
        IInputElement? element = root.InputHitTest(point) as IInputElement;
        if (element is not Control control)
            return false;

        if (control is ScrollBar)
            return true;

        return control.FindAncestorOfType<ScrollBar>() != null;
    }

    private static bool TryFindContextMenu(IInputElement root, Point point, out ContextMenu? menu, out Control? target)
    {
        menu = null;
        target = null;

        IInputElement? element = root.InputHitTest(point) as IInputElement;
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

    private bool TryOpenContextMenu(ListBox list, PointerPressedEventArgs e, PointerPoint point)
    {
        T? hit = GetItemAtPoint(list, point.Position);
        if (hit == null)
            return false;

        if (!TryFindContextMenu(list, point.Position, out ContextMenu? menu, out Control? target))
            return false;

        if (list.SelectedItems != null &&
            (list.SelectedItems.Count == 0 || !list.SelectedItems.Contains(hit)))
        {
            list.SelectedItems.Clear();
            list.SelectedItems.Add(hit);
        }

        UpdateSelectionState(list);
        menu!.DataContext = hit;

        e.Handled = true;
        return true;
    }

    private bool TryOpenContextMenu(DataGrid grid, PointerPressedEventArgs e, PointerPoint point)
    {
        T? hit = GetItemAtPoint(grid, point.Position);
        if (hit == null)
            return false;

        if (!TryFindContextMenu(grid, point.Position, out ContextMenu? menu, out Control? target))
            return false;

        if (grid.SelectedItems != null &&
            (grid.SelectedItems.Count == 0 || !grid.SelectedItems.Contains(hit)))
        {
            grid.SelectedItems.Clear();
            grid.SelectedItems.Add(hit);
        }

        UpdateSelectionState(grid);
        menu!.DataContext = hit;

        e.Handled = true;
        return true;
    }
}
