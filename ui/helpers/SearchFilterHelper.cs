using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using ModHearth.Models;

namespace ModHearth.UI;

public static class SearchFilterHelper
{
    public static List<ModRefViewModel> ApplyFilterAndSort(
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        bool sortDescending,
        bool isSortingEnabled = true)
    {
        string trimmed = filter?.Trim() ?? string.Empty;
        bool hasFilter = !string.IsNullOrWhiteSpace(trimmed);

        // Visit every item once to set IsFilteredOut and IsVisible
        List<ModRefViewModel> items = source is List<ModRefViewModel> list ? list : source.ToList();
        foreach (var vm in items)
        {
            bool match = !hasFilter || vm.MatchesFilter(trimmed, searchMode);
            vm.IsFilteredOut = hasFilter && !match;
            vm.IsVisible = !hideFiltered || match;
        }

        // Get the surviving subset (visible items)
        IEnumerable<ModRefViewModel> visibleItems = items.Where(vm => vm.IsVisible);

        // Sort only the surviving subset
        return (!isSortingEnabled)
            ? visibleItems.ToList()
            : (searchMode, sortDescending) switch
            {
                (SearchFilterMode.ModifiedTime, true) => visibleItems.OrderBy(vm => vm.LastModifiedTime ?? DateTime.MinValue).ToList(),
                (SearchFilterMode.ModifiedTime, false) => visibleItems.OrderByDescending(vm => vm.LastModifiedTime ?? DateTime.MinValue).ToList(),
                (_, true) => visibleItems.OrderByDescending(vm => vm.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
                (_, false) => visibleItems.OrderBy(vm => vm.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
            };
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
        List<ModRefViewModel> retained = list.SelectedItems
            .OfType<ModRefViewModel>()
            .Where(visibleSet.Contains)
            .ToList();

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
            isSortingEnabled);

        ReplaceCollection(targetCollection, displayItems);

        logAction?.Invoke($"filter='{trimmed}' mode={searchMode} hideFiltered={hideFiltered} sortDescending={sortDescending} total={source.Count()} visible={displayItems.Count}");

        DropNonDisplayedSelections(list, displayItems, modListController);
    }
}
