using Avalonia;
using Avalonia.Controls;
using ModHearth.Config;
using ModHearth.UI.Helpers;
using ModHearth.Utils.DwarfFortress;
using ModHearth.Utils.Logging;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private readonly CancellationTokenSource autoNewWorldCancellation = new();
        private Flyout? runDwarfFortressOptionsFlyout;
        private CheckBox? autoOpenNewWorldSetupCheckBox;
        private bool suppressAutoOpenNewWorldSetupEvents;

        public bool IsAutoOpenNewWorldSetupEnabled
        {
            get => ConfigManager.IsAutoOpenNewWorldSetupEnabled();
            set
            {
                if (value == ConfigManager.IsAutoOpenNewWorldSetupEnabled())
                    return;

                ConfigManager.SetAutoOpenNewWorldSetupEnabled(value);
                NotifyOfPropertyChange();
            }
        }

        /// <summary>
        /// Mirrors the auto-open-new-world setting into the flyout checkbox without firing its
        /// change handler.
        /// </summary>
        private void SyncAutoOpenNewWorldSetupCheckBox()
        {
            if (autoOpenNewWorldSetupCheckBox == null)
                return;

            suppressAutoOpenNewWorldSetupEvents = true;
            autoOpenNewWorldSetupCheckBox.IsChecked = IsAutoOpenNewWorldSetupEnabled;
            suppressAutoOpenNewWorldSetupEvents = false;
        }

        private void EnsureRunDwarfFortressOptionsFlyout()
        {
            if (runDwarfFortressOptionsFlyout != null)
                return;

            CheckBox checkBox = new()
            {
                Content = "Auto click \"Create new world\" on title screen",
                IsChecked = IsAutoOpenNewWorldSetupEnabled
            };
            ToolTip.SetTip(checkBox, "Clicks \"Create new world\" when Dwarf Fortress reaches the title screen. Requires DFHack");
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (suppressAutoOpenNewWorldSetupEvents)
                    return;

                bool enabled = checkBox.IsChecked == true;
                IsAutoOpenNewWorldSetupEnabled = enabled;
                ShowNotification(
                    enabled ? "World creation screen will open on launch" : "World creation screen on launch disabled",
                    enabled ? "playMouseBorderIcon.svg" : "playCancelBorderIcon.svg");
            };
            autoOpenNewWorldSetupCheckBox = checkBox;

            StackPanel panel = new()
            {
                Margin = new Thickness(6),
                Spacing = 8
            };
            panel.Children.Add(checkBox);

            runDwarfFortressOptionsFlyout = ButtonHelpers.CreateCompactFlyout(panel);
        }

        private void StartAutoOpenNewWorldSetupIfEnabled()
        {
            if (!IsAutoOpenNewWorldSetupEnabled)
                return;

            string dfFolderPath = ConfigManager.Config.DFFolderPath;
            if (string.IsNullOrWhiteSpace(dfFolderPath))
                return;

            _ = RunAutoOpenNewWorldSetupAsync(dfFolderPath);
        }

        private async Task RunAutoOpenNewWorldSetupAsync(string dfFolderPath)
        {
            AutoNewWorldResult result;
            try
            {
                result = await Task.Run(
                    () => DwarfFortressAutoNewWorldRunner.RunAsync(
                        dfFolderPath,
                        autoNewWorldCancellation.Token));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LogAndNotifyAutoOpenFailure(ex.Message);
                return;
            }

            switch (result.Outcome)
            {
                case AutoNewWorldOutcome.Opened:
                    ShowNotification("Dwarf Fortress opened the world creation screen", "playMouseBorderIcon.svg");
                    break;

                case AutoNewWorldOutcome.AlreadyOpen:
                    break;

                case AutoNewWorldOutcome.Failed:
                    LogAndNotifyAutoOpenFailure(result.Message, notificationDelay: 6000);
                    break;

                case AutoNewWorldOutcome.Cancelled:
                    break;
            }
        }

        private void LogAndNotifyAutoOpenFailure(string message, int notificationDelay = 3500)
        {
            string errorMessage = $"Autoclick new world failed: {message}";
            Loggers.DfRunner.LogError(errorMessage);
            ShowNotification(errorMessage, "playWarningBorderIcon.svg", notificationDelay: notificationDelay);
        }
    }
}
