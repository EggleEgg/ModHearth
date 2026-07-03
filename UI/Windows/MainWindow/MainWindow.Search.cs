using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void ApplySearchFilter()
    {
        string leftFilter = leftSearchBar.Text.Trim();
        string rightFilter = rightSearchBar.Text.Trim();
        SearchFilterMode leftMode = leftSearchBar.SearchMode;
        SearchFilterMode rightMode = rightSearchBar.SearchMode;
        SearchLogging.Log(
            $"ApplySearchFilter start left='{TrimForLog(leftFilter)}' leftMode={DescribeSearchMode(leftMode)} " +
            $"right='{TrimForLog(rightFilter)}' rightMode={DescribeSearchMode(rightMode)} leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");
        bool ensureVisible = ensureSearchResultVisibleOnNextFilter;

        isApplyingSearchFilter = true;
        try
        {
            ApplyFilterFlags(
                inactiveMods,
                manager.disabledMods,
                leftFilter,
                leftMode,
                leftSearchBar.HideFiltered,
                leftModlist);
            ApplyFilterFlags(
                activeMods,
                manager.enabledMods,
                rightFilter,
                rightMode,
                rightSearchBar.HideFiltered,
                rightModlist,
                preserveSourceOrder: true);
        }
        finally
        {
            isApplyingSearchFilter = false;
        }

        if (ensureVisible)
        {
            EnsureFirstVisibleSearchResultInView(leftModlist, inactiveMods, leftFilter, leftSearchBar.HideFiltered);
            EnsureFirstVisibleSearchResultInView(rightModlist, activeMods, rightFilter, rightSearchBar.HideFiltered);
            ensureSearchResultVisibleOnNextFilter = false;
            SearchLogging.Log("ApplySearchFilter ensureSearchResultVisibleOnNextFilter consumed");
        }

        SearchLogging.Log("ApplySearchFilter end");
        LogVisualFilterState("ApplySearchFilter");
    }

    private void OnSearchInputChanged(object? sender, EventArgs e)
    {
        if (suppressSearchInputEvents)
        {
            SearchLogging.Log($"OnSearchInputChanged suppressed source={DescribeSearchSender(sender)}");
            return;
        }

        SearchLogging.Log($"OnSearchInputChanged source={DescribeSearchSender(sender)} left='{TrimForLog(leftSearchBar.Text)}' right='{TrimForLog(rightSearchBar.Text)}'");
        ScheduleSearchFilter();
    }

    private void OnHideFilteredChanged(object? sender, EventArgs e)
    {
        if (suppressSearchInputEvents)
        {
            SearchLogging.Log($"OnHideFilteredChanged suppressed source={DescribeSearchSender(sender)}");
            return;
        }

        SearchLogging.Log($"OnHideFilteredChanged source={DescribeSearchSender(sender)} leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");

        if (sender is ModSearchBar searchBar &&
            searchBar.HideFiltered &&
            !string.IsNullOrWhiteSpace(searchBar.Text))
        {
            ensureSearchResultVisibleOnNextFilter = true;
            SearchLogging.Log($"OnHideFilteredChanged scheduled ensure-visible source={DescribeSearchSender(sender)}");
        }

        ApplySearchFilterImmediately();
    }

    private void OnSearchModeChanged(object? sender, EventArgs e)
    {
        if (suppressSearchInputEvents)
        {
            SearchLogging.Log($"OnSearchModeChanged suppressed source={DescribeSearchSender(sender)}");
            return;
        }

        SearchLogging.Log(
            $"OnSearchModeChanged source={DescribeSearchSender(sender)} leftMode={DescribeSearchMode(leftSearchBar.SearchMode)} " +
            $"rightMode={DescribeSearchMode(rightSearchBar.SearchMode)}");

        if (sender is ModSearchBar searchBar &&
            searchBar.HideFiltered &&
            !string.IsNullOrWhiteSpace(searchBar.Text))
        {
            ensureSearchResultVisibleOnNextFilter = true;
            SearchLogging.Log($"OnSearchModeChanged scheduled ensure-visible source={DescribeSearchSender(sender)}");
        }

        ApplySearchFilterImmediately();
    }

    private void ApplyFilterFlags(
        ObservableCollection<ModRefViewModel> targetCollection,
        IEnumerable<DFHMod> sourceMods,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        ListBox list,
        bool preserveSourceOrder = false)
    {
        string trimmed = filter?.Trim() ?? string.Empty;
        bool hasFilter = !string.IsNullOrWhiteSpace(trimmed);

        List<ModRefViewModel> displayItems = ApplyFilterAndSort(
            sourceMods.Select(m => modViewMap[m.ToString()]),
            trimmed,
            searchMode,
            hideFiltered,
            preserveSourceOrder);

        ReplaceCollection(targetCollection, displayItems);

        SearchLogging.Log(
            $"ApplyFilterFlags list={DescribeList(list)} filter='{TrimForLog(filter)}' mode={DescribeSearchMode(searchMode)} hideFiltered={hideFiltered} total={sourceMods.Count()} visible={displayItems.Count}");

        DropNonDisplayedSelections(list, displayItems);
    }

    private List<ModRefViewModel> ApplyFilterAndSort(
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        bool preserveSourceOrder)
    {
        bool hasFilter = !string.IsNullOrWhiteSpace(filter);

        IEnumerable<ModRefViewModel> sorted = searchMode switch
        {
            SearchFilterMode.ModifiedTime => source.OrderByDescending(vm => vm.LastModifiedTime ?? DateTime.MinValue),
            _ when preserveSourceOrder => source,
            _ => source.OrderBy(vm => vm.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        return sorted.Where(vm =>
        {
            bool match = !hasFilter || vm.MatchesFilter(filter, searchMode);
            vm.IsFilteredOut = hasFilter && !match;
            vm.IsVisible = !hideFiltered || match;
            return vm.IsVisible;
        }).ToList();
    }

    private void DropNonDisplayedSelections(ListBox list, IReadOnlyCollection<ModRefViewModel> displayItems)
    {
        if (list.SelectedItems == null || list.SelectedItems.Count == 0)
            return;

        HashSet<ModRefViewModel> visibleSet = new HashSet<ModRefViewModel>(displayItems);
        int before = list.SelectedItems.Count;
        List<ModRefViewModel> retained = list.SelectedItems
            .OfType<ModRefViewModel>()
            .Where(vm => visibleSet.Contains(vm))
            .ToList();

        if (retained.Count == list.SelectedItems.Count)
            return;

        list.SelectedItems.Clear();
        foreach (ModRefViewModel vm in retained)
            list.SelectedItems.Add(vm);

        modListController.UpdateSelectionState(list);
        SearchLogging.Log($"DropNonDisplayedSelections list={DescribeList(list)} before={before} after={retained.Count}");
    }

    private void ScheduleSearchFilter()
    {
        if (searchDebounceTimer == null)
        {
            SearchLogging.Log("ScheduleSearchFilter no-debounce -> immediate");
            ApplySearchFilter();
            return;
        }

        SearchLogging.Log("ScheduleSearchFilter restart timer");
        searchDebounceTimer.Stop();
        searchDebounceTimer.Start();
    }

    private void ApplySearchFilterImmediately()
    {
        SearchLogging.Log("ApplySearchFilterImmediately");
        searchDebounceTimer?.Stop();
        ApplySearchFilter();
    }



    private string DescribeSearchSender(object? sender)
    {
        if (ReferenceEquals(sender, leftSearchBar))
            return "leftSearchBar";
        if (ReferenceEquals(sender, rightSearchBar))
            return "rightSearchBar";
        return sender?.GetType().Name ?? "<null>";
    }

    private string DescribeList(ListBox list)
    {
        if (ReferenceEquals(list, leftModlist))
            return "leftModlist";
        if (ReferenceEquals(list, rightModlist))
            return "rightModlist";
        return list.Name ?? "<unnamedList>";
    }

    private static string TrimForLog(string? value)
    {
        return StringFormatter.TrimForLog(value);
    }
    private static string DescribeSearchMode(SearchFilterMode mode)
    {
        return mode switch
        {
            SearchFilterMode.Name => "name",
            SearchFilterMode.Regex => "regex",
            SearchFilterMode.ModifiedTime => "modified_time",
            SearchFilterMode.Id => "id",
            SearchFilterMode.SteamFileId => "steam_file_id",
            _ => "name"
        };
    }

    private void EnsureFirstVisibleSearchResultInView(
        ListBox list,
        IEnumerable<ModRefViewModel> source,
        string filter,
        bool hideFiltered)
    {
        if (!hideFiltered || string.IsNullOrWhiteSpace(filter))
            return;

        ModRefViewModel? firstVisible = source.FirstOrDefault(vm => vm.IsVisible);
        if (firstVisible == null)
            return;

        list.ScrollIntoView(firstVisible);
        SearchLogging.Log(
            $"EnsureFirstVisibleSearchResultInView list={DescribeList(list)} targetId='{TrimForLog(firstVisible.ModReference.ID)}'");
    }

    private void LogVisualFilterState(string phase)
    {
        if (!DevMode.IsEnabled)
            return;

        LogListVisualState(phase, leftModlist, inactiveMods, leftSearchBar.Text, leftSearchBar.SearchMode, leftSearchBar.HideFiltered);
        LogListVisualState(phase, rightModlist, activeMods, rightSearchBar.Text, rightSearchBar.SearchMode, rightSearchBar.HideFiltered);
    }

    private void LogListVisualState(
        string phase,
        ListBox list,
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode mode,
        bool hideFiltered)
    {
        int total = 0;
        int vmVisible = 0;
        foreach (ModRefViewModel vm in source)
        {
            total++;
            if (vm.IsVisible)
                vmVisible++;
        }

        List<ListBoxItem> realizedItems = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        int realizedTotal = realizedItems.Count;
        int realizedVisible = realizedItems.Count(item => item.IsVisible);
        int realizedHidden = realizedTotal - realizedVisible;

        SearchLogging.Log(
            $"VisualState phase={phase} list={DescribeList(list)} filter='{TrimForLog(filter)}' mode={DescribeSearchMode(mode)} hideFiltered={hideFiltered} " +
            $"vmVisible={vmVisible}/{total} realizedVisible={realizedVisible}/{realizedTotal} realizedHidden={realizedHidden}");
    }
}
