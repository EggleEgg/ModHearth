using Avalonia.Controls;
using ModHearth.UI.Theme;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly Func<string, Task>? restoreBackupAsync;
        private readonly Func<Task>? redoConfigAsync;
        private bool busy;
        private bool hasBackup;

        public SettingsDialog()
        {
            InitializeComponent();
            WindowThemeManager.Register(this);

            restoreBackupButton.Click += async (_, _) => await RestoreBackupAsync();
            backupNowButton.Click += async (_, _) => await BackupNowAsync();
            redoConfigButton.Click += async (_, _) => await RedoConfigAsync();

            RefreshBackupInfo();
        }

        private SettingsDialog(Func<string, Task> restoreBackupAsync, Func<Task> redoConfigAsync)
            : this()
        {
            this.restoreBackupAsync = restoreBackupAsync;
            this.redoConfigAsync = redoConfigAsync;
        }

        public static async Task ShowAsync(
            Window owner,
            Func<string, Task> restoreBackupAsync,
            Func<Task> redoConfigAsync)
        {
            SettingsDialog dialog = new(restoreBackupAsync, redoConfigAsync)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            await dialog.ShowDialog(owner);
        }

        private void RefreshBackupInfo()
        {
            string? archivePath = BackupService.FindArchive();
            hasBackup = archivePath != null;
            if (archivePath == null)
            {
                backupInfoText.Text = "No backup found. Backups are created automatically before updates.";
                restoreBackupButton.IsEnabled = false;
                return;
            }

            BackupArchiveInfo? info = BackupService.TryReadInfo(archivePath);
            backupInfoText.Text = info == null
                ? $"Backup found at '{archivePath}'."
                : $"Backup from {info.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} (ModHearth {info.ModHearthVersion}).";
            restoreBackupButton.IsEnabled = !busy;
        }

        private async Task RestoreBackupAsync()
        {
            if (busy)
                return;

            string? archivePath = BackupService.FindArchive();
            if (archivePath == null)
            {
                await DialogService.ShowMessageAsync(
                    this,
                    "No backup archive (backup.zip or backup.tar.gz) was found next to ModHearth.",
                    "Restore Backup");
                RefreshBackupInfo();
                return;
            }

            BackupArchiveInfo? info = BackupService.TryReadInfo(archivePath);
            string backupLabel = info == null
                ? Path.GetFileName(archivePath)
                : $"{Path.GetFileName(archivePath)} from {info.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} (ModHearth {info.ModHearthVersion})";

            bool confirm = await DialogService.ShowConfirmAsync(
                this,
                $"Restore {backupLabel}?\n\nCurrent config, sort rules, metadata and DFHack mod-manager settings will be overwritten, and ModHearth will restart.",
                "Restore Backup");
            if (!confirm)
                return;

            // Close the modal dialog first so restart prompts owned by the main window are visible.
            Close();

            if (restoreBackupAsync != null)
                await restoreBackupAsync(archivePath);
        }

        private async Task BackupNowAsync()
        {
            if (busy)
                return;

            SetBusy(true);
            try
            {
                BackupResult result = await Task.Run(() => BackupService.CreateBackup());
                SetStatus(result.Message, isError: !result.Success);
                RefreshBackupInfo();
            }
            catch (Exception ex)
            {
                Loggers.Backup.LogError($"Backup now failed: {ex.Message}");
                SetStatus($"Backup failed: {ex.Message}", isError: true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task RedoConfigAsync()
        {
            if (busy)
                return;

            // Close the modal dialog first so the confirmation and unsaved-changes prompts owned by
            // the main window are visible.
            Close();

            if (redoConfigAsync != null)
                await redoConfigAsync();
        }

        private void SetBusy(bool isBusy)
        {
            busy = isBusy;
            restoreBackupButton.IsEnabled = hasBackup && !isBusy;
            backupNowButton.IsEnabled = !isBusy;
            redoConfigButton.IsEnabled = !isBusy;
        }

        private void SetStatus(string message, bool isError)
        {
            statusText.Text = message;
            statusText.IsVisible = true;
            statusText.Opacity = isError ? 1.0 : 0.75;
        }
    }
}
