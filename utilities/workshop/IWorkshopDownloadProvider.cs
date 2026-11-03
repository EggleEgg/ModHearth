using Avalonia.Media;

namespace ModHearth.Utilities.Workshop
{
    public enum DownloadState
    {
        Waiting,
        Resolving,
        Downloading,
        Completed,
        Failed,
        RetryAvailable,
        Cancelled
    }

    public record DownloadProgress(long BytesDownloaded, long TotalBytes, double Percentage);

    public record BatchDownloadItem(ulong WorkshopId, string DownloadPath, IProgress<DownloadProgress> Progress, CancellationToken CancellationToken);

    public interface IWorkshopDownloadProvider
    {
        string Name { get; }
        bool IsAvailable { get; }
        IBrush StatusBrush => IsAvailable ? Brushes.LimeGreen : Brushes.Red;

        Task<bool> DownloadAsync(
            ulong workshopId,
            string downloadPath,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken);
    }
}
