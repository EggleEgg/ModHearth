using System.Globalization;
using System.Text.RegularExpressions;
using ModHearth.Utilities.Logging;
using ModHearth.Utilities.Steam;

namespace ModHearth.Utilities.Workshop
{
    public class SteamWorkerDownloadProvider : IWorkshopDownloadProvider
    {
        public string Name => "Steam Client (via SteamAPI)";
        public bool IsAvailable => SteamWorkshopService.IsAvailable;

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

    public partial class SteamCmdDownloadProvider : IWorkshopDownloadProvider
    {
        private readonly SteamCmdService _steamCmdService = new();

        public const string SteamApps = "steamapps";
        private const string Workshop = "workshop";
        private const string Content = "content";

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

            Progress<string> progressProxy = new(line =>
            {
                if (TryParseProgressPercent(line, out double pct))
                    progress.Report(new DownloadProgress((long)(pct * 1000), 100000, pct));
            });

            int exitCode = await _steamCmdService.ExecuteAsync(args, progressProxy, cancellationToken);

            // Checked independently of exitCode: a killed process's exit code isn't a reliable success/failure
            // signal, and even if steamcmd happened to finish successfully right as cancellation was requested,
            // the user's cancel intent should win rather than silently keeping an unwanted download.
            if (cancellationToken.IsCancellationRequested)
                return false;

            if (exitCode != 0)
            {
                Loggers.Workshop.Log($"SteamCmd exited with code {exitCode} for {workshopId}; skipping content check.");
                return false;
            }

            string nestedContentDir = Path.Combine(fullDownloadPath, SteamApps, Workshop, Content, appId, workshopId.ToString());
            string workshopSource = ResolveWorkshopSource(GetSteamCmdInstallDirectory(), appId, workshopId);

            // Fallback only matters when steamcmd ignored +force_install_dir and placed content relative
            // to its own install directory instead.
            if (TryFinalizeDownload(nestedContentDir, fullDownloadPath, workshopSource, progress))
                return true;

            Loggers.Workshop.Log($"SteamCmd completed but no valid mod content was found for {workshopId} under '{nestedContentDir}' or '{workshopSource}'.");
            return false;
        }

        public async Task<Dictionary<ulong, bool>> DownloadBatchAsync(
            IEnumerable<BatchDownloadItem> items,
            CancellationToken cancellationToken)
        {
            Dictionary<ulong, bool> result = [];
            List<BatchDownloadItem> itemList = [.. items];
            if (itemList.Count == 0)
                return result;

            string appId = ConfigManager.DwarfFortressSteamAppId;
            string stagingPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ModHearth_Batch_" + Guid.NewGuid().ToString("N")));
            _ = Directory.CreateDirectory(stagingPath);

            string scriptPath = Path.Combine(stagingPath, "download_batch.txt");
            try
            {
                using StreamWriter writer = new(scriptPath);
                await writer.WriteLineAsync($"force_install_dir \"{stagingPath}\"");
                await writer.WriteLineAsync("login anonymous");
                foreach (BatchDownloadItem? item in itemList)
                {
                    result[item.WorkshopId] = false;
                    await writer.WriteLineAsync($"workshop_download_item {appId} {item.WorkshopId} validate");
                }
                await writer.WriteLineAsync("quit");
            }
            catch (Exception ex)
            {
                AppLogging.LogException($"Failed to write SteamCMD batch script at {scriptPath}", ex);
                TryDeleteDirectory(stagingPath, "staging directory");
                return result;
            }

            string args = $"+runscript \"{scriptPath}\"";

            ulong currentlyDownloadingId = 0;
            Dictionary<ulong, BatchDownloadItem> itemLookup = itemList.ToDictionary(i => i.WorkshopId);

            Progress<string> progressProxy = new(line =>
            {
                if (line.Contains("Downloading item", StringComparison.OrdinalIgnoreCase))
                {
                    Match matchId = DownloadingItemRegex().Match(line);
                    if (matchId.Success && ulong.TryParse(matchId.Groups[1].Value, out ulong id))
                        currentlyDownloadingId = id;
                }

                if (!TryParseProgressPercent(line, out double pct))
                    return;

                if (currentlyDownloadingId != 0 && itemLookup.TryGetValue(currentlyDownloadingId, out BatchDownloadItem? activeItem))
                {
                    activeItem.Progress.Report(new DownloadProgress((long)(pct * 1000), 100000, pct));
                }
            });

            try
            {
                int exitCode = await _steamCmdService.ExecuteAsync(args, progressProxy, cancellationToken);

                if (exitCode != 0)
                    Loggers.Workshop.Log($"SteamCmd exited with code {exitCode} for {itemList.Count} batched elements; skipping content check.");

                if (cancellationToken.IsCancellationRequested)
                {
                    return result;
                }

                foreach (BatchDownloadItem? item in itemList)
                {
                    ulong workshopId = item.WorkshopId;
                    string fullDownloadPath = Path.GetFullPath(item.DownloadPath);
                    _ = Directory.CreateDirectory(fullDownloadPath);

                    string nestedContentDir = Path.Combine(stagingPath, SteamApps, Workshop, Content, appId, workshopId.ToString());
                    string workshopSource = ResolveWorkshopSource(GetSteamCmdInstallDirectory(), appId, workshopId);

                    if (TryFinalizeDownload(nestedContentDir, fullDownloadPath, workshopSource, item.Progress))
                        result[workshopId] = true;
                }
            }
            finally
            {
                TryDeleteDirectory(stagingPath, "staging directory");
            }

            return result;
        }

        // Moves the actual downloaded content up out of steamcmd's nested
        // steamapps/workshop/content/<appId>/<id>/ layout into downloadPath directly, then removes the
        // now-empty scaffolding (steamapps/, logs/, etc.) steamcmd left behind so it doesn't linger
        // next to the flattened content or confuse a future re-download into the same folder.
        private string GetSteamCmdInstallDirectory()
        {
            string exe = _steamCmdService.GetExecutablePath();
            return Path.GetDirectoryName(Path.GetFullPath(exe)) ?? AppContext.BaseDirectory;
        }

        /// <summary>
        /// Resolves where steamcmd places content when it ignores +force_install_dir: relative to its
        /// own install directory, or the per-user ~/.steam/steamcmd fallback.
        /// </summary>
        private static string ResolveWorkshopSource(string steamCmdDir, string appId, ulong workshopId)
        {
            string workshopSource = Path.Combine(steamCmdDir, SteamApps, Workshop, Content, appId, workshopId.ToString());
            if (Directory.Exists(workshopSource))
                return workshopSource;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string homeFallback = Path.Combine(home, ".steam", "steamcmd", SteamApps, Workshop, Content, appId, workshopId.ToString());
            return Directory.Exists(homeFallback) ? homeFallback : workshopSource;
        }

        private static void FlattenNestedContent(string nestedContentDir, string downloadPath)
        {
            if (!Directory.Exists(nestedContentDir))
                return;

            try
            {
                _ = Directory.CreateDirectory(downloadPath);
                foreach (string filePath in Directory.GetFiles(nestedContentDir))
                {
                    string destFile = Path.Combine(downloadPath, Path.GetFileName(filePath));
                    if (File.Exists(destFile))
                        File.Delete(destFile);
                    try
                    {
                        File.Move(filePath, destFile);
                    }
                    catch (IOException)
                    {
                        File.Copy(filePath, destFile, true);
                        try { File.Delete(filePath); } catch (Exception ex) { AppLogging.LogException($"Failed to delete file {filePath}", ex); }
                    }
                }
                foreach (string subDir in Directory.GetDirectories(nestedContentDir))
                {
                    string destSubDir = Path.Combine(downloadPath, Path.GetFileName(subDir));
                    if (Directory.Exists(destSubDir))
                        Directory.Delete(destSubDir, true);
                    try
                    {
                        Directory.Move(subDir, destSubDir);
                    }
                    catch (IOException)
                    {
                        MoveOrCopyDirectory(subDir, destSubDir);
                        try { Directory.Delete(subDir, true); } catch (Exception ex) { AppLogging.LogException($"Failed to delete subdirectory {subDir}", ex); }
                    }
                }

                string scaffoldRoot = Path.Combine(downloadPath, SteamApps);
                if (Directory.Exists(scaffoldRoot))
                {
                    try { Directory.Delete(scaffoldRoot, true); } catch (Exception ex) { AppLogging.LogException($"Failed to delete scaffold root {scaffoldRoot}", ex); }
                }
            }
            catch (Exception ex)
            {
                AppLogging.LogException($"SteamCmd flatten failed from {nestedContentDir} to {downloadPath}", ex);
            }
        }

        private static bool HasModContent(string path) => !string.IsNullOrWhiteSpace(ConfigManager.ResolveInfoFilePath(path));

        /// <summary>
        /// Flattens steamcmd's nested library layout into <paramref name="targetPath"/>, then falls back
        /// to copying from steamcmd's own install directory when +force_install_dir was ignored. Reports
        /// 100% and returns true only when valid mod content ends up in the target folder.
        /// </summary>
        private static bool TryFinalizeDownload(
            string nestedContentDir,
            string targetPath,
            string workshopSource,
            IProgress<DownloadProgress> progress)
        {
            FlattenNestedContent(nestedContentDir, targetPath);

            if (!HasModContent(targetPath))
            {
                if (!Directory.Exists(workshopSource))
                    return false;

                try
                {
                    MoveOrCopyDirectory(workshopSource, targetPath);
                }
                catch (Exception ex)
                {
                    AppLogging.LogException($"SteamCmd copy failed from {workshopSource} to {targetPath}", ex);
                    return false;
                }

                if (!HasModContent(targetPath))
                    return false;
            }

            progress.Report(new DownloadProgress(100, 100, 100));
            return true;
        }

        private static void TryDeleteDirectory(string path, string description)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                AppLogging.LogException($"Failed to delete {description} at {path}", ex);
            }
        }

        private static bool TryParseProgressPercent(string line, out double percent)
        {
            percent = 0;
            if (!line.Contains("progress:", StringComparison.OrdinalIgnoreCase))
                return false;

            Match match = ProgressRegex().Match(line);
            return match.Success
                && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out percent);
        }

        private static void MoveOrCopyDirectory(string sourceDir, string destinationDir)
        {
            _ = Directory.CreateDirectory(destinationDir);
            try
            {
                // Fast path: attempt moving files directly
                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                    if (File.Exists(destFile)) File.Delete(destFile);
                    File.Move(file, destFile);
                }
                foreach (string subDir in Directory.GetDirectories(sourceDir))
                {
                    string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                    MoveOrCopyDirectory(subDir, destSubDir);
                }
            }
            catch (IOException)
            {
                // Fallback for cross-volume moves
                CopyDirectory(sourceDir, destinationDir);
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            _ = Directory.CreateDirectory(destinationDir);
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

        [GeneratedRegex(@"progress:\s+([0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex ProgressRegex();
        [GeneratedRegex(@"Downloading item (\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex DownloadingItemRegex();
    }
}
