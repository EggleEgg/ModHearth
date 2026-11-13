using ModHearth.Config;
using ModHearth.Utils.Logging;

namespace ModHearth.Utils.DwarfFortress
{
    internal enum AutoNewWorldOutcome
    {
        Opened,
        AlreadyOpen,
        Failed,
        Cancelled
    }

    internal readonly record struct AutoNewWorldResult(AutoNewWorldOutcome Outcome, string Message)
    {
        public static AutoNewWorldResult Opened(string message) => new(AutoNewWorldOutcome.Opened, message);

        public static AutoNewWorldResult AlreadyOpen(string message) => new(AutoNewWorldOutcome.AlreadyOpen, message);

        public static AutoNewWorldResult Failed(string message) => new(AutoNewWorldOutcome.Failed, message);

        public static AutoNewWorldResult Cancelled() => new(AutoNewWorldOutcome.Cancelled, string.Empty);
    }

    /// <summary>
    /// Waits for a Dwarf Fortress process launched by ModHearth to finish loading, then asks the
    /// DFHack script <c>lua/AutoCreateNewWorld.lua</c> to click "Create new world". The script is
    /// polled instead of driving the UI from C# so DFHack keeps ownership of screen input.
    /// </summary>
    internal static class DwarfFortressAutoNewWorldRunner
    {
        private const string ScriptName = "AutoCreateNewWorld.lua";
        private static readonly TimeSpan ProcessStartupTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RpcStartupTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan TitleScreenTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan ClickConfirmationTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        public static async Task<AutoNewWorldResult> RunAsync(
            string dfFolderPath,
            CancellationToken cancellationToken = default)
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "lua", ScriptName);
            if (!File.Exists(scriptPath))
                return AutoNewWorldResult.Failed($"Automation script not found: {scriptPath}");

            string dfhackRunPath = ConfigManager.GetDfhackRunPath();
            if (string.IsNullOrWhiteSpace(dfhackRunPath) || !File.Exists(dfhackRunPath))
            {
                return AutoNewWorldResult.Failed(
                    "DFHack was not found. Install DFHack or configure its folder in ModHearth.");
            }

            Loggers.DfRunner.Log(
                "Waiting for Dwarf Fortress to finish loading before opening the world creation screen.");

            try
            {
                if (!await WaitForRpcAsync(dfFolderPath, cancellationToken))
                {
                    return AutoNewWorldResult.Failed(
                        $"DFHack did not become available within {RpcStartupTimeout.TotalSeconds:0} seconds.");
                }

                return await WaitForNewWorldScreenAsync(
                    dfFolderPath,
                    scriptPath,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return AutoNewWorldResult.Cancelled();
            }
            catch (Exception ex)
            {
                Loggers.DfRunner.LogError($"Autoclick create new world failed: {ex.Message}");
                return AutoNewWorldResult.Failed(ex.Message);
            }
        }

        private static async Task<bool> WaitForRpcAsync(string dfFolderPath, CancellationToken cancellationToken)
        {
            // Steam launches can return before the game process is visible, so wait for the
            // process first and only then start the DFHack RPC grace period.
            DateTime processDeadline = DateTime.UtcNow + ProcessStartupTimeout;
            while (!DFMonitor.Shared.IsProcessRunning())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (DateTime.UtcNow >= processDeadline)
                {
                    throw new InvalidOperationException(
                        $"Dwarf Fortress did not start within {ProcessStartupTimeout.TotalSeconds:0} seconds.");
                }

                await Task.Delay(PollInterval, cancellationToken);
            }

            DateTime rpcDeadline = DateTime.UtcNow + RpcStartupTimeout;
            while (DateTime.UtcNow < rpcDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!DFMonitor.Shared.IsProcessRunning())
                    throw new InvalidOperationException("Dwarf Fortress exited before DFHack became available.");

                if (DFHackRpcClient.IsDFHackRunning(dfFolderPath))
                    return true;

                await Task.Delay(PollInterval, cancellationToken);
            }

            return false;
        }

        private static async Task<AutoNewWorldResult> WaitForNewWorldScreenAsync(
            string dfFolderPath,
            string scriptPath,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + TitleScreenTimeout;
            DateTime? clickedAtUtc = null;
            string lastError = string.Empty;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!DFMonitor.Shared.IsProcessRunning())
                    return AutoNewWorldResult.Failed("Dwarf Fortress exited before the title screen appeared.");

                string? output = DFHackRpcClient.ExecuteDFHackCommandViaRpc(
                    "lua",
                    ["-f", scriptPath],
                    dfFolderPath,
                    out string error);

                if (output == null)
                {
                    lastError = error;
                    Loggers.DfRunner.Dev($"Autoclick create new world probe failed: {error}");
                }
                else
                {
                    switch (ParseResult(output))
                    {
                        case "clicked":
                            clickedAtUtc ??= DateTime.UtcNow;
                            break;

                        case "already-open":
                            if (clickedAtUtc == null)
                            {
                                Loggers.DfRunner.Log("Dwarf Fortress already had the world creation screen open.");
                                return AutoNewWorldResult.AlreadyOpen("The world creation screen was already open.");
                            }

                            Loggers.DfRunner.Log("Dwarf Fortress opened the world creation screen.");
                            return AutoNewWorldResult.Opened("The world creation screen is open.");

                        case "game":
                            Loggers.DfRunner.Dev(
                                $"Autoclick create new world refused to click: {output.Trim()}");
                            return AutoNewWorldResult.Failed(
                                "A saved game is loaded. Return Dwarf Fortress to the title screen and launch it again.");

                        case "waiting":
                            break;

                        default:
                            Loggers.DfRunner.Dev(
                                $"Unexpected autoclick create new world output: {output.Trim()}");
                            break;
                    }
                }

                if (clickedAtUtc is { } clickedAt
                    && DateTime.UtcNow - clickedAt > ClickConfirmationTimeout)
                {
                    return AutoNewWorldResult.Failed(
                        "The Create new world button was clicked, but the world creation screen did not open.");
                }

                await Task.Delay(PollInterval, cancellationToken);
            }

            string detail = string.IsNullOrWhiteSpace(lastError)
                ? "the title screen did not expose \"Create new world\""
                : $"last DFHack error: {lastError}";
            return AutoNewWorldResult.Failed(
                $"Timed out after {TitleScreenTimeout.TotalSeconds:0} seconds; {detail}.");
        }

        internal static string? ParseResult(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("RESULT=", StringComparison.Ordinal))
                    continue;

                string value = trimmed["RESULT=".Length..].Trim();
                return value.Length == 0 ? null : value.ToLowerInvariant();
            }

            return null;
        }
    }
}
