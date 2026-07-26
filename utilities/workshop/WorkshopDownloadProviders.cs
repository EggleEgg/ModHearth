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
        public string Name => "Steam Client (via Worker)";
        public bool IsAvailable => new SteamWorkshopService().IsAvailable;

        public Task<bool> DownloadAsync(
            ulong workshopId, 
            string downloadPath, 
            IProgress<DownloadProgress> progress, 
            CancellationToken cancellationToken)
        {
            progress.Report(new DownloadProgress(0, 100, 10));
            
            // Execute SteamWorkshopService.Download
            bool success = SteamWorkshopService.Download(workshopId);
            
            if (success)
            {
                progress.Report(new DownloadProgress(100, 100, 100));
                return Task.FromResult(true);
            }
            
            return Task.FromResult(false);
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
                while (!process.StandardOutput.EndOfStream)
                {
                    string? line = await process.StandardOutput.ReadLineAsync();
                    if (line == null) break;

                    var match = ProgressRegex.Match(line);
                    if (match.Success && double.TryParse(match.Groups[1].Value, out double pct))
                    {
                        progress.Report(new DownloadProgress((long)(pct * 1000), 100000, pct));
                    }
                }
            });

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

    public class WorkshopDlDownloadProvider : IWorkshopDownloadProvider
    {
        private static readonly Regex ProgressRegex = new Regex(@"([0-9.]+)%", RegexOptions.Compiled);

        public string Name => "WorkshopDL";
        public bool IsAvailable
        {
            get
            {
                string cmd = OperatingSystem.IsWindows() ? "workshop-dl.exe" : "workshop-dl";
                return TryFindInPath(cmd) || File.Exists(Path.Combine(AppContext.BaseDirectory, cmd));
            }
        }

        public string WorkshopDlPath { get; set; } = string.Empty;

        private string ResolveExecutable()
        {
            if (!string.IsNullOrEmpty(WorkshopDlPath) && File.Exists(WorkshopDlPath))
                return WorkshopDlPath;

            string cmd = OperatingSystem.IsWindows() ? "workshop-dl.exe" : "workshop-dl";
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, cmd)))
                return Path.Combine(AppContext.BaseDirectory, cmd);

            return cmd;
        }

        public async Task<bool> DownloadAsync(
            ulong workshopId, 
            string downloadPath, 
            IProgress<DownloadProgress> progress, 
            CancellationToken cancellationToken)
        {
            string exe = ResolveExecutable();
            // workshop-dl -i <workshopId> -o <downloadPath>
            string args = $"-i {workshopId} -o \"{downloadPath}\"";

            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDL: Running {exe} {args}");

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
                if (DevMode.IsEnabled) AppLogging.LogException("WorkshopDL start failed", ex);
                return false;
            }

            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    string? line = await process.StandardOutput.ReadLineAsync();
                    if (line == null) break;

                    var match = ProgressRegex.Match(line);
                    if (match.Success && double.TryParse(match.Groups[1].Value, out double pct))
                    {
                        progress.Report(new DownloadProgress((long)(pct * 1000), 100000, pct));
                    }
                }
            });

            await Task.Run(() => process.WaitForExit());
            await outputTask;

            if (process.ExitCode == 0)
            {
                progress.Report(new DownloadProgress(100, 100, 100));
                return true;
            }

            return false;
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
