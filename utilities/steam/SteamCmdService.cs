using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Formats.Tar;
using System.Threading;
using System.Threading.Tasks;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.Steam
{
    public class SteamCmdService : ISteamCmdService
    {
        private const string Steamcmd = "steamcmd";
        private const string SteamcmdExe = "steamcmd.exe";
        private const string SteamcmdSh = "steamcmd.sh";
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

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

        public async Task<bool> InstallAsync(string installDir, IProgress<string> progress, CancellationToken cancellationToken = default)
        {
            try
            {
                progress.Report("Creating installation directory...");
                _ = Directory.CreateDirectory(installDir);

                bool isWindows = OperatingSystem.IsWindows();
                string archiveName = isWindows ? "steamcmd.zip" : "steamcmd_linux.tar.gz";
                string archivePath = Path.Combine(installDir, archiveName);
                string url = isWindows
                    ? "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"
                    : "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";

                // Download with retries and exponential backoff
                if (!File.Exists(archivePath))
                {
                    bool downloaded = false;
                    int maxRetries = 3;
                    int delayMs = 1000;

                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            progress.Report($"Downloading SteamCMD (attempt {attempt}/{maxRetries})...");
                            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                            _ = response.EnsureSuccessStatusCode();

                            using var fs = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
                            await response.Content.CopyToAsync(fs, cancellationToken);
                            downloaded = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (File.Exists(archivePath))
                            {
                                try { File.Delete(archivePath); } catch { }
                            }

                            if (attempt == maxRetries)
                                throw new IOException($"Failed to download SteamCMD after {maxRetries} attempts: {ex.Message}", ex);

                            progress.Report($"Download failed ({ex.Message}). Retrying in {delayMs / 1000}s...");
                            await Task.Delay(delayMs, cancellationToken);
                            delayMs *= 2;
                        }
                    }

                    if (!downloaded)
                        return false;
                }

                progress.Report("Extracting SteamCMD archive...");
                if (isWindows)
                {
                    ZipFile.ExtractToDirectory(archivePath, installDir, true);
                }
                else
                {
                    string extractedTar = Path.Combine(installDir, "steamcmd.tar");
                    try
                    {
                        using (var gzStream = new GZipStream(File.OpenRead(archivePath), CompressionMode.Decompress))
                        using (var tarFileStream = File.Create(extractedTar))
                        {
                            await gzStream.CopyToAsync(tarFileStream, cancellationToken);
                        }

                        TarFile.ExtractToDirectory(extractedTar, installDir, true);
                    }
                    finally
                    {
                        if (File.Exists(extractedTar))
                        {
                            try { File.Delete(extractedTar); } catch { }
                        }
                    }

                    string shPath = Path.Combine(installDir, SteamcmdSh);
                    if (File.Exists(shPath))
                    {
                        // Ensure executable
                        using var chmodProcess = Process.Start("chmod", $"+x \"{shPath}\"");
                        if (chmodProcess != null)
                        {
                            await chmodProcess.WaitForExitAsync(cancellationToken);
                        }
                    }
                }

                string exe = isWindows
                    ? Path.Combine(installDir, SteamcmdExe)
                    : Path.Combine(installDir, SteamcmdSh);

                if (!File.Exists(exe))
                {
                    throw new FileNotFoundException($"Expected SteamCMD executable not found at {exe}");
                }

                progress.Report("Running initial self-update and setup...");
                // Launch once to allow SteamCMD to self-update and install required files
                _ = await ExecuteCommandInternalAsync(exe, "+quit", progress, cancellationToken);

                progress.Report("Validating installation...");
                bool isValid = await ValidateAsync(exe, cancellationToken);
                if (!isValid)
                {
                    throw new InvalidOperationException("SteamCMD validation failed after installation.");
                }

                // Save installation path in config
                ConfigManager.Config.SteamCmdPath = exe;
                ConfigManager.SaveConfigFile("SteamCmdPath updated");

                progress.Report("SteamCMD setup completed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogging.LogException("SteamCmd Install failed", ex);
                progress.Report($"Error: {ex.Message}");
                throw;
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

        private async Task<int> ExecuteCommandInternalAsync(string exe, string arguments, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            InfoLogger.LogRunDf($"SteamCmd: Executing {exe} {arguments}");

            using var process = new Process { StartInfo = startInfo };
            try
            {
                _ = process.Start();
            }
            catch (Exception ex)
            {
                AppLogging.LogException("SteamCmd execution failed to start", ex);
                throw;
            }

            using var reg = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            });

            var outputTask = Task.Run(async () =>
            {
                using var reader = process.StandardOutput;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        progress?.Report(line);
                        InfoLogger.LogRunDf($"SteamCmd stdout: {line}");
                    }
                }
            }, cancellationToken);

            var errorTask = Task.Run(async () =>
            {
                using var reader = process.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        progress?.Report($"[ERR] {line}");
                        InfoLogger.LogRunDf($"SteamCmd stderr: {line}");
                    }
                }
            }, cancellationToken);

            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    break;
                }
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            try { await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false); } catch { }

            return process.HasExited ? process.ExitCode : -1;
        }

        private static bool TryFindInPath(string tool, out string fullPath)
        {
            fullPath = string.Empty;
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var path in paths)
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
                catch { }
            }
            return false;
        }
    }
}
