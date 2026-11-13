using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ModHearth.Config;
using ModHearth.UI.Helpers;
using ModHearth.Utils.Logging;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private Flyout? issueSearchFlyout;
        private CheckBox? searchPossibleIssuesCheckBox;
        private CheckBox? searchErrorLogIssuesCheckBox;
        private int issueSearchRefreshVersion;

        private void EnsureIssueSearchFlyout()
        {
            if (issueSearchFlyout != null)
                return;

            searchPossibleIssuesCheckBox = CreateIssueSearchCheckBox(
                "Search possible issues",
                "Statically check the active mods' info.txt and raw files for problems Dwarf Fortress writes to errorlog.txt  without launching the game.",
                ConfigManager.GetSearchPossibleIssues(),
                enabled => _ = OnSearchPossibleIssuesToggledAsync(enabled));

            searchErrorLogIssuesCheckBox = CreateIssueSearchCheckBox(
                "Search errorlog issues",
                "Read Dwarf Fortress's errorlog.txt and attribute its errors to the installed mods.",
                ConfigManager.GetSearchErrorLogIssues(),
                enabled => _ = OnSearchErrorLogIssuesToggledAsync(enabled));

            issueSearchFlyout = ButtonHelpers.CreateCompactFlyout(
                new StackPanel
                {
                    Margin = new Thickness(8),
                    Spacing = 10,
                    Children =
                    {
                        searchPossibleIssuesCheckBox,
                        searchErrorLogIssuesCheckBox
                    }
                },
                PlacementMode.Top);
        }

        private static CheckBox CreateIssueSearchCheckBox(
            string text,
            string tooltip,
            bool isChecked,
            Action<bool> onChanged)
        {
            CheckBox checkBox = new()
            {
                Content = text,
                IsChecked = isChecked,
                Tag = "Themed"
            };
            ToolTip.SetTip(checkBox, tooltip);

            // Subscribed after the initial value so creating the flyout cannot persist a toggle.
            checkBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == ToggleButton.IsCheckedProperty && checkBox.IsChecked is bool value)
                    onChanged(value);
            };

            return checkBox;
        }

        private async Task OnSearchPossibleIssuesToggledAsync(bool enabled)
        {
            ConfigManager.SetSearchPossibleIssues(enabled);

            // Enabling needs the raw scan to be present; disabling only needs the caches to clear.
            await RefreshIssueSearchAsync(rescanRawFiles: enabled);
        }

        private async Task OnSearchErrorLogIssuesToggledAsync(bool enabled)
        {
            ConfigManager.SetSearchErrorLogIssues(enabled);
            await RefreshIssueSearchAsync(rescanRawFiles: false);
        }

        private async Task RefreshIssueSearchAsync(bool rescanRawFiles)
        {
            int version = ++issueSearchRefreshVersion;
            try
            {
                if (rescanRawFiles)
                    await Task.Run(manager.RefreshRawAnalysisAsync);
                else
                    await Task.Run(manager.RefreshIssueAnalysis);
            }
            catch (Exception ex)
            {
                AppLogging.LogException("Failed to refresh the issue search settings", ex);
                return;
            }

            // A newer toggle superseded this refresh; it will update the indicators instead.
            if (version != issueSearchRefreshVersion)
                return;

            UpdateProblemIndicators();
            UpdateDuplicateWarningIndicators();
        }
    }
}
