using ModHearth.UI.Helpers;

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

        private Task<bool> ConfirmUnsavedChangesBeforeRestartAsync()
            // The app is about to close, so do not let the close handler prompt again.
            => ConfirmUnsavedChangesAsync("restart");

        private void RelaunchApplication()
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
                _ = ShellLauncher.TryOpenAsync(null, exePath, "Restart ModHearth");

            Close();
        }
    }
}
