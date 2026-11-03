using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ModHearth.Models;
using ModHearth.UI.Behaviors;
using ModHearth.UI.Contracts;
using ModHearth.UI.Dialogs;
using ModHearth.UI.ModList;
using ModHearth.UI.Rendering;
using ModHearth.UI.Theme;
using ModHearth.UI.ViewModels;
using ModHearth.Utilities.Logging;
using ModHearth.Utilities.Workshop;

namespace ModHearth.UI
{
    public partial class WorkshopDownloaderControl : UserControl, INotifyPropertyChanged, IDisposable, IModRefContextMenuProvider
    {
        public new event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void NotifyOfPropertyChange([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public bool IsAutoResolveAndQueueEnabled
        {
            get => ConfigManager.IsAutoResolveAndQueueEnabled();
            set
            {
                if (value != ConfigManager.IsAutoResolveAndQueueEnabled())
                {
                    ConfigManager.SetAutoResolveAndQueueEnabled(value);
                    NotifyOfPropertyChange();
                }
            }
        }

        private IWorkshopDownloadProvider? _selectedProvider;
        public IWorkshopDownloadProvider? SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (_selectedProvider != value)
                {
                    _selectedProvider = value;
                    NotifyOfPropertyChange();
                }
            }
        }

        public bool IsAutoRetryAllEnabled
        {
            get => ConfigManager.IsAutoRetryAllEnabled();
            set
            {
                if (value != ConfigManager.IsAutoRetryAllEnabled())
                {
                    ConfigManager.SetAutoRetryAllEnabled(value);
                    NotifyOfPropertyChange();
                    if (value)
                    {
                        _queueManager.RetryAll();
                    }
                }
            }
        }

        private readonly WorkshopQueueManager _queueManager;
        private readonly DispatcherTimer _clipboardTimer;
        private string _lastResolvedInput = string.Empty;
        private string _lastRawClipboard = string.Empty;
        private bool _isCheckingClipboard;
        private bool _suppressClipboardTextBoxAutoResolve;
        private readonly HashSet<ulong> _idsBeingResolved = [];
        private CancellationTokenSource? _statusCts;
        private readonly NotifyCollectionChangedEventHandler? _queueCollectionChangedHandler;
        private readonly ModRefControl? _contextMenuHost;
        private bool _isSetupDialogShowing;

        public WorkshopDownloaderControl() : this(null!) { }

        public WorkshopDownloaderControl(ModHearthManager manager)
        {
            InitializeComponent();
            DataContext = this;
            Loaded += async (_, _) => await CheckProviderSetupAsync();

            _queueManager = new WorkshopQueueManager(manager);

            SelectedProvider = _queueManager.SelectedProvider;
            ProviderComboBox.ItemsSource = _queueManager.Providers;
            ProviderComboBox.SelectedItem = SelectedProvider;
            ProviderComboBox.SelectionChanged += async (_, _) =>
            {
                IWorkshopDownloadProvider? provider = ProviderComboBox.SelectedItem as IWorkshopDownloadProvider;
                _queueManager.SelectedProvider = provider;
                SelectedProvider = provider;
                if (provider != null)
                {
                    InfoLogger.LogRunDf($"WorkshopDownloaderControl: Provider changed to {provider.Name}");
                    ConfigManager.SetDefaultWorkshopProvider(provider.GetType().Name);
                }

                if (provider is SteamCmdDownloadProvider && !provider.IsAvailable)
                {
                    await ShowSteamCmdSetupAsync();
                }
            };

            DownloadQueueList.ItemsSource = _queueManager.Queue;
            _contextMenuHost = this.FindControl<ModRefControl>("ContextMenuHost");
            if (_contextMenuHost != null)
                DownloadQueueList.ContextMenu = _contextMenuHost.ContextMenu;
            DownloadQueueList.SelectionChanged += DownloadQueueListSelectionChanged;
            _queueCollectionChangedHandler = (_, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (WorkshopDownloadItem item in e.NewItems)
                    {
                        item.PropertyChanged += Item_PropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (WorkshopDownloadItem item in e.OldItems)
                    {
                        item.PropertyChanged -= Item_PropertyChanged;
                    }
                }
                UpdateQueueActionStates();
            };
            _queueManager.Queue.CollectionChanged += _queueCollectionChangedHandler;
            foreach (WorkshopDownloadItem item in _queueManager.Queue)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
            UpdateQueueActionStates();

            BtnDownloadFromClipboard.PropertyChanged += (_, e) =>
            {
                if (e.Property == ToggleButton.IsCheckedProperty && BtnDownloadFromClipboard.IsChecked == true)
                    _lastRawClipboard = string.Empty;
            };

            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _clipboardTimer.Tick += async (_, _) =>
            {
                if (BtnDownloadFromClipboard.IsChecked != true || _isCheckingClipboard)
                    return;

                _isCheckingClipboard = true;
                try
                {
                    string text = await GetClipboardTextAsync();
                    if (string.IsNullOrWhiteSpace(text))
                        return;

                    InfoLogger.LogRunDf($"WorkshopDownloaderControl: Auto-detected clipboard change: {text}");

                    string currentText = WorkshopUrlTextBox.Text ?? string.Empty;
                    if (!currentText.Contains(text))
                    {
                        _suppressClipboardTextBoxAutoResolve = true;
                        try
                        {
                            WorkshopUrlTextBox.Text = string.IsNullOrWhiteSpace(currentText)
                                ? text
                                : currentText + Environment.NewLine + text;
                        }
                        finally
                        {
                            _suppressClipboardTextBoxAutoResolve = false;
                        }
                    }

                    if (IsAutoResolveAndQueueEnabled)
                    {
                        _lastResolvedInput = WorkshopUrlTextBox.Text ?? string.Empty;
                        await ResolveAndEnqueueAsync(text);
                    }
                }
                finally
                {
                    _isCheckingClipboard = false;
                }
            };
            _clipboardTimer.Start();

            BtnResolveAndQueue.Click += async (_, _) =>
            {
                await ResolveAndEnqueueAsync(WorkshopUrlTextBox.Text ?? string.Empty);
            };

            BtnResolveAndQueue.AddHandler(PointerPressedEvent, BtnResolveAndQueuePointerPressed, RoutingStrategies.Tunnel, true);

            WorkshopUrlTextBox.TextChanged += async (_, _) =>
            {
                UpdateClearButtonState();
                if (_suppressClipboardTextBoxAutoResolve || !IsAutoResolveAndQueueEnabled) return;
                string text = WorkshopUrlTextBox.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text) || text == _lastResolvedInput) return;

                List<ulong> ids = WorkshopUrlResolver.ParseUrls(text);
                if (ids.Count > 0)
                {
                    _lastResolvedInput = text;
                    await ResolveAndEnqueueAsync(text);
                }
            };

            BtnClearText.Click += (_, _) => WorkshopUrlTextBox.Text = string.Empty;
            UpdateClearButtonState();
            BtnClearCompleted.Click += (_, _) => _queueManager.ClearCompleted();
            BtnRetryAll.Click += (_, _) => _queueManager.RetryAll();
            BtnRetryAll.AddHandler(PointerPressedEvent, BtnRetryAllPointerPressed, RoutingStrategies.Tunnel, true);
            BtnCancelAll.Click += (_, _) => _queueManager.CancelAll();
        }

        private async Task<string> GetClipboardTextAsync()
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
                return string.Empty;

            try
            {
                string rawText = await clipboard.TryGetTextAsync() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawText) || rawText == _lastRawClipboard)
                    return string.Empty;

                _lastRawClipboard = rawText;
                string filteredText = WorkshopUrlResolver.FilterUrls(rawText);
                if (string.IsNullOrWhiteSpace(filteredText))
                    return string.Empty;

                return filteredText;
            }
            catch (Exception ex)
            {
                InfoLogger.LogRunDf($"WorkshopDownloaderControl: Error reading clipboard: {ex.Message}");
                return string.Empty;
            }
        }

        private void SetStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                StatusTextBlock.Text = string.Empty;
                return;
            }

            StatusTextBlock.Text = message;
            _statusCts?.Cancel();
            _statusCts?.Dispose();
            _statusCts = new CancellationTokenSource();
            CancellationToken token = _statusCts.Token;

            _ = Task.Delay(5000, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (StatusTextBlock.Text == message) StatusTextBlock.Text = string.Empty;
                    });
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async Task ResolveAndEnqueueAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return;

            List<ulong> ids = WorkshopUrlResolver.ParseUrls(input);
            List<ulong> nonExistingIds = [];
            int ignoredCount = 0;
            int inFlightCount = 0;
            foreach (ulong id in ids)
            {
                switch (_queueManager.ClassifyMod(id))
                {
                    case ModStatusClassification.AlreadyInstalled:
                        ignoredCount++;
                        break;
                    default:
                        if (!_idsBeingResolved.Add(id))
                        {
                            // Already being resolved by an overlapping trigger (e.g. the clipboard monitor and a manual "Resolve and Queue" click landing on the same id at once)
                            inFlightCount++;
                        }
                        else
                        {
                            nonExistingIds.Add(id);
                        }

                        break;

                }
            }

            List<string> statusParts = [];
            if (ignoredCount > 0)
                statusParts.Add($"Ignored {ignoredCount} existing mod(s).");
            if (inFlightCount > 0)
                statusParts.Add($"Skipped {inFlightCount} mod(s) already resolving.");
            if (statusParts.Count > 0)
                SetStatus(string.Join(" ", statusParts));

            if (nonExistingIds.Count == 0)
                return;

            try
            {
                string filteredInput = string.Join(" ", nonExistingIds);
                await _queueManager.ResolveAndEnqueueUrlsAsync(filteredInput, async (collectionMetadata) =>
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (TopLevel.GetTopLevel(this) is Window ownerWindow)
                        {
                            List<WorkshopItemMetadata>? selected = await CollectionChecklistDialog.ShowAsync(ownerWindow, collectionMetadata, _queueManager);
                            if (selected != null && selected.Count > 0)
                            {
                                foreach (WorkshopItemMetadata meta in selected)
                                {
                                    _queueManager.Enqueue(meta);
                                }
                            }
                        }
                    });
                });
            }
            finally
            {
                foreach (ulong id in nonExistingIds)
                    _ = _idsBeingResolved.Remove(id);
            }
        }

        private async void BtnResolveAndQueuePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(BtnResolveAndQueue).Properties.IsRightButtonPressed)
                return;

            e.Handled = true;
            IsAutoResolveAndQueueEnabled = !IsAutoResolveAndQueueEnabled;
            if (IsAutoResolveAndQueueEnabled)
                await ResolveAndEnqueueAsync(WorkshopUrlTextBox.Text ?? string.Empty);

        }

        private void BtnRetryAllPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(BtnRetryAll).Properties.IsRightButtonPressed)
                return;

            e.Handled = true;
            IsAutoRetryAllEnabled = !IsAutoRetryAllEnabled;
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(WorkshopDownloadItem.State):
                case nameof(WorkshopDownloadItem.CanRetry):
                case nameof(WorkshopDownloadItem.CanCancel):
                    Dispatcher.UIThread.Post(UpdateQueueActionStates);
                    break;

            }
        }

        private void UpdateQueueActionStates()
        {
            if (BtnCancelAll != null)
                BtnCancelAll.IsEnabled = _queueManager.Queue.Any(i => i.CanCancel);
        }

        /// <summary>
        /// Queue items are not mods on disk yet, so context-menu actions need a synthetic reference
        /// describing the Steam workshop item they will become.
        /// </summary>
        private static ModReference CreateQueueItemReference(WorkshopDownloadItem item)
        {
            string workshopId = item.PublishedFileId.ToString();
            return new ModReference
            {
                ID = workshopId,
                NumericVersion = "0",
                Name = item.Title,
                Author = item.Author,
                SteamID = workshopId,
                Source = ModSource.Steam
            };
        }

        private void DownloadQueueListSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox list)
                return;

            WorkshopDownloadItem? selected = list.SelectedItem as WorkshopDownloadItem
                ?? list.SelectedItems?.OfType<WorkshopDownloadItem>().FirstOrDefault();
            if (selected != null && _contextMenuHost != null)
            {
                _contextMenuHost.DataContext = new ModRefViewModel(CreateQueueItemReference(selected));
            }
        }

        public ModHearthManager? GetManager() => _queueManager?.Manager;

        public IEnumerable<ModReference> GetSelectedModReferences(ModRefViewModel contextVm)
        {
            if (DownloadQueueList.SelectedItems != null && DownloadQueueList.SelectedItems.Count > 0)
            {
                return [.. DownloadQueueList.SelectedItems.Cast<WorkshopDownloadItem>()
                    .Select(CreateQueueItemReference)];
            }
            return [contextVm.ModReference];
        }

        public async Task RedownloadModsAsync(IEnumerable<ulong> workshopIds)
        {
            if (_queueManager != null)
            {
                await _queueManager.RedownloadWorkshopItemsAsync(workshopIds);
            }
        }

        public async void OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm)
        {
            ModHearthManager? manager = _queueManager?.Manager;
            if (manager == null)
                return;

            if (TopLevel.GetTopLevel(this) is not Window ownerWindow)
                return;

            switch (item.Tag?.ToString())
            {
                case ModContextMenuSupport.OpenSteamTag:
                    await ModContextMenuSupport.OpenSteamPageAsync(ownerWindow, vm.ModReference);
                    break;
                case ModContextMenuSupport.CopyIdTag:
                    await ModContextMenuSupport.CopyModIdAsync(ownerWindow, vm.ModReference);
                    break;
                case ModContextMenuSupport.RedownloadTag:
                    {
                        IEnumerable<ModReference> targets = GetSelectedModReferences(vm);
                        List<ulong> workshopIds = [];
                        foreach (ModReference target in targets)
                        {
                            if (ModHearthManager.TryGetSteamWorkshopItemId(target, out string steamId) && ulong.TryParse(steamId, out ulong id))
                            {
                                workshopIds.Add(id);
                            }
                        }

                        if (workshopIds.Count > 0 && new Utilities.Steam.SteamCmdService().IsAvailable())
                        {
                            await RedownloadModsAsync(workshopIds);
                        }
                        else
                        {
                            await ModContextMenuSupport.RedownloadSteamWithConfirmAsync(ownerWindow, manager, targets);
                        }
                        break;
                    }
                case ModContextMenuSupport.UnsubscribeTag:
                    await ModContextMenuSupport.UnsubscribeSteamWithConfirmAsync(ownerWindow, manager, GetSelectedModReferences(vm));
                    break;
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _clipboardTimer?.Stop();
                _statusCts?.Cancel();
                _statusCts?.Dispose();
                _queueManager?.CancelAll();
                _queueManager?.Dispose();
                if (_queueManager?.Queue != null)
                {
                    if (_queueCollectionChangedHandler != null)
                    {
                        _queueManager.Queue.CollectionChanged -= _queueCollectionChangedHandler;
                    }
                    foreach (WorkshopDownloadItem item in _queueManager.Queue)
                    {
                        item.PropertyChanged -= Item_PropertyChanged;
                    }
                }
            }

            _disposed = true;
        }

        private async Task ShowSteamCmdSetupAsync()
        {
            if (_isSetupDialogShowing)
                return;

            _isSetupDialogShowing = true;
            try
            {
                if (TopLevel.GetTopLevel(this) is Window ownerWindow)
                {
                    bool success = await SteamCmdSetupDialog.ShowAsync(ownerWindow);
                    if (success)
                    {
                        IWorkshopDownloadProvider? current = SelectedProvider;
                        SelectedProvider = null;
                        SelectedProvider = current;
                        NotifyOfPropertyChange("SelectedProvider.StatusBrush");
                        if (ProviderComboBox != null)
                        {
                            ProviderComboBox.SelectedItem = SelectedProvider;
                        }
                    }
                }
            }
            finally
            {
                _isSetupDialogShowing = false;
            }
        }

        public async Task CheckProviderSetupAsync()
        {
            IWorkshopDownloadProvider? provider = _queueManager?.SelectedProvider;
            if (provider is SteamCmdDownloadProvider && !provider.IsAvailable)
            {
                await ShowSteamCmdSetupAsync();
            }
        }

        public void ApplyCustomStyle(Style style)
        {
            if (style == null)
                return;

            SearchButtonBehavior.ApplyStyle(BtnClearText, style);
            if (BtnClearText.Content is Image img)
            {
                img.Source = ImageSourceLoader.LoadFromAssetUri("broomMenuIcon", style.ButtonTextColor.ToAvaloniaColor());
            }
        }

        private void UpdateClearButtonState()
        {
            if (BtnClearText != null)
            {
                BtnClearText.IsEnabled = !string.IsNullOrEmpty(WorkshopUrlTextBox?.Text);
            }
        }
    }
}
