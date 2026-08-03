using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ModHearth.Utilities;

/// <summary>
/// Detects whether Steam (or one of its helper processes) is currently running.
/// </summary>
internal static class SteamProcessHelper
{
    private static readonly string[] CandidateProcessNames =
    [
        "steam",
        "Steam",
        "steamwebhelper",
        "SteamWebHelper"
    ];

    public static bool TryDetectSteamProcess(out List<string> runningProcesses)
    {
        runningProcesses = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string name in CandidateProcessNames)
            {
                Process[]? procs = null;
                try
                {
                    procs = Process.GetProcessesByName(name);
                    if (procs.Length > 0 && seen.Add(name))
                        runningProcesses.Add(name);
                }
                catch
                {
                    // Ignore process query failures for one process name.
                }
                finally
                {
                    if (procs != null)
                    {
                        foreach (var p in procs)
                        {
                            p?.Dispose();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SteamConnectionLogger.LogError($"Steam process detection failed: {ex.Message}");
        }

        return runningProcesses.Count > 0;
    }
}
