using Avalonia.Threading;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void InitializeDfHackStatusTimer()
    {
        dfHackStatusTimer = new DispatcherTimer
        {
            Interval = DfHackStatusRefreshInterval
        };
        dfHackStatusTimer.Tick += async (_, _) => await UpdateDfHackStatusAsync();
    }

    private void StartDfHackStatusTimer()
    {
        if (dfHackStatusTimer == null)
            return;

        dfHackStatusTimer.Stop();
        dfHackStatusTimer.Start();
    }

    private async Task UpdateDfHackStatusAsync()
    {
        if (dfhackStatusLabel == null)
            return;

        if (TryShowTransientStatusNotice())
            return;

        (bool dfRunning, bool hasDfhackExecutable, bool isDfHackRpcRunning, bool isDfHackInstalled) =
            await Task.Run(() => (
                manager.DwarfFortressRunning(),
                manager.HasDfhackExecutable(),
                manager.IsDfhackRpcRunning(),
                manager.IsDFHackInstalled()));

        if (dfhackStatusLabel == null)
            return;

        ApplyDfHackStatusToLabel(dfRunning, hasDfhackExecutable, isDfHackRpcRunning, isDfHackInstalled);
    }

    private void ApplyDfHackStatusToLabel(bool dfRunning, bool hasDfhackExecutable, bool isDfHackRpcRunning, bool isDfHackInstalled)
    {
        var current = (dfRunning, hasDfhackExecutable, isDfHackRpcRunning, isDfHackInstalled);
        bool changed = current != lastDfHackStatus;
        lastDfHackStatus = current;

        string dfStatus = dfRunning ? "Dwarf Fortress running" : "Dwarf Fortress not running";
        string dfhackConfigStatus = isDfHackInstalled ? "DFHack configured" : "DFHack path not set";
        string dfhackExecutableStatus = hasDfhackExecutable ? "DFHack executable found" : "DFHack executable NOT found!";
        string dfhackRpcStatus = isDfHackRpcRunning ? "DFHack RPC server running" : "DFHack RPC server not reachable";

        if (changed) Console.WriteLine($"{dfStatus}, {dfhackConfigStatus}, {dfhackExecutableStatus}, {dfhackRpcStatus}");

        if (!isDfHackInstalled || !hasDfhackExecutable)
            dfhackStatusLabel.Text = $"{dfStatus}, DFHack not found!";
        else if (isDfHackRpcRunning || (dfRunning && hasDfhackExecutable))
            dfhackStatusLabel.Text = $"{dfStatus}, DFHack running";
        else
            dfhackStatusLabel.Text = dfStatus;

        dfhackStatusLabel.IsVisible = true;
    }

    private void ShowTransientStatusNotice(string message)
    {
        transientStatusNotice = message;
        transientStatusNoticeUntilUtc = DateTime.UtcNow.AddSeconds(6);
        TryShowTransientStatusNotice();
    }

    private bool TryShowTransientStatusNotice()
    {
        if (string.IsNullOrWhiteSpace(transientStatusNotice))
            return false;

        if (DateTime.UtcNow >= transientStatusNoticeUntilUtc)
        {
            transientStatusNotice = string.Empty;
            return false;
        }

        dfhackStatusLabel.Text = transientStatusNotice;
        dfhackStatusLabel.IsVisible = true;
        return true;
    }
}
