using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using ModHearth.Config;
using ModHearth.Utils.IO;
using ModHearth.Utils.Logging;

namespace ModHearth.Utils.Steam
{
    public class SteamCmdService : ISteamCmdService
    {
        private const string Steamcmd = "steamcmd";
        private const string SteamcmdExe = "steamcmd.exe";
        private const string SteamcmdSh = "steamcmd.sh";
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

        // Stage boundaries for overall installation progress. The self-update receives the largest
        // share because it downloads the bulk of SteamCMD's files on first launch.
        private const double DownloadProgressStart = 0;
        private const double DownloadProgressEnd = 25;
        private const double ExtractProgressEnd = 30;
        private const double SelfUpdateProgressEnd = 92;
        private const double SelfUpdateCompleteProgress = 95;
        private const double ValidationProgress = 98;
        private const double CompleteProgress = 100;

        // Self-update stages, expressed as a percentage of that stage rather than the whole
        // installation. SteamCMD's bootstrap updater reports these through "[ 42%] ..." lines.
        private const double SelfUpdateDownloadStageEnd = 55;
        private const double SelfUpdateDownloadCompleteStage = 58;
        private const double SelfUpdateExtractStage = 65;
        private const double SelfUpdateInstallStage = 78;
        private const double SelfUpdateCleanupStage = 92;
        private const double SelfUpdateCompleteStage = 100;

        public string GetExecutablePath()
        {
            string? found = FindExisting();
            if (!string.IsNullOrEmpty(found))
                return found;

            return OperatingSystem.IsWindows()
                ? Path.Combine(AppContext.BaseDirectory, Steamcmd, SteamcmdExe)
                : Path.Combine(AppContext.BaseDirectory, Steamcmd, SteamcmdSh);
        }

        public bool IsAvailable()
        {
            string? found = FindExisting();
            return !string.IsNullOrEmpty(found) && File.Exists(found);
        }

        public string? FindExisting()
        {
            // 1. Check config
            string configPath = ConfigManager.Config.SteamCmdPath;
            if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                return Path.GetFullPath(configPath);

            // 2. Check local folder inside ModHearth root
            string localDir = Path.Combine(AppContext.BaseDirectory, Steamcmd);
            string localExe = OperatingSystem.IsWindows()
                ? Path.Combine(localDir, SteamcmdExe)
                : Path.Combine(localDir, SteamcmdSh);
            if (File.Exists(localExe))
                return Path.GetFullPath(localExe);

            // 3. On Linux, check system PATH
            if (OperatingSystem.IsLinux() && TryFindInPath(Steamcmd, out string systemPath))
                return systemPath;

            return null;
        }

        public async Task<bool> ValidateAsync(string exePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            int exitCode = await ExecuteCommandInternalAsync(exePath, "+quit", null, cancellationToken);
            if (exitCode != 0)
                return false;

            string dir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            if (OperatingSystem.IsWindows())
            {
                return File.Exists(Path.Combine(dir, SteamcmdExe));
            }
            else
            {
                return File.Exists(Path.Combine(dir, SteamcmdSh)) || File.Exists(Path.Combine(dir, Steamcmd));
            }
        }

        public async Task<bool> InstallAsync(string installDir, IProgress<SteamCmdInstallProgress> progress, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(progress);
            InstallProgressReporter reporter = new(progress);

            try
            {
                reporter.Report(DownloadProgressStart, "Creating installation directory...");
                _ = Directory.CreateDirectory(installDir);

                bool isWindows = OperatingSystem.IsWindows();
                string archiveName = isWindows ? "steamcmd.zip" : "steamcmd_linux.tar.gz";
                string archivePath = Path.Combine(installDir, archiveName);
                string url = isWindows
                    ? "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"
                    : "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";

                if (File.Exists(archivePath))
                {
                    reporter.Report(DownloadProgressEnd, "Using previously downloaded SteamCMD archive.");
                }
                else
                {
                    await DownloadArchiveAsync(url, archivePath, reporter, cancellationToken);
                }

                reporter.Report(DownloadProgressEnd, "Extracting SteamCMD archive...");
                await ExtractArchiveAsync(archivePath, installDir, isWindows, cancellationToken);
                reporter.Report(ExtractProgressEnd, "SteamCMD archive extracted.");

                string exe = isWindows
                    ? Path.Combine(installDir, SteamcmdExe)
                    : Path.Combine(installDir, SteamcmdSh);

                if (!File.Exists(exe))
                {
                    throw new FileNotFoundException($"Expected SteamCMD executable not found at {exe}");
                }

                reporter.Report(ExtractProgressEnd, "Running initial SteamCMD self-update and setup...");
                // Launch once to allow SteamCMD to self-update and install required files
                _ = await ExecuteCommandInternalAsync(exe, "+quit", new SelfUpdateProgressReporter(reporter), cancellationToken);

                reporter.Report(SelfUpdateCompleteProgress, "SteamCMD self-update complete.");

                reporter.Report(ValidationProgress, "Validating installation...");
                bool isValid = await ValidateAsync(exe, cancellationToken);
                if (!isValid)
                {
                    throw new InvalidOperationException("SteamCMD validation failed after installation.");
                }

                // Save installation path in config
                ConfigManager.SetSteamCmdPath(exe);

                reporter.Report(CompleteProgress, "SteamCMD setup completed successfully.");
                return true;
            }
            catch (OperationCanceledException)
            {
                reporter.ReportFailure("Installation cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                AppLogging.LogException("SteamCmd Install failed", ex);
                reporter.ReportFailure($"Error: {ex.Message}");
                throw;
            }
        }

        private static async Task DownloadArchiveAsync(
            string url,
            string archivePath,
            InstallProgressReporter progress,
            CancellationToken cancellationToken)
        {
            const int maxRetries = 3;
            int delayMs = 1000;
            string partialPath = archivePath + ".part";

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = FileSystemHelper.TryDeleteFile(partialPath, "incomplete SteamCMD archive");

                try
                {
                    progress.Report(DownloadProgressStart, $"Downloading SteamCMD (attempt {attempt}/{maxRetries})...");
                    using HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    long totalRead = 0;
                    int lastWholePercent = -1;
                    long lastReportedMegabyte = -1;

                    await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (FileStream destination = new(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                    {
                        byte[] buffer = new byte[81920];
                        int bytesRead;
                        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            totalRead += bytesRead;

                            if (totalBytes is > 0)
                            {
                                double overall = DownloadProgressStart
                                    + (Math.Clamp((double)totalRead / totalBytes.Value, 0, 1) * (DownloadProgressEnd - DownloadProgressStart));
                                int wholePercent = (int)Math.Floor(overall);
                                if (wholePercent != lastWholePercent)
                                {
                                    lastWholePercent = wholePercent;
                                    progress.Report(overall, $"Downloading SteamCMD (attempt {attempt}/{maxRetries}): {FormatMegabytes(totalRead)} / {FormatMegabytes(totalBytes.Value)}");
                                }
                            }
                            else
                            {
                                long megabyte = totalRead / (1024 * 1024);
                                if (megabyte != lastReportedMegabyte)
                                {
                                    lastReportedMegabyte = megabyte;
                                    progress.Report(DownloadProgressStart, $"Downloading SteamCMD (attempt {attempt}/{maxRetries}): {FormatMegabytes(totalRead)} downloaded");
                                }
                            }
                        }
                    }

                    File.Move(partialPath, archivePath, overwrite: true);
                    progress.Report(DownloadProgressEnd, "SteamCMD archive downloaded.");
                    return;
                }
                catch (OperationCanceledException)
                {
                    _ = FileSystemHelper.TryDeleteFile(partialPath, "incomplete SteamCMD archive");
                    throw;
                }
                catch (Exception ex)
                {
                    _ = FileSystemHelper.TryDeleteFile(partialPath, "incomplete SteamCMD archive");

                    if (attempt == maxRetries)
                        throw new IOException($"Failed to download SteamCMD after {maxRetries} attempts: {ex.Message}", ex);

                    progress.Report(DownloadProgressStart, $"Download failed ({ex.Message}). Retrying in {delayMs / 1000}s...");
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2;
                }
            }
        }

        private static async Task ExtractArchiveAsync(
            string archivePath,
            string installDir,
            bool isWindows,
            CancellationToken cancellationToken)
        {
            try
            {
                if (isWindows)
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, installDir, true), cancellationToken);
                    return;
                }

                string extractedTar = Path.Combine(installDir, "steamcmd.tar");
                try
                {
                    await using (FileStream archiveStream = File.OpenRead(archivePath))
                    await using (GZipStream gzipStream = new(archiveStream, CompressionMode.Decompress))
                    await using (FileStream tarStream = File.Create(extractedTar))
                    {
                        await gzipStream.CopyToAsync(tarStream, cancellationToken);
                    }

                    await TarFile.ExtractToDirectoryAsync(extractedTar, installDir, true, cancellationToken);

                    string shPath = Path.Combine(installDir, SteamcmdSh);
                    if (File.Exists(shPath))
                    {
                        // Ensure executable
                        using Process chmodProcess = Process.Start("chmod", $"+x \"{shPath}\"");
                        if (chmodProcess != null)
                        {
                            await chmodProcess.WaitForExitAsync(cancellationToken);
                        }
                    }
                }
                finally
                {
                    _ = FileSystemHelper.TryDeleteFile(extractedTar, "temporary extracted tar file");
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A damaged cached archive would otherwise make every retry fail at the same point.
                _ = FileSystemHelper.TryDeleteFile(archivePath, "invalid SteamCMD archive");
                throw;
            }
        }

        private static string FormatMegabytes(long bytes) => $"{bytes / (1024d * 1024d):F1} MB";

        private sealed class InstallProgressReporter(IProgress<SteamCmdInstallProgress> progress)
        {
            private readonly Lock gate = new();
            private double lastPercentage;

            public void Report(double percentage, string message)
            {
                double clamped = Math.Clamp(percentage, 0, 100);
                lock (gate)
                {
                    // Installation progress is cumulative. A retried download reports its own
                    // progress from zero, which must not move the overall bar backwards.
                    clamped = Math.Max(clamped, lastPercentage);
                    lastPercentage = clamped;
                }

                progress.Report(new SteamCmdInstallProgress(clamped, message));
            }

            /// <summary>Reports a failure at the last known percentage so the bar does not jump.</summary>
            public void ReportFailure(string message)
            {
                double percentage;
                lock (gate)
                    percentage = lastPercentage;

                progress.Report(new SteamCmdInstallProgress(percentage, message));
            }
        }

        /// <summary>
        /// Maps SteamCMD's stage-local self-update output onto the self-update portion of the
        /// overall installation bar. The bootstrap updater reports "[ 42%] Downloading update..."
        /// and "[----] Installing update..." lines, while the client reports "progress: 42"
        /// lines. Multiple phases can each restart at 0%, so the overall value stays monotonic.
        /// </summary>
        private sealed class SelfUpdateProgressReporter(InstallProgressReporter progress) : IProgress<string>
        {
            private readonly Lock gate = new();
            private double lastOverall = ExtractProgressEnd;

            public void Report(string value)
            {
                if (!TryMapStage(value, out double stagePercent, out string message))
                    return;

                double overall = ExtractProgressEnd
                    + (Math.Clamp(stagePercent, 0, 100) / 100 * (SelfUpdateProgressEnd - ExtractProgressEnd));

                lock (gate)
                {
                    if (overall <= lastOverall)
                        return;
                    lastOverall = overall;
                }

                progress.Report(overall, message);
            }

            private static bool TryMapStage(string line, out double stagePercent, out string message)
            {
                if (SteamCmdOutputParser.TryParseBracketLine(line, out double? bracketPercent, out string bracketMessage))
                {
                    stagePercent = MapBracketStage(bracketMessage, bracketPercent);
                    message = $"SteamCMD self-update: {bracketMessage}";
                    return true;
                }

                if (SteamCmdOutputParser.TryParseProgressPercent(line, out double progressPercent))
                {
                    stagePercent = Math.Clamp(progressPercent, 0, 100) / 100 * SelfUpdateDownloadStageEnd;
                    message = $"Updating SteamCMD... ({progressPercent:F0}%)";
                    return true;
                }

                stagePercent = 0;
                message = string.Empty;
                return false;
            }

            private static double MapBracketStage(string message, double? percent)
            {
                if (message.Contains("Downloading update", StringComparison.OrdinalIgnoreCase))
                {
                    double downloadPercent = percent ?? 0;
                    return Math.Clamp(downloadPercent, 0, 100) / 100 * SelfUpdateDownloadStageEnd;
                }

                if (message.Contains("Download complete", StringComparison.OrdinalIgnoreCase))
                    return SelfUpdateDownloadCompleteStage;

                if (message.Contains("Extracting package", StringComparison.OrdinalIgnoreCase))
                    return SelfUpdateExtractStage;

                if (message.Contains("Installing update", StringComparison.OrdinalIgnoreCase))
                    return SelfUpdateInstallStage;

                if (message.Contains("Cleaning up", StringComparison.OrdinalIgnoreCase))
                    return SelfUpdateCleanupStage;

                if (message.Contains("Update complete", StringComparison.OrdinalIgnoreCase))
                    return SelfUpdateCompleteStage;

                if (message.Contains("Verifying installation", StringComparison.OrdinalIgnoreCase))
                    return 1;

                // "Checking for available updates..." and any other startup messages.
                return 0;
            }
        }

        public async Task<int> ExecuteAsync(string arguments, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            string exe = GetExecutablePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                throw new FileNotFoundException($"SteamCMD executable not found at {exe}");
            }

            return await ExecuteCommandInternalAsync(exe, arguments, progress, cancellationToken);
        }

        private static async Task<int> ExecuteCommandInternalAsync(string exe, string arguments, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Loggers.Workshop.Log($"SteamCmd: Executing {exe} {arguments}");

            using Process process = new() { StartInfo = startInfo };
            try
            {
                _ = process.Start();
            }
            catch (Exception ex)
            {
                AppLogging.LogException("SteamCmd execution failed to start", ex);
                throw;
            }

            using CancellationTokenRegistration reg = cancellationToken.Register(() =>
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    AppLogging.LogException("Failed to kill SteamCMD process on cancellation registration", killEx);
                }
            });

            Task outputTask = Task.Run(async () =>
            {
                using StreamReader reader = process.StandardOutput;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        progress?.Report(line);
                        Loggers.Workshop.Log($"SteamCmd stdout: {line}");
                    }
                }
            }, cancellationToken);

            Task errorTask = Task.Run(async () =>
            {
                using StreamReader reader = process.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        progress?.Report($"[ERR] {line}");
                        Loggers.Workshop.Log($"SteamCmd stderr: {line}");
                    }
                }
            }, cancellationToken);

            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception killEx)
                    {
                        AppLogging.LogException("Failed to kill SteamCMD process on cancellation request", killEx);
                    }
                    break;
                }
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch (Exception taskEx)
            {
                AppLogging.LogException("Error waiting for SteamCMD output/error reader tasks", taskEx);
            }

            return process.HasExited ? process.ExitCode : -1;
        }

        private static bool TryFindInPath(string tool, out string fullPath)
        {
            fullPath = string.Empty;
            string[] paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
            foreach (string path in paths)
            {
                try
                {
                    string candidate = Path.Combine(path, tool);
                    if (File.Exists(candidate))
                    {
                        fullPath = Path.GetFullPath(candidate);
                        return true;
                    }
                }
                catch (Exception pathEx)
                {
                    AppLogging.LogException($"Failed to inspect path candidate '{path}' during steamcmd search", pathEx);
                }
            }
            return false;
        }
    }
}
