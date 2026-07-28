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
            string fullDownloadPath = Path.GetFullPath(downloadPath);
            string quotedPath = $"\"{fullDownloadPath}\"";

            // +workshop_download_item does NOT extract directly into +force_install_dir -- it recreates
            // a normal Steam library layout inside it: <force_install_dir>/steamapps/workshop/content/<appId>/<id>/.
            // ModHearth's mod scanner expects info.txt directly inside downloadPath, so the real content
            // has to be relocated up out of that nesting once steamcmd finishes.
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

            // Checked independently of exitCode: a killed process's exit code isn't a reliable success/failure
            // signal, and even if steamcmd happened to finish successfully right as cancellation was requested,
            // the user's cancel intent should win rather than silently keeping an unwanted download.
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (exitCode != 0)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamCmd exited with code {exitCode} for {workshopId}; skipping content check.");
                return false;
            }

            string nestedContentDir = Path.Combine(fullDownloadPath, "steamapps", "workshop", "content", appId, workshopId.ToString());
            FlattenNestedContent(nestedContentDir, fullDownloadPath);

            if (HasModContent(fullDownloadPath))
            {
                progress.Report(new DownloadProgress(100, 100, 100));
                return true;
            }

            // Fallback: some steamcmd invocations (persistent installs without a per-item
            // force_install_dir) place content relative to steamcmd's own install directory instead.
            string exe = _steamCmdService.GetExecutablePath();
            string steamCmdDir = Path.GetDirectoryName(Path.GetFullPath(exe)) ?? AppContext.BaseDirectory;
            string workshopSource = Path.Combine(steamCmdDir, "steamapps", "workshop", "content", appId, workshopId.ToString());

            if (!Directory.Exists(workshopSource))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string homeFallback = Path.Combine(home, ".steam", "steamcmd", "steamapps", "workshop", "content", appId, workshopId.ToString());
                if (Directory.Exists(homeFallback))
                    workshopSource = homeFallback;
            }

            if (Directory.Exists(workshopSource))
            {
                try
                {
                    CopyDirectory(workshopSource, fullDownloadPath);
                }
                catch (Exception ex)
                {
                    AppLogging.LogException($"SteamCmd copy failed from {workshopSource} to {fullDownloadPath}", ex);
                    return false;
                }

                if (HasModContent(fullDownloadPath))
                {
                    progress.Report(new DownloadProgress(100, 100, 100));
                    return true;
                }
            }

            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"SteamCmd completed but no valid mod content was found for {workshopId} under '{nestedContentDir}' or '{workshopSource}'.");
            return false;
        }

        // TODO Windows limits command-line argument lengths to 8,191 characters. This may be an issue with large collections!
        public async Task<Dictionary<ulong, bool>> DownloadBatchAsync(
            IEnumerable<BatchDownloadItem> items,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<ulong, bool>();
            var itemList = items.ToList();
            if (itemList.Count == 0)
                return result;

            string appId = ConfigManager.DwarfFortressSteamAppId;
            string stagingPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ModHearth_Batch_" + Guid.NewGuid().ToString("N")));
            string quotedStaging = $"\"{stagingPath}\"";

            var sb = new System.Text.StringBuilder();
            sb.Append($"+force_install_dir {quotedStaging} +login anonymous");
            foreach (var item in itemList)
            {
                result[item.WorkshopId] = false;
                sb.Append($" +workshop_download_item {appId} {item.WorkshopId} validate");
            }
            sb.Append(" +quit");
            string args = sb.ToString();

            var progressProxy = new Progress<string>(line =>
            {
                var match = ProgressRegex.Match(line);
                if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                {
                    var prog = new DownloadProgress((long)(pct * 1000), 100000, pct);
                    foreach (var item in itemList)
                    {
                        item.Progress.Report(prog);
                    }
                }
            });

            int exitCode = await _steamCmdService.ExecuteAsync(args, progressProxy, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                try { Directory.Delete(stagingPath, true); } catch { }
                return result;
            }

            foreach (var item in itemList)
            {
                ulong workshopId = item.WorkshopId;
                string fullDownloadPath = Path.GetFullPath(item.DownloadPath);
                Directory.CreateDirectory(fullDownloadPath);

                string nestedContentDir = Path.Combine(stagingPath, "steamapps", "workshop", "content", appId, workshopId.ToString());
                FlattenNestedContent(nestedContentDir, fullDownloadPath);

                if (HasModContent(fullDownloadPath))
                {
                    item.Progress.Report(new DownloadProgress(100, 100, 100));
                    result[workshopId] = true;
                    continue;
                }

                string exe = _steamCmdService.GetExecutablePath();
                string steamCmdDir = Path.GetDirectoryName(Path.GetFullPath(exe)) ?? AppContext.BaseDirectory;
                string workshopSource = Path.Combine(steamCmdDir, "steamapps", "workshop", "content", appId, workshopId.ToString());

                if (!Directory.Exists(workshopSource))
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string homeFallback = Path.Combine(home, ".steam", "steamcmd", "steamapps", "workshop", "content", appId, workshopId.ToString());
                    if (Directory.Exists(homeFallback))
                        workshopSource = homeFallback;
                }

                if (Directory.Exists(workshopSource))
                {
                    try
                    {
                        CopyDirectory(workshopSource, fullDownloadPath);
                    }
                    catch (Exception ex)
                    {
                        AppLogging.LogException($"SteamCmd copy failed from {workshopSource} to {fullDownloadPath}", ex);
                    }

                    if (HasModContent(fullDownloadPath))
                    {
                        item.Progress.Report(new DownloadProgress(100, 100, 100));
                        result[workshopId] = true;
                        continue;
                    }
                }
            }

            try { Directory.Delete(stagingPath, true); } catch { }
            return result;
        }

        // Moves the actual downloaded content up out of steamcmd's nested
        // steamapps/workshop/content/<appId>/<id>/ layout into downloadPath directly, then removes the
        // now-empty scaffolding (steamapps/, logs/, etc.) steamcmd left behind so it doesn't linger
        // next to the flattened content or confuse a future re-download into the same folder.
        private static void FlattenNestedContent(string nestedContentDir, string downloadPath)
        {
            if (!Directory.Exists(nestedContentDir))
                return;

            try
            {
                foreach (string filePath in Directory.GetFiles(nestedContentDir))
                {
                    string destFile = Path.Combine(downloadPath, Path.GetFileName(filePath));
                    if (File.Exists(destFile))
                        File.Delete(destFile);
                    File.Move(filePath, destFile);
                }
                foreach (string subDir in Directory.GetDirectories(nestedContentDir))
                {
                    string destSubDir = Path.Combine(downloadPath, Path.GetFileName(subDir));
                    if (Directory.Exists(destSubDir))
                        Directory.Delete(destSubDir, true);
                    Directory.Move(subDir, destSubDir);
                }

                string scaffoldRoot = Path.Combine(downloadPath, "steamapps");
                if (Directory.Exists(scaffoldRoot))
                {
                    try { Directory.Delete(scaffoldRoot, true); } catch { /* best effort cleanup */ }
                }
            }
            catch (Exception ex)
            {
                AppLogging.LogException($"SteamCmd flatten failed from {nestedContentDir} to {downloadPath}", ex);
            }
        }

        private static bool HasModContent(string path)
        {
            return !string.IsNullOrWhiteSpace(ConfigManager.ResolveInfoFilePath(path));
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
