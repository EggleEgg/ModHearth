using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModHearth.UI;

public sealed class ListSelectionController<T> where T : class, ISelectableItem
{
    private bool suppressSelectionHandling;
    private bool suppressSelectionForScrollbar;
    private List<T>? suppressSelectionSnapshot;
    private ListBox? suppressSelectionList;
    private List<T>? contextSelectionSnapshot;
    private ListBox? contextSelectionList;

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

    public void UpdateSelectionState(ListBox list)
    {
        if (list.ItemsSource is not IEnumerable<T> items)
            return;

        IEnumerable<T> selectedItems = list.SelectedItems?.Cast<T>() ?? Enumerable.Empty<T>();
        HashSet<T> selected = new HashSet<T>(selectedItems);
        foreach (T item in items)
            item.IsSelected = selected.Contains(item);
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

    public bool TryRestoreContextSelection(ListBox list, T item)
    {
        bool restored = false;
        if (contextSelectionSnapshot != null &&
            contextSelectionList == list &&
            contextSelectionSnapshot.Contains(item))
        {
            RestoreListSelection(list, contextSelectionSnapshot);
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

    private bool TryRestoreScrollbarSelection(ListBox list)
    {
        if (!suppressSelectionForScrollbar || suppressSelectionList != list)
            return false;

        suppressSelectionForScrollbar = false;
        suppressSelectionList = null;

        if (suppressSelectionSnapshot != null)
            RestoreListSelection(list, suppressSelectionSnapshot);

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

        TryRestoreContextSelection(list, hit);

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
