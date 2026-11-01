using System.Collections.ObjectModel;
using Avalonia.Controls;

namespace ModHearth.UI;

public static class SearchFilterHelper
{
    public static List<ModRefViewModel> ApplyFilterAndSort(
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        bool sortDescending,
        bool isSortingEnabled,
        out int totalCount)
    {
        // Build the filter once instead of re-compiling regexes and re-splitting color lists per item.
        SearchFilterContext context = new(filter, searchMode);

        List<ModRefViewModel> items = source is List<ModRefViewModel> list ? list : [.. source];
        totalCount = items.Count;

        // Single pass: update flags and collect survivors in the same loop.
        List<ModRefViewModel> visibleItems = new(items.Count);
        foreach (ModRefViewModel vm in items)
        {
            bool match = !context.HasFilter || vm.MatchesFilter(context);
            vm.IsFilteredOut = context.HasFilter && !match;
            vm.IsVisible = !hideFiltered || match;
            if (vm.IsVisible)
                visibleItems.Add(vm);
        }

        // Sort the surviving subset in place to avoid the intermediate buffers that LINQ's OrderBy allocates.
        if (isSortingEnabled)
        {
            IComparer<ModRefViewModel> comparer = new ModRefComparer(searchMode, sortDescending);
            visibleItems.Sort(comparer);
        }

        return visibleItems;
    }

    public static List<ModRefViewModel> ApplyFilterAndSort(
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        bool sortDescending,
        bool isSortingEnabled = true)
        => ApplyFilterAndSort(source, filter, searchMode, hideFiltered, sortDescending, isSortingEnabled, out _);

    /// <summary>
    /// Orders visible mods. The tie-breakers keep the order deterministic even though
    /// <see cref="List{T}.Sort(IComparer{T})"/> is not stable.
    /// </summary>
    private sealed class ModRefComparer(SearchFilterMode mode, bool sortDescending) : IComparer<ModRefViewModel>
    {
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        public int Compare(ModRefViewModel? x, ModRefViewModel? y)
        {
            if (mode == SearchFilterMode.ModifiedTime)
            {
                DateTime left = x?.LastModifiedTime ?? DateTime.MinValue;
                DateTime right = y?.LastModifiedTime ?? DateTime.MinValue;

                // Preserve the historical (inverted) semantics: descending=true sorts oldest first.
                int timeResult = sortDescending ? left.CompareTo(right) : right.CompareTo(left);
                if (timeResult != 0)
                    return timeResult;
            }
            else
            {
                int nameResult = NameComparer.Compare(x?.DisplayName, y?.DisplayName);
                if (nameResult != 0)
                    return sortDescending ? -nameResult : nameResult;
            }

            int displayNameResult = NameComparer.Compare(x?.DisplayName, y?.DisplayName);
            return displayNameResult != 0
                ? displayNameResult
                : NameComparer.Compare(x?.ModReference.ID, y?.ModReference.ID);
        }
    }

    public static void ReplaceCollection<T>(ObservableCollection<T> target, List<T> items)
    {
        if (target.Count == items.Count)
        {
            bool same = true;
            for (int i = 0; i < target.Count; i++)
            {
                if (!ReferenceEquals(target[i], items[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same) return;
        }

        if (target is BulkObservableCollection<T> bulk)
        {
            // One Reset notification instead of Clear() + N Add() notifications.
            bulk.ReplaceAll(items);
            return;
        }

        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }

    public static void DropNonDisplayedSelections(
        ListBox list,
        IReadOnlyCollection<ModRefViewModel> displayItems,
        ModListDragDropController modListController)
    {
        if (list.SelectedItems == null || list.SelectedItems.Count == 0)
            return;

        HashSet<ModRefViewModel> visibleSet = [.. displayItems];
        List<ModRefViewModel> retained = [.. list.SelectedItems
            .OfType<ModRefViewModel>()
            .Where(visibleSet.Contains)];

        if (retained.Count == list.SelectedItems.Count)
            return;

        list.SelectedItems.Clear();
        foreach (ModRefViewModel vm in retained)
        {
            _ = list.SelectedItems.Add(vm);
        }

        modListController.UpdateSelectionState(list);
    }

    public static void ApplyFilterFlags(
        ObservableCollection<ModRefViewModel> targetCollection,
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        bool sortDescending,
        ListBox list,
        ModListDragDropController modListController,
        bool isSortingEnabled = true,
        Action<string>? logAction = null)
    {
        string trimmed = filter?.Trim() ?? string.Empty;

        List<ModRefViewModel> displayItems = ApplyFilterAndSort(
            source,
            trimmed,
            searchMode,
            hideFiltered,
            sortDescending,
            isSortingEnabled,
            out int totalCount);

        ReplaceCollection(targetCollection, displayItems);

        logAction?.Invoke($"filter='{trimmed}' mode={searchMode} hideFiltered={hideFiltered} sortDescending={sortDescending} total={totalCount} visible={displayItems.Count}");

        DropNonDisplayedSelections(list, displayItems, modListController);
    }
}
