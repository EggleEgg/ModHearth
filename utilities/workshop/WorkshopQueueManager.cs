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

        // Helper methods to immediately refresh the ui
        public void SetState(DownloadState state)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                State = state;
            }
            else
            {
                Dispatcher.UIThread.Post(() => State = state);
            }
        }
        public void SetProgress(double percentage, string statusText)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ProgressPercentage = percentage;
                StatusText = statusText;
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressPercentage = percentage;
                    StatusText = statusText;
                });
            }
        }
        public void SetStatus(string statusText)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                StatusText = statusText;
            }
            else
            {
                Dispatcher.UIThread.Post(() => StatusText = statusText);
            }
        }

        public bool CanRetry => State == DownloadState.Failed || State == DownloadState.Cancelled;
        public bool CanCancel => State == DownloadState.Waiting || State == DownloadState.Resolving || State == DownloadState.Downloading;

        public int AutoRetryCount { get; set; } = 0;

        public CancellationTokenSource? Cts { get; set; }

        public ICommand? RetryCommand { get; set; }
        public ICommand? CancelCommand { get; set; }
    }

    public class WorkshopQueueManager
    {
        public const int MaxQueueSize = 100;

        private readonly SemaphoreSlim _concurrencySemaphore = new SemaphoreSlim(3, 3);
        private readonly SteamWebApiClient _apiClient = new();
        private readonly ModHearthManager _manager;

        // Timer for debouncing UI reloads
        private DispatcherTimer? _reloadTimer;

        private void TriggerDebouncedUIReload()
        {
            // Marshal timer reset to the UI Thread
            Dispatcher.UIThread.Post(() =>
            {
                if (_reloadTimer == null)
                {
                    _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                    _reloadTimer.Tick += (_, _) =>
                    {
                        _reloadTimer.Stop();
                        _manager.TriggerUIReload();
                    };
                }

                // Restart the timer (resets the 500ms window on every completed download)
                _reloadTimer.Stop();
                _reloadTimer.Start();
            });
        }

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

        public async Task ResolveAndEnqueueUrlsAsync(string inputUrls, Func<List<WorkshopItemMetadata>, Task>? onCollectionFound)
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
                    if (onCollectionFound != null)
                        await onCollectionFound(childrenMeta);
                }
                else
                {
                    // Enqueue immediately so the UI populates in real time. May be a performance issue in the future with enough downloads
                    await Dispatcher.UIThread.InvokeAsync(() => Enqueue(meta));
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
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => Enqueue(meta));
                return;
            }

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

            while (Queue.Count >= MaxQueueSize)
            {
                var oldestRemovable = Queue.FirstOrDefault(i => i.State == DownloadState.Completed || i.State == DownloadState.Failed || i.State == DownloadState.Cancelled)
                    ?? Queue.FirstOrDefault();
                if (oldestRemovable != null)
                    RemoveFromQueue(oldestRemovable);
                else
                    break;
            }

            Queue.Add(item);
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Item {meta.PublishedFileId} added to queue. Queue size: {Queue.Count}");
            _ = ProcessQueueItemAsync(item);
        }

        public static void CancelDownload(WorkshopDownloadItem item)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => CancelDownload(item));
                return;
            }

            if (item.CanCancel)
            {
                item.Cts?.Cancel();
                item.SetState(DownloadState.Cancelled);
                item.SetStatus("Cancelled");
            }
        }

        public void RetryDownload(WorkshopDownloadItem item)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RetryDownload(item));
                return;
            }

            if (item.CanRetry)
            {
                item.AutoRetryCount = 0;
                item.SetState(DownloadState.Waiting);
                item.SetProgress(0, "Waiting...");
                _ = ProcessQueueItemAsync(item);
            }
        }

        private void FailOrAutoRetry(WorkshopDownloadItem item, string statusText)
        {
            if (ConfigManager.IsAutoRetryAllEnabled() && item.AutoRetryCount < 3)
            {
                item.AutoRetryCount++;
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Auto-retrying download for {item.PublishedFileId} (attempt {item.AutoRetryCount}/3)...");
                item.SetState(DownloadState.Waiting);
                item.SetProgress(0, $"Auto-retrying ({item.AutoRetryCount}/3)...");
                _ = ProcessQueueItemAsync(item);
                return;
            }

            item.SetState(DownloadState.Failed);
            item.SetStatus(statusText);
        }

        public void RemoveFromQueue(WorkshopDownloadItem item)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RemoveFromQueue(item));
                return;
            }

            CancelDownload(item);
            Queue.Remove(item);
        }

        public void RetryAll()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RetryAll);
                return;
            }

            foreach (var item in Queue.Where(i => i.CanRetry).ToList())
            {
                RetryDownload(item);
            }
        }

        public void CancelAll()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(CancelAll);
                return;
            }

            foreach (var item in Queue.Where(i => i.CanCancel).ToList())
            {
                CancelDownload(item);
            }
        }

        public void ClearCompleted()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ClearCompleted);
                return;
            }

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

                item.SetState(DownloadState.Downloading);
                item.SetStatus("Downloading...");

                var provider = SelectedProvider ?? Providers.FirstOrDefault(p => p.IsAvailable);
                if (provider == null)
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: No available download provider found for {item.PublishedFileId}.");
                    FailOrAutoRetry(item, "No download provider available");
                    return;
                }

                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Using provider '{provider.Name}' for {item.PublishedFileId}.");

                string modsDir = ConfigManager.GetModsPath();
                if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Mods folder not found at '{modsDir}'. Download failed for {item.PublishedFileId}.");
                    FailOrAutoRetry(item, "Mods folder not configured/found");
                    return;
                }

                string targetDir = Path.Combine(modsDir, item.PublishedFileId.ToString());
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Target directory for {item.PublishedFileId}: {targetDir}");

                var progressReporter = new Progress<DownloadProgress>(p =>
                {
                    if (item.State == DownloadState.Failed || item.State == DownloadState.Cancelled || item.State == DownloadState.Completed)
                        return;

                    if (p.Percentage >= 100)
                        item.SetProgress(100, "Download complete");
                    else
                        item.SetProgress(p.Percentage, $"Downloading {p.Percentage:F1}%");
                });

                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Starting download for {item.PublishedFileId}...");
                bool success = await provider.DownloadAsync(item.PublishedFileId, targetDir, progressReporter, token);

                if (success && !token.IsCancellationRequested)
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Download successful for {item.PublishedFileId}. Triggering mod list reload.");
                    item.SetState(DownloadState.Completed);
                    item.SetProgress(100, "Completed");

                    TriggerDebouncedUIReload();
                }
                else if (token.IsCancellationRequested)
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Download cancelled for {item.PublishedFileId}.");
                    item.SetState(DownloadState.Cancelled);
                    item.SetStatus("Cancelled");
                }
                else
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Download failed for {item.PublishedFileId} via {provider.Name}.");
                    FailOrAutoRetry(item, "Download failed");
                }
            }
            catch (Exception ex)
            {
                InfoLogger.LogRunDf($"WorkshopQueueManager: Exception during download of {item.PublishedFileId}: {ex.Message}");
                AppLogging.LogException($"QueueManager error downloading {item.PublishedFileId}", ex);
                FailOrAutoRetry(item, $"Error: {ex.Message}");
            }
            finally
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopQueueManager: Releasing semaphore for {item.PublishedFileId}.");
                _concurrencySemaphore.Release();
            }
        }
    }
}
