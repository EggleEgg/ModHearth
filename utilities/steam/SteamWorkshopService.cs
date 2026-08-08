using System.Diagnostics;
using System.Text;

namespace ModHearth.Utilities;

/// <summary>
/// Talks to the Steamworks Workshop API by shelling out to ModHearth.SteamWorker, a separate helper
/// process that owns every direct SteamAPI_Init/Shutdown call for App 975370. See
/// ModHearth.SteamWorker/Program.cs for why.
/// </summary>
public sealed class SteamWorkshopService
{
    private const string Message = "ModHearth.SteamWorker executable not found alongside ModHearth.";

    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(20);
    // Must stay longer than ModHearth.SteamWorker's DownloadCallbackWaitTimeout (10 minutes), plus
    // headroom for process startup/shutdown.
    private static readonly TimeSpan DownloadWorkerTimeout = TimeSpan.FromMinutes(11);

    // Cheap process-presence check only -- a real Init() attempt now only happens inside a worker
    // invocation, so this can no longer guarantee success the way the old in-process check did.
    // It exists so callers fail fast with a clear message instead of spawning a doomed worker
    // process when Steam obviously isn't running at all.
    public bool IsAvailable => SteamProcessHelper.TryDetectSteamProcess(out _);
    public static bool Subscribe(ulong workshopId) => RunWorker("subscribe", workshopId.ToString());

    public static bool Unsubscribe(ulong workshopId) => RunWorker("unsubscribe", workshopId.ToString());

    public static bool Download(
        ulong workshopId,
        bool highPriority = true,
        Action<ulong, ulong>? onProgress = null,
        CancellationToken cancellationToken = default) =>
        RunWorker("download", workshopId.ToString(), DownloadWorkerTimeout, onProgress, cancellationToken);

    // Sends wake-up calls for multiple workshop items concurrently.
    public static int DownloadMany(IEnumerable<ulong> workshopIds)
    {
        List<ulong> ids = workshopIds.Distinct().ToList();
        if (ids.Count == 0)
            return 0;

        string? workerPath = ResolveWorkerPath();
        if (workerPath == null)
        {
            SteamConnectionLogger.LogError(Message);
            return 0;
        }

        int successCount = 0;
        _ = Parallel.ForEach(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, id =>
        {
            if (RunWorker("download", id.ToString()))
                _ = Interlocked.Increment(ref successCount);
        });

        return successCount;
    }

    public static int UnsubscribeMany(IEnumerable<ulong> workshopIds)
    {
        List<ulong> ids = workshopIds.Distinct().ToList();
        if (ids.Count == 0)
            return 0;

        string? workerPath = ResolveWorkerPath();
        if (workerPath == null)
        {
            SteamConnectionLogger.LogError(Message);
            return 0;
        }

        int successCount = 0;
        _ = Parallel.ForEach(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, id =>
        {
            if (RunWorker("unsubscribe", id.ToString()))
                _ = Interlocked.Increment(ref successCount);
        });

        return successCount;
    }

    public static int SubscribeMany(IEnumerable<ulong> workshopIds)
    {
        List<ulong> ids = workshopIds.Distinct().ToList();
        if (ids.Count == 0)
            return 0;

        string? workerPath = ResolveWorkerPath();
        if (workerPath == null)
        {
            SteamConnectionLogger.LogError(Message);
            return 0;
        }

        int successCount = 0;
        _ = Parallel.ForEach(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, id =>
        {
            if (RunWorker("subscribe", id.ToString()))
                _ = Interlocked.Increment(ref successCount);
        });

        return successCount;
    }

    // Not currently called anywhere in the codebase -- kept for API parity with the previous
    // implementation. Costs the same worker round-trip as Subscribe/Unsubscribe if used.
    public static bool IsSubscribed(ulong workshopId) => RunWorker("issubscribed", workshopId.ToString());

    private static bool RunWorker(
        string action,
        string arg,
        TimeSpan? timeout = null,
        Action<ulong, ulong>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        string? workerPath = ResolveWorkerPath();
        if (workerPath == null)
        {
            SteamConnectionLogger.LogError(Message);
            return false;
        }

        TimeSpan effectiveTimeout = timeout ?? WorkerTimeout;

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = workerPath,
                Arguments = $"{action} {arg}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process == null)
                return false;

            StringBuilder stdoutBuilder = new();
            StringBuilder stderrBuilder = new();
            object outputGate = new();

            // Read output line-by-line as it's produced (rather than blocking on ReadToEndAsync
            // until the process exits) so progress lines reach onProgress in real time instead of
            // only becoming visible after the whole download finishes.
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                if (onProgress != null && TryParseProgressLine(e.Data, out ulong bytesDownloaded, out ulong bytesTotal))
                {
                    onProgress(bytesDownloaded, bytesTotal);
                    return;
                }

                lock (outputGate)
                    stdoutBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                lock (outputGate)
                    stderrBuilder.AppendLine(e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            DateTime deadline = DateTime.UtcNow + effectiveTimeout;
            bool exited = false;
            while (DateTime.UtcNow < deadline)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    SteamConnectionLogger.LogInfo($"ModHearth.SteamWorker '{action} {arg}' cancelled; stopping worker.");
                    TryKill(process);
                    return false;
                }

                if (process.WaitForExit(200))
                {
                    exited = true;
                    break;
                }
            }

            if (!exited)
            {
                SteamConnectionLogger.LogError($"ModHearth.SteamWorker timed out running '{action} {arg}'.");
                TryKill(process);
                return false;
            }

            // Ensures buffered async output has been fully flushed before reading it -- the
            // polling WaitForExit(200) above only guarantees process exit, not stream completion.
            process.WaitForExit();

            string stdout;
            string stderr;
            lock (outputGate)
            {
                stdout = stdoutBuilder.ToString();
                stderr = stderrBuilder.ToString();
            }

            if (!string.IsNullOrWhiteSpace(stdout))
                SteamConnectionLogger.LogInfo($"ModHearth.SteamWorker '{action} {arg}' output: {stdout.Trim()}");

            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                    SteamConnectionLogger.LogError($"ModHearth.SteamWorker '{action} {arg}' failed: {stderr.Trim()}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            SteamConnectionLogger.LogError($"Failed to run ModHearth.SteamWorker '{action} {arg}': {ex.Message}");
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
    }

    private static bool TryParseProgressLine(string line, out ulong bytesDownloaded, out ulong bytesTotal)
    {
        bytesDownloaded = 0;
        bytesTotal = 0;

        if (!line.StartsWith("PROGRESS ", StringComparison.Ordinal))
            return false;

        string[] parts = line.Substring("PROGRESS ".Length).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && ulong.TryParse(parts[0], out bytesDownloaded)
            && ulong.TryParse(parts[1], out bytesTotal);
    }

    private static string? ResolveWorkerPath()
    {
        string fileName = "ModHearth.SteamWorker" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        string candidate = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(candidate) ? candidate : null;
    }
}
