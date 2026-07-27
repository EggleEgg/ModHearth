using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ModHearth;
using ModHearth.Utilities.Logging;

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
            progress.Report(new DownloadProgress(0, 100, 5));

            // Run the synchronous Steam API download on a background thread
            var downloadTask = Task.Run(() => SteamWorkshopService.Download(workshopId), cancellationToken);

            // Poll while waiting for Steam to finish downloading
            int pseudoProgress = 10;
            while (!downloadTask.IsCompleted)
            {
                await Task.Delay(250, cancellationToken);
                
                // If SteamWorkshopService has a method to get progress, call it here.
                // Otherwise, keep the UI responsive showing an active download state.
                if (pseudoProgress < 90)
                {
                    pseudoProgress += 5;
                    progress.Report(new DownloadProgress(pseudoProgress, 100, pseudoProgress));
                }
            }

            bool success = await downloadTask;
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
        
        public string Name => "SteamCMD";
        public bool IsAvailable
        {
            get
            {
                string cmd = OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd";
                return TryFindInPath(cmd) || File.Exists(Path.Combine(AppContext.BaseDirectory, cmd));
            }
        }

        public string SteamCmdPath { get; set; } = string.Empty;

        private string ResolveExecutable()
        {
            if (!string.IsNullOrEmpty(SteamCmdPath) && File.Exists(SteamCmdPath))
                return SteamCmdPath;
            
            string cmd = OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd";
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, cmd)))
                return Path.Combine(AppContext.BaseDirectory, cmd);

            return cmd; // fallback to system PATH
        }

        public async Task<bool> DownloadAsync(
            ulong workshopId, 
            string downloadPath, 
            IProgress<DownloadProgress> progress, 
            CancellationToken cancellationToken)
        {
            string exe = ResolveExecutable();
            string appId = ConfigManager.DwarfFortressSteamAppId;
            
            // steamcmd +login anonymous +workshop_download_item <appId> <workshopId> +quit
            string args = $"+login anonymous +workshop_download_item {appId} {workshopId} +quit";
            
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamCmd: Running {exe} {args}");

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                if (DevMode.IsEnabled) AppLogging.LogException("SteamCmd start failed", ex);
                return false;
            }

            var outputTask = Task.Run(async () =>
            {
                var charBuffer = new char[1024];
                var lineBuilder = new System.Text.StringBuilder();

                while (!process.StandardOutput.EndOfStream)
                {
                    int read = await process.StandardOutput.ReadAsync(charBuffer, 0, charBuffer.Length);
                    if (read == 0) break;

                    for (int i = 0; i < read; i++)
                    {
                        char c = charBuffer[i];
                        if (c == '\r' || c == '\n')
                        {
                            string line = lineBuilder.ToString();
                            lineBuilder.Clear();

                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                var match = ProgressRegex.Match(line);
                                if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                                {
                                    progress.Report(new DownloadProgress((long)(pct * 1000), 100000, pct));
                                }
                            }
                        }
                        else
                        {
                            lineBuilder.Append(c);
                        }
                    }
                }
            }, cancellationToken);

            await Task.Run(() => process.WaitForExit());
            await outputTask;

            if (process.ExitCode != 0)
            {
                return false;
            }

            // Move files from SteamCMD download location to target ModHearth downloadPath
            // Default steamcmd workshop directory is under: <steamcmd_dir>/steamapps/workshop/content/<appId>/<workshopId>
            string steamCmdDir = Path.GetDirectoryName(Path.GetFullPath(exe)) ?? AppContext.BaseDirectory;
            string workshopSource = Path.Combine(steamCmdDir, "steamapps", "workshop", "content", appId, workshopId.ToString());

            if (!Directory.Exists(workshopSource))
            {
                // Try searching in system steamcmd locations (e.g. ~/.steam/steam/steamapps/workshop/content/ or similar)
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

        private static bool TryFindInPath(string tool)
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(Path.Combine(path, tool)))
                        return true;
                }
                catch { /* ignore */ }
            }
            return false;
        }
    }
}
