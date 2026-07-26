using System;
using System.Threading;
using System.Threading.Tasks;

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

    public interface IWorkshopDownloadProvider
    {
        string Name { get; }
        bool IsAvailable { get; }
        
        Task<bool> DownloadAsync(
            ulong workshopId, 
            string downloadPath, 
            IProgress<DownloadProgress> progress, 
            CancellationToken cancellationToken);
    }
}
