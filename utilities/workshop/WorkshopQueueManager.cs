using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
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
        public List<ulong> Dependencies { get; set; } = [];

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

        private bool _isBatched;
        public bool IsBatched
        {
            get => _isBatched;
            set
            {
                _isBatched = value;
                OnPropertyChanged(nameof(IsBatched));
            }
        }

        public int AutoRetryCount { get; set; } = 0;

        public CancellationTokenSource? Cts { get; set; }

        public ICommand? RetryCommand { get; set; }
        public ICommand? CancelCommand { get; set; }
    }

    public class WorkshopQueueManager : IDisposable
    {
        public const int MaxQueueSize = 100;

        private readonly SemaphoreSlim _concurrencySemaphore = new(3, 3);
        private readonly object _steamCmdBatchLock = new();
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
                    _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                    _reloadTimer.Tick += (_, _) =>
                    {
                        _reloadTimer.Stop();
                        _manager.TriggerUIReload();
                    };
                }

                // Restart the timer (resets the window on every completed download)
                _reloadTimer.Stop();
                _reloadTimer.Start();
            });
        }

        public ObservableCollection<WorkshopDownloadItem> Queue { get; } = [];
        public List<IWorkshopDownloadProvider> Providers { get; } = [];
        public IWorkshopDownloadProvider? SelectedProvider { get; set; }
        public ModHearthManager Manager => _manager;

        public WorkshopQueueManager(ModHearthManager manager)
        {
            _manager = manager;

            // Initialize providers
            Providers.Add(new SteamCmdDownloadProvider());
            Providers.Add(new SteamWorkerDownloadProvider());

            // Select default configured provider, or fallback to SteamCmdDownloadProvider
            string savedProviderName = ConfigManager.GetDefaultWorkshopProvider();
            SelectedProvider = Providers.FirstOrDefault(p => string.Equals(p.GetType().Name, savedProviderName, StringComparison.OrdinalIgnoreCase))
                ?? Providers.FirstOrDefault(p => string.Equals(p.GetType().Name, "SteamCmdDownloadProvider", StringComparison.OrdinalIgnoreCase))
                ?? Providers.FirstOrDefault(p => string.Equals(p.GetType().Name, "SteamWorkerDownloadProvider", StringComparison.OrdinalIgnoreCase))
                ?? Providers.FirstOrDefault();
        }

        private static bool IsDuplicateSelection(ulong id, List<ulong>? allIds)
        {
            if (allIds == null)
                return false;
            int firstIdx = allIds.IndexOf(id);
            if (firstIdx < 0)
                return false;
            int lastIdx = allIds.LastIndexOf(id);
            return firstIdx != lastIdx;
        }

        public ModStatusClassification ClassifyMod(ulong id, List<ulong>? allSelectedIds = null)
        {
            string idStr = id.ToString();

            // Check for duplicates in current selection/checklist
            if (IsDuplicateSelection(id, allSelectedIds))
            {
                return ModStatusClassification.Duplicate;
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
            if (IsDuplicateSelection(meta.PublishedFileId, allIds))
            {
                return ModStatusClassification.Duplicate;
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
            InfoLogger.LogRunDf($"WorkshopQueueManager: Resolving URLs: {inputUrls}");
            var ids = WorkshopUrlResolver.ParseUrls(inputUrls);
            if (ids.Count == 0)
            {
                InfoLogger.LogRunDf("WorkshopQueueManager: No valid IDs found in input.");
                return;
            }

            InfoLogger.LogRunDf($"WorkshopQueueManager: Fetching metadata for {ids.Count} IDs...");
            var metadataList = await _apiClient.GetPublishedFileDetailsAsync(ids);

            if (metadataList.Count == 0)
            {
                InfoLogger.LogRunDf("WorkshopQueueManager: Steam API returned no metadata for the given IDs.");
                return;
            }

            var itemsToEnqueue = new List<WorkshopItemMetadata>();

            foreach (var meta in metadataList)
            {
                InfoLogger.LogRunDf($"WorkshopQueueManager: Checking if {meta.PublishedFileId} ('{meta.Title}') is a collection...");
                var children = await SteamWebApiClient.GetCollectionDetailsAsync(meta.PublishedFileId);
                switch (children.Count)
                {
                    case > 0:
                        {
                            InfoLogger.LogRunDf($"WorkshopQueueManager: Item {meta.PublishedFileId} is a collection with {children.Count} children.");
                            meta.IsCollection = true;
                            meta.ChildrenIds = children;

                            var childrenMeta = await _apiClient.GetPublishedFileDetailsAsync(children);
                            if (onCollectionFound != null)
                                await onCollectionFound(childrenMeta);
                            break;
                        }

                    default:
                        // Enqueue immediately so the UI populates in real time. May be a performance issue in the future with enough downloads
                        await Dispatcher.UIThread.InvokeAsync(() => Enqueue(meta));
                        break;

                }
            }

            if (itemsToEnqueue.Count > 0)
            {
                InfoLogger.LogRunDf($"WorkshopQueueManager: Enqueueing {itemsToEnqueue.Count} items to UI thread...");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var item in itemsToEnqueue)
                    {
                        Enqueue(item);
                    }
                });
            }
        }

        public async Task RedownloadWorkshopItemsAsync(IEnumerable<ulong> workshopIds)
        {
            var ids = workshopIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0)
                return;

            InfoLogger.LogRunDf($"WorkshopQueueManager: Redownloading {ids.Count} workshop items...");
            var metadataList = await _apiClient.GetPublishedFileDetailsAsync(ids);
            if (metadataList.Count == 0)
            {
                InfoLogger.LogRunDf("WorkshopQueueManager: Steam API returned no metadata for redownload IDs.");
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var meta in metadataList)
                {
                    var existing = Queue.FirstOrDefault(i => i.PublishedFileId == meta.PublishedFileId);
                    if (existing != null && (existing.State == DownloadState.Completed || existing.State == DownloadState.Failed || existing.State == DownloadState.Cancelled))
                    {
                        RemoveFromQueue(existing);
                    }
                    Enqueue(meta);
                }
            });
        }

        public void Enqueue(WorkshopItemMetadata meta)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => Enqueue(meta));
                return;
            }

            InfoLogger.LogRunDf($"WorkshopQueueManager: Attempting to enqueue {meta.PublishedFileId} ('{meta.Title}')...");
            if (Queue.Any(i => i.PublishedFileId == meta.PublishedFileId && i.State != DownloadState.Completed && i.State != DownloadState.Failed && i.State != DownloadState.Cancelled))
            {
                InfoLogger.LogRunDf($"WorkshopQueueManager: Item {meta.PublishedFileId} is already in the queue and active. Skipping.");
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

            Queue.Insert(0, item);
            InfoLogger.LogRunDf($"WorkshopQueueManager: Item {meta.PublishedFileId} added to queue. Queue size: {Queue.Count}");
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
                item.Cts?.Dispose();
                item.Cts = null;
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
                InfoLogger.LogRunDf($"WorkshopQueueManager: Auto-retrying download for {item.PublishedFileId} (attempt {item.AutoRetryCount}/3)...");
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
            _ = Queue.Remove(item);
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
                _ = Queue.Remove(item);
            }
        }

        private async Task ProcessQueueItemAsync(WorkshopDownloadItem item)
        {
            InfoLogger.LogRunDf($"WorkshopQueueManager: Processing queue item {item.PublishedFileId} ('{item.Title}'). Waiting for semaphore...");
            await _concurrencySemaphore.WaitAsync();
            try
            {
                if (item.State == DownloadState.Cancelled || item.State != DownloadState.Waiting)
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Item {item.PublishedFileId} was cancelled or already processed.");
                    return;
                }

                var provider = SelectedProvider ?? Providers.FirstOrDefault(p => p.IsAvailable);
                if (provider == null)
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: No available download provider found for {item.PublishedFileId}.");
                    FailOrAutoRetry(item, "No download provider available");
                    return;
                }

                InfoLogger.LogRunDf($"WorkshopQueueManager: Using provider '{provider.Name}' for {item.PublishedFileId}.");

                string modsDir = ConfigManager.GetModsPath();
                if (string.IsNullOrEmpty(modsDir))
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Mods folder not configured. Download failed for {item.PublishedFileId}.");
                    FailOrAutoRetry(item, "Mods folder not configured");
                    return;
                }

                try
                {
                    _ = Directory.CreateDirectory(modsDir);
                }
                catch (Exception ex)
                {
                    InfoLogger.LogRunDf($"WorkshopQueueManager: Failed to create mods folder at '{modsDir}': {ex.Message}");
                    FailOrAutoRetry(item, "Mods folder creation failed");
                    return;
                }

                List<WorkshopDownloadItem> batchGroup = [item];
                switch (provider)
                {
                    case SteamCmdDownloadProvider:
                        {
                            lock (_steamCmdBatchLock)
                            {
                                if (item.State != DownloadState.Waiting)
                                {
                                    return;
                                }

                                var waitingItems = Queue.Where(i => i.State == DownloadState.Waiting && i != item).Take(9).ToList();
                                batchGroup.AddRange(waitingItems);

                                foreach (var batchItem in batchGroup)
                                {
                                    batchItem.Cts?.Dispose();
                                    batchItem.Cts = new CancellationTokenSource();
                                    batchItem.SetState(DownloadState.Downloading);
                                    batchItem.SetStatus(batchGroup.Count > 1 ? "Downloading (Batched)..." : "Downloading...");
                                    batchItem.IsBatched = batchGroup.Count > 1;
                                }
                            }

                            break;
                        }

                    default:
                        item.IsBatched = false;
                        item.Cts?.Dispose();
                        item.Cts = new CancellationTokenSource();
                        item.SetState(DownloadState.Downloading);
                        item.SetStatus("Downloading...");
                        break;
                }

                switch (batchGroup.Count)
                {
                    case >= 2 when provider is SteamCmdDownloadProvider steamCmdProvider:
                        {
                            InfoLogger.LogRunDf($"WorkshopQueueManager: Batching {batchGroup.Count} items into single SteamCMD run.");

                            var batchItems = new List<BatchDownloadItem>();
                            var ctsList = batchGroup.Select(b => b.Cts!).ToList();

                            foreach (var batchItem in batchGroup)
                            {
                                string targetDir = Path.Combine(modsDir, batchItem.PublishedFileId.ToString());
                                var prog = new Progress<DownloadProgress>(p =>
                                {
                                    switch (batchItem.State)
                                    {
                                        case DownloadState.Failed:
                                        case DownloadState.Cancelled:
                                        case DownloadState.Completed:
                                            return;
                                    }

                                    switch (p.Percentage)
                                    {
                                        case >= 100:
                                            batchItem.SetProgress(100, "Download complete");
                                            break;
                                        default:
                                            batchItem.SetProgress(p.Percentage, $"Downloading {p.Percentage:F1}% (Batched)");
                                            break;
                                    }
                                });

                                batchItems.Add(new BatchDownloadItem(batchItem.PublishedFileId, targetDir, prog, batchItem.Cts!.Token));
                            }

                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ctsList.Select(c => c.Token).ToArray());
                            var results = await steamCmdProvider.DownloadBatchAsync(batchItems, linkedCts.Token);

                            bool anySuccess = false;
                            foreach (var batchItem in batchGroup)
                            {
                                bool success = results.TryGetValue(batchItem.PublishedFileId, out bool val) && val;
                                if (success && !batchItem.Cts!.Token.IsCancellationRequested)
                                {
                                    InfoLogger.LogRunDf($"WorkshopQueueManager: Batch download successful for {batchItem.PublishedFileId}.");
                                    batchItem.SetState(DownloadState.Completed);
                                    batchItem.SetProgress(100, "Completed");
                                    anySuccess = true;

                                    string targetDir = Path.Combine(modsDir, batchItem.PublishedFileId.ToString());
                                    CleanupOldModFolders(batchItem.PublishedFileId, targetDir);
                                }
                                else if (batchItem.Cts!.Token.IsCancellationRequested)
                                {
                                    batchItem.SetState(DownloadState.Cancelled);
                                    batchItem.SetStatus("Cancelled");
                                }
                                else
                                {
                                    InfoLogger.LogRunDf($"WorkshopQueueManager: Batch download failed for {batchItem.PublishedFileId}.");
                                    FailOrAutoRetry(batchItem, "Download failed");
                                }
                            }

                            if (anySuccess)
                            {
                                TriggerDebouncedUIReload();
                            }

                            break;
                        }

                    default:
                        {
                            var token = item.Cts!.Token;
                            string targetDir = Path.Combine(modsDir, item.PublishedFileId.ToString());
                            InfoLogger.LogRunDf($"WorkshopQueueManager: Target directory for {item.PublishedFileId}: {targetDir}");

                            var progressReporter = new Progress<DownloadProgress>(p =>
                            {
                                switch (item.State)
                                {
                                    case DownloadState.Failed:
                                    case DownloadState.Cancelled:
                                    case DownloadState.Completed:
                                        return;
                                }

                                switch (p.Percentage)
                                {
                                    case >= 100:
                                        item.SetProgress(100, "Download complete");
                                        break;
                                    default:
                                        item.SetProgress(p.Percentage, $"Downloading {p.Percentage:F1}%");
                                        break;
                                }
                            });

                            InfoLogger.LogRunDf($"WorkshopQueueManager: Starting download for {item.PublishedFileId}...");
                            bool success = await provider.DownloadAsync(item.PublishedFileId, targetDir, progressReporter, token);

                            if (success && !token.IsCancellationRequested)
                            {
                                InfoLogger.LogRunDf($"WorkshopQueueManager: Download successful for {item.PublishedFileId}. Triggering mod list reload.");
                                item.SetState(DownloadState.Completed);
                                item.SetProgress(100, "Completed");

                                CleanupOldModFolders(item.PublishedFileId, targetDir);

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

                            break;
                        }

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
                InfoLogger.LogRunDf($"WorkshopQueueManager: Releasing semaphore for {item.PublishedFileId}.");
                _ = _concurrencySemaphore.Release();
            }
        }

        private void CleanupOldModFolders(ulong publishedFileId, string targetDir)
        {
            try
            {
                string idStr = publishedFileId.ToString();
                string canonicalTargetDir = ConfigManager.ResolveCanonicalPath(targetDir);

                var existingMods = _manager.LoadedMods.Where(m =>
                    string.Equals(m.steamID, idStr, StringComparison.OrdinalIgnoreCase) ||
                     (ModHearthManager.TryGetSteamWorkshopItemId(m, out string sId) && string.Equals(sId, idStr, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                foreach (var oldMod in existingMods)
                {
                    if (!string.IsNullOrWhiteSpace(oldMod.path))
                    {
                        string canonicalOldPath = ConfigManager.ResolveCanonicalPath(oldMod.path);
                        if (!string.Equals(canonicalOldPath, canonicalTargetDir, StringComparison.OrdinalIgnoreCase)
                        && ModHearthManager.CanDeleteModFromModsFolder(oldMod) && _manager.DeleteModFromModsFolder(oldMod, out string _))
                        {
                            _manager.ShowNotification($"Deleted old mod folder: {Path.GetFileName(oldMod.path)}", "trashIcon.svg");
                            InfoLogger.LogRunDf($"WorkshopQueueManager: Deleted old mod folder '{oldMod.path}' for workshop item {publishedFileId}.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                InfoLogger.LogRunDf($"WorkshopQueueManager: Error cleaning up old mod folders for {publishedFileId}: {ex.Message}");
                AppLogging.LogException($"Error cleaning up old mod folders for {publishedFileId}", ex);
            }
        }

        private bool _disposed;
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // dispose managed resources
                try
                {
                    // Stop the UI timer on the UI thread
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { _reloadTimer?.Stop(); } catch (Exception ex) { AppLogging.LogException("Failed to stop reload timer", ex); }
                        _reloadTimer = null;
                    });
                }
                catch (Exception ex)
                {
                    AppLogging.LogException("Failed to post reload timer stop action to UI thread", ex);
                }

                try
                {
                    foreach (var item in Queue)
                    {
                        try
                        {
                            item.Cts?.Cancel();
                            item.Cts?.Dispose();
                            item.Cts = null;
                        }
                        catch (Exception ex)
                        {
                            AppLogging.LogException($"Failed to cancel/dispose cancellation token source for workshop item {item.PublishedFileId}", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogging.LogException("Failed to iterate workshop queue items during disposal", ex);
                }

                try { _concurrencySemaphore?.Dispose(); } catch (Exception ex) { AppLogging.LogException("Failed to dispose concurrency semaphore", ex); }
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
