using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ModHearth.UI;

internal static class UpdateService
{
    private const int RecentBuildCount = 5;
    private const string UpdateRepoOwner = "EggleEgg";
    private const string UpdateRepoName = "ModHearth";
    private static readonly HttpClient UpdateHttpClient = CreateUpdateHttpClient();

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
        if (!EnsureDirectoryWritable(baseDir, out string rightsMessage))
        {
            UpdateLogger.LogError($"Update failed: {rightsMessage}");
            await DialogService.ShowMessageAsync(owner, rightsMessage, "Update requires permission");
            return false;
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

        if (!TryStartUpdateScript(payloadDir, baseDir, configBackup, out string? startError))
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

    private static bool EnsureDirectoryWritable(string directory, out string message)
    {
        message = string.Empty;
        try
        {
            string testFile = Path.Combine(directory, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            message = OperatingSystem.IsWindows()
                ? "ModHearth does not have permission to update this folder. Please run ModHearth as Administrator and try again."
                : "ModHearth does not have permission to update this folder. Please run with sudo or move ModHearth to a writable directory.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Failed to access the application directory: {ex.Message}";
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

    private static bool TryStartUpdateScript(string sourceDir, string destinationDir, string? configBackup, out string? error)
    {
        error = null;
        int pid = Environment.ProcessId;
        if (OperatingSystem.IsWindows())
            return StartWindowsUpdateScript(sourceDir, destinationDir, configBackup, pid, out error);

        return StartUnixUpdateScript(sourceDir, destinationDir, configBackup, pid, out error);
    }

    private static bool StartWindowsUpdateScript(string sourceDir, string destinationDir, string? configBackup, int pid, out string? error)
    {
        error = null;
        try
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"ModHearth_update_{Guid.NewGuid():N}.cmd");
            string sourceDirTrimmed = Path.TrimEndingDirectorySeparator(sourceDir);
            string destinationDirTrimmed = Path.TrimEndingDirectorySeparator(destinationDir);
            string exePath = ResolveUpdatedExecutablePath(destinationDirTrimmed);
            string exeArgs = ResolveUpdatedExecutableArgs(destinationDirTrimmed, exePath);

            StringBuilder script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine($"set \"PID={pid}\"");
            script.AppendLine($"set \"SRC={sourceDirTrimmed}\"");
            script.AppendLine($"set \"DEST={destinationDirTrimmed}\"");
            script.AppendLine($"set \"EXE={exePath}\"");
            script.AppendLine($"set \"EXE_ARGS={exeArgs}\"");
            script.AppendLine("set \"LOG=%DEST%\\logs\\updatelog.txt\"");
            script.AppendLine("if not exist \"%DEST%\\logs\" mkdir \"%DEST%\\logs\"");
            script.AppendLine("echo [%date% %time%] ModHearth updater started>>\"%LOG%\"");
            script.AppendLine(":wait");
            script.AppendLine("tasklist /FI \"PID eq %PID%\" | find \"%PID%\" >nul");
            script.AppendLine("if not errorlevel 1 (");
            script.AppendLine("  timeout /t 1 /nobreak >nul");
            script.AppendLine("  goto wait");
            script.AppendLine(")");
            script.AppendLine("echo [%date% %time%] Copying update files>>\"%LOG%\"");
            script.AppendLine("robocopy \"%SRC%\" \"%DEST%\" /E /COPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XF config.json >>\"%LOG%\" 2>&1");
            if (!string.IsNullOrWhiteSpace(configBackup))
                script.AppendLine($"copy /Y \"{configBackup}\" \"%DEST%\\config.json\" >>\"%LOG%\" 2>&1");
            script.AppendLine("if not \"%EXE%\"==\"\" (");
            script.AppendLine("  echo [%date% %time%] Restarting ModHearth>>\"%LOG%\"");
            script.AppendLine("  start \"\" \"%EXE%\" %EXE_ARGS%");
            script.AppendLine(")");
            script.AppendLine("echo [%date% %time%] ModHearth updater finished>>\"%LOG%\"");

            File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);

            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch the update script: {ex.Message}";
            return false;
        }
    }

    private static bool StartUnixUpdateScript(string sourceDir, string destinationDir, string? configBackup, int pid, out string? error)
    {
        error = null;
        try
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"modhearth_update_{Guid.NewGuid():N}.sh");
            string exePath = ResolveUpdatedExecutablePath(destinationDir);
            string exeArgs = ResolveUpdatedExecutableArgs(destinationDir, exePath);

            StringBuilder script = new StringBuilder();
            script.AppendLine("#!/bin/sh");
            script.AppendLine($"PID={pid}");
            script.AppendLine($"SRC=\"{sourceDir}\"");
            script.AppendLine($"DEST=\"{destinationDir}\"");
            if (!string.IsNullOrWhiteSpace(configBackup))
                script.AppendLine($"CONFIG_BACKUP=\"{configBackup}\"");
            script.AppendLine("LOG=\"$DEST/logs/updatelog.txt\"");
            script.AppendLine("mkdir -p \"$DEST/logs\"");
            script.AppendLine("echo \"[$(date +%Y-%m-%d\\ %H:%M:%S)] ModHearth updater started\" >> \"$LOG\"");
            script.AppendLine("while kill -0 \"$PID\" 2>/dev/null; do sleep 0.2; done");
            script.AppendLine("echo \"[$(date +%Y-%m-%d\\ %H:%M:%S)] Copying update files\" >> \"$LOG\"");
            script.AppendLine("cp -a \"$SRC/.\" \"$DEST/\" >> \"$LOG\" 2>&1");
            if (!string.IsNullOrWhiteSpace(configBackup))
                script.AppendLine("cp \"$CONFIG_BACKUP\" \"$DEST/config.json\" >> \"$LOG\" 2>&1");
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                script.AppendLine($"if [ -f \"{exePath}\" ]; then chmod +x \"{exePath}\"; fi");
                if (string.IsNullOrWhiteSpace(exeArgs))
                    script.AppendLine($"\"{exePath}\" &");
                else
                    script.AppendLine($"{exePath} {exeArgs} &");
            }
            script.AppendLine("echo \"[$(date +%Y-%m-%d\\ %H:%M:%S)] ModHearth updater finished\" >> \"$LOG\"");

            File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = scriptPath,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch the update script: {ex.Message}";
            return false;
        }
    }

    private static string ResolveUpdatedExecutablePath(string destinationDir)
    {
        if (OperatingSystem.IsWindows())
        {
            string exe = Path.Combine(destinationDir, "ModHearth.exe");
            if (File.Exists(exe))
                return exe;
        }
        else
        {
            string bin = Path.Combine(destinationDir, "ModHearth");
            if (File.Exists(bin))
                return bin;
        }

        string dll = Path.Combine(destinationDir, "ModHearth.dll");
        if (File.Exists(dll))
            return "dotnet";

        return string.Empty;
    }

    private static string ResolveUpdatedExecutableArgs(string destinationDir, string exePath)
    {
        if (!string.Equals(exePath, "dotnet", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        string dll = Path.Combine(destinationDir, "ModHearth.dll");
        if (File.Exists(dll))
            return $"\"{dll}\"";

        return string.Empty;
    }

    private static HttpClient CreateUpdateHttpClient()
    {
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ModHearth/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
