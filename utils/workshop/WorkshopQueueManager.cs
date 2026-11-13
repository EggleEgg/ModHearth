using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using ModHearth.Config;
using ModHearth.Manager;
using ModHearth.Models;
using ModHearth.Utils.IO;
using ModHearth.Utils.Logging;
using ModHearth.Utils.Thread;

namespace ModHearth.Utils.Workshop
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

    public class SimpleCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        private readonly Func<bool>? _canExecute = canExecute;

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    }

    public class WorkshopDownloadItem : ObservableObject
    {
        public ulong PublishedFileId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<ulong> Dependencies { get; set; } = [];

        public DownloadState State
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(CanRetry));
                OnPropertyChanged(nameof(CanCancel));
                ((SimpleCommand?)RetryCommand)?.RaiseCanExecuteChanged();
                ((SimpleCommand?)CancelCommand)?.RaiseCanExecuteChanged();
            }
        } = DownloadState.Waiting;

        public double ProgressPercentage
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }

        public string StatusText
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged(nameof(StatusText));
            }
        } = "Waiting...";

        // Helper methods to immediately refresh the ui
        public void SetState(DownloadState state) => UiThread.Run(() => State = state);

        public void SetProgress(double percentage, string statusText) => UiThread.Run(() =>
        {
            ProgressPercentage = percentage;
            StatusText = statusText;
        });

        public void SetStatus(string statusText) => UiThread.Run(() => StatusText = statusText);

        public bool CanRetry => State is DownloadState.Failed or DownloadState.Cancelled;
        public bool CanCancel => State is DownloadState.Waiting or DownloadState.Resolving or DownloadState.Downloading;

        public bool IsBatched
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged(nameof(IsBatched));
            }
        }

        public int AutoRetryCount { get; set; }

        public CancellationTokenSource? Cts { get; set; }

        public ICommand? RetryCommand { get; set; }
        public ICommand? CancelCommand { get; set; }
    }

    public class WorkshopQueueManager : IDisposable
    {
        public const int MaxQueueSize = 100;

        private readonly SemaphoreSlim _concurrencySemaphore = new(3, 3);
        private readonly Lock _steamCmdBatchLock = new();

        // Timer for debouncing UI reloads

        private DispatcherTimer? _reloadTimer;

        // Aggregate progress for the main-window indicator. Each logical run gets its own tracker
        // session so stale Completed items from a previous run never leak into the next one.
        private WorkshopDownloadSession? _progressSession;
        private readonly Dictionary<WorkshopDownloadItem, long> _progressEntries = [];
        private int _aggregateRefreshQueued;
        private volatile bool _aggregateUnsubscribed;

        private void TriggerDebouncedUIReload() =>
            // Marshal timer reset to the UI Thread
            Dispatcher.UIThread.Post(() =>
            {
                if (_reloadTimer == null)
                {
                    _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                    _reloadTimer.Tick += (_, _) =>
                    {
                        _reloadTimer.Stop();
                        Manager.TriggerUIReload();
                    };
                }

                // Restart the timer (resets the window on every completed download)
                _reloadTimer.Stop();
                _reloadTimer.Start();
            });

        public ObservableCollection<WorkshopDownloadItem> Queue { get; } = [];
        public List<IWorkshopDownloadProvider> Providers { get; } = [];
        public IWorkshopDownloadProvider? SelectedProvider { get; set; }
        public ModHearthManager Manager { get; }

        public WorkshopQueueManager(ModHearthManager manager)
        {
            Manager = manager;

            Queue.CollectionChanged += OnQueueCollectionChanged;

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
            string idStr = id.ToString(CultureInfo.InvariantCulture);

            // Check for duplicates in current selection/checklist
            if (IsDuplicateSelection(id, allSelectedIds))
            {
                return ModStatusClassification.Duplicate;
            }

            // Find installed mod
            IReadOnlyCollection<ModReference> installedMods = Manager.LoadedMods;
            ModReference? installedMod = installedMods.FirstOrDefault(m =>
                string.Equals(m.SteamID, idStr, StringComparison.OrdinalIgnoreCase));

            if (installedMod != null)
            {
                return ModStatusClassification.AlreadyInstalled;
            }

            return ModStatusClassification.New;
        }

        public ModStatusClassification ClassifyModWithMetadata(WorkshopItemMetadata meta, List<ulong> allIds)
        {
            string idStr = meta.PublishedFileId.ToString(CultureInfo.InvariantCulture);

            // Duplicate check
            if (IsDuplicateSelection(meta.PublishedFileId, allIds))
            {
                return ModStatusClassification.Duplicate;
            }

            IReadOnlyCollection<ModReference> installedMods = Manager.LoadedMods;
            ModReference? installedMod = installedMods.FirstOrDefault(m =>
                string.Equals(m.SteamID, idStr, StringComparison.OrdinalIgnoreCase));

            if (installedMod != null)
            {
                if (installedMod.LastModifiedTime.HasValue && meta.UpdatedAt > installedMod.LastModifiedTime.Value.AddMinutes(5))
                    return ModStatusClassification.UpdateAvailable;
                return ModStatusClassification.AlreadyInstalled;
            }

            // Check missing dependencies
            if (meta.ChildrenIds != null && meta.ChildrenIds.Count > 0)
            {
                foreach (ulong childId in meta.ChildrenIds)
                {
                    bool isInstalled = installedMods.Any(m => string.Equals(m.SteamID, childId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase));
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
            Loggers.Workshop.Log($"WorkshopQueueManager: Resolving URLs: {inputUrls}");
            List<ulong> ids = WorkshopUrlResolver.ParseUrls(inputUrls);
            if (ids.Count == 0)
            {
                Loggers.Workshop.Log("WorkshopQueueManager: No valid IDs found in input.");
                return;
            }

            Loggers.Workshop.Log($"WorkshopQueueManager: Fetching metadata for {ids.Count} IDs...");
            List<WorkshopItemMetadata> metadataList = await SteamWebApiClient.GetPublishedFileDetailsAsync(ids);

            if (metadataList.Count == 0)
            {
                Loggers.Workshop.Log("WorkshopQueueManager: Steam API returned no metadata for the given IDs.");
                return;
            }

            foreach (WorkshopItemMetadata meta in metadataList)
            {
                Loggers.Workshop.Log($"WorkshopQueueManager: Checking if {meta.PublishedFileId} ('{meta.Title}') is a collection...");
                List<ulong> children = await SteamWebApiClient.GetCollectionDetailsAsync(meta.PublishedFileId);
                if (children.Count > 0)
                {
                    Loggers.Workshop.Log($"WorkshopQueueManager: Item {meta.PublishedFileId} is a collection with {children.Count} children.");
                    meta.IsCollection = true;
                    meta.ChildrenIds = children;

                    List<WorkshopItemMetadata> childrenMeta = await SteamWebApiClient.GetPublishedFileDetailsAsync(children);
                    if (onCollectionFound != null)
                        await onCollectionFound(childrenMeta);
                }
                else
                {
                    // Enqueue immediately so the UI populates in real time. May be a performance issue in the future with enough downloads
                    await Dispatcher.UIThread.InvokeAsync(() => Enqueue(meta));
                }
            }
        }

        public async Task RedownloadWorkshopItemsAsync(IEnumerable<ulong> workshopIds)
        {
            List<ulong> ids = [.. workshopIds.Where(id => id > 0).Distinct()];
            if (ids.Count == 0)
                return;

            Loggers.Workshop.Log($"WorkshopQueueManager: Redownloading {ids.Count} workshop items...");
            List<WorkshopItemMetadata> metadataList = await SteamWebApiClient.GetPublishedFileDetailsAsync(ids);
            if (metadataList.Count == 0)
            {
                Loggers.Workshop.Log("WorkshopQueueManager: Steam API returned no metadata for redownload IDs.");
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (WorkshopItemMetadata meta in metadataList)
                {
                    WorkshopDownloadItem? existing = Queue.FirstOrDefault(i => i.PublishedFileId == meta.PublishedFileId);
                    if (existing != null && (existing.State == DownloadState.Completed || existing.State == DownloadState.Failed || existing.State == DownloadState.Cancelled))
                    {
                        RemoveFromQueue(existing);
                    }
                    Enqueue(meta);
                }
            });
        }

        public void Enqueue(WorkshopItemMetadata meta) => UiThread.Run(() => EnqueueCore(meta));

        private void EnqueueCore(WorkshopItemMetadata meta)
        {
            Loggers.Workshop.Log($"WorkshopQueueManager: Attempting to enqueue {meta.PublishedFileId} ('{meta.Title}')...");
            if (Queue.Any(i => i.PublishedFileId == meta.PublishedFileId && i.State != DownloadState.Completed && i.State != DownloadState.Failed && i.State != DownloadState.Cancelled))
            {
                Loggers.Workshop.Log($"WorkshopQueueManager: Item {meta.PublishedFileId} is already in the queue and active. Skipping.");
                return;
            }

            WorkshopDownloadItem item = new()

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
                WorkshopDownloadItem? oldestRemovable = Queue.FirstOrDefault(i => i.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled)
                    ?? Queue.FirstOrDefault();
                if (oldestRemovable != null)
                    RemoveFromQueue(oldestRemovable);
                else
                    break;
            }

            Queue.Insert(0, item);
            Loggers.Workshop.Log($"WorkshopQueueManager: Item {meta.PublishedFileId} added to queue. Queue size: {Queue.Count}");
            _ = ProcessQueueItemAsync(item);
        }

        public static void CancelDownload(WorkshopDownloadItem item) => UiThread.Run(() => CancelDownloadCore(item));

        private static void CancelDownloadCore(WorkshopDownloadItem item)
        {
            if (item.CanCancel)
            {
                item.Cts?.Cancel();
                item.Cts?.Dispose();
                item.Cts = null;
                item.SetState(DownloadState.Cancelled);
                item.SetStatus("Cancelled");
            }
        }

        public void RetryDownload(WorkshopDownloadItem item) => UiThread.Run(() => RetryDownloadCore(item));

        private void RetryDownloadCore(WorkshopDownloadItem item)
        {
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
                Loggers.Workshop.Log($"WorkshopQueueManager: Auto-retrying download for {item.PublishedFileId} (attempt {item.AutoRetryCount}/3)...");
                item.SetState(DownloadState.Waiting);
                item.SetProgress(0, $"Auto-retrying ({item.AutoRetryCount}/3)...");
                _ = ProcessQueueItemAsync(item);
                return;
            }

            item.SetState(DownloadState.Failed);
            item.SetStatus(statusText);
        }

        public void RemoveFromQueue(WorkshopDownloadItem item) => UiThread.Run(() => RemoveFromQueueCore(item));

        private void RemoveFromQueueCore(WorkshopDownloadItem item)
        {
            CancelDownloadCore(item);
            _ = Queue.Remove(item);
        }

        public void RetryAll() => UiThread.Run(RetryAllCore);

        private void RetryAllCore()
        {
            foreach (WorkshopDownloadItem? item in Queue.Where(i => i.CanRetry).ToList())
            {
                RetryDownloadCore(item);
            }
        }

        public void CancelAll() => UiThread.Run(CancelAllCore);

        private void CancelAllCore()
        {
            foreach (WorkshopDownloadItem? item in Queue.Where(i => i.CanCancel).ToList())
            {
                CancelDownloadCore(item);
            }
        }

        public void ClearCompleted() => UiThread.Run(ClearCompletedCore);

        private void ClearCompletedCore()
        {
            List<WorkshopDownloadItem> completed = [.. Queue.Where(i => i.State == DownloadState.Completed)];
            foreach (WorkshopDownloadItem? item in completed)
            {
                _ = Queue.Remove(item);
            }
        }

        private static bool IsActiveState(DownloadState state) =>
            state is DownloadState.Waiting or DownloadState.Resolving or DownloadState.Downloading;

        private void OnQueueCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (WorkshopDownloadItem item in e.OldItems.OfType<WorkshopDownloadItem>())
                    item.PropertyChanged -= OnQueuedItemPropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (WorkshopDownloadItem item in e.NewItems.OfType<WorkshopDownloadItem>())
                    item.PropertyChanged += OnQueuedItemPropertyChanged;
            }

            RequestAggregateRefresh();
        }

        private void OnQueuedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(WorkshopDownloadItem.State) or nameof(WorkshopDownloadItem.ProgressPercentage)))
                return;

            RequestAggregateRefresh();
        }

        /// <summary>Coalesces bursts of per-item updates into a single UI-thread refresh.</summary>
        private void RequestAggregateRefresh()
        {
            if (_aggregateUnsubscribed)
                return;

            if (Interlocked.Exchange(ref _aggregateRefreshQueued, 1) != 0)
                return;

            UiThread.Run(() =>
            {
                _aggregateRefreshQueued = 0;
                RefreshAggregateProgressCore();
            });
        }

        private void RefreshAggregateProgressCore()
        {
            if (_aggregateUnsubscribed)
                return;

            // Items leave Queue when the user clears completed downloads or removes an entry; drop them
            // so the run total stops counting something that is no longer tracked.
            if (_progressEntries.Count > 0)
            {
                List<WorkshopDownloadItem> removed = [.. _progressEntries.Keys.Where(item => !Queue.Contains(item))];
                foreach (WorkshopDownloadItem item in removed)
                {
                    _progressSession?.Drop(_progressEntries[item]);
                    _ = _progressEntries.Remove(item);
                }
            }

            WorkshopDownloadProgressTracker? tracker = ProgressTracker;
            if (tracker == null)
            {
                EndProgressSession();
                return;
            }

            bool anyActive = false;
            foreach (WorkshopDownloadItem item in Queue)
            {
                bool tracked = _progressEntries.TryGetValue(item, out long entryId);

                if (IsActiveState(item.State))
                {
                    anyActive = true;
                    if (!tracked)
                    {
                        _progressSession ??= tracker.BeginSession();
                        entryId = _progressSession.Track(item.ProgressPercentage);
                        _progressEntries[item] = entryId;
                    }
                    else
                    {
                        _progressSession?.Report(entryId, item.ProgressPercentage);
                    }
                }
                else if (tracked)
                {
                    // Completed items stay counted (as 100%) until the whole run ends so the bar keeps
                    // moving forward; failed/cancelled items are dropped since they have no progress.
                    if (item.State == DownloadState.Completed)
                    {
                        _progressSession?.Complete(entryId);
                    }
                    else
                    {
                        _progressSession?.Drop(entryId);
                        _ = _progressEntries.Remove(item);
                    }
                }
            }

            if (!anyActive)
                EndProgressSession();
        }

        private WorkshopDownloadProgressTracker? ProgressTracker => Manager?.WorkshopDownloads;

        private void EndProgressSession()
        {
            _progressSession?.Dispose();
            _progressSession = null;
            _progressEntries.Clear();
        }

        private void DetachAggregateTracking()
        {
            _aggregateUnsubscribed = true;
            UiThread.Run(DetachAggregateTrackingCore);
        }

        private void DetachAggregateTrackingCore()
        {
            Queue.CollectionChanged -= OnQueueCollectionChanged;
            foreach (WorkshopDownloadItem item in Queue)
                item.PropertyChanged -= OnQueuedItemPropertyChanged;
            EndProgressSession();
        }

        private async Task ProcessQueueItemAsync(WorkshopDownloadItem item)
        {
            Loggers.Workshop.Log($"WorkshopQueueManager: Processing queue item {item.PublishedFileId} ('{item.Title}'). Waiting for semaphore...");
            await _concurrencySemaphore.WaitAsync();
            try
            {
                if (item.State is DownloadState.Cancelled or not DownloadState.Waiting)
                {
                    Loggers.Workshop.Log($"WorkshopQueueManager: Item {item.PublishedFileId} was cancelled or already processed.");
                    return;
                }

                IWorkshopDownloadProvider? provider = SelectedProvider ?? Providers.FirstOrDefault(p => p.IsAvailable);
                if (provider == null)
                {
                    Loggers.Workshop.Log($"WorkshopQueueManager: No available download provider found for {item.PublishedFileId}.");
                    FailOrAutoRetry(item, "No download provider available");
                    return;
                }

                Loggers.Workshop.Log($"WorkshopQueueManager: Using provider '{provider.Name}' for {item.PublishedFileId}.");

                string modsDir = ConfigManager.GetModsPath();
                if (string.IsNullOrEmpty(modsDir))
                {
                    Loggers.Workshop.Log($"WorkshopQueueManager: Mods folder not configured. Download failed for {item.PublishedFileId}.");
                    FailOrAutoRetry(item, "Mods folder not configured");
                    return;
                }

                try
                {
                    _ = Directory.CreateDirectory(modsDir);
                }
                catch (Exception ex)
                {
                    Loggers.Workshop.Log($"WorkshopQueueManager: Failed to create mods folder at '{modsDir}': {ex.Message}");
                    FailOrAutoRetry(item, "Mods folder creation failed");
                    return;
                }

                List<WorkshopDownloadItem> batchGroup = [item];
                if (provider is SteamCmdDownloadProvider)
                {
                    lock (_steamCmdBatchLock)
                    {
                        if (item.State != DownloadState.Waiting)
                        {
                            return;
                        }

                        List<WorkshopDownloadItem> waitingItems = [.. Queue.Where(i => i.State == DownloadState.Waiting && i != item).Take(9)];
                        batchGroup.AddRange(waitingItems);

                        foreach (WorkshopDownloadItem batchItem in batchGroup)
                        {
                            batchItem.Cts?.Dispose();
                            batchItem.Cts = new CancellationTokenSource();
                            batchItem.SetState(DownloadState.Downloading);
                            batchItem.SetStatus(batchGroup.Count > 1 ? "Downloading (Batched)..." : "Downloading...");
                            batchItem.IsBatched = batchGroup.Count > 1;
                        }
                    }
                }
                else
                {
                    item.IsBatched = false;
                    item.Cts?.Dispose();
                    item.Cts = new CancellationTokenSource();
                    item.SetState(DownloadState.Downloading);
                    item.SetStatus("Downloading...");
                }

                if (batchGroup.Count >= 2 && provider is SteamCmdDownloadProvider steamCmdProvider)
                {
                    Loggers.Workshop.Log($"WorkshopQueueManager: Batching {batchGroup.Count} items into single SteamCMD run.");

                    List<BatchDownloadItem> batchItems = [];
                    List<CancellationTokenSource> ctsList = [.. batchGroup.Select(b => b.Cts!)];

                    foreach (WorkshopDownloadItem batchItem in batchGroup)
                    {
                        string targetDir = Path.Combine(modsDir, batchItem.PublishedFileId.ToString(CultureInfo.InvariantCulture));
                        Progress<DownloadProgress> prog = CreateProgressReporter(batchItem, batched: true);

                        batchItems.Add(new BatchDownloadItem(batchItem.PublishedFileId, targetDir, prog, batchItem.Cts!.Token));
                    }

                    using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource([.. ctsList.Select(c => c.Token)]);
                    Dictionary<ulong, bool> results = await steamCmdProvider.DownloadBatchAsync(batchItems, linkedCts.Token);

                    bool anySuccess = false;
                    foreach (WorkshopDownloadItem batchItem in batchGroup)
                    {
                        bool success = results.TryGetValue(batchItem.PublishedFileId, out bool val) && val;
                        if (success && !batchItem.Cts!.Token.IsCancellationRequested)
                        {
                            Loggers.Workshop.Log($"WorkshopQueueManager: Batch download successful for {batchItem.PublishedFileId}.");
                            batchItem.SetState(DownloadState.Completed);
                            batchItem.SetProgress(100, "Completed");
                            anySuccess = true;

                            string targetDir = Path.Combine(modsDir, batchItem.PublishedFileId.ToString(CultureInfo.InvariantCulture));
                            CleanupOldModFolders(batchItem.PublishedFileId, targetDir);
                        }
                        else if (batchItem.Cts!.Token.IsCancellationRequested)
                        {
                            batchItem.SetState(DownloadState.Cancelled);
                            batchItem.SetStatus("Cancelled");
                        }
                        else
                        {
                            Loggers.Workshop.Log($"WorkshopQueueManager: Batch download failed for {batchItem.PublishedFileId}.");
                            FailOrAutoRetry(batchItem, "Download failed");
                        }
                    }

                    if (anySuccess)
                    {
                        TriggerDebouncedUIReload();
                    }
                }
                else
                {
                    CancellationToken token = item.Cts!.Token;
                    string targetDir = Path.Combine(modsDir, item.PublishedFileId.ToString(CultureInfo.InvariantCulture));
                    Loggers.Workshop.Log($"WorkshopQueueManager: Target directory for {item.PublishedFileId}: {targetDir}");

                    Progress<DownloadProgress> progressReporter = CreateProgressReporter(item, batched: false);

                    Loggers.Workshop.Log($"WorkshopQueueManager: Starting download for {item.PublishedFileId}...");
                    bool success = await provider.DownloadAsync(item.PublishedFileId, targetDir, progressReporter, token);

                    if (success && !token.IsCancellationRequested)
                    {
                        Loggers.Workshop.Log($"WorkshopQueueManager: Download successful for {item.PublishedFileId}. Triggering mod list reload.");
                        item.SetState(DownloadState.Completed);
                        item.SetProgress(100, "Completed");

                        CleanupOldModFolders(item.PublishedFileId, targetDir);

                        TriggerDebouncedUIReload();
                    }
                    else if (token.IsCancellationRequested)
                    {
                        Loggers.Workshop.Log($"WorkshopQueueManager: Download cancelled for {item.PublishedFileId}.");
                        item.SetState(DownloadState.Cancelled);
                        item.SetStatus("Cancelled");
                    }
                    else
                    {
                        Loggers.Workshop.Log($"WorkshopQueueManager: Download failed for {item.PublishedFileId} via {provider.Name}.");
                        FailOrAutoRetry(item, "Download failed");
                    }
                }
            }
            catch (Exception ex)
            {
                Loggers.Workshop.Log($"WorkshopQueueManager: Exception during download of {item.PublishedFileId}: {ex.Message}");
                AppLogging.LogException($"QueueManager error downloading {item.PublishedFileId}", ex);
                FailOrAutoRetry(item, $"Error: {ex.Message}");
            }
            finally
            {
                Loggers.Workshop.Log($"WorkshopQueueManager: Releasing semaphore for {item.PublishedFileId}.");
                _ = _concurrencySemaphore.Release();
            }
        }

        private static Progress<DownloadProgress> CreateProgressReporter(WorkshopDownloadItem item, bool batched)
        {
            string batchedSuffix = batched ? " (Batched)" : string.Empty;
            return new Progress<DownloadProgress>(p =>
            {
                if (item.State is DownloadState.Failed or DownloadState.Cancelled or DownloadState.Completed)
                    return;

                if (p.Percentage >= 100)
                    item.SetProgress(100, "Download complete");
                else
                    item.SetProgress(p.Percentage, $"Downloading {p.Percentage:F1}%{batchedSuffix}");
            });
        }

        private void CleanupOldModFolders(ulong publishedFileId, string targetDir)
        {
            try
            {
                string idStr = publishedFileId.ToString(CultureInfo.InvariantCulture);
                string canonicalTargetDir = ConfigManager.ResolveCanonicalPath(targetDir);

                List<ModReference> existingMods = [.. Manager.LoadedMods.Where(m =>
                    string.Equals(m.SteamID, idStr, StringComparison.OrdinalIgnoreCase) ||
                     (ModHearthManager.TryGetSteamWorkshopItemId(m, out string sId) && string.Equals(sId, idStr, StringComparison.OrdinalIgnoreCase))
                )];

                using (Manager.BeginDeletionBatch())
                {
                    foreach (ModReference? oldMod in existingMods)
                    {
                        if (!string.IsNullOrWhiteSpace(oldMod.Path))
                        {
                            string canonicalOldPath = ConfigManager.ResolveCanonicalPath(oldMod.Path);
                            if (!PathHelper.PathsEqual(canonicalOldPath, canonicalTargetDir)
                            && ModHearthManager.CanDeleteModFromModsFolder(oldMod) && Manager.DeleteModFromModsFolder(oldMod, out string _))
                            {
                                Loggers.Workshop.Log($"WorkshopQueueManager: Deleted old mod folder '{oldMod.Path}' for workshop item {publishedFileId}.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loggers.Workshop.Log($"WorkshopQueueManager: Error cleaning up old mod folders for {publishedFileId}: {ex.Message}");
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
                    foreach (WorkshopDownloadItem item in Queue)
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

                try { DetachAggregateTracking(); }
                catch (Exception ex)
                {
                    AppLogging.LogException("Failed to detach workshop aggregate progress tracking", ex);
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
