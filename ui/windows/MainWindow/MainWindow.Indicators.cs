using Avalonia.Controls;
using ModHearth.Models;
using ModHearth.UI.ModList;
using ModHearth.UI.Rendering;
using ModHearth.UI.ViewModels;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private void UpdateCachedIndicators() => ModListIndicatorUpdater.UpdateCachedIndicators(modViewMap.Values, manager.GetInstalledCacheModIds());

        private int lastRelationshipRulesVersion = -1;
        private string? lastWarningTooltip;

        private void UpdateRelationshipBadges()
        {
            int currentVersion = manager.GetRelationshipRulesVersion();
            if (currentVersion == lastRelationshipRulesVersion)
                return;

            ModListIndicatorUpdater.UpdateRelationshipBadges(modViewMap.Values, manager.GetModRelationshipRules());
            lastRelationshipRulesVersion = currentVersion;
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

            string? tooltip = ModListIndicatorUpdater.BuildWarningIssuesTooltip(problemCount, duplicateCount);
            if (tooltip != lastWarningTooltip)
            {
                lastWarningTooltip = tooltip;
                ToolTip.SetTip(warningIssuesButton, tooltip);
            }

            if (hasIssues && warningIssuesIcon != null)
            {
                string iconName;
                if (hasProblems && hasDuplicates)
                    iconName = "warningErrorIcon.svg";
                else if (hasProblems)
                    iconName = "errorIcon.svg";
                else iconName = "warningIcon.svg";

                warningIssuesIcon.Source = ImageSourceLoader.LoadFromAssetUri(iconName)
                    ?? warningIssuesIcon.Source;
            }

            UpdateModIssuesOverlay();
        }

        private readonly IssueJumpCursors inactiveIssueCursors = new();
        private readonly IssueJumpCursors activeIssueCursors = new();

        private void UpdateModIssuesOverlay()
        {
            UpdateListIssuesOverlay(inactiveMods, modIssuesOverlayLeft);
            UpdateListIssuesOverlay(activeMods, modIssuesOverlayRight);
        }

        private static void UpdateListIssuesOverlay(IEnumerable<ModRefViewModel> mods, ModIssuesOverlay overlay)
        {
            List<ModRefViewModel> modList = [.. mods];
            overlay.UpdateCounts(
                modList.Count(m => m.IssueTier == ModIssueTier.Error),
                modList.Count(m => m.IssueTier == ModIssueTier.Warning),
                modList.Count(m => m.IssueTier == ModIssueTier.Info));
        }

        // Per-list jump position for each tier. Kept in one object so the severity buttons only
        // need a tier, not a separate callback and ref field each.
        private sealed class IssueJumpCursors
        {
            private int errorIndex;
            private int warningIndex;
            private int infoIndex;

            public int Next(ModIssueTier tier, int count)
            {
                if (count <= 0)
                    return 0;

                int index = tier switch
                {
                    ModIssueTier.Error => errorIndex,
                    ModIssueTier.Warning => warningIndex,
                    ModIssueTier.Info => infoIndex,
                    _ => 0
                };

                if (index >= count)
                    index = 0;

                int next = (index + 1) % count;
                switch (tier)
                {
                    case ModIssueTier.Error:
                        errorIndex = next;
                        break;
                    case ModIssueTier.Warning:
                        warningIndex = next;
                        break;
                    case ModIssueTier.Info:
                        infoIndex = next;
                        break;
                }

                return index;
            }
        }

        private void JumpToNextIssueTier(
            IEnumerable<ModRefViewModel> mods,
            ListBox targetList,
            ModIssueTier tier,
            IssueJumpCursors cursors)
        {
            List<ModRefViewModel> matchingMods = [.. mods.Where(m => m.IssueTier == tier)];
            if (matchingMods.Count == 0)
                return;

            ModRefViewModel vm = matchingMods[cursors.Next(tier, matchingMods.Count)];

            if (currentJumpHighlighted != null)
                currentJumpHighlighted.IsJumpHighlighted = false;

            vm.IsJumpHighlighted = true;
            currentJumpHighlighted = vm;
            targetList.SelectedItems?.Clear();
            _ = (targetList.SelectedItems?.Add(vm));
            targetList.ScrollIntoView(vm);
            ShowModInfo(vm.ModReference);
        }

        private ModRefViewModel? currentJumpHighlighted;

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

        private void JumpToNextIssue(List<DfhMod> issues, ref int index)
        {
            if (issues.Count == 0)
                return;

            if (index >= issues.Count)
                index = 0;

            DfhMod target = issues[index];
            index = (index + 1) % issues.Count;

            ModRefViewModel? vm = activeMods.FirstOrDefault(m => m.DfMod == target);
            if (vm == null)
                return;

            if (currentJumpHighlighted != null)
                currentJumpHighlighted.IsJumpHighlighted = false;

            vm.IsJumpHighlighted = true;
            currentJumpHighlighted = vm;
            rightModlist.SelectedItems?.Clear();
            _ = (rightModlist.SelectedItems?.Add(vm));
            rightModlist.ScrollIntoView(vm);
            ShowModInfo(vm.ModReference);
        }

        private bool HasJumpHighlights() => currentJumpHighlighted != null;

        private void ClearJumpHighlights()
        {
            if (currentJumpHighlighted == null)
                return;
            currentJumpHighlighted.IsJumpHighlighted = false;
            currentJumpHighlighted = null;
        }
    }
}
