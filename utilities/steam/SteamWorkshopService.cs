using System;
using System.Diagnostics;
using System.IO;

namespace ModHearth.Utilities;

/// <summary>
/// Talks to the Steamworks Workshop API by shelling out to ModHearth.SteamWorker, a separate helper
/// process that owns every direct SteamAPI_Init/Shutdown call for App 975370. See
/// ModHearth.SteamWorker/Program.cs for why.
/// </summary>
public sealed class SteamWorkshopService
{
    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(20);

    // Cheap process-presence check only -- a real Init() attempt now only happens inside a worker
    // invocation, so this can no longer guarantee success the way the old in-process check did.
    // It exists so callers fail fast with a clear message instead of spawning a doomed worker
    // process when Steam obviously isn't running at all.
    public bool IsAvailable => SteamProcessHelper.TryDetectSteamProcess(out _);

    public static bool Subscribe(ulong workshopId) => RunWorker("subscribe", workshopId.ToString());

    public bool Unsubscribe(ulong workshopId) => RunWorker("unsubscribe", workshopId.ToString());

    public static bool Download(ulong workshopId, bool highPriority = true) =>
        RunWorker("download", workshopId.ToString());

    // Not currently called anywhere in the codebase -- kept for API parity with the previous
    // implementation. Costs the same worker round-trip as Subscribe/Unsubscribe if used.
    public static bool IsSubscribed(ulong workshopId) => RunWorker("issubscribed", workshopId.ToString());

    private static bool RunWorker(string action, string arg)
    {
        string? workerPath = ResolveWorkerPath();
        if (workerPath == null)
        {
            SteamConnectionLogger.LogError("ModHearth.SteamWorker executable not found alongside ModHearth.");
            return false;
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = workerPath,
                Arguments = $"{action} {arg}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            });

            if (process == null)
                return false;

            string stderr = process.StandardError.ReadToEnd();
            bool exited = process.WaitForExit((int)WorkerTimeout.TotalMilliseconds);
            if (!exited)
            {
                SteamConnectionLogger.LogError($"ModHearth.SteamWorker timed out running '{action} {arg}'.");
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }

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

    private static string? ResolveWorkerPath()
    {
        string fileName = "ModHearth.SteamWorker" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        string candidate = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(candidate) ? candidate : null;
    }
}