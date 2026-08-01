using Avalonia.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ModHearth.UI;

[Flags]
internal enum CleanupPlatforms
{
    Windows = 1 << 0,
    Linux = 1 << 1,
    macOS = 1 << 2,
    All = Windows | Linux | macOS
}

/// <summary>
/// Handles everything needed to change versions
/// </summary>
internal static class UpdateService
{
    private const int RecentBuildCount = 5;
    private const string UpdateRepoOwner = "EggleEgg";
    private const string UpdateRepoName = "ModHearth";
    private static readonly HttpClient UpdateHttpClient = CreateUpdateHttpClient();

    // List of legacy, non-self-contained files and folders to clean up. Paths should be relative to the installation root directory.
    // Examples:
    //   - ("libs", true, CleanupPlatforms.All) -> Cleaned on Windows, Linux, and macOS
    //   - ("libs", true, CleanupPlatforms.Linux | CleanupPlatforms.macOS) -> "" Linux AND MacOs. Logical OR operations are not possible
    private static readonly (string Path, bool IsDirectory, CleanupPlatforms Platform)[] LegacyPathsToClean =
    {
        ("libs", true, CleanupPlatforms.All),
        ("native", true, CleanupPlatforms.All),
        ("runtimes", true, CleanupPlatforms.All),
        ("dlls", true, CleanupPlatforms.All),
        ("libsteam_api.dylib", false, CleanupPlatforms.Windows | CleanupPlatforms.Linux),
        ("libsteam_api.so", false, CleanupPlatforms.Windows | CleanupPlatforms.macOS),
        ("steam_api64.dll", false, CleanupPlatforms.Linux | CleanupPlatforms.macOS ),
        ("libSkiaSharp.pdb", false, CleanupPlatforms.All),
        ("libHarfBuzzSharp.pdb", false, CleanupPlatforms.All),
        ("ModHearth.SteamWorker.pdb", false, CleanupPlatforms.All),
        ("ModHearth.pdb", false, CleanupPlatforms.All),
        ("ModHearth.runtimeconfig.json", false, CleanupPlatforms.All),
        ("ModHearth.deps.json", false, CleanupPlatforms.All)
    };

    public static async Task<bool> TryRunUpdateAsync(Window owner, string currentBuild)
    {
        UpdateLogger.Log("Update check started.");

        try
        {
            List<GitHubRelease> releases = await FetchRecentBuildsAsync(RecentBuildCount);
            if (releases.Count == 0)
            {
                UpdateLogger.LogError("No builds found in the repository.");
                await DialogService.ShowMessageAsync(owner, "No builds found in the repository.", "Update ModHearth");
                return false;
            }

            GitHubRelease? selected = await UpdateDialog.ShowAsync(owner, releases, currentBuild);
            if (selected == null)
            {
                UpdateLogger.Log("Update canceled by user.");
                return false;
            }

            UpdateLogger.Log($"Update selected: {UpdateHelpers.GetReleaseTitle(selected, 0)} ({selected.TagName ?? "no tag"}).");
            return await PerformUpdateAsync(owner, selected);
        }
        catch (Exception ex)
        {
            UpdateLogger.LogError($"Update failed: {ex.Message}");
            await DialogService.ShowMessageAsync(owner, ex.Message, "Update failed");
            return false;
        }
    }

    private static async Task<List<GitHubRelease>> FetchRecentBuildsAsync(int count)
    {
        string url = $"https://api.github.com/repos/{UpdateRepoOwner}/{UpdateRepoName}/releases?per_page={count}";
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await UpdateHttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string status = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            throw new InvalidOperationException($"Failed to fetch builds from GitHub ({status}).");
        }

        using Stream stream = await response.Content.ReadAsStreamAsync();
        GitHubRelease[]? releases = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return releases?.ToList() ?? new List<GitHubRelease>();
    }

    private static async Task<bool> PerformUpdateAsync(Window owner, GitHubRelease release)
    {
        if (!TryGetAssetForCurrentOs(release, out GitHubAsset? asset, out string? error))
        {
            UpdateLogger.LogError($"Update failed: {error ?? "No compatible build found."}");
            await DialogService.ShowMessageAsync(owner, error ?? "No compatible build found for this OS.", "Update failed");
            return false;
        }

        if (asset == null)
        {
            UpdateLogger.LogError("Update failed: release asset is null.");
            await DialogService.ShowMessageAsync(owner, "No compatible build asset was found.", "Update failed");
            return false;
        }

        string baseDir = AppContext.BaseDirectory;
        UpdateLogger.Log($"Update base directory: {baseDir}");

        // Check write permission and trigger elevation if needed
        bool needsElevation = !EnsureDirectoryWritable(baseDir);
        if (needsElevation)
        {
            UpdateLogger.Log("Installation folder is read-only for current user. Elevated permissions will be requested.");
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"ModHearth_update_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        UpdateLogger.Log($"Update temp directory: {tempRoot}");

        string assetPath = await DownloadAssetAsync(asset, tempRoot);
        UpdateLogger.Log($"Downloaded update asset: {assetPath}");
        string extractDir = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractDir);

        if (asset.Name != null && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ZipFile.ExtractToDirectory(assetPath, extractDir, true);
        else
            ExtractTarGz(assetPath, extractDir);

        string payloadDir = FindPayloadRoot(extractDir);
        if (string.IsNullOrWhiteSpace(payloadDir))
        {
            UpdateLogger.LogError($"Update failed: no payload found in {extractDir}");
            await DialogService.ShowMessageAsync(owner, "Update package does not contain ModHearth binaries.", "Update failed");
            return false;
        }

        UpdateLogger.Log($"Update payload directory: {payloadDir}");
        string? configBackup = BackupConfig(baseDir, tempRoot);
        if (!string.IsNullOrWhiteSpace(configBackup))
            UpdateLogger.Log($"Backed up config: {configBackup}");

        if (!TryStartUpdateScript(payloadDir, baseDir, configBackup, needsElevation, out string? startError))
        {
            UpdateLogger.LogError($"Update failed: {startError}");
            await DialogService.ShowMessageAsync(owner, startError ?? "Failed to start the updater.", "Update failed");
            return false;
        }

        await DialogService.ShowMessageAsync(owner, "Update downloaded. ModHearth will restart shortly.", "Updating");
        return true;
    }

    private static bool TryGetAssetForCurrentOs(GitHubRelease release, out GitHubAsset? asset, out string? error)
    {
        asset = null;
        error = null;

        (string rid, string ext) = GetCurrentOsAssetInfo();
        string expectedName = $"ModHearth-{rid}.{ext}";
        asset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, expectedName, StringComparison.OrdinalIgnoreCase));

        if (asset == null)
            error = $"No release asset named '{expectedName}' was found.";

        return asset != null;
    }

    private static (string rid, string ext) GetCurrentOsAssetInfo()
    {
        if (OperatingSystem.IsWindows())
            return ("win-x64", "zip");
        if (OperatingSystem.IsLinux())
            return ("linux-x64", "tar.gz");
        if (OperatingSystem.IsMacOS())
            return ("osx-x64", "tar.gz");

        throw new PlatformNotSupportedException("Unsupported operating system for auto-update.");
    }

    private static bool EnsureDirectoryWritable(string directory)
    {
        try
        {
            string testFile = Path.Combine(directory, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FindPayloadRoot(string extractDir)
    {
        if (ContainsAppFiles(extractDir))
            return extractDir;

        DirectoryInfo root = new DirectoryInfo(extractDir);
        DirectoryInfo[] subDirs;
        try
        {
            subDirs = root.GetDirectories();
        }
        catch
        {
            return string.Empty;
        }

        if (subDirs.Length == 1 && ContainsAppFiles(subDirs[0].FullName))
            return subDirs[0].FullName;

        foreach (DirectoryInfo dir in subDirs)
        {
            string candidate = FindPayloadInSubtree(dir.FullName, 2);
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }

        return string.Empty;
    }

    private static string FindPayloadInSubtree(string root, int depth)
    {
        if (ContainsAppFiles(root))
            return root;

        if (depth <= 0)
            return string.Empty;

        try
        {
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                string candidate = FindPayloadInSubtree(dir, depth - 1);
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }
        }
        catch
        {
            // Ignore traversal errors.
        }

        return string.Empty;
    }

    private static bool ContainsAppFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return false;

        string exe = Path.Combine(directory, "ModHearth.exe");
        string bin = Path.Combine(directory, "ModHearth");
        string dll = Path.Combine(directory, "ModHearth.dll");

        return File.Exists(exe) || File.Exists(bin) || File.Exists(dll);
    }

    private static async Task<string> DownloadAssetAsync(GitHubAsset asset, string tempRoot)
    {
        if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            throw new InvalidOperationException("Release asset is missing a download URL.");

        string fileName = asset.Name ?? "ModHearth-update";
        string destinationPath = Path.Combine(tempRoot, fileName);

        using HttpResponseMessage response = await UpdateHttpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            string status = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            throw new InvalidOperationException($"Failed to download update ({status}).");
        }

        await using Stream responseStream = await response.Content.ReadAsStreamAsync();
        await using FileStream fileStream = File.Create(destinationPath);
        await responseStream.CopyToAsync(fileStream);
        return destinationPath;
    }

    private static void ExtractTarGz(string archivePath, string destinationDirectory)
    {
        using FileStream fileStream = File.OpenRead(archivePath);
        using GZipStream gzip = new GZipStream(fileStream, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destinationDirectory, true);
    }

    private static string? BackupConfig(string baseDir, string tempRoot)
    {
        string configPath = Path.Combine(baseDir, "config.json");
        if (!File.Exists(configPath))
            return null;

        string backupPath = Path.Combine(tempRoot, "config.json.backup");
        File.Copy(configPath, backupPath, true);
        return backupPath;
    }

    private static bool TryStartUpdateScript(string sourceDir, string destinationDir, string? configBackup, bool needsElevation, out string? error)
    {
        error = null;
        int pid = Environment.ProcessId;
        if (OperatingSystem.IsWindows())
            return StartWindowsUpdateScript(sourceDir, destinationDir, configBackup, pid, needsElevation, out error);

        return StartUnixUpdateScript(sourceDir, destinationDir, configBackup, pid, needsElevation, out error);
    }

    private static void AppendLegacyCleanupCommands(StringBuilder script, bool isWindows, string destVarName)
    {
        if (isWindows)
        {
            script.AppendLine($"echo [%date% %time%] Cleaning legacy framework-dependent files>>\"%LOG%\"");
            foreach (var (path, isDir, platform) in LegacyPathsToClean)
            {
                // Only clean if this item is targeted for Windows
                if (!platform.HasFlag(CleanupPlatforms.Windows))
                    continue;

                // Normalize slashes to Windows backslashes
                string winPath = path.Replace('/', '\\');
                if (isDir)
                    script.AppendLine($"if exist \"%{destVarName}%\\{winPath}\" rmdir /s /q \"%{destVarName}%\\{winPath}\" >>\"%LOG%\" 2>&1");
                else
                    script.AppendLine($"if exist \"%{destVarName}%\\{winPath}\" del /f /q \"%{destVarName}%\\{winPath}\" >>\"%LOG%\" 2>&1");
            }
        }
        else // Unix / Linux / macOS
        {
            script.AppendLine($"echo \"[$(date +%Y-%m-%d\\ %H:%M:%S)] Cleaning legacy framework-dependent files\" >> \"$LOG\"");
            foreach (var (path, _, platform) in LegacyPathsToClean)
            {
                // Verify if the item targets the active operating system environment
                if (!((OperatingSystem.IsLinux() && platform.HasFlag(CleanupPlatforms.Linux)) || (OperatingSystem.IsMacOS() && platform.HasFlag(CleanupPlatforms.macOS))))
                    continue;

                // Normalize slashes to Unix forward slashes
                string unixPath = path.Replace('\\', '/');
                // 'rm -rf' recursively deletes files and directories alike on Unix
                script.AppendLine($"rm -rf \"${destVarName}/{unixPath}\" >> \"$LOG\" 2>&1");
            }
        }
    }

    private static bool StartWindowsUpdateScript(string sourceDir, string destinationDir, string? configBackup, int pid, bool needsElevation, out string? error)
    {
        error = null;
        try
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"ModHearth_update_{Guid.NewGuid():N}.cmd");
            string sourceDirTrimmed = Path.TrimEndingDirectorySeparator(sourceDir);
            string destinationDirTrimmed = Path.TrimEndingDirectorySeparator(destinationDir);
            string exePath = ResolveUpdatedExecutablePath(destinationDirTrimmed);

            StringBuilder script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine($"set \"PID={pid}\"");
            script.AppendLine($"set \"SRC={sourceDirTrimmed}\"");
            script.AppendLine($"set \"DEST={destinationDirTrimmed}\"");
            script.AppendLine($"set \"EXE={exePath}\"");
            script.AppendLine("set \"LOG=%DEST%\\logs\\updatelog.txt\"");
            script.AppendLine("if not exist \"%DEST%\\logs\" mkdir \"%DEST%\\logs\"");
            script.AppendLine("echo [%date% %time%] ModHearth updater started>>\"%LOG%\"");
            script.AppendLine(":wait");
            script.AppendLine("tasklist /FI \"PID eq %PID%\" | find \"%PID%\" >nul");
            script.AppendLine("if not errorlevel 1 (");
            script.AppendLine("  timeout /t 1 /nobreak >nul");
            script.AppendLine("  goto wait");
            script.AppendLine(")");

            AppendLegacyCleanupCommands(script, isWindows: true, destVarName: "DEST");

            script.AppendLine("echo [%date% %time%] Copying update files>>\"%LOG%\"");
            script.AppendLine("robocopy \"%SRC%\" \"%DEST%\" /E /COPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XF config.json >>\"%LOG%\" 2>&1");
            if (!string.IsNullOrWhiteSpace(configBackup))
                script.AppendLine($"copy /Y \"{configBackup}\" \"%DEST%\\config.json\" >>\"%LOG%\" 2>&1");
            script.AppendLine("if exist \"%EXE%\" (");
            script.AppendLine("  echo [%date% %time%] Restarting ModHearth>>\"%LOG%\"");
            script.AppendLine("  start \"\" \"%EXE%\"");
            script.AppendLine(") else (");
            script.AppendLine("  echo [%date% %time%] Restart skipped, executable not found at %EXE%>>\"%LOG%\"");
            script.AppendLine(")");
            script.AppendLine("echo [%date% %time%] ModHearth updater finished>>\"%LOG%\"");

            File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{scriptPath}\"\"",
                UseShellExecute = true
            };

            if (needsElevation)
            {
                psi.Verb = "runas"; // Triggers Windows UAC Prompt
            }
            else
            {
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
            }

            using Process? process = Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED by user on UAC
        {
            error = "Update canceled: administrator permissions were refused.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch the update script: {ex.Message}";
            return false;
        }
    }

    private static bool StartUnixUpdateScript(string sourceDir, string destinationDir, string? configBackup, int pid, bool needsElevation, out string? error)
    {
        error = null;
        try
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"modhearth_update_{Guid.NewGuid():N}.sh");
            string exePath = ResolveUpdatedExecutablePath(destinationDir);

            StringBuilder script = new StringBuilder();
            script.AppendLine("#!/bin/sh");
            script.AppendLine($"PID={pid}");
            script.AppendLine($"SRC=\"{sourceDir}\"");
            script.AppendLine($"DEST=\"{destinationDir}\"");
            script.AppendLine($"EXE=\"{exePath}\"");
            if (!string.IsNullOrWhiteSpace(configBackup))
                script.AppendLine($"CONFIG_BACKUP=\"{configBackup}\"");
            script.AppendLine("LOG=\"$DEST/logs/updatelog.txt\"");
            script.AppendLine("mkdir -p \"$DEST/logs\"");
            script.AppendLine("echo \"[$(date +%Y-%m-%d\\ %H:%M:%S)] ModHearth updater started\" >> \"$LOG\"");
            script.AppendLine("while kill -0 \"$PID\" 2>/dev/null; do sleep 0.2; done");

            AppendLegacyCleanupCommands(script, isWindows: false, destVarName: "DEST");

            script.AppendLine("echo \"[$(date +%Y-%m-%d\\ %H:%M:%S)] Copying update files\" >> \"$LOG\"");
            script.AppendLine("cp -a \"$SRC/.\" \"$DEST/\" >> \"$LOG\" 2>&1");
            if (!string.IsNullOrWhiteSpace(configBackup))
                script.AppendLine("cp \"$CONFIG_BACKUP\" \"$DEST/config.json\" >> \"$LOG\" 2>&1");

            // Restore user ownership if script ran with elevated privileges
            script.AppendLine("if [ -n \"$PKEXEC_UID\" ]; then");
            script.AppendLine("  chown -R \"$PKEXEC_UID\" \"$DEST\" >> \"$LOG\" 2>&1");
            script.AppendLine("elif [ -n \"$SUDO_USER\" ]; then");
            script.AppendLine("  chown -R \"$SUDO_USER\" \"$DEST\" >> \"$LOG\" 2>&1");
            script.AppendLine("fi");

            script.AppendLine("if [ -f \"$EXE\" ]; then");
            script.AppendLine("  chmod +x \"$EXE\"");
            script.AppendLine("  \"$EXE\" &");
            script.AppendLine("else");
            script.AppendLine("  echo \"[$(date +%Y-%m-%d\\ %H:%M:%S)] Restart skipped, executable not found at $EXE\" >> \"$LOG\"");
            script.AppendLine("fi");
            script.AppendLine("echo \"[$(date +%Y-%m-%d\\ %H:%M:%S)] ModHearth updater finished\" >> \"$LOG\"");

            File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (needsElevation)
            {
                if (OperatingSystem.IsLinux())
                {
                    // pkexec launches native graphical password prompt on Linux (Polkit)
                    psi.FileName = "pkexec";
                    psi.Arguments = $"/bin/sh \"{scriptPath}\"";
                }
                else if (OperatingSystem.IsMacOS())
                {
                    // osascript triggers native macOS privileges prompt
                    psi.FileName = "osascript";
                    psi.Arguments = $"-e \"do shell script \\\"/bin/sh '{scriptPath}'\\\" with administrator privileges\"";
                }
                else
                {
                    psi.FileName = "/bin/sh";
                    psi.Arguments = scriptPath;
                }
            }
            else
            {
                psi.FileName = "/bin/sh";
                psi.Arguments = scriptPath;
            }

            using Process? process = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch elevated update script: {ex.Message}";
            return false;
        }
    }

    private static string ResolveUpdatedExecutablePath(string destinationDir)
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(destinationDir, "ModHearth.exe");

        return Path.Combine(destinationDir, "ModHearth");
    }

    private static HttpClient CreateUpdateHttpClient()
    {
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ModHearth/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}