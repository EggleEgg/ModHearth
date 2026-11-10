using ModHearth.UI.Dialogs;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private async Task OpenSettingsDialogAsync()
        {
            await SettingsDialog.ShowAsync(this, RestoreBackupAsync, RedoConfigAsync);
        }

        /// <summary>
        /// Restores the selected backup only after the restart guards approve, then relaunches.
        /// Restoring inside the approved callback keeps a cancelled unsaved-changes prompt from
        /// overwriting files with stale data.
        /// </summary>
        private async Task RestoreBackupAsync(string archivePath)
        {
            try
            {
                _ = await RestartApplicationAsync(async () =>
                {
                    BackupResult result = await Task.Run(() => BackupService.RestoreBackup(archivePath));
                    if (!result.Success)
                        throw new InvalidOperationException(result.Message);
                });
            }
            catch (Exception ex)
            {
                Loggers.Backup.LogError($"Restore backup failed: {ex.Message}");
                await DialogService.ShowMessageAsync(this, $"Restore failed: {ex.Message}", "Restore Backup");
            }
        }
    }
}
