using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using System.Collections.ObjectModel;
using ModHearth.Utilities;
using ModHearth.Models;
using ModHearth.Utilities.Logging;
using Avalonia.Media;

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
                leftSearchBar.SortDescending,
                leftModlist);
            ApplyFilterFlags(
                activeMods,
                manager.enabledMods,
                rightFilter,
                rightMode,
                rightSearchBar.HideFiltered,
                rightSearchBar.SortDescending,
                rightModlist,
                isSortingEnabled: false);
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

    private void UpdateSearchBarAvailableColors()
    {
        if (leftSearchBar == null || rightSearchBar == null) return;

        var leftColors = inactiveMods
            .Select(m => m.ModReference.AssignedColor)
            .Where(c => c != ModColor.None)
            .Distinct();
        leftSearchBar.SetAvailableColors(leftColors);

        var rightColors = activeMods
            .Select(m => m.ModReference.AssignedColor)
            .Where(c => c != ModColor.None)
            .Distinct();
        rightSearchBar.SetAvailableColors(rightColors);
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

    private void OnColorPickerClicked(object? sender, EventArgs e)
    {
        if (sender is not ModSearchBar searchBar) return;

        // Use available colors from the search bar (which are restricted to the current list)
        // or union them if that's what's preferred. But let's stay consistent with the bar.
        var availableColors = searchBar.ColorPicker.AvailableColors.Select(c => c.ModColor).ToList();

        if (availableColors.Count == 0)
        {
            // Fallback: if no colors are in the list, maybe show all colors that HAVE been used globally?
            // But let's stick to the bar's logic for now.
        }

        var grid = new UniformGrid
        {
            Columns = (int)Math.Sqrt(availableColors.Count + 1)
        };

        void RefreshGrid()
        {
            grid.Children.Clear();
            
            // Add "Clear" option
            grid.Children.Add(CreateColorSwatchButton(new ModColorInfo
            {
                ModColor = ModColor.None,
                Name = "Clear all filters",
                Color = Colors.Transparent,
                IsSelected = false
            }, _ => {
                searchBar.Text = string.Empty;
                ApplySearchFilterImmediately();
                RefreshGrid();
            }));

            var currentSelection = searchBar.Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            foreach (var color in availableColors)
            {
                var info = new ModColorInfo
                {
                    ModColor = color,
                    Name = ModColorMap.ColorNames.TryGetValue(color, out var name) ? name : color.ToString(),
                    Color = ModColorMap.GetColor(color),
                    IsSelected = currentSelection.Contains(color.ToString())
                };
                grid.Children.Add(CreateColorSwatchButton(info, c => {
                    ToggleColor(c);
                    RefreshGrid();
                }));
            }
        }

        void ToggleColor(ModColor color)
        {
            var text = searchBar.Text;
            var selectedColors = text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            var colorStr = color.ToString();
            if (selectedColors.Contains(colorStr))
                selectedColors.Remove(colorStr);
            else
                selectedColors.Add(colorStr);

            searchBar.Text = string.Join(",", selectedColors);
            ApplySearchFilterImmediately();
        }

        RefreshGrid();

        var flyout = new Flyout
        {
            Content = new Border
            {
                Padding = new Thickness(4),
                Child = grid
            }
        };

        flyout.ShowAt(searchBar);
    }

    private void ApplyFilterFlags(
        ObservableCollection<ModRefViewModel> targetCollection,
        IEnumerable<DFHMod> sourceMods,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        bool sortDescending,
        ListBox list,
        bool isSortingEnabled = true)
    {
        string trimmed = filter?.Trim() ?? string.Empty;

        List<ModRefViewModel> displayItems = ApplyFilterAndSort(
            sourceMods.Select(m => modViewMap[m.ToString()]),
            trimmed,
            searchMode,
            hideFiltered,
            sortDescending,
            isSortingEnabled);

        ReplaceCollection(targetCollection, displayItems);

        SearchLogging.Log(
            $"ApplyFilterFlags list={DescribeList(list)} filter='{TrimForLog(filter)}' mode={DescribeSearchMode(searchMode)} hideFiltered={hideFiltered} sortDescending={sortDescending} total={sourceMods.Count()} visible={displayItems.Count}");

        DropNonDisplayedSelections(list, displayItems);
    }

    private List<ModRefViewModel> ApplyFilterAndSort(
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        bool sortDescending,
        bool isSortingEnabled = true)
    {
        bool hasFilter = !string.IsNullOrWhiteSpace(filter);

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