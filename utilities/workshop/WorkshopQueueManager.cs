using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using ModHearth;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.Workshop
{
    public enum ModStatusClassification
    {
        New,
        AlreadyInstalled,
        UpdateAvailable,
        Duplicate,
        MissingDependency,
        PotentiallyIncompatible
    }

    public class SimpleCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public SimpleCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class WorkshopDownloadItem : INotifyPropertyChanged
    {
        private DownloadState _state = DownloadState.Waiting;
        private double _progressPercentage;
        private string _statusText = "Waiting...";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ulong PublishedFileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<ulong> Dependencies { get; set; } = new();

        public DownloadState State
        {
            get => _state;
            set
            {
                _state = value;
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(CanRetry));
                OnPropertyChanged(nameof(CanCancel));
                ((SimpleCommand?)RetryCommand)?.RaiseCanExecuteChanged();
                ((SimpleCommand?)CancelCommand)?.RaiseCanExecuteChanged();
            }
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set
            {
                _progressPercentage = value;
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool CanRetry => State == DownloadState.Failed || State == DownloadState.Cancelled;
        public bool CanCancel => State == DownloadState.Waiting || State == DownloadState.Resolving || State == DownloadState.Downloading;

        public CancellationTokenSource? Cts { get; set; }

        public ICommand? RetryCommand { get; set; }
        public ICommand? CancelCommand { get; set; }
    }

    public class WorkshopQueueManager
    {
        private readonly SemaphoreSlim _concurrencySemaphore = new SemaphoreSlim(3, 3);
        private readonly SteamWebApiClient _apiClient = new();
        private readonly ModHearthManager _manager;

        public ObservableCollection<WorkshopDownloadItem> Queue { get; } = new();
        public List<IWorkshopDownloadProvider> Providers { get; } = new();
        public IWorkshopDownloadProvider? SelectedProvider { get; set; }

        public WorkshopQueueManager(ModHearthManager manager)
        {
            _manager = manager;

            // Initialize providers
            Providers.Add(new SteamWorkerDownloadProvider());
            Providers.Add(new SteamCmdDownloadProvider());

            // Select default configured provider, or fallback to SteamWorkerDownloadProvider
            string savedProviderName = ConfigManager.GetDefaultWorkshopProvider();
            SelectedProvider = Providers.FirstOrDefault(p => string.Equals(p.GetType().Name, savedProviderName, StringComparison.OrdinalIgnoreCase))
                ?? Providers.FirstOrDefault(p => string.Equals(p.GetType().Name, "SteamWorkerDownloadProvider", StringComparison.OrdinalIgnoreCase))
                ?? Providers.FirstOrDefault();
        }

        public ModStatusClassification ClassifyMod(ulong id, List<ulong>? allSelectedIds = null)
        {
            string idStr = id.ToString();

            // Check for duplicates in current selection/checklist
            if (allSelectedIds != null && allSelectedIds.Count(x => x == id) > 1)
            {
                int index = allSelectedIds.IndexOf(id);
                if (index >= 0 && allSelectedIds.FindIndex(index + 1, x => x == id) >= 0)
                {
                    return ModStatusClassification.Duplicate;
                }
            }

            // Find installed mod
            var installedMods = _manager.LoadedMods;
            var installedMod = installedMods.FirstOrDefault(m =>
                string.Equals(m.steamID, idStr, StringComparison.OrdinalIgnoreCase));

            if (installedMod != null)
            {
                return ModStatusClassification.AlreadyInstalled;
            }

            return ModStatusClassification.New;
        }

        public ModStatusClassification ClassifyModWithMetadata(WorkshopItemMetadata meta, List<ulong> allIds)
        {
            string idStr = meta.PublishedFileId.ToString();

            // Duplicate check
            if (allIds.Count(x => x == meta.PublishedFileId) > 1)
            {
                int firstIdx = allIds.IndexOf(meta.PublishedFileId);
                int lastIdx = allIds.LastIndexOf(meta.PublishedFileId);
                if (firstIdx != lastIdx)
                {
                    return ModStatusClassification.Duplicate;
                }
            }

            var installedMods = _manager.LoadedMods;
            var installedMod = installedMods.FirstOrDefault(m =>
                string.Equals(m.steamID, idStr, StringComparison.OrdinalIgnoreCase));

            if (installedMod != null)
            {
                if (installedMod.LastModifiedTime.HasValue && meta.UpdatedAt > installedMod.LastModifiedTime.Value.AddMinutes(5))
                    return ModStatusClassification.UpdateAvailable;
                return ModStatusClassification.AlreadyInstalled;
            }

            // Check missing dependencies
            if (meta.ChildrenIds != null && meta.ChildrenIds.Count > 0)
            {
                foreach (var childId in meta.ChildrenIds)
                {
                    bool isInstalled = installedMods.Any(m => string.Equals(m.steamID, childId.ToString(), StringComparison.OrdinalIgnoreCase));
                    bool isQueued = allIds.Contains(childId);
                    if (!isInstalled && !isQueued)
                    {
                        return ModStatusClassification.MissingDependency;
                    }
                }
            }

            return ModStatusClassification.New;
        }

        public async Task ResolveAndEnqueueUrlsAsync(string inputUrls, Action<List<WorkshopItemMetadata>> onCollectionFound)
        {
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Resolving URLs: {inputUrls}");
            var ids = WorkshopUrlResolver.ParseUrls(inputUrls);
            if (ids.Count == 0)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopQueueManager: No valid IDs found in input.");
                return;
            }

            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Fetching metadata for {ids.Count} IDs...");
            var metadataList = await _apiClient.GetPublishedFileDetailsAsync(ids);

            if (metadataList.Count == 0)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopQueueManager: Steam API returned no metadata for the given IDs.");
                return;
            }

            var itemsToEnqueue = new List<WorkshopItemMetadata>();

            foreach (var meta in metadataList)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Checking if {meta.PublishedFileId} ('{meta.Title}') is a collection...");
                var children = await _apiClient.GetCollectionDetailsAsync(meta.PublishedFileId);
                if (children.Count > 0)
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Item {meta.PublishedFileId} is a collection with {children.Count} children.");
                    meta.IsCollection = true;
                    meta.ChildrenIds = children;

                    var childrenMeta = await _apiClient.GetPublishedFileDetailsAsync(children);
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Fetched metadata for {childrenMeta.Count} children of collection {meta.PublishedFileId}.");
                    onCollectionFound?.Invoke(childrenMeta);
                }
                else
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Item {meta.PublishedFileId} is a normal workshop item.");
                    itemsToEnqueue.Add(meta);
                }
            }

            if (itemsToEnqueue.Count > 0)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Enqueueing {itemsToEnqueue.Count} items to UI thread...");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var item in itemsToEnqueue)
                    {
                        Enqueue(item);
                    }
                });
            }
        }

        public void Enqueue(WorkshopItemMetadata meta)
        {
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Attempting to enqueue {meta.PublishedFileId} ('{meta.Title}')...");
            if (Queue.Any(i => i.PublishedFileId == meta.PublishedFileId && i.State != DownloadState.Completed && i.State != DownloadState.Failed && i.State != DownloadState.Cancelled))
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Item {meta.PublishedFileId} is already in the queue and active. Skipping.");
                return;
            }

            var item = new WorkshopDownloadItem
            {
                PublishedFileId = meta.PublishedFileId,
                Title = meta.Title,
                PreviewUrl = meta.PreviewUrl,
                Author = meta.Author,
                FileSize = meta.FileSize,
                UpdatedAt = meta.UpdatedAt,
                State = DownloadState.Waiting,
                StatusText = "Waiting...",
                ProgressPercentage = 0
            };

            item.RetryCommand = new SimpleCommand(() => RetryDownload(item), () => item.CanRetry);
            item.CancelCommand = new SimpleCommand(() => CancelDownload(item), () => item.CanCancel);

            Queue.Add(item);
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Item {meta.PublishedFileId} added to queue. Queue size: {Queue.Count}");
            _ = ProcessQueueItemAsync(item);
        }

        public static void CancelDownload(WorkshopDownloadItem item)
        {
            if (item.CanCancel)
            {
                item.Cts?.Cancel();
                item.State = DownloadState.Cancelled;
                item.StatusText = "Cancelled";
            }
        }

        public void RetryDownload(WorkshopDownloadItem item)
        {
            if (item.CanRetry)
            {
                item.State = DownloadState.Waiting;
                item.StatusText = "Waiting...";
                item.ProgressPercentage = 0;
                _ = ProcessQueueItemAsync(item);
            }
        }

        public void RemoveFromQueue(WorkshopDownloadItem item)
        {
            CancelDownload(item);
            Queue.Remove(item);
        }

        public void ClearCompleted()
        {
            var completed = Queue.Where(i => i.State == DownloadState.Completed).ToList();
            foreach (var item in completed)
            {
                Queue.Remove(item);
            }
        }

        private async Task ProcessQueueItemAsync(WorkshopDownloadItem item)
        {
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Processing queue item {item.PublishedFileId} ('{item.Title}'). Waiting for semaphore...");
            await _concurrencySemaphore.WaitAsync();
            try
            {
                if (item.State == DownloadState.Cancelled)
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Item {item.PublishedFileId} was cancelled before processing started.");
                    return;
                }

                item.Cts = new CancellationTokenSource();
                var token = item.Cts.Token;

                item.State = DownloadState.Downloading;
                item.StatusText = "Downloading...";

                var provider = SelectedProvider ?? Providers.FirstOrDefault(p => p.IsAvailable);
                if (provider == null)
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: No available download provider found for {item.PublishedFileId}.");
                    item.State = DownloadState.Failed;
                    item.StatusText = "No download provider available";
                    return;
                }

                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Using provider '{provider.Name}' for {item.PublishedFileId}.");

                string modsDir = ConfigManager.GetModsPath();
                if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Mods folder not found at '{modsDir}'. Download failed for {item.PublishedFileId}.");
                    item.State = DownloadState.Failed;
                    item.StatusText = "Mods folder not configured/found";
                    return;
                }

                string targetDir = Path.Combine(modsDir, item.PublishedFileId.ToString());
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Target directory for {item.PublishedFileId}: {targetDir}");

                var progressReporter = new Progress<DownloadProgress>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (item.State == DownloadState.Failed || item.State == DownloadState.Cancelled || item.State == DownloadState.Completed)
                            return;

                        item.ProgressPercentage = p.Percentage;
                        if (p.Percentage >= 100)
                            item.StatusText = $"Download complete";
                        else
                            item.StatusText = $"Downloading {p.Percentage:F1}%";
                    });
                });

                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Starting download for {item.PublishedFileId}...");
                bool success = await provider.DownloadAsync(item.PublishedFileId, targetDir, progressReporter, token);

                if (success && !token.IsCancellationRequested)
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Download successful for {item.PublishedFileId}. Triggering mod list reload.");
                    item.State = DownloadState.Completed;
                    item.StatusText = "Completed";
                    item.ProgressPercentage = 100;

                    _manager.TriggerUIReload();
                }
                else if (token.IsCancellationRequested)
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Download cancelled for {item.PublishedFileId}.");
                    item.State = DownloadState.Cancelled;
                    item.StatusText = "Cancelled";
                }
                else
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Download failed for {item.PublishedFileId} via {provider.Name}.");
                    item.State = DownloadState.Failed;
                    item.StatusText = "Download failed";
                }
            }
            catch (Exception ex)
            {
                InfoLogger.LogRunDf($"WorkshopQueueManager: Exception during download of {item.PublishedFileId}: {ex.Message}");
                AppLogging.LogException($"QueueManager error downloading {item.PublishedFileId}", ex);
                item.State = DownloadState.Failed;
                item.StatusText = $"Error: {ex.Message}";
            }
            finally
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Releasing semaphore for {item.PublishedFileId}.");
                _concurrencySemaphore.Release();
            }
        }
    }
}
