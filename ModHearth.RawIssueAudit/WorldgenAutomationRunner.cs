using System.Diagnostics;
using System.Globalization;
using System.Text;
using ModHearth.Config;
using ModHearth.Manager;
using ModHearth.Utils.DwarfFortress;

namespace ModHearth.RawIssueAudit
{
    internal enum DfScreenState
    {
        Starting,
        Title,
        WorldGen,
        WorldGenActive,
        Game,
        Other
    }

    internal sealed record DfScreenProbe(DfScreenState State, string? ScreenName);

    internal enum WorldgenAutomationOutcome
    {
        Running,
        Done,
        Failed
    }

    internal sealed record WorldgenAutomationStatus(
        WorldgenAutomationOutcome Outcome,
        string? Error,
        string? LastStatus);

    /// <summary>
    /// Opt-in helper that regenerates Dwarf Fortress's errorlog by driving the title screen through
    /// "Create new world" -> "Create world", completing a Pocket-sized world with five years of
    /// history, and then returning to the title screen. The actual UI driving lives in
    /// <c>lua/GenerateErrorLog.lua</c>; this class handles process lifetime, the errorlog
    /// backup/clear, generated-save cleanup, RPC startup and status-file polling.
    ///
    /// It is deliberately separate from the read-only audit path: it only runs when
    /// <see cref="AuditOptions.AutomateWorldgen"/> is set.
    /// </summary>
    internal static class WorldgenAutomationRunner
    {
        private const string ErrorLogBackupName = "errorlog.audit-backup.txt";
        private static readonly TimeSpan RpcStartupTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan PreflightTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ErrorLogStabilityTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SaveDeleteRetryDelay = TimeSpan.FromMilliseconds(250);
        private const int SaveDeleteRetryCount = 20;

        private static StringComparer PathComparer
            => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public static void Run(AuditOptions options, AuditPathSet paths, List<string> warnings)
        {
            string dfPath = paths.DwarfFortressPath
                ?? throw new AuditInputException(
                    "--automate requires a Dwarf Fortress folder. Pass --df <folder> or --config <config.json>.");

            if (!Directory.Exists(dfPath))
                throw new AuditInputException($"Dwarf Fortress folder not found: {dfPath}");

            bool wasRunning = DFMonitor.Shared.IsProcessRunning();
            HashSet<string>? existingWorldSaves = TrySnapshotWorldSaves(dfPath, warnings);
            Process? launched = null;

            try
            {
                if (!wasRunning)
                {
                    launched = LaunchDwarfFortress(dfPath);
                    warnings.Add(
                        $"Launched Dwarf Fortress for errorlog generation (process {launched.Id}).");
                }
                else
                {
                    warnings.Add(
                        "Dwarf Fortress was already running; automation will leave it running when it finishes.");
                }

                WaitForRpc(paths, launched);

                DfScreenProbe probe = WaitForStartScreen(paths, launched);
                warnings.Add($"Worldgen automation starting from {DescribeScreen(probe)}.");

                string? backupPath = RotateErrorLog(paths.ErrorLogPath);
                if (backupPath != null)
                {
                    warnings.Add(
                        $"Previous errorlog backed up to '{backupPath}' and cleared before automation.");
                }
                else
                {
                    warnings.Add(
                        "No previous errorlog content was available; automation starts from an empty log.");
                }

                string statusPath = Path.Combine(
                    Path.GetTempPath(),
                    $"modhearth-worldgen-{Guid.NewGuid():N}.status");

                try
                {
                    string scriptPath = ResolveLuaScriptPath("GenerateErrorLog.lua");
                    int timeoutMilliseconds = checked(options.AutomationTimeoutSeconds * 1000);
                    _ = RunLuaScript(
                        paths,
                        scriptPath,
                        [statusPath, timeoutMilliseconds.ToString(CultureInfo.InvariantCulture)],
                        TimeSpan.FromSeconds(60),
                        out string error)
                        ?? throw new AuditInputException(
                            $"Could not start worldgen automation through DFHack: {error}");

                    _ = WaitForAutomation(statusPath, options.AutomationTimeoutSeconds, launched);
                }
                finally
                {
                    TryDelete(statusPath);
                }

                // If the audit launched DF, close it before reading the log so any shutdown
                // diagnostics are included and no further writes can race the analyzer.
                if (launched != null && !options.KeepDwarfFortressRunning)
                {
                    CloseDwarfFortress(launched, paths, warnings);
                    launched = null;
                }
                else if (launched != null)
                {
                    warnings.Add(
                        "Dwarf Fortress was launched by the audit and is still running (--keep-df-running).");
                    launched.Dispose();
                    launched = null;
                }

                WaitForErrorLog(paths.ErrorLogPath, warnings, ErrorLogStabilityTimeout);
                warnings.Add(
                    $"Worldgen automation completed; auditing '{paths.ErrorLogPath}'.");
            }
            finally
            {
                if (launched != null)
                {
                    if (options.KeepDwarfFortressRunning)
                    {
                        launched.Dispose();
                    }
                    else
                    {
                        CloseDwarfFortress(launched, paths, warnings);
                    }
                }

                _ = RemoveGeneratedWorldSaves(dfPath, existingWorldSaves, warnings);
            }
        }

        private static Process LaunchDwarfFortress(string dfPath)
        {
            if (!DwarfFortressExecutableLocator.TryResolvePath(dfPath, out string executablePath))
            {
                throw new AuditInputException(
                    $"Dwarf Fortress executable not found under '{dfPath}'. Install Dwarf Fortress or pass --df <folder>.");
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                WorkingDirectory = dfPath,
                UseShellExecute = false
            };

            if (OperatingSystem.IsLinux()
                && string.Equals(Path.GetFileName(executablePath), "dwarfort", StringComparison.Ordinal))
            {
                string? bundledLibraries = DwarfFortressExecutableLocator.TryResolveBundledLibraryPath(dfPath);
                if (!string.IsNullOrWhiteSpace(bundledLibraries))
                {
                    string existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty;
                    startInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existing)
                        ? bundledLibraries
                        : $"{bundledLibraries}:{existing}";
                }
            }

            try
            {
                Process process = Process.Start(startInfo)
                    ?? throw new AuditInputException(
                        $"Could not launch Dwarf Fortress: process creation returned no handle for '{executablePath}'.");

                DFMonitor.Shared.RegisterLaunchedPid(process.Id);
                return process;
            }
            catch (AuditInputException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AuditInputException($"Could not launch Dwarf Fortress: {ex.Message}");
            }
        }

        private static void WaitForRpc(AuditPathSet paths, Process? launched)
        {
            DateTime deadline = DateTime.UtcNow + RpcStartupTimeout;
            while (DateTime.UtcNow < deadline)
            {
                EnsureProcessAlive(launched);

                if (DFHackRpcClient.IsDFHackRunning(paths.DwarfFortressPath))
                    return;

                if (launched == null && !DFMonitor.Shared.IsProcessRunning())
                {
                    throw new AuditInputException(
                        "Dwarf Fortress stopped running while waiting for the DFHack RPC server.");
                }

                Thread.Sleep(StatusPollInterval);
            }

            throw new AuditInputException(
                $"DFHack RPC did not become available within {RpcStartupTimeout.TotalSeconds:0} seconds. " +
                "Launch Dwarf Fortress through DFHack and try again.");
        }

        private static DfScreenProbe WaitForStartScreen(AuditPathSet paths, Process? launched)
        {
            string scriptPath = ResolveLuaScriptPath("CheckDFState.lua");
            DateTime deadline = DateTime.UtcNow + PreflightTimeout;
            DateTime? unexpectedSince = null;
            string? lastError = null;
            DfScreenProbe lastProbe = new(DfScreenState.Other, null);

            while (DateTime.UtcNow < deadline)
            {
                EnsureProcessAlive(launched);

                string? output = RunLuaScript(
                    paths,
                    scriptPath,
                    [],
                    TimeSpan.FromSeconds(15),
                    out string error);

                if (output == null)
                {
                    lastError = error;
                }
                else
                {
                    lastError = null;
                    lastProbe = ParseState(output);

                    switch (lastProbe.State)
                    {
                        case DfScreenState.Title:
                        case DfScreenState.WorldGen:
                            return lastProbe;

                        case DfScreenState.WorldGenActive:
                            throw new AuditInputException(
                                "World generation is already in progress in Dwarf Fortress. " +
                                "Return to the title screen before running --automate.");

                        case DfScreenState.Game:
                            throw new AuditInputException(
                                "A saved game is loaded in Dwarf Fortress. " +
                                "Return to the title screen before running --automate.");

                        case DfScreenState.Starting:
                            unexpectedSince = null;
                            break;

                        case DfScreenState.Other:
                            if (IsTransitionalScreen(lastProbe.ScreenName))
                            {
                                unexpectedSince = null;
                            }
                            else
                            {
                                unexpectedSince ??= DateTime.UtcNow;
                                if (DateTime.UtcNow - unexpectedSince.Value > TimeSpan.FromSeconds(10))
                                {
                                    throw new AuditInputException(
                                        $"Dwarf Fortress is showing {DescribeScreen(lastProbe)}, not the title screen. " +
                                        "Return to the title screen before running --automate.");
                                }
                            }

                            break;
                    }
                }

                Thread.Sleep(StatusPollInterval);
            }

            throw new AuditInputException(
                $"Dwarf Fortress did not reach the title screen within {PreflightTimeout.TotalSeconds:0} seconds " +
                $"(last screen: {DescribeScreen(lastProbe)}" +
                (string.IsNullOrWhiteSpace(lastError) ? ")." : $", last error: {lastError})."));
        }

        private static WorldgenAutomationStatus WaitForAutomation(
            string statusPath,
            int timeoutSeconds,
            Process? launched)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds + 30);
            WorldgenAutomationStatus lastStatus = new(WorldgenAutomationOutcome.Running, null, null);
            string? lastReadError = null;

            while (DateTime.UtcNow < deadline)
            {
                EnsureProcessAlive(launched);

                if (TryReadStatus(statusPath, out WorldgenAutomationStatus status, out string? readError))
                {
                    lastStatus = status;
                    if (status.Outcome == WorldgenAutomationOutcome.Done)
                        return status;

                    if (status.Outcome == WorldgenAutomationOutcome.Failed)
                    {
                        throw new AuditInputException(
                            $"Dwarf Fortress worldgen automation failed: {status.Error}. " +
                            $"Last status: {status.LastStatus ?? "(none)"}");
                    }
                }
                else
                {
                    lastReadError = readError;
                }

                Thread.Sleep(StatusPollInterval);
            }

            string detail = lastStatus.LastStatus ?? lastReadError ?? "(no status written)";
            throw new AuditInputException(
                $"Dwarf Fortress worldgen automation did not finish within {timeoutSeconds} seconds. " +
                $"Last status: {detail}");
        }

        private static string? RotateErrorLog(string errorLogPath)
        {
            if (!File.Exists(errorLogPath))
                return null;

            string directory = Path.GetDirectoryName(errorLogPath)
                ?? throw new AuditInputException($"Could not resolve the errorlog directory: {errorLogPath}");
            string backupPath = Path.Combine(directory, ErrorLogBackupName);

            try
            {
                using (FileStream source = new(
                    errorLogPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    // Nothing to preserve, and copying an empty log would overwrite the previous
                    // backup with nothing useful.
                    if (source.Length == 0)
                        return null;

                    using FileStream destination = new(
                        backupPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None);
                    source.CopyTo(destination);
                }

                // DF keeps the errorlog stream open for the lifetime of the process. Deleting the
                // file while DF is running leaves it writing into an unlinked handle, so the audit
                // would never see the new diagnostics. Truncating in place keeps the handle valid.
                using FileStream target = new(
                    errorLogPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                target.SetLength(0);

                return backupPath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AuditInputException(
                    $"Could not back up and clear '{errorLogPath}' before automation: {ex.Message}. " +
                    "Close Dwarf Fortress and retry.");
            }
        }

        /// <summary>
        /// Records the save folders that already existed before automation. Cleanup only removes
        /// folders that appear afterwards, so a failed run can never delete a player's world.
        /// Returns null when the save directory cannot be inspected; cleanup is then skipped.
        /// </summary>
        internal static HashSet<string>? TrySnapshotWorldSaves(string dfPath, ICollection<string> warnings)
        {
            string savePath = Path.Combine(dfPath, "save");
            try
            {
                HashSet<string> snapshot = new(PathComparer);
                if (!Directory.Exists(savePath))
                    return snapshot;

                foreach (string directory in Directory.EnumerateDirectories(savePath))
                {
                    if (IsRegionSaveFolder(directory))
                        _ = snapshot.Add(Path.GetFullPath(directory));
                }

                return snapshot;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add(
                    $"Could not inspect '{savePath}' before automation; generated worlds will not be removed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Removes region folders that were created by the automation. DF writes each generated
        /// world directly into <c>&lt;df&gt;/save/regionN</c>, so keeping only pre-existing folders
        /// preserves every real save while leaving no audit artifact behind.
        /// </summary>
        internal static IReadOnlyList<string> RemoveGeneratedWorldSaves(
            string dfPath,
            HashSet<string>? existingWorldSaves,
            ICollection<string> warnings)
        {
            List<string> removed = [];
            if (existingWorldSaves == null)
                return removed;

            string savePath = Path.Combine(dfPath, "save");
            if (!Directory.Exists(savePath))
                return removed;

            List<string> generated;
            try
            {
                generated = [.. Directory.EnumerateDirectories(savePath)
                    .Where(IsRegionSaveFolder)
                    .Select(Path.GetFullPath)
                    .Where(directory => !existingWorldSaves.Contains(directory))
                    .OrderBy(directory => directory, PathComparer)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not enumerate generated worlds under '{savePath}': {ex.Message}");
                return removed;
            }

            foreach (string directory in generated)
            {
                if (TryDeleteDirectory(directory, warnings))
                    removed.Add(directory);
            }

            if (removed.Count > 0)
            {
                warnings.Add(
                    $"Removed generated world save folder(s): {string.Join(", ", removed.Select(Path.GetFileName))}.");
            }

            return removed;
        }

        private static bool TryDeleteDirectory(string directory, ICollection<string> warnings)
        {
            for (int attempt = 1; attempt <= SaveDeleteRetryCount; attempt++)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    return true;
                }
                catch (DirectoryNotFoundException)
                {
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == SaveDeleteRetryCount)
                    {
                        warnings.Add(
                            $"Could not remove generated world save '{directory}': {ex.Message}. " +
                            "Close Dwarf Fortress and delete it manually.");
                        return false;
                    }

                    Thread.Sleep(SaveDeleteRetryDelay);
                }
            }

            return false;
        }

        private static bool IsRegionSaveFolder(string directory)
        {
            string name = Path.GetFileName(directory);
            if (!name.StartsWith("region", StringComparison.OrdinalIgnoreCase))
                return false;

            string suffix = name["region".Length..];
            return suffix.Length > 0 && suffix.All(char.IsAsciiDigit);
        }

        internal static string? BackupErrorLog(string errorLogPath) => RotateErrorLog(errorLogPath);

        /// <summary>
        /// Waits until the log exists and stops growing. If DF produced no diagnostics at all it
        /// never creates the file; an empty file keeps the analyzer's contract intact and lets the
        /// report distinguish "no new errors" from "automation did not run".
        /// </summary>
        internal static void WaitForErrorLog(
            string errorLogPath,
            ICollection<string> warnings,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            long lastLength = -1;
            DateTime lastWriteUtc = DateTime.MinValue;
            int stableChecks = 0;

            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(errorLogPath))
                {
                    try
                    {
                        FileInfo info = new(errorLogPath);
                        info.Refresh();
                        long length = info.Length;
                        DateTime writeUtc = info.LastWriteTimeUtc;

                        if (length == lastLength && writeUtc == lastWriteUtc)
                        {
                            stableChecks++;
                            if (stableChecks >= 2)
                                return;
                        }
                        else
                        {
                            stableChecks = 0;
                            lastLength = length;
                            lastWriteUtc = writeUtc;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        stableChecks = 0;
                    }
                }

                Thread.Sleep(250);
            }

            if (!File.Exists(errorLogPath))
            {
                try
                {
                    File.WriteAllText(errorLogPath, string.Empty);
                    warnings.Add(
                        $"Dwarf Fortress did not create '{errorLogPath}' during automation; " +
                        "created an empty log.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new AuditInputException(
                        $"Could not create an empty errorlog after automation: {ex.Message}");
                }
            }
            else
            {
                warnings.Add(
                    $"'{errorLogPath}' was still changing after {timeout.TotalSeconds:0} seconds; " +
                    "the audit may not include every error.");
            }
        }

        private static void CloseDwarfFortress(Process process, AuditPathSet paths, List<string> warnings)
        {
            try
            {
                if (process.HasExited)
                    return;

                // 'die' is DFHack's controlled immediate shutdown. It is safe here because
                // automation never loads a save; if the command or RPC socket fails, fall back to
                // terminating the process.
                _ = DFHackRpcClient.ExecuteDFHackCommandViaRpc(
                    "die",
                    [],
                    paths.DwarfFortressPath,
                    out _);

                if (!process.WaitForExit(10000))
                {
                    warnings.Add(
                        "Dwarf Fortress did not exit after the DFHack 'die' command; terminating it.");
                    process.Kill(entireProcessTree: true);
                    _ = process.WaitForExit(5000);
                }
                else
                {
                    warnings.Add("Closed Dwarf Fortress after generating the errorlog.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not close Dwarf Fortress cleanly: {ex.Message}");
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        _ = process.WaitForExit(5000);
                    }
                }
                catch (Exception killException)
                {
                    warnings.Add($"Could not terminate Dwarf Fortress: {killException.Message}");
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        private static string? RunLuaScript(
            AuditPathSet paths,
            string scriptPath,
            IReadOnlyList<string> scriptArguments,
            TimeSpan timeout,
            out string error)
        {
            List<string> arguments = ["-f", scriptPath, .. scriptArguments];
            string? output = DFHackRpcClient.ExecuteDFHackCommandViaRpc(
                "lua",
                arguments,
                paths.DwarfFortressPath,
                out error);

            if (output != null)
                return output;

            if (string.Equals(error, "Dwarf Fortress is not running", StringComparison.Ordinal))
                return null;

            string? dfhackRunPath = ResolveDfHackRunPath(paths);
            if (string.IsNullOrWhiteSpace(dfhackRunPath))
                return null;

            return RunDfhackRunLua(paths, dfhackRunPath, arguments, timeout, out error);
        }

        private static string? RunDfhackRunLua(
            AuditPathSet paths,
            string dfhackRunPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            out string error)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = dfhackRunPath,
                WorkingDirectory = paths.DwarfFortressPath ?? Path.GetDirectoryName(dfhackRunPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            ModHearthManager.SetDfHackRunEnvironmentVariables(startInfo);

            try
            {
                using Process process = new() { StartInfo = startInfo };
                _ = process.Start();

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex)
                    {
                        error = $"dfhack-run did not exit within {timeout.TotalSeconds:0} seconds: {ex.Message}";
                        return null;
                    }

                    error = $"dfhack-run did not exit within {timeout.TotalSeconds:0} seconds.";
                    return null;
                }

                string output = outputTask.GetAwaiter().GetResult();
                error = errorTask.GetAwaiter().GetResult();
                return output;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        internal static string? ResolveDfHackRunPath(AuditPathSet paths)
        {
            string executableName = OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run";
            HashSet<string> candidates = new(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            void AddDirectory(string? directory)
            {
                if (!string.IsNullOrWhiteSpace(directory))
                    _ = candidates.Add(Path.Combine(directory, executableName));
            }

            string? environmentPath = Environment.GetEnvironmentVariable("DFHACK_RUN");
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                if (Directory.Exists(environmentPath))
                    AddDirectory(environmentPath);
                else
                    _ = candidates.Add(environmentPath);
            }

            AddDirectory(paths.DfHackPath);
            AddDirectory(
                string.IsNullOrWhiteSpace(paths.DfHackPath)
                    ? null
                    : Path.Combine(paths.DfHackPath, "hack"));
            AddDirectory(paths.DwarfFortressPath);
            AddDirectory(
                string.IsNullOrWhiteSpace(paths.DwarfFortressPath)
                    ? null
                    : Path.Combine(paths.DwarfFortressPath, "hack"));

            if (!string.IsNullOrWhiteSpace(paths.DwarfFortressPath))
            {
                DirectoryInfo? parent = Directory.GetParent(paths.DwarfFortressPath);
                if (parent != null)
                {
                    AddDirectory(Path.Combine(parent.FullName, "DFHack", "hack"));
                    AddDirectory(Path.Combine(parent.FullName, "DFHack"));
                }
            }

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            string? steamFolder = ConfigManager.TryFindSteamDFHackFolder();
            if (!string.IsNullOrWhiteSpace(steamFolder))
            {
                string candidate = Path.Combine(steamFolder, executableName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            return null;
        }

        private static string ResolveLuaScriptPath(string fileName)
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "lua", fileName);
            if (!File.Exists(scriptPath))
            {
                throw new AuditInputException(
                    $"Worldgen automation script not found: {scriptPath}. Rebuild ModHearth.RawIssueAudit.");
            }

            return scriptPath;
        }

        internal static DfScreenProbe ParseState(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return new DfScreenProbe(DfScreenState.Other, null);

            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("STATE=", StringComparison.Ordinal))
                    continue;

                string value = trimmed["STATE=".Length..].Trim();
                if (string.Equals(value, "starting", StringComparison.OrdinalIgnoreCase))
                    return new DfScreenProbe(DfScreenState.Starting, null);
                if (string.Equals(value, "title", StringComparison.OrdinalIgnoreCase))
                    return new DfScreenProbe(DfScreenState.Title, null);
                if (string.Equals(value, "worldgen", StringComparison.OrdinalIgnoreCase))
                    return new DfScreenProbe(DfScreenState.WorldGen, null);
                if (string.Equals(value, "worldgen-active", StringComparison.OrdinalIgnoreCase))
                    return new DfScreenProbe(DfScreenState.WorldGenActive, null);
                if (string.Equals(value, "game", StringComparison.OrdinalIgnoreCase))
                    return new DfScreenProbe(DfScreenState.Game, null);
                if (value.StartsWith("other:", StringComparison.OrdinalIgnoreCase))
                    return new DfScreenProbe(DfScreenState.Other, value["other:".Length..].Trim());
            }

            return new DfScreenProbe(DfScreenState.Other, null);
        }

        internal static WorldgenAutomationStatus ParseStatus(IReadOnlyList<string> lines)
        {
            WorldgenAutomationOutcome outcome = WorldgenAutomationOutcome.Running;
            string? error = null;
            string? lastStatus = null;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (string.Equals(line, "DONE", StringComparison.Ordinal))
                {
                    outcome = WorldgenAutomationOutcome.Done;
                    error = null;
                }
                else if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                {
                    outcome = WorldgenAutomationOutcome.Failed;
                    error = line["ERROR:".Length..].Trim();
                }
                else
                {
                    lastStatus = line;
                }
            }

            return new WorldgenAutomationStatus(outcome, error, lastStatus);
        }

        private static bool TryReadStatus(
            string statusPath,
            out WorldgenAutomationStatus status,
            out string? error)
        {
            status = new WorldgenAutomationStatus(WorldgenAutomationOutcome.Running, null, null);
            error = null;

            if (!File.Exists(statusPath))
                return false;

            try
            {
                List<string> lines = [];
                using (FileStream stream = new(
                    statusPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new(stream, Encoding.UTF8))
                {
                    while (reader.ReadLine() is { } line)
                        lines.Add(line);
                }

                status = ParseStatus(lines);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool IsTransitionalScreen(string? screenName)
            => string.IsNullOrWhiteSpace(screenName)
                || screenName.Contains("viewscreen_game_cleanerst", StringComparison.OrdinalIgnoreCase);

        private static string DescribeScreen(DfScreenProbe probe)
            => probe.State switch
            {
                DfScreenState.Starting => "the DFHack startup screen",
                DfScreenState.Title => "the title screen",
                DfScreenState.WorldGen => "the new-world setup screen",
                DfScreenState.WorldGenActive => "an in-progress worldgen screen",
                DfScreenState.Game => "a loaded game",
                _ => string.IsNullOrWhiteSpace(probe.ScreenName)
                    ? "an unknown screen"
                    : $"'{probe.ScreenName}'"
            };

        private static void EnsureProcessAlive(Process? process)
        {
            if (process == null)
                return;

            try
            {
                if (process.HasExited)
                {
                    throw new AuditInputException(
                        $"Dwarf Fortress exited during automation (exit code {process.ExitCode}).");
                }
            }
            catch (InvalidOperationException)
            {
                // The process handle has not been fully initialized; the next check will retry.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of the status file; the audit result does not depend on it.
            }
        }
    }
}
