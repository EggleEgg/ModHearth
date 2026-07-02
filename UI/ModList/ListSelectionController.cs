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
    private ListBox? suppressSelectionList;
    private List<T>? contextSelectionSnapshot;
    private ListBox? contextSelectionList;

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

    public bool HandleSelectionChanged(DataGrid list) => HandleSelectionChangedCore(list?.SelectedItems);
    public bool HandleSelectionChanged(ListBox list) => HandleSelectionChangedCore(list?.SelectedItems);
    public bool HandleSelectionChangedCore(System.Collections.IList? SelectedItems)
    {
        if (suppressSelectionHandling)
            return true;

        return TryRestoreScrollbarSelectionCore(SelectedItems);
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
        //SearchLogging.Log($"UpdateSelectionState START for list={listName ?? "<unnamed>"}");

        if (items == null)
            return;

        // Capture current selection in a hashset for O(1) lookup
        HashSet<T> selected = selectedItems?.Cast<T>().ToHashSet() ?? new HashSet<T>();

        // Only update property if it actually changed to minimize NotifyPropertyChanged spam.
        // This is critical to preventing layout churn during bulk updates.
        foreach (T item in items)
        {
            bool isCurrentlySelected = selected.Contains(item);
            if (item.IsSelected != isCurrentlySelected)
                item.IsSelected = isCurrentlySelected;
        }

        //SearchLogging.Log($"UpdateSelectionState END for list={listName ?? "<unnamed>"}");
    }

    public void RestoreListSelection(System.Collections.IList? list, IEnumerable<T> selection) => RestoreListSelection(list, selection);
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

    public bool TryRestoreContextSelection(DataGrid list, T item) => TryRestoreContextSelectionCore(list?.SelectedItems, item);
    public bool TryRestoreContextSelection(ListBox list, T item) => TryRestoreContextSelectionCore(list?.SelectedItems, item);
    public bool TryRestoreContextSelectionCore(System.Collections.IList? selectedItems, T item)
    {
        if (selectedItems == null) return false;
        bool restored = false;
        if (contextSelectionSnapshot != null &&
            contextSelectionList == selectedItems &&
            contextSelectionSnapshot.Contains(item))
        {
            RestoreListSelection(selectedItems, contextSelectionSnapshot);
            restored = true;
        }

        contextSelectionSnapshot = null;
        contextSelectionList = null;
        return restored;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        suppressSelectionForScrollbar = false;
        suppressSelectionList = null;
        suppressSelectionSnapshot = null;

        if (sender is not ListBox list)
            return;

        PointerPoint point = e.GetCurrentPoint(list);
        CaptureContextSelectionSnapshot(list, point);

        if (IsPointerOverScrollBar(list, point.Position))
        {
            CaptureScrollbarSelectionSnapshot(list, point);
            return;
        }

        if (point.Properties.IsRightButtonPressed && TryOpenContextMenu(list, e, point))
            return;
    }

    private void CaptureScrollbarSelectionSnapshot(ListBox list, PointerPoint point)
    {
        if (!point.Properties.IsLeftButtonPressed)
            return;

        suppressSelectionForScrollbar = true;
        suppressSelectionList = list;
        suppressSelectionSnapshot = list.SelectedItems?.Cast<T>().ToList()
            ?? new List<T>();
    }

    public bool TryRestoreScrollbarSelection(DataGrid list) => TryRestoreScrollbarSelectionCore(list?.SelectedItems);
    public bool TryRestoreScrollbarSelection(ListBox list) => TryRestoreScrollbarSelectionCore(list?.SelectedItems);
    public bool TryRestoreScrollbarSelectionCore(System.Collections.IList? selectedItems)
    {
        if (!suppressSelectionForScrollbar || suppressSelectionList != selectedItems)
            return false;

        suppressSelectionForScrollbar = false;
        suppressSelectionList = null;

        if (suppressSelectionSnapshot != null)
            RestoreListSelection(selectedItems, suppressSelectionSnapshot);

        suppressSelectionSnapshot = null;
        return true;
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
            contextSelectionList = list;
        }
    }

    private static T? GetItemAtPoint(ListBox list, Point point)
    {
        IInputElement? element = list.InputHitTest(point) as IInputElement;
        Control? control = element as Control;
        ListBoxItem? item = control?.FindAncestorOfType<ListBoxItem>();
        return item?.DataContext as T;
    }

    private static bool IsPointerOverScrollBar(ListBox list, Point point)
    {
        IInputElement? element = list.InputHitTest(point) as IInputElement;
        if (element is not Control control)
            return false;

        if (control is ScrollBar)
            return true;

        return control.FindAncestorOfType<ScrollBar>() != null;
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

        menu!.Open(target);
        e.Handled = true;
        return true;
    }

    private static bool TryFindContextMenu(ListBox list, Point point, out ContextMenu? menu, out Control? target)
    {
        menu = null;
        target = null;

        IInputElement? element = list.InputHitTest(point) as IInputElement;
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
}
