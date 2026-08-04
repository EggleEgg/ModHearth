using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ModHearth.Utilities;

/// <summary>
/// Monitors the Dwarf Fortress process and provides a robust way to check if it's running, especially on Linux where process checks can be unreliable.
/// </summary> <summary>
public class DFMonitor
{
    private int? _trackedPid = null;
    private DateTime? _processStartTime = null;
    private readonly TimeSpan _rpcStartupTimeout = TimeSpan.FromSeconds(30);
    private readonly object _lock = new();

    public static readonly DFMonitor Shared = new();

    /// <summary>
    /// Checks if Dwarf Fortress is alive using Linux /proc filesystem or robust PID polling.
    /// Does NOT rely on .NET process events.
    /// </summary>
    public bool IsProcessRunning()
    {
        lock (_lock)
        {
            // 1. If we already have a PID, verify if it still exists
            if (_trackedPid.HasValue)
            {
                if (IsPidAlive(_trackedPid.Value))
                {
                    return true;
                }

                // PID died
                _trackedPid = null;
                _processStartTime = null;
            }

            // 2. Scan for process (Linux /proc aware)
            int? pid = FindDwarfFortressPid();
            if (pid.HasValue)
            {
                _trackedPid = pid.Value;
                _processStartTime = DateTime.UtcNow;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Checks if the DF process is running but still within the RPC startup grace period (booting).
    /// Prevents false flagging or log spam when the DFHack RPC server is taking time to start up.
    /// </summary>
    public bool IsBooting()
    {
        lock (_lock)
        {
            if (!_trackedPid.HasValue || !_processStartTime.HasValue)
                return false;
            return (DateTime.UtcNow - _processStartTime.Value) < _rpcStartupTimeout;
        }
    }

    /// <summary>
    /// Registers a newly launched Dwarf Fortress process PID.
    /// </summary>
    public void RegisterLaunchedPid(int pid)
    {
        lock (_lock)
        {
            _trackedPid = pid;
            _processStartTime = DateTime.UtcNow;
        }
    }

    private static bool IsPidAlive(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Direct /proc check is 100% reliable on Linux for any process
            return Directory.Exists($"/proc/{pid}");
        }

        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // Process not running
        }
    }

    private static int? FindDwarfFortressPid()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Scan /proc directly to bypass 15-char comm name limits and Wine wrapper issues
            try
            {
                foreach (var dir in Directory.EnumerateDirectories("/proc"))
                {
                    var name = Path.GetFileName(dir);
                    if (!int.TryParse(name, out int pid)) continue;

                    string cmdlinePath = Path.Combine(dir, "cmdline");
                    if (!File.Exists(cmdlinePath)) continue;

                    try
                    {
                        // /proc/[pid]/cmdline separates args with null bytes
                        string cmdline = File.ReadAllText(cmdlinePath);
                        if (cmdline.Contains("Dwarf Fortress", StringComparison.OrdinalIgnoreCase) ||
                            cmdline.Contains("dwarfort", StringComparison.OrdinalIgnoreCase))
                        {
                            return pid;
                        }
                    }
                    catch (Exception) { /* Handle access permission errors on system PIDs */ }
                }
            }
            catch (Exception)
            {
                // Ignored
            }
        }

        // Fallback for Windows / macOS
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.ProcessName.Contains("Dwarf Fortress", StringComparison.OrdinalIgnoreCase) ||
                        p.ProcessName.Contains("dwarfort", StringComparison.OrdinalIgnoreCase))
                    {
                        return p.Id;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignored
        }

        return null;
    }

    public async Task SafeExecuteRpcAsync(Func<Task> rpcAction)
    {
        // Fail-fast if process is dead on Linux
        if (!IsProcessRunning())
        {
            return; // Abort silently; suppresses log spam completely when DF is closed
        }

        try
        {
            await rpcAction();
        }
        catch (Exception ex)
        {
            // Process exists, but RPC refused connection
            bool isBooting = _processStartTime.HasValue &&
                (DateTime.UtcNow - _processStartTime.Value) < _rpcStartupTimeout;

            if (isBooting)
            {
                // Quietly wait for DFHack server to spin up on port 5000
                return;
            }

            // Log only if process has been up for > 30s and RPC is genuinely broken
            Console.WriteLine($"[DFHackRpcClient] RPC failed while DF (PID {_trackedPid}) is running: {ex.Message}");
        }
    }
}