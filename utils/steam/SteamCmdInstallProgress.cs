namespace ModHearth.Utils.Steam
{
    /// <summary>
    /// One progress update from <see cref="ISteamCmdService.InstallAsync"/>.
    /// <see cref="Percentage"/> is always overall installation progress (0-100), not the
    /// progress of the individual stage that produced the message.
    /// </summary>
    public readonly record struct SteamCmdInstallProgress(double Percentage, string Message);
}
