using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ModHearth.Utilities.Logging;

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

            issueSearchFlyout = new Flyout
            {
                FlyoutPresenterClasses = { "compact-flyout" },
                Placement = PlacementMode.Top,
                Content = new StackPanel
                {
                    Margin = new Thickness(8),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Issue search",
                            FontWeight = FontWeight.SemiBold
                        },
                        searchPossibleIssuesCheckBox,
                        searchErrorLogIssuesCheckBox
                    }
                }
            };
        }

        private void WarningIssuesButtonPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(warningIssuesButton).Properties.IsRightButtonPressed)
                return;

            e.Handled = true;
            EnsureIssueSearchFlyout();
            issueSearchFlyout?.ShowAt(warningIssuesButton);
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

        private bool HasWarningIssues() => (problemMods?.Count ?? 0) > 0 || (duplicateWarningMods?.Count ?? 0) > 0;
    }
}
