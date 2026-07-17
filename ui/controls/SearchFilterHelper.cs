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

        // handles sorting
        IEnumerable<ModRefViewModel> sorted = (!isSortingEnabled)
            ? source
            : (searchMode, sortDescending) switch
            {
                (SearchFilterMode.ModifiedTime, true) => source.OrderByDescending(vm => vm.LastModifiedTime ?? DateTime.MinValue),
                (SearchFilterMode.ModifiedTime, false) => source.OrderBy(vm => vm.LastModifiedTime ?? DateTime.MinValue),
                (_, true) => source.OrderByDescending(vm => vm.DisplayName, StringComparer.OrdinalIgnoreCase),
                (_, false) => source.OrderBy(vm => vm.DisplayName, StringComparer.OrdinalIgnoreCase)
            };

        // handles filtering
        return sorted.Where(vm =>
        {
            bool match = !hasFilter || vm.MatchesFilter(trimmed, searchMode);
            vm.IsFilteredOut = hasFilter && !match;
            vm.IsVisible = !hideFiltered || match;
            return vm.IsVisible;
        }).ToList();
    }

    public static void ReplaceCollection(ObservableCollection<ModRefViewModel> target, List<ModRefViewModel> items)
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
        foreach (ModRefViewModel vm in items)
        {
            target.Add(vm);
        }
    }

    public static void DropNonDisplayedSelections(
        ListBox list,
        IReadOnlyCollection<ModRefViewModel> displayItems,
        ModListDragDropController modListController)
    {
        if (list.SelectedItems == null || list.SelectedItems.Count == 0)
            return;

        HashSet<ModRefViewModel> visibleSet = new HashSet<ModRefViewModel>(displayItems);
        List<ModRefViewModel> retained = list.SelectedItems
            .OfType<ModRefViewModel>()
            .Where(visibleSet.Contains)
            .ToList();

        if (retained.Count == list.SelectedItems.Count)
            return;

        list.SelectedItems.Clear();
        foreach (ModRefViewModel vm in retained)
        {
            list.SelectedItems.Add(vm);
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
