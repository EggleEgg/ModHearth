using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ModHearth;
using ModHearth.Utilities.Logging;
using ModHearth.Utilities.Steam;

namespace ModHearth.Utilities.Workshop
{
    public class SteamWorkerDownloadProvider : IWorkshopDownloadProvider
    {
        public string Name => "Steam Client (via SteamAPI)";
        public bool IsAvailable => new SteamWorkshopService().IsAvailable;

        public async Task<bool> DownloadAsync(
            ulong workshopId, 
            string downloadPath, 
            IProgress<DownloadProgress> progress, 
            CancellationToken cancellationToken)
        {
            progress.Report(new DownloadProgress(0, 100, 0));

            void OnProgress(ulong bytesDownloaded, ulong bytesTotal)
            {
                if (bytesTotal == 0)
                    return;

                double percentage = Math.Clamp((double)bytesDownloaded / bytesTotal * 100, 0, 100);
                progress.Report(new DownloadProgress((long)bytesDownloaded, (long)bytesTotal, percentage));
            }

            bool success = await Task.Run(() =>
                SteamWorkshopService.Download(workshopId, onProgress: OnProgress, cancellationToken: cancellationToken), cancellationToken);

            if (success)
            {
                progress.Report(new DownloadProgress(100, 100, 100));
                return true;
            }

            return false;
        }
    }

    public class SteamCmdDownloadProvider : IWorkshopDownloadProvider
    {
        private static readonly Regex ProgressRegex = new Regex(@"progress:\s+([0-9.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly ISteamCmdService _steamCmdService = new SteamCmdService();
        
        public string Name => "SteamCMD";
        public bool IsAvailable => _steamCmdService.IsAvailable();

        public async Task<bool> DownloadAsync(
            ulong workshopId, 
            string downloadPath, 
            IProgress<DownloadProgress> progress, 
            CancellationToken cancellationToken)
        {
            string appId = ConfigManager.DwarfFortressSteamAppId;
            string quotedPath = $"\"{Path.GetFullPath(downloadPath)}\"";
            
            // Support +force_install_dir "<path>" +login anonymous +workshop_download_item <appId> <workshopId> validate +quit
            string args = $"+force_install_dir {quotedPath} +login anonymous +workshop_download_item {appId} {workshopId} validate +quit";

            var progressProxy = new Progress<string>(line =>
            {
                var match = ProgressRegex.Match(line);
                if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                {
                    progress.Report(new DownloadProgress((long)(pct * 1000), 100000, pct));
                }
            });

            int exitCode = await _steamCmdService.ExecuteAsync(args, progressProxy, cancellationToken);
            if (exitCode != 0 && cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (Directory.Exists(downloadPath) && Directory.GetFileSystemEntries(downloadPath).Length > 0)
            {
                progress.Report(new DownloadProgress(100, 100, 100));
                return true;
            }

            // Fallback source check in steamapps/workshop/content
            string exe = _steamCmdService.GetExecutablePath();
            string steamCmdDir = Path.GetDirectoryName(Path.GetFullPath(exe)) ?? AppContext.BaseDirectory;
            string workshopSource = Path.Combine(steamCmdDir, "steamapps", "workshop", "content", appId, workshopId.ToString());

            if (!Directory.Exists(workshopSource))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string fallback = Path.Combine(home, ".steam", "steamcmd", "steamapps", "workshop", "content", appId, workshopId.ToString());
                if (Directory.Exists(fallback))
                    workshopSource = fallback;
            }

            if (Directory.Exists(workshopSource))
            {
                try
                {
                    CopyDirectory(workshopSource, downloadPath);
                    progress.Report(new DownloadProgress(100, 100, 100));
                    return true;
                }
                catch (Exception ex)
                {
                    if (DevMode.IsEnabled) AppLogging.LogException($"SteamCmd move failed from {workshopSource} to {downloadPath}", ex);
                    return false;
                }
            }

            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamCmd completed but downloaded directory not found at {workshopSource}");
            return false;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}
