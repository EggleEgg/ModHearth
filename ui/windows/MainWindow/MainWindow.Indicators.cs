using Avalonia.Controls;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void UpdateCachedIndicators()
    {
        ModListIndicatorUpdater.UpdateCachedIndicators(modViewMap.Values, manager.GetInstalledCacheModIds());
    }

    private void UpdateProblemIndicators()
    {
        problemMods = ModListIndicatorUpdater.UpdateProblemIndicators(manager, modViewMap.Values);
        problemModIndex = 0;
        UpdateWarningIssuesButton();
    }

    private void UpdateDuplicateWarningIndicators()
    {
        duplicateWarningMods = ModListIndicatorUpdater.UpdateDuplicateWarningIndicators(manager, modViewMap.Values);
        duplicateWarningIndex = 0;
        UpdateWarningIssuesButton();
    }

    private void UpdateWarningIssuesButton()
    {
        int problemCount = problemMods?.Count ?? 0;
        int duplicateCount = duplicateWarningMods?.Count ?? 0;
        bool hasProblems = problemCount > 0;
        bool hasDuplicates = duplicateCount > 0;
        bool hasIssues = hasProblems || hasDuplicates;

        warningIssuesButton.IsVisible = hasIssues;
        warningIssuesButton.IsEnabled = hasIssues;
        ToolTip.SetTip(warningIssuesButton, ModListIndicatorUpdater.BuildWarningIssuesTooltip(problemCount, duplicateCount));

        if (!hasIssues || warningIssuesIcon == null)
            return;

        string iconName;
        if (hasProblems && hasDuplicates)
            iconName = "warningErrorIcon.svg";
        else if (hasProblems)
            iconName = "errorIcon.svg";
        else iconName = "warningIcon.svg";

        warningIssuesIcon.Source = ImageSourceLoader.LoadFromAssetUri(iconName)
            ?? warningIssuesIcon.Source;
    }

    private void JumpToNextProblem()
    {
        if (problemMods != null && problemMods.Count > 0)
        {
            JumpToNextIssue(problemMods, ref problemModIndex);
            return;
        }

        if (duplicateWarningMods != null && duplicateWarningMods.Count > 0)
            JumpToNextIssue(duplicateWarningMods, ref duplicateWarningIndex);
    }

    private void JumpToNextIssue(List<DFHMod> issues, ref int index)
    {
        if (issues.Count == 0)
            return;

        if (index >= issues.Count)
            index = 0;

        DFHMod target = issues[index];
        index = (index + 1) % issues.Count;

        ModRefViewModel? vm = activeMods.FirstOrDefault(m => m.DfMod == target);
        if (vm == null)
            return;

        foreach (ModRefViewModel other in activeMods)
            other.IsJumpHighlighted = false;

        vm.IsJumpHighlighted = true;
        rightModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Add(vm);
        rightModlist.ScrollIntoView(vm);
        ShowModInfo(vm.ModReference);
    }

    private bool HasJumpHighlights()
    {
        return modViewMap.Values.Any(vm => vm.IsJumpHighlighted);
    }

    private void ClearJumpHighlights()
    {
        foreach (ModRefViewModel vm in modViewMap.Values)
            vm.IsJumpHighlighted = false;
    }
}
