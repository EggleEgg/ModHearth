using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ModHearth.Models;
using ModHearth.UI.Helpers;
using ModHearth.UI.ViewModels;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private void ApplySearchFilter()
        {
            string leftFilter = leftSearchBar.Text.Trim();
            string rightFilter = rightSearchBar.Text.Trim();
            SearchFilterMode leftMode = leftSearchBar.SearchMode;
            SearchFilterMode rightMode = rightSearchBar.SearchMode;
            if (DevMode.IsEnabled)
            {
                SearchLogging.Log(
                    $"ApplySearchFilter start left='{TrimForLog(leftFilter)}' leftMode={DescribeSearchMode(leftMode)} " +
                    $"right='{TrimForLog(rightFilter)}' rightMode={DescribeSearchMode(rightMode)} leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");
            }

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
                if (DevMode.IsEnabled)
                    SearchLogging.Log("ApplySearchFilter ensureSearchResultVisibleOnNextFilter consumed");
            }

            if (DevMode.IsEnabled)
                SearchLogging.Log("ApplySearchFilter end");
            LogVisualFilterState("ApplySearchFilter");
        }

        private void OnSearchBarStateChanged()
        {
            if (searchStateSaveTimer == null)
            {
                SaveSearchBarStates();
                return;
            }
            searchStateSaveTimer.Stop();
            searchStateSaveTimer.Start();
        }

        private void SaveSearchBarStates()
        {
            ConfigManager.SetLeftSearchBarState(leftSearchBar.GetStringState());
            ConfigManager.SetRightSearchBarState(rightSearchBar.GetStringState());
        }

        private void UpdateSearchBarAvailableColors()
        {
            if (leftSearchBar == null || rightSearchBar == null) return;

            IEnumerable<ModColor> leftColors = inactiveMods
                .Select(m => m.ModReference.AssignedColor)
                .Where(c => c != ModColor.None)
                .Distinct();
            leftSearchBar.SetAvailableColors(leftColors);

            IEnumerable<ModColor> rightColors = activeMods
                .Select(m => m.ModReference.AssignedColor)
                .Where(c => c != ModColor.None)
                .Distinct();
            rightSearchBar.SetAvailableColors(rightColors);
        }

        private void OnSearchInputChanged(object? sender, EventArgs e)
        {
            if (suppressSearchInputEvents)
            {
                if (DevMode.IsEnabled)
                    SearchLogging.Log($"OnSearchInputChanged suppressed source={DescribeSearchSender(sender)}");
                return;
            }

            if (DevMode.IsEnabled)
                SearchLogging.Log($"OnSearchInputChanged source={DescribeSearchSender(sender)} left='{TrimForLog(leftSearchBar.Text)}' right='{TrimForLog(rightSearchBar.Text)}'");
            ScheduleSearchFilter();
        }

        private void OnHideFilteredChanged(object? sender, EventArgs e)
            => HandleSearchCriteriaChanged(
                "OnHideFilteredChanged",
                sender,
                () => $"leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");

        private void OnSearchModeChanged(object? sender, EventArgs e)
            => HandleSearchCriteriaChanged(
                "OnSearchModeChanged",
                sender,
                () => $"leftMode={DescribeSearchMode(leftSearchBar.SearchMode)} rightMode={DescribeSearchMode(rightSearchBar.SearchMode)}");

        // Hide-filtered, search-mode and sort-order changes all use the same apply/ensure-visible
        // pipeline; only the diagnostic line differs.
        private void HandleSearchCriteriaChanged(string eventName, object? sender, Func<string> describeState)
        {
            if (suppressSearchInputEvents)
            {
                if (DevMode.IsEnabled)
                    SearchLogging.Log($"{eventName} suppressed source={DescribeSearchSender(sender)}");
                return;
            }

            if (DevMode.IsEnabled)
                SearchLogging.Log($"{eventName} source={DescribeSearchSender(sender)} {describeState()}");

            if (sender is ModSearchBar searchBar &&
                searchBar.HideFiltered &&
                !string.IsNullOrWhiteSpace(searchBar.Text))
            {
                ensureSearchResultVisibleOnNextFilter = true;
                if (DevMode.IsEnabled)
                    SearchLogging.Log($"{eventName} scheduled ensure-visible source={DescribeSearchSender(sender)}");
            }

            ApplySearchFilterImmediately();
        }



        private void ApplyFilterFlags(
            ObservableCollection<ModRefViewModel> targetCollection,
            IEnumerable<DfhMod> sourceMods,
            string filter,
            SearchFilterMode searchMode,
            bool hideFiltered,
            bool sortDescending,
            ListBox list,
            bool isSortingEnabled = true) => SearchFilterHelper.ApplyFilterFlags(
                targetCollection,
                sourceMods
                    .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
                    .Where(vm => vm != null)
                    .Cast<ModRefViewModel>(),
                filter,
                searchMode,
                hideFiltered,
                sortDescending,
                list,
                modListController,
                isSortingEnabled,
                msg =>
                {
                    if (DevMode.IsEnabled)
                        SearchLogging.Log($"ApplyFilterFlags list={DescribeList(list)} " + msg);
                }
            );

        private void ScheduleSearchFilter()
        {
            if (searchDebounceTimer == null)
            {
                if (DevMode.IsEnabled)
                    SearchLogging.Log("ScheduleSearchFilter no-debounce -> immediate");
                ApplySearchFilter();
                return;
            }

            if (DevMode.IsEnabled)
                SearchLogging.Log("ScheduleSearchFilter restart timer");
            searchDebounceTimer.Stop();
            searchDebounceTimer.Start();
        }

        private void ApplySearchFilterImmediately()
        {
            if (DevMode.IsEnabled)
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

        private static string TrimForLog(string? value) => StringFormatter.TrimForLog(value);

        private static string DescribeSearchMode(SearchFilterMode mode) => mode switch
        {
            SearchFilterMode.Name => "name",
            SearchFilterMode.Regex => "regex",
            SearchFilterMode.ModifiedTime => "modified_time",
            SearchFilterMode.Id => "id",
            SearchFilterMode.SteamFileId => "steam_file_id",
            SearchFilterMode.Color => throw new NotImplementedException(),
            _ => "name"
        };

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
            if (DevMode.IsEnabled)
            {
                SearchLogging.Log(
                    $"EnsureFirstVisibleSearchResultInView list={DescribeList(list)} targetId='{TrimForLog(firstVisible.ModReference.ID)}'");
            }
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
            if (!DevMode.IsEnabled)
                return;

            int total = 0;
            int vmVisible = 0;
            foreach (ModRefViewModel vm in source)
            {
                total++;
                if (vm.IsVisible)
                    vmVisible++;
            }

            List<ListBoxItem> realizedItems = [.. list.GetVisualDescendants().OfType<ListBoxItem>()];
            int realizedTotal = realizedItems.Count;
            int realizedVisible = realizedItems.Count(item => item.IsVisible);
            int realizedHidden = realizedTotal - realizedVisible;

            SearchLogging.Log(
                $"VisualState phase={phase} list={DescribeList(list)} filter='{TrimForLog(filter)}' mode={DescribeSearchMode(mode)} hideFiltered={hideFiltered} " +
                $"vmVisible={vmVisible}/{total} realizedVisible={realizedVisible}/{realizedTotal} realizedHidden={realizedHidden}");
        }
    }
}