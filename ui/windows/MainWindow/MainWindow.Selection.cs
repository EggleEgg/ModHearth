using Avalonia.Controls;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void TrackSelectedMod(ModRefViewModel selected)
    {
        string id = selected.ModReference.ID?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (string.Equals(currentSelectedModId, id, StringComparison.OrdinalIgnoreCase))
            return;

        previousSelectedModId = currentSelectedModId;
        currentSelectedModId = id;
    }

    private bool TrySelectModById(string? modId)
    {
        string targetId = modId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        ModRefViewModel? vm = modViewMap.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.ModReference.ID, targetId, StringComparison.OrdinalIgnoreCase));
        if (vm == null)
            return false;

        ListBox? list = GetListForMod(vm);
        if (list?.SelectedItems == null)
            return false;

        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();
        list.SelectedItems.Add(vm);
        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
        list.ScrollIntoView(vm);
        TrackSelectedMod(vm);
        ShowModInfo(vm.ModReference);
        return true;
    }

    private ModSelectionSnapshot CaptureSelectionSnapshot()
    {
        List<ModSelectionToken> rightTokens = MainWindowSelectionState.CaptureSelectionTokens(rightModlist);
        if (rightTokens.Count > 0)
            return new ModSelectionSnapshot(false, rightTokens, currentSelectedModId, previousSelectedModId);

        List<ModSelectionToken> leftTokens = MainWindowSelectionState.CaptureSelectionTokens(leftModlist);
        if (leftTokens.Count > 0)
            return new ModSelectionSnapshot(true, leftTokens, currentSelectedModId, previousSelectedModId);

        return new ModSelectionSnapshot(null, new List<ModSelectionToken>(), currentSelectedModId, previousSelectedModId);
    }

    private SearchFilterStateSnapshot CaptureSearchFilterStateSnapshot()
    {
        SearchFilterStateSnapshot snapshot = new SearchFilterStateSnapshot(
            leftSearchBar.Text ?? string.Empty,
            leftSearchBar.HideFiltered,
            leftSearchBar.SearchMode,
            rightSearchBar.Text ?? string.Empty,
            rightSearchBar.HideFiltered,
            rightSearchBar.SearchMode);
        SearchLogging.Log(
            $"CaptureSearchFilterStateSnapshot left='{TrimForLog(snapshot.LeftText)}' leftHide={snapshot.LeftHideFiltered} leftMode={DescribeSearchMode(snapshot.LeftMode)} " +
            $"right='{TrimForLog(snapshot.RightText)}' rightHide={snapshot.RightHideFiltered} rightMode={DescribeSearchMode(snapshot.RightMode)}");
        return snapshot;
    }

    private void RestoreSearchFilterStateSnapshot(SearchFilterStateSnapshot snapshot)
    {
        SearchLogging.Log(
            $"RestoreSearchFilterStateSnapshot begin left='{TrimForLog(snapshot.LeftText)}' leftHide={snapshot.LeftHideFiltered} leftMode={DescribeSearchMode(snapshot.LeftMode)} " +
            $"right='{TrimForLog(snapshot.RightText)}' rightHide={snapshot.RightHideFiltered} rightMode={DescribeSearchMode(snapshot.RightMode)}");
        suppressSearchInputEvents = true;
        searchDebounceTimer?.Stop();
        try
        {
            if (!string.Equals(leftSearchBar.Text, snapshot.LeftText, StringComparison.Ordinal))
                leftSearchBar.Text = snapshot.LeftText;
            if (leftSearchBar.HideFiltered != snapshot.LeftHideFiltered)
                leftSearchBar.HideFiltered = snapshot.LeftHideFiltered;
            if (leftSearchBar.SearchMode != snapshot.LeftMode)
                leftSearchBar.SearchMode = snapshot.LeftMode;

            if (!string.Equals(rightSearchBar.Text, snapshot.RightText, StringComparison.Ordinal))
                rightSearchBar.Text = snapshot.RightText;
            if (rightSearchBar.HideFiltered != snapshot.RightHideFiltered)
                rightSearchBar.HideFiltered = snapshot.RightHideFiltered;
            if (rightSearchBar.SearchMode != snapshot.RightMode)
                rightSearchBar.SearchMode = snapshot.RightMode;
        }
        finally
        {
            suppressSearchInputEvents = false;
        }
        SearchLogging.Log(
            $"RestoreSearchFilterStateSnapshot end left='{TrimForLog(leftSearchBar.Text)}' leftHide={leftSearchBar.HideFiltered} leftMode={DescribeSearchMode(leftSearchBar.SearchMode)} " +
            $"right='{TrimForLog(rightSearchBar.Text)}' rightHide={rightSearchBar.HideFiltered} rightMode={DescribeSearchMode(rightSearchBar.SearchMode)}");
    }

    private void RestoreSelectionSnapshot(ModSelectionSnapshot snapshot)
    {
        if (snapshot.IsLeftList == null || snapshot.Tokens.Count == 0)
            return;

        List<ModRefViewModel> leftMatches = MainWindowSelectionState.ResolveSelectionTokens(inactiveMods, snapshot.Tokens);
        List<ModRefViewModel> rightMatches = MainWindowSelectionState.ResolveSelectionTokens(activeMods, snapshot.Tokens);
        bool preferLeft = snapshot.IsLeftList == true;

        bool useLeft;
        List<ModRefViewModel> restored;
        if (preferLeft)
        {
            useLeft = leftMatches.Count > 0 || rightMatches.Count == 0;
            restored = useLeft ? leftMatches : rightMatches;
        }
        else
        {
            useLeft = !(rightMatches.Count > 0 || leftMatches.Count == 0);
            restored = useLeft ? leftMatches : rightMatches;
        }

        ListBox targetList = useLeft ? leftModlist : rightModlist;
        if (HasActiveHideFilter(targetList))
            restored = restored.Where(vm => vm.IsVisible).ToList();

        if (restored.Count == 0)
        {
            ShowFallbackInfo();
            return;
        }

        string targetId = snapshot.CurrentSelectedId?.Trim() ?? string.Empty;
        ModRefViewModel primary = restored.FirstOrDefault(vm =>
            string.Equals(vm.ModReference.ID, targetId, StringComparison.OrdinalIgnoreCase)) ?? restored[0];

        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();
        targetList.SelectedItems?.Add(primary);
        foreach (ModRefViewModel vm in restored)
        {
            if (!ReferenceEquals(vm, primary))
                targetList.SelectedItems?.Add(vm);
        }

        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
        targetList.ScrollIntoView(primary);
        currentSelectedModId = primary.ModReference.ID?.Trim();
        previousSelectedModId = snapshot.PreviousSelectedId;
        ShowModInfo(primary.ModReference);
    }

    private bool HasActiveHideFilter(ListBox list)
    {
        if (list == leftModlist)
            return leftSearchBar.HideFiltered && !string.IsNullOrWhiteSpace(leftSearchBar.Text);
        if (list == rightModlist)
            return rightSearchBar.HideFiltered && !string.IsNullOrWhiteSpace(rightSearchBar.Text);
        return false;
    }
}
