using System.Diagnostics;
using Avalonia.Controls;
using ModHearth.UI.Dialogs;
using ModHearth.Utils.Logging;

namespace ModHearth.UI.Helpers
{
    /// <summary>
    /// Opens a file, folder or URL through the OS shell. Failures are logged and, when an owner
    /// window is available, shown to the user — never swallowed.
    /// </summary>
    public static class ShellLauncher
    {
        /// <summary>
        /// Launches <paramref name="target"/> with the default shell handler. Returns false when the
        /// launch failed (already logged, and reported to <paramref name="owner"/> when non-null).
        /// </summary>
        public static async Task<bool> TryOpenAsync(Window? owner, string target, string actionTitle)
        {
            if (string.IsNullOrWhiteSpace(target))
                return false;

            try
            {
                using Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                AppLogging.LogException($"Failed to open '{target}' ({actionTitle})", ex);

                if (owner != null)
                    await DialogService.ShowMessageAsync(owner, ex.Message, actionTitle);

                return false;
            }
        }
    }
}
