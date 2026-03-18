using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ModHearth.UI;

public sealed record ModListDropContext(ListBox DestinationList, ListBox? SourceList, List<ModRefViewModel> Items, int InsertIndex);

public sealed class ModListDragDropController
{
    private const string DragDataKey = "ModHearth.ModRefs";
    private static readonly DataFormat<string> DragDataFormat =
        DataFormat.CreateStringApplicationFormat(DragDataKey);

    private readonly Window owner;
    private readonly Func<IEnumerable<ModRefViewModel>> allItemsProvider;
    private readonly Func<string, ModRefViewModel?> resolveItem;
    private readonly Func<ModRefViewModel, string> getItemKey;
    private readonly Dictionary<ListBox, bool> sortableLists = new();

    private Point? dragStartPoint;
    private ListBox? dragSourceList;
    private List<ModRefViewModel>? dragSelectionSnapshot;
    private ModRefViewModel? dragHitItem;
    private bool dragPreserveSelection;
    private List<ModRefViewModel>? dragHighlightedItems;
    private Cursor? dragCursor;
    private Cursor? previousCursor;

    private bool suppressSelectionHandling;
    private bool suppressSelectionForScrollbar;
    private List<ModRefViewModel>? suppressSelectionSnapshot;
    private ListBox? suppressSelectionList;
    private List<ModRefViewModel>? contextSelectionSnapshot;
    private ListBox? contextSelectionList;

    public event Action<ModListDropContext>? Dropped;

    public ModListDragDropController(
        Window owner,
        Func<IEnumerable<ModRefViewModel>> allItemsProvider,
        Func<string, ModRefViewModel?> resolveItem,
        Func<ModRefViewModel, string> getItemKey)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.allItemsProvider = allItemsProvider ?? throw new ArgumentNullException(nameof(allItemsProvider));
        this.resolveItem = resolveItem ?? throw new ArgumentNullException(nameof(resolveItem));
        this.getItemKey = getItemKey ?? throw new ArgumentNullException(nameof(getItemKey));
    }

    public void RegisterList(ListBox list, bool allowReorder, bool allowDrop = true)
    {
        if (list == null)
            throw new ArgumentNullException(nameof(list));

        sortableLists[list] = allowReorder;

        list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        list.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
        list.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);

        if (allowDrop)
        {
            DragDrop.SetAllowDrop(list, true);
            list.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            list.AddHandler(DragDrop.DropEvent, OnDrop);
            list.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        }
    }

    public bool HandleSelectionChanged(ListBox list)
    {
        if (suppressSelectionHandling)
            return true;

        return TryRestoreScrollbarSelection(list);
    }

    public void UpdateSelectionState(ListBox list)
    {
        if (list.ItemsSource is not IEnumerable<ModRefViewModel> items)
            return;

        IEnumerable<ModRefViewModel> selectedItems = list.SelectedItems?.Cast<ModRefViewModel>()
            ?? Enumerable.Empty<ModRefViewModel>();
        HashSet<ModRefViewModel> selected = new HashSet<ModRefViewModel>(selectedItems);
        foreach (ModRefViewModel vm in items)
            vm.IsSelected = selected.Contains(vm);
    }

    public void RestoreListSelection(ListBox list, IEnumerable<ModRefViewModel> selection)
    {
        if (list.SelectedItems == null)
            return;

        suppressSelectionHandling = true;
        list.SelectedItems.Clear();
        foreach (ModRefViewModel vm in selection)
            list.SelectedItems.Add(vm);
        UpdateSelectionState(list);
        suppressSelectionHandling = false;
    }

    public bool TryRestoreContextSelection(ListBox list, ModRefViewModel vm)
    {
        bool restored = false;
        if (contextSelectionSnapshot != null &&
            contextSelectionList == list &&
            contextSelectionSnapshot.Contains(vm))
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
        ResetDragState();
        ClearDragHighlight();
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

        if (!point.Properties.IsLeftButtonPressed)
            return;

        dragStartPoint = e.GetPosition(list);
        dragSourceList = list;
        dragHitItem = GetItemAtPoint(list, dragStartPoint.Value);

        bool hasModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!hasModifier && dragHitItem != null && list.SelectedItems?.Count > 1 && list.SelectedItems.Contains(dragHitItem))
        {
            dragPreserveSelection = true;
            dragSelectionSnapshot = list.SelectedItems.Cast<ModRefViewModel>().ToList();
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragStartPoint == null || dragSourceList == null)
            return;

        if (!e.GetCurrentPoint(dragSourceList).Properties.IsLeftButtonPressed)
            return;

        Point current = e.GetPosition(dragSourceList);
        if (Math.Abs(current.X - dragStartPoint.Value.X) < 4 && Math.Abs(current.Y - dragStartPoint.Value.Y) < 4)
            return;

        List<ModRefViewModel> selected = dragSourceList.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
        ModRefViewModel? hit = dragHitItem ?? GetItemAtPoint(dragSourceList, current);

        if (dragPreserveSelection && dragSelectionSnapshot != null && dragSelectionSnapshot.Count > 0)
        {
            selected = new List<ModRefViewModel>(dragSelectionSnapshot);
            RestoreListSelection(dragSourceList, dragSelectionSnapshot);
        }

        if (hit != null && selected.Count > 0 && !selected.Contains(hit))
        {
            dragSourceList.SelectedItems?.Clear();
            dragSourceList.SelectedItems?.Add(hit);
            selected = new List<ModRefViewModel> { hit };
        }
        else if (selected.Count == 0 && hit != null)
        {
            dragSourceList.SelectedItems?.Clear();
            dragSourceList.SelectedItems?.Add(hit);
            selected.Add(hit);
        }

        if (selected.Count == 0)
            return;

        selected = OrderSelectionByList(dragSourceList, selected);

        SetDragHighlight(selected);
        try
        {
            string payload = SerializeDragData(selected);
            DataTransfer data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragDataFormat, payload));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            ClearDragHighlight();
            ResetDragState();
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!suppressSelectionForScrollbar)
            return;

        suppressSelectionForScrollbar = false;
        suppressSelectionList = null;
        suppressSelectionSnapshot = null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox list)
            return;

        if (!e.DataTransfer.Contains(DragDataFormat))
            return;

        e.DragEffects = DragDropEffects.Move;
        Point pos = e.GetPosition(list);
        UpdateDropHighlight(list, pos);
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropHighlights();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox list)
            return;
        if (!e.DataTransfer.Contains(DragDataFormat))
            return;

        ClearDropHighlights();
        ClearDragHighlight();
        Point pos = e.GetPosition(list);
        int index = GetInsertIndex(list, pos);

        string? payload = e.DataTransfer.TryGetValue(DragDataFormat);
        if (string.IsNullOrWhiteSpace(payload))
        {
            ResetDragState();
            return;
        }

        List<ModRefViewModel> selected = DeserializeDragData(payload);
        if (selected.Count == 0)
        {
            ResetDragState();
            return;
        }

        if (dragSourceList == list && !IsSortable(list))
        {
            ResetDragState();
            return;
        }

        Dropped?.Invoke(new ModListDropContext(list, dragSourceList, selected, index));
        ResetDragState();
    }

    private bool IsSortable(ListBox list)
    {
        return sortableLists.TryGetValue(list, out bool sortable) && sortable;
    }

    private string SerializeDragData(IEnumerable<ModRefViewModel> mods)
    {
        List<string> keys = mods.Select(getItemKey).ToList();
        return JsonSerializer.Serialize(keys);
    }

    private List<ModRefViewModel> DeserializeDragData(string payload)
    {
        List<string>? keys = JsonSerializer.Deserialize<List<string>>(payload);
        if (keys == null || keys.Count == 0)
            return new List<ModRefViewModel>();

        List<ModRefViewModel> mods = new List<ModRefViewModel>();
        foreach (string key in keys)
        {
            ModRefViewModel? vm = resolveItem(key);
            if (vm != null)
                mods.Add(vm);
        }
        return mods;
    }

    private void UpdateDropHighlight(ListBox list, Point position)
    {
        ClearDropHighlights();
        if (list.ItemCount == 0)
            return;

        (int index, bool after) = GetDropTarget(list, position);
        if (list.ItemsSource is not IEnumerable<ModRefViewModel> items)
            return;

        List<ModRefViewModel> itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        if (index >= itemList.Count)
        {
            itemList[^1].ShowDropBelow = true;
            return;
        }

        ModRefViewModel target = itemList[index];
        if (after)
            target.ShowDropBelow = true;
        else
            target.ShowDropAbove = true;
    }

    private void ClearDropHighlights()
    {
        HashSet<ModRefViewModel> seen = new HashSet<ModRefViewModel>();
        foreach (ModRefViewModel vm in allItemsProvider())
        {
            if (!seen.Add(vm))
                continue;
            vm.ShowDropAbove = false;
            vm.ShowDropBelow = false;
        }
    }

    private void SetDragHighlight(List<ModRefViewModel> items)
    {
        ClearDragHighlight();
        dragHighlightedItems = items;
        foreach (ModRefViewModel vm in items)
            vm.IsDragging = true;
        SetDragCursor(true);
    }

    private void ClearDragHighlight()
    {
        if (dragHighlightedItems == null)
            return;

        foreach (ModRefViewModel vm in dragHighlightedItems)
            vm.IsDragging = false;
        dragHighlightedItems = null;
        SetDragCursor(false);
    }

    private void SetDragCursor(bool active)
    {
        if (active)
        {
            dragCursor ??= new Cursor(StandardCursorType.Hand);
            previousCursor ??= owner.Cursor;
            owner.Cursor = dragCursor;
        }
        else
        {
            owner.Cursor = previousCursor;
            previousCursor = null;
        }
    }

    private static ModRefViewModel? GetItemAtPoint(ListBox list, Point point)
    {
        IInputElement? element = list.InputHitTest(point) as IInputElement;
        Control? control = element as Control;
        ListBoxItem? item = control?.FindAncestorOfType<ListBoxItem>();
        return item?.DataContext as ModRefViewModel;
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

    private void CaptureScrollbarSelectionSnapshot(ListBox list, PointerPoint point)
    {
        if (!point.Properties.IsLeftButtonPressed)
            return;

        suppressSelectionForScrollbar = true;
        suppressSelectionList = list;
        suppressSelectionSnapshot = list.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
    }

    private bool TryRestoreScrollbarSelection(ListBox list)
    {
        if (!suppressSelectionForScrollbar || suppressSelectionList != list)
            return false;

        suppressSelectionForScrollbar = false;
        suppressSelectionList = null;

        if (suppressSelectionSnapshot != null)
        {
            RestoreListSelection(list, suppressSelectionSnapshot);
        }

        suppressSelectionSnapshot = null;
        return true;
    }

    private void CaptureContextSelectionSnapshot(ListBox list, PointerPoint point)
    {
        contextSelectionSnapshot = null;
        contextSelectionList = null;

        if (!point.Properties.IsRightButtonPressed)
            return;

        ModRefViewModel? hit = GetItemAtPoint(list, point.Position);
        if (hit == null)
            return;

        List<ModRefViewModel> selected = list.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
        if (selected.Count > 1 && selected.Contains(hit))
        {
            contextSelectionSnapshot = selected;
            contextSelectionList = list;
        }
    }

    private static List<ModRefViewModel> OrderSelectionByList(ListBox list, IEnumerable<ModRefViewModel> selection)
    {
        HashSet<ModRefViewModel> selectedSet = new HashSet<ModRefViewModel>(selection);
        if (list.ItemsSource is IEnumerable<ModRefViewModel> items)
            return items.Where(vm => selectedSet.Contains(vm)).ToList();

        return selection.ToList();
    }

    private static (int index, bool after) GetDropTarget(ListBox list, Point point)
    {
        for (int i = 0; i < list.ItemCount; i++)
        {
            if (list.ContainerFromIndex(i) is not Control container)
                continue;

            Point? topLeft = container.TranslatePoint(new Point(0, 0), list);
            if (topLeft == null)
                continue;

            double mid = topLeft.Value.Y + container.Bounds.Height / 2;
            if (point.Y <= mid)
                return (i, false);
        }

        return (list.ItemCount, true);
    }

    private static int GetInsertIndex(ListBox list, Point point)
    {
        (int index, bool after) = GetDropTarget(list, point);
        if (after && index < list.ItemCount)
            return index + 1;
        return index;
    }

    private void ResetDragState()
    {
        dragStartPoint = null;
        dragSourceList = null;
        dragSelectionSnapshot = null;
        dragHitItem = null;
        dragPreserveSelection = false;
    }
}
