namespace ModHearth.Utilities.Steam
{
    public interface ISteamCmdService
    {
        string GetExecutablePath();
        bool IsAvailable();
        Task<bool> ValidateAsync(string exePath, CancellationToken cancellationToken = default);
        Task<bool> InstallAsync(string installDir, IProgress<string> progress, CancellationToken cancellationToken = default);
        Task<int> ExecuteAsync(string arguments, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
        string? FindExisting();
    }
}
