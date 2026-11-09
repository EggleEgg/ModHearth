using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModHearth.UI.ViewModels;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI.ModList
{
    public sealed record ModListDropContext(
        ListBox DestinationList,
        ListBox? SourceList,
        List<ModRefViewModel> Items,
        int InsertIndex);

    public sealed class ModListDragDropController
    {
        private const string DragDataKey = "ModHearth.ModRefs";
        private const string InternalDragPayload = "internal";
        private const double DragStartDistance = 4;
        private const double AutoScrollThreshold = 60;
        private const double AutoScrollMinimumPixelsPerSecond = 120;
        private const double AutoScrollMaximumPixelsPerSecond = 2000;
        private static readonly DataFormat<string> DragDataFormat = DataFormat.CreateStringApplicationFormat(DragDataKey);
        private readonly Window owner;
        private readonly Dictionary<ListBox, bool> sortableLists = [];
        private readonly DispatcherTimer autoScrollTimer;

        private Point? dragStartPoint;
        private PointerPressedEventArgs? dragTriggerEvent;
        private ListBox? dragSourceList;
        private List<ModRefViewModel>? dragSelectionSnapshot;
        private List<ModRefViewModel>? activeDragItems;
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
        private DateTime lastDragOverTime = DateTime.MinValue;
        private Point lastDragOverPos;
        private ListBox? cachedScrollViewerOwner;
        private ScrollViewer? cachedScrollViewer;
        private DateTime lastAutoScrollTickUtc;
        private long dragPointerPressedTimestamp;

        public event Func<ModListDropContext, Task>? Dropped;

        public ModListDragDropController(Window owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

            autoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            autoScrollTimer.Tick += (_, _) => AutoScrollTick();
        }

        public void RegisterList(ListBox list, bool allowReorder, bool allowDrop = true)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            sortableLists[list] = allowReorder;

            list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
            list.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
            list.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
            selectionController.RegisterList(list);

            if (allowDrop)
            {
                DragDrop.SetAllowDrop(list, true);
                list.AddHandler(DragDrop.DragOverEvent, OnDragOver);
                list.AddHandler(DragDrop.DropEvent, OnDrop);
                list.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            }
        }

        public bool HandleSelectionChanged(ListBox list) => selectionController.HandleSelectionChanged(list);

        public void UpdateSelectionState(ListBox list) => selectionController.UpdateSelectionState(list);

        public void RestoreListSelection(ListBox list, IEnumerable<ModRefViewModel> selection) => selectionController.RestoreListSelection(list, selection);

        public bool TryRestoreContextSelection(ListBox list, ModRefViewModel vm) => selectionController.TryRestoreContextSelection(list, vm);

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            StopAutoScroll();
            dragTriggerEvent = e;
            dragStartPoint = e.GetPosition(sender as Control);
            ResetDragState();
            ClearDragHighlight();

            if (e.ClickCount > 1)
                return;

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
                dragSelectionSnapshot = [.. list.SelectedItems.Cast<ModRefViewModel>()];
            }

            // Start as soon as the pointer crosses the normal drag distance. The old fixed hold
            // timer delayed quick, deliberate multi-item drags even after the user had moved.
            dragPointerPressedTimestamp = Stopwatch.GetTimestamp();
        }

        private async void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (isDragging || dragStartPoint == null || dragSourceList == null || dragTriggerEvent == null)
                return;

            if (!e.GetCurrentPoint(dragSourceList).Properties.IsLeftButtonPressed)
                return;

            Point current = e.GetPosition(dragSourceList);
            if (Math.Abs(current.X - dragStartPoint.Value.X) < DragStartDistance &&
                Math.Abs(current.Y - dragStartPoint.Value.Y) < DragStartDistance)
            {
                return;
            }

            List<ModRefViewModel> selected = dragSourceList.SelectedItems?.Cast<ModRefViewModel>().ToList()
                ?? [];
            ModRefViewModel? hit = dragHitItem ?? GetItemAtPoint(dragSourceList, current);

            if (dragPreserveSelection && dragSelectionSnapshot != null && dragSelectionSnapshot.Count > 0)
            {
                selected = [.. dragSelectionSnapshot];
                RestoreListSelection(dragSourceList, dragSelectionSnapshot);
            }

            if (hit != null && selected.Count > 0 && !selected.Contains(hit))
            {
                dragSourceList.SelectedItems?.Clear();
                _ = (dragSourceList.SelectedItems?.Add(hit));
                selected = [hit];
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

            activeDragItems = selected;
            SetDragHighlight(dragSourceList, selected);
            isDragging = true;
            StartAutoScroll();
            try
            {
                DataTransfer data = new();
                // Retain the typed selection for this in-process drag. The transfer item merely
                // identifies it as ours, avoiding JSON work proportional to the selection size.
                data.Add(DataTransferItem.Create(DragDataFormat, InternalDragPayload));

                Loggers.Drag.Dev($"Started {selected.Count} item drag after {Stopwatch.GetElapsedTime(dragPointerPressedTimestamp).TotalMilliseconds:F1} ms.");

                // The native OS blocking loop runs here
                _ = await DragDrop.DoDragDropAsync(dragTriggerEvent, data, DragDropEffects.Move);
            }
            finally
            {
                // Flipping this to false immediately halts the background thread loop
                dragTriggerEvent = null;
                isDragging = false;
                StopAutoScroll();
                ClearDragHighlight();
                ResetDragState();
            }
        }
        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            // During a native drag, release is part of the drop sequence. Keep the in-process
            // selection alive until DoDragDropAsync completes and its finally block cleans up.
            if (isDragging)
                return;

            StopAutoScroll();
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

            if ((DateTime.UtcNow - lastDragOverTime).TotalMilliseconds < 40 && Math.Abs(pos.X - lastDragOverPos.X) < 3 && Math.Abs(pos.Y - lastDragOverPos.Y) < 3)
            {
                currentDragOverList = list;
                currentDragOverPosition = pos;
                return;
            }
            lastDragOverTime = DateTime.UtcNow;
            lastDragOverPos = pos;

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

        private async void OnDrop(object? sender, DragEventArgs e)
        {
            if (sender is not ListBox list)
                return;
            if (!e.DataTransfer.Contains(DragDataFormat))
                return;

            ClearDropHighlights();
            ClearDragHighlight();
            Point pos = e.GetPosition(list);
            (int targetIndex, bool after) = GetDropTarget(list, pos);
            int index = after && targetIndex < list.ItemCount ? targetIndex + 1 : targetIndex;

            // The controller owns the typed selection for the duration of an in-process drag.
            if (activeDragItems is not { Count: > 0 })
            {
                ResetDragState();
                return;
            }

            List<ModRefViewModel> selected = [.. activeDragItems];

            if (dragSourceList == list && !IsSortable(list))
            {
                ResetDragState();
                return;
            }

            try
            {
                if (Dropped != null)
                {
                    await Dropped(new ModListDropContext(list, dragSourceList, selected, index));
                }
            }
            catch (Exception ex)
            {
                Loggers.Drag.LogError($"Error handling mod list drop: {ex}");
            }
            finally
            {
                ResetDragState();
            }
        }

        private bool IsSortable(ListBox list) => sortableLists.TryGetValue(list, out bool sortable) && sortable;

        private void UpdateDropHighlight(ListBox list, Point position)
        {
            if (list.ItemCount == 0)
            {
                ClearDropHighlights();
                return;
            }

            (int index, bool after) = GetDropTarget(list, position);
            if (list.ItemsSource is not IEnumerable<ModRefViewModel> items)
            {
                ClearDropHighlights();
                return;
            }

            if (items is not IReadOnlyList<ModRefViewModel> itemList || itemList.Count == 0)
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

        private void SetDragHighlight(ListBox sourceList, List<ModRefViewModel> items)
        {
            ClearDragHighlight();
            HashSet<ModRefViewModel> selected = [.. items];

            // Only realized rows can be visible. Updating every selected item used to cause an
            // expensive background refresh and property notification for off-screen rows.
            dragHighlightedItems = [.. sourceList.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Select(item => item.DataContext as ModRefViewModel)
                .Where(vm => vm != null && selected.Contains(vm))
                .Cast<ModRefViewModel>()];

            foreach (ModRefViewModel vm in dragHighlightedItems)
                vm.IsDragging = true;
            if (dragHighlightedItems.Count > 0)
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
            _ = TryGetContainerAtPoint(list, point, out ListBoxItem? item);
            return item?.DataContext as ModRefViewModel;
        }

        private static bool TryGetContainerAtPoint(ListBox list, Point point, out ListBoxItem? item)
        {
            IInputElement? element = list.InputHitTest(point);
            item = (element as Control)?.FindAncestorOfType<ListBoxItem>();
            return item != null;
        }

        private static bool IsPointerOverScrollBar(ListBox list, Point point)
            => list.InputHitTest(point) is Control control
                && (control is ScrollBar || control.FindAncestorOfType<ScrollBar>() != null);

        private static List<ModRefViewModel> OrderSelectionByList(ListBox list, IEnumerable<ModRefViewModel> selection)
        {
            HashSet<ModRefViewModel> selectedSet = [.. selection];
            return list.ItemsSource is IEnumerable<ModRefViewModel> items
                ? [.. items.Where(selectedSet.Contains)]
                : [.. selection];
        }

        private static (int index, bool after) GetDropTarget(ListBox list, Point point)
        {
            if (TryGetContainerAtPoint(list, point, out ListBoxItem? hitItem) && hitItem != null)
            {
                int hitIndex = list.IndexFromContainer(hitItem);
                Point? topLeft = hitItem.TranslatePoint(new Point(0, 0), list);
                if (hitIndex >= 0 && topLeft != null)
                {
                    double top = topLeft.Value.Y;
                    double height = hitItem.Bounds.Height;
                    bool after = point.Y > top + (height / 2);
                    return (hitIndex, after);
                }
            }

            int nearestIndex = -1;
            double nearestTop = 0;
            double nearestHeight = 0;
            double nearestDistance = double.PositiveInfinity;

            foreach (ListBoxItem item in list.GetVisualDescendants().OfType<ListBoxItem>())
            {
                int index = list.IndexFromContainer(item);
                if (index < 0)
                    continue;

                Point? topLeft = item.TranslatePoint(new Point(0, 0), list);
                if (topLeft == null)
                    continue;

                double top = topLeft.Value.Y;
                double height = item.Bounds.Height;
                double bottom = top + height;
                double distance;
                if (point.Y < top)
                {
                    distance = top - point.Y;
                }
                else
                {
                    distance = point.Y > bottom ? point.Y - bottom : 0;
                }

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                    nearestTop = top;
                    nearestHeight = height;
                }
            }

            if (nearestIndex < 0)
                return (list.ItemCount, true);

            bool nearestAfter = point.Y > nearestTop + (nearestHeight / 2);
            return (nearestIndex, nearestAfter);
        }

        private void ResetDragState()
        {
            dragStartPoint = null;
            dragSourceList = null;
            dragSelectionSnapshot = null;
            activeDragItems = null;
            dragHitItem = null;
            dragPreserveSelection = false;
        }

        private ScrollViewer? GetScrollViewerFor(ListBox list)
        {
            if (!ReferenceEquals(cachedScrollViewerOwner, list))
            {
                cachedScrollViewerOwner = list;
                cachedScrollViewer = list.FindDescendantOfType<ScrollViewer>()
                    ?? list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            }
            return cachedScrollViewer;
        }

        private void StartAutoScroll()
        {
            lastAutoScrollTickUtc = DateTime.UtcNow;
            autoScrollTimer.Start();
        }

        private void StopAutoScroll()
        {
            autoScrollTimer.Stop();
            lastAutoScrollTickUtc = default;
        }

        private void AutoScrollTick()
        {
            if (!isDragging || currentDragOverList == null || currentDragOverPosition == null)
                return;

            DateTime now = DateTime.UtcNow;
            double elapsedSeconds = Math.Max(0, (now - lastAutoScrollTickUtc).TotalSeconds);
            lastAutoScrollTickUtc = now;

            ListBox list = currentDragOverList;
            ScrollViewer? scrollViewer = GetScrollViewerFor(list);
            if (scrollViewer == null)
                return;

            Point position = currentDragOverPosition.Value;
            double deltaY = 0;
            if (position.Y < AutoScrollThreshold)
            {
                double intensity = Math.Clamp((AutoScrollThreshold - position.Y) / AutoScrollThreshold, 0, 2);
                deltaY = -GetAutoScrollSpeed(intensity) * elapsedSeconds;
            }
            else if (position.Y > list.Bounds.Height - AutoScrollThreshold)
            {
                double intensity = Math.Clamp((position.Y - (list.Bounds.Height - AutoScrollThreshold)) / AutoScrollThreshold, 0, 2);
                deltaY = GetAutoScrollSpeed(intensity) * elapsedSeconds;
            }

            if (Math.Abs(deltaY) <= 0.01)
                return;

            double oldOffset = scrollViewer.Offset.Y;
            double maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            double newOffset = Math.Clamp(oldOffset + deltaY, 0, maxOffset);
            if (Math.Abs(newOffset - oldOffset) <= 0.01)
                return;

            scrollViewer.Offset = scrollViewer.Offset.WithY(newOffset);
            UpdateDropHighlight(list, position);
        }

        private static double GetAutoScrollSpeed(double intensity) => AutoScrollMinimumPixelsPerSecond +
                   ((AutoScrollMaximumPixelsPerSecond - AutoScrollMinimumPixelsPerSecond) * Math.Pow(intensity, 1.5));
    }
}
