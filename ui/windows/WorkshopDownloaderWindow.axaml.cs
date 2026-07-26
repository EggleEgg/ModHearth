using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ModHearth.Utilities.Workshop;

namespace ModHearth.UI
{
    public partial class WorkshopDownloaderWindow : Window, IStyleAwareWindow
    {
        private readonly WorkshopQueueManager _queueManager;
        private string _lastClipboard = string.Empty;
        private CancellationTokenSource? _statusCts;

        public WorkshopDownloaderWindow() : this(null!) { }

        public WorkshopDownloaderWindow(ModHearthManager manager)
        {
            InitializeComponent();
            WindowThemeManager.Register(this);

            _queueManager = new WorkshopQueueManager(manager);

            ProviderComboBox.ItemsSource = _queueManager.Providers;
            ProviderComboBox.SelectedItem = _queueManager.SelectedProvider;
            ProviderComboBox.SelectionChanged += (_, _) =>
            {
                var provider = ProviderComboBox.SelectedItem as IWorkshopDownloadProvider;
                _queueManager.SelectedProvider = provider;
                if (provider != null)
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Provider changed to {provider.Name}");
                    ConfigManager.SetDefaultWorkshopProvider(provider.GetType().Name);
                }
            };

            DownloadQueueList.ItemsSource = _queueManager.Queue;

            // Setup clipboard monitoring
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += async (_, _) =>
            {
                if (BtnDownloadFromClipboard.IsChecked != true) return;

                string text = await GetClipboardTextAsync();
                if (!string.IsNullOrWhiteSpace(text) && text != _lastClipboard)
                {
                    _lastClipboard = text;
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Auto-detected clipboard change: {text}");
                    
                    await Dispatcher.UIThread.InvokeAsync(async () => {
                        var currentText = WorkshopUrlTextBox.Text ?? string.Empty;
                        if (!currentText.Contains(text))
                        {
                            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Appending clipboard text to TextBox.");
                            WorkshopUrlTextBox.Text = string.IsNullOrWhiteSpace(currentText) ? text : currentText + Environment.NewLine + text;
                        }
                        await ResolveAndEnqueueAsync(text);
                    });
                }
            };
            timer.Start();

            BtnResolveAndQueue.Click += async (_, _) =>
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Resolve & Queue clicked. Input: {WorkshopUrlTextBox.Text}");
                await ResolveAndEnqueueAsync(WorkshopUrlTextBox.Text ?? string.Empty);
            };

            BtnClearCompleted.Click += (_, _) =>
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopDownloaderWindow: Clear Completed clicked.");
                _queueManager.ClearCompleted();
            };

            BtnClose.Click += (_, _) =>
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopDownloaderWindow: Close clicked.");
                Close();
            };
        }

        public void ApplyCustomStyle(Style style)
        {
            WindowThemeManager.ApplyToWindow(this, style);
        }

        private async Task<string> GetClipboardTextAsync()
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return string.Empty;

            try
            {
                var extType = typeof(IClipboard).Assembly.GetType("Avalonia.Input.Platform.ClipboardExtensions");
                if (extType != null)
                {
                    var method = extType.GetMethod("GetTextAsync", new[] { typeof(IClipboard) });
                    if (method != null)
                    {
                        var task = method.Invoke(null, new object[] { clipboard }) as Task<string?>;
                        if (task != null)
                        {
                            return await task ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Error reading clipboard: {ex.Message}");
            }
            return string.Empty;
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
            _statusCts = new CancellationTokenSource();
            var token = _statusCts.Token;

            Task.Delay(5000, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Dispatcher.UIThread.Post(() => {
                        if (StatusTextBlock.Text == message) StatusTextBlock.Text = string.Empty;
                    });
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async Task ResolveAndEnqueueAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopDownloaderWindow: ResolveAndEnqueueAsync called with empty input.");
                return;
            }

            // Filter out existing mods instead of returning when 1 input id already exists
            var ids = WorkshopUrlResolver.ParseUrls(input);
            var nonExistingIds = new List<ulong>();
            int ignoredCount = 0;
            foreach (var id in ids)
            {
                if (_queueManager.ClassifyMod(id) == ModStatusClassification.AlreadyInstalled)
                {
                    ignoredCount++;
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Mod {id} already exists on disk, ignoring.");
                }
                else
                {
                    nonExistingIds.Add(id);
                }
            }

            if (ignoredCount > 0)
            {
                SetStatus($"Ignored {ignoredCount} existing mod(s).");
            }

            if (nonExistingIds.Count == 0)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopDownloaderWindow: All input mods already exist on disk.");
                return;
            }

            var filteredInput = string.Join(" ", nonExistingIds);
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Resolving input with {nonExistingIds.Count} detected IDs ({ignoredCount} ignored).");
            await _queueManager.ResolveAndEnqueueUrlsAsync(filteredInput, async (collectionMetadata) =>
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Collection found with {collectionMetadata.Count} items. Showing checklist...");
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var selected = await CollectionChecklistDialog.ShowAsync(this, collectionMetadata, _queueManager);
                    if (selected != null && selected.Count > 0)
                    {
                        if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: User selected {selected.Count} items from collection. Enqueueing...");
                        foreach (var meta in selected)
                        {
                            _queueManager.Enqueue(meta);
                        }
                    }
                    else
                    {
                        if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopDownloaderWindow: User cancelled collection checklist or selected no items.");
                    }
                });
            });
        }
    }
}
