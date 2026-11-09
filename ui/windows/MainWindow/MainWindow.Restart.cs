using System.Diagnostics;
using ModHearth.UI.Dialogs;
using ModHearth.UI.Helpers;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        // Checks that can veto a restart. Register new preconditions here instead of in each caller;
        // they run in registration order and the first one returning false aborts the restart.
        private readonly AsyncGuardChain restartGuards = new();

        private void InitializeRestartGuards()
        {
            restartGuards.Register(ConfirmUnsavedChangesBeforeRestartAsync);
        }

        /// <summary>
        /// Restarts the application only after every registered guard approves. <paramref name="onApproved"/>
        /// runs after the guards pass but before the relaunch, which lets callers apply destructive changes only once the user has committed to restarting.
        /// Returns false when a guard vetoed the restart.
        /// </summary>
        private async Task<bool> RestartApplicationAsync(Func<Task>? onApproved = null)
        {
            if (!await restartGuards.RunAsync())
                return false;

            if (onApproved != null)
                await onApproved();

            RelaunchApplication();
            return true;
        }

        private async Task<bool> ConfirmUnsavedChangesBeforeRestartAsync()
        {
            if (!changesMade)
                return true;

            UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
                this,
                manager.SelectedModlist?.Name,
                "restart");

            switch (choice)
            {
                case UnsavedChangesChoice.Cancel:
                    return false;
                case UnsavedChangesChoice.Save:
                    await SaveCurrentModpackAsync();
                    return true;
                default:
                    // The app is about to close, so do not let the close handler prompt again.
                    SetChangesMade(false);
                    return true;
            }
        }

        private void RelaunchApplication()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                    _ = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLogging.LogException("Failed to restart application", ex);
            }

            Close();
        }
    }
}
