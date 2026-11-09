using ModHearth.Utilities;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private volatile bool versionRefreshInFlight;

        /// <summary>
        /// Fills the DF/DFHack version labels from the files shipped with the installations. Disk
        /// access runs off the UI thread; the labels are assigned back on the UI thread.
        /// </summary>
        private async Task RefreshVersionLabelsAsync()
        {
            if (versionRefreshInFlight)
                return;
            versionRefreshInFlight = true;
            try
            {
                string? dfFolderPath = ConfigManager.Config?.DFFolderPath;
                string? dfHackFolderPath = ConfigManager.Config?.DFHackFolderPath;

                DwarfFortressVersionInfo versions = await Task.Run(
                    () => DwarfFortressVersionDetector.Detect(dfFolderPath, dfHackFolderPath));

                if (dfVersionLabel == null || dfhackVersionLabel == null)
                    return;

                dfVersionLabel.Text = versions.DwarfFortressVersion is { Length: > 0 } dfVersion
                    ? $"Df version {dfVersion}"
                    : "Df version unknown";

                dfhackVersionLabel.Text = versions.DFHackVersion is { Length: > 0 } dfHackVersion
                    ? $"DFHack {dfHackVersion}"
                    : "DFHack version unknown";
            }
            catch (Exception ex)
            {
                Loggers.Diag.LogWarning($"Failed to refresh version labels: {ex.Message}");
            }
            finally
            {
                versionRefreshInFlight = false;
            }
        }
    }
}
