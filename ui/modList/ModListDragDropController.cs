using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Text.Json;

namespace ModHearth.UI;

public sealed record ModListDropContext(
    ListBox DestinationList,
    ListBox? SourceList,
    List<ModRefViewModel> Items,
    int InsertIndex,
    bool DropAfter,
    bool GapDrop,
    KeyModifiers Modifiers);

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
    private PointerPressedEventArgs? dragTriggerEvent;
    private ListBox? dragSourceList;
    private List<ModRefViewModel>? dragSelectionSnapshot;
    private ModRefViewModel? dragHitItem;
    private bool dragPreserveSelection;
    private List<ModRefViewModel>? dragHighlightedItems;
    private Cursor? dragCursor;
    private Cursor? previousCursor;
    private readonly ListSelectionController<ModRefViewModel> selectionController = new();
    private ModRefViewModel? lastHighlightedItem;

    private bool isDragging;
    private ListBox? currentDragOverList;
    private Point? currentDragOverPosition;

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
        selectionController.RegisterList(list);

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
        return selectionController.HandleSelectionChanged(list);
    }

    public void UpdateSelectionState(ListBox list)
    {
        selectionController.UpdateSelectionState(list);
    }

    public void RestoreListSelection(ListBox list, IEnumerable<ModRefViewModel> selection)
    {
        selectionController.RestoreListSelection(list, selection);
    }

    public bool TryRestoreContextSelection(ListBox list, ModRefViewModel vm)
    {
        return selectionController.TryRestoreContextSelection(list, vm);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        dragTriggerEvent = e;
        dragStartPoint = e.GetPosition(sender as Control);
        ResetDragState();
        ClearDragHighlight();

        if (sender is not ListBox list)
            return;

        PointerPoint point = e.GetCurrentPoint(list);
        if (IsPointerOverScrollBar(list, point.Position))
            return;

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
        if (dragStartPoint == null || dragSourceList == null || dragTriggerEvent == null)
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
            _ = (dragSourceList.SelectedItems?.Add(hit));
            selected = new List<ModRefViewModel> { hit };
        }
        else if (selected.Count == 0 && hit != null)
        {
            dragSourceList.SelectedItems?.Clear();
            _ = (dragSourceList.SelectedItems?.Add(hit));
            selected.Add(hit);
        }

        if (selected.Count == 0)
            return;

        selected = OrderSelectionByList(dragSourceList, selected);

        SetDragHighlight(selected);
        // Set the flag and spin up the background loop thread
        isDragging = true;
        await StartBackgroundScrollLoop();
        try
        {
            string payload = SerializeDragData(selected);
            DataTransfer data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragDataFormat, payload));

            // The native OS blocking loop runs here
            _ = await DragDrop.DoDragDropAsync(dragTriggerEvent, data, DragDropEffects.Move);
        }
        finally
        {
            // Flipping this to false immediately halts the background thread loop
            dragTriggerEvent = null;
            isDragging = false;
            ClearDragHighlight();
            ResetDragState();
        }
    }
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        dragTriggerEvent = null;
        ResetDragState();
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

        currentDragOverList = list;
        currentDragOverPosition = pos;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropHighlights();
        if (sender is ListBox list && currentDragOverList == list)
        {
            currentDragOverList = null;
            currentDragOverPosition = null;
        }
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
        (int targetIndex, bool after, bool gapDrop) = GetDropTarget(list, pos);
        int index = after && targetIndex < list.ItemCount ? targetIndex + 1 : targetIndex;

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

        Dropped?.Invoke(new ModListDropContext(list, dragSourceList, selected, index, after, gapDrop, e.KeyModifiers));
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
        if (list.ItemCount == 0)
        {
            ClearDropHighlights();
            return;
        }

        (int index, bool after, _) = GetDropTarget(list, position);
        if (list.ItemsSource is not IEnumerable<ModRefViewModel> items)
        {
            ClearDropHighlights();
            return;
        }

        IReadOnlyList<ModRefViewModel> itemList = items as IReadOnlyList<ModRefViewModel> ?? items.ToList();
        if (itemList.Count == 0)
        {
            ClearDropHighlights();
            return;
        }

        ModRefViewModel targetItem;
        bool showBelow;

        // 1. NORMALIZE INDEX: Treat "Below Item N" as "Above Item N+1".
        // This stops the coordinate calculation from flip-flopping at row seams.
        if (index >= itemList.Count)
        {
            targetItem = itemList[^1];
            showBelow = true;
        }
        else if (after)
        {
            if (index + 1 < itemList.Count)
            {
                targetItem = itemList[index + 1];
                showBelow = false;
            }
            else
            {
                targetItem = itemList[index];
                showBelow = true;
            }
        }
        else
        {
            targetItem = itemList[index];
            showBelow = false;
        }

        // 2. GUARD CLAUSE: If the highlight target and position are identical to 
        // the previous frame, do nothing. This prevents constant redraw cycles.
        if (lastHighlightedItem == targetItem &&
            ((showBelow && targetItem.ShowDropBelow) || (!showBelow && targetItem.ShowDropAbove)))
        {
            return;
        }

        // 3. STATE CHANGED: Safely wipe out old flags and apply the new ones
        ClearDropHighlights();

        if (showBelow)
            targetItem.ShowDropBelow = true;
        else
            targetItem.ShowDropAbove = true;

        lastHighlightedItem = targetItem;
    }

    private void ClearDropHighlights()
    {
        if (lastHighlightedItem != null)
        {
            lastHighlightedItem.ShowDropAbove = false;
            lastHighlightedItem.ShowDropBelow = false;
            lastHighlightedItem = null;
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

        switch (control)
        {
            case ScrollBar:
                return true;
            default:
                return control.FindAncestorOfType<ScrollBar>() != null;
        }
    }

    private static List<ModRefViewModel> OrderSelectionByList(ListBox list, IEnumerable<ModRefViewModel> selection)
    {
        HashSet<ModRefViewModel> selectedSet = new HashSet<ModRefViewModel>(selection);
        switch (list.ItemsSource)
        {
            case IEnumerable<ModRefViewModel> items:
                return items.Where(selectedSet.Contains).ToList();
            default:
                return selection.ToList();
        }
    }

    private static (int index, bool after, bool gapDrop) GetDropTarget(ListBox list, Point point)
    {
        Control? lastContainer = null;
        double lastTop = 0;
        double lastBottom = 0;
        double lastHeight = 0;

        for (int i = 0; i < list.ItemCount; i++)
        {
            if (list.ContainerFromIndex(i) is not Control container)
                continue;

            Point? topLeft = container.TranslatePoint(new Point(0, 0), list);
            if (topLeft == null)
                continue;

            double top = topLeft.Value.Y;
            double height = container.Bounds.Height;
            double bottom = top + height;

            lastContainer = container;
            lastTop = top;
            lastBottom = bottom;
            lastHeight = height;

            double mid = top + height / 2;
            if (point.Y <= mid)
                return (i, false, IsGapDrop(point.Y, top, bottom, height, after: false));

            if (point.Y <= bottom)
                return (i, true, IsGapDrop(point.Y, top, bottom, height, after: true));
        }

        if (lastContainer != null)
            return (list.ItemCount, true, IsGapDrop(point.Y, lastTop, lastBottom, lastHeight, after: true));

        return (list.ItemCount, true, false);
    }

    private static bool IsGapDrop(double y, double top, double bottom, double height, bool after)
    {
        if (height <= 0)
            return false;

        double gapZone = Math.Max(0, height * 0.02);
        if (after)
            return (bottom - y) <= gapZone;
        return (y - top) <= gapZone;
    }

    private void ResetDragState()
    {
        dragStartPoint = null;
        dragSourceList = null;
        dragSelectionSnapshot = null;
        dragHitItem = null;
        dragPreserveSelection = false;
    }

    private async Task StartBackgroundScrollLoop()
    {
        while (isDragging)
        {
            // Poll every 50ms (matching your original timer resolution)
            await Task.Delay(50);

            // Early escape if the drag ended or the cursor left a valid target list
            if (!isDragging || currentDragOverList == null || currentDragOverPosition == null)
                continue;

            // Capture references safely for the UI thread callback
            var list = currentDragOverList;

            // Post the work to the UI thread
            Dispatcher.UIThread.Post(() =>
            {
                // Verify state hasn't drifted while waiting for the dispatch frame
                if (!isDragging || currentDragOverList != list || currentDragOverPosition == null)
                    return;

                ScrollViewer? scrollViewer = list.FindDescendantOfType<ScrollViewer>()
                    ?? list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

                if (scrollViewer == null) return;

                Point pos = currentDragOverPosition.Value;

                //TODO Make these configurable in the future
                double scrollThreshold = 60.0; // Increased zone width to give users room to adjust speed
                double minSpeed = 6.0;         // Minimum crawl speed right at the boundary edge
                double maxSpeed = 100.0;       // Maximum cap when pulled far past the boundary

                double listHeight = list.Bounds.Height;
                double deltaY = 0;

                if (pos.Y < scrollThreshold)
                {
                    // Distance past the top scroll trigger (larger = further into/above the zone)
                    double distanceIntoZone = scrollThreshold - pos.Y;

                    // Normalize ratio [0.0 to 2.0] (allows scaling up if cursor goes above list bounds)
                    double intensity = Math.Clamp(distanceIntoZone / scrollThreshold, 0.0, 2.0);

                    // Math.Pow(..., 1.5) provides a smooth, progressive acceleration curve
                    double speed = minSpeed + (maxSpeed - minSpeed) * Math.Pow(intensity, 1.5);

                    deltaY = -speed;
                }
                else if (pos.Y > (listHeight - scrollThreshold))
                {
                    // Distance past the bottom scroll trigger
                    double distanceIntoZone = pos.Y - (listHeight - scrollThreshold);

                    double intensity = Math.Clamp(distanceIntoZone / scrollThreshold, 0.0, 2.0);
                    double speed = minSpeed + (maxSpeed - minSpeed) * Math.Pow(intensity, 1.5);

                    deltaY = speed;
                }

                // Apply scroll delta if active
                if (Math.Abs(deltaY) > 0.01)
                {
                    double oldOffset = scrollViewer.Offset.Y;
                    double maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                    double newOffset = Math.Clamp(oldOffset + deltaY, 0, maxOffset);

                    if (Math.Abs(newOffset - oldOffset) > 0.01)
                    {
                        scrollViewer.Offset = scrollViewer.Offset.WithY(newOffset);
                        UpdateDropHighlight(list, pos);
                    }
                }
            }, DispatcherPriority.Input);
        }
    }
}
