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

    public record BatchDownloadItem(ulong WorkshopId, string DownloadPath, IProgress<DownloadProgress> Progress, CancellationToken CancellationToken);

    public interface IWorkshopDownloadProvider
    {
        string Name { get; }
        bool IsAvailable { get; }
        
        Task<bool> DownloadAsync(
            ulong workshopId, 
            string downloadPath, 
            IProgress<DownloadProgress> progress, 
            CancellationToken cancellationToken);

        Task<Dictionary<ulong, bool>> DownloadBatchAsync(
            IEnumerable<BatchDownloadItem> items,
            CancellationToken cancellationToken)
        {
            var dict = new Dictionary<ulong, bool>();
            foreach (var item in items)
            {
                bool success = DownloadAsync(item.WorkshopId, item.DownloadPath, item.Progress, cancellationToken).GetAwaiter().GetResult();
                dict[item.WorkshopId] = success;
            }
            return Task.FromResult(dict);
        }
    }
}
