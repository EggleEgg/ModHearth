using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ModHearth.Utilities.Workshop;

namespace ModHearth.UI
{
    public partial class WorkshopDownloaderControl : UserControl, INotifyPropertyChanged
    {
        public new event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void NotifyOfPropertyChange([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

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

        private readonly WorkshopQueueManager _queueManager;
        private string _lastResolvedInput = string.Empty;
        private string _lastRawClipboard = string.Empty;
        private bool _isCheckingClipboard;
        private CancellationTokenSource? _statusCts;

        public event EventHandler? CloseRequested;
        public event EventHandler? DockToggleRequested;

        public WorkshopDownloaderControl() : this(null!) { }

        public WorkshopDownloaderControl(ModHearthManager manager)
        {
            InitializeComponent();
            DataContext = this;

            _queueManager = new WorkshopQueueManager(manager);

            ProviderComboBox.ItemsSource = _queueManager.Providers;
            ProviderComboBox.SelectedItem = _queueManager.SelectedProvider;
            ProviderComboBox.SelectionChanged += (_, _) =>
            {
                var provider = ProviderComboBox.SelectedItem as IWorkshopDownloadProvider;
                _queueManager.SelectedProvider = provider;
                if (provider != null)
                {
                    if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderControl: Provider changed to {provider.Name}");
                    ConfigManager.SetDefaultWorkshopProvider(provider.GetType().Name);
                }
            };

            DownloadQueueList.ItemsSource = _queueManager.Queue;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += async (_, _) =>
            {
                if (BtnDownloadFromClipboard.IsChecked != true || _isCheckingClipboard)
                    return;

                _isCheckingClipboard = true;
                try
                {
                    string text = await GetClipboardTextAsync();
                    if (string.IsNullOrWhiteSpace(text))
                        return;

                    if (DevMode.IsEnabled)
                        InfoLogger.LogRunDf($"WorkshopDownloaderControl: Auto-detected clipboard change: {text}");

                    var currentText = WorkshopUrlTextBox.Text ?? string.Empty;
                    if (!currentText.Contains(text))
                    {
                        WorkshopUrlTextBox.Text = string.IsNullOrWhiteSpace(currentText)
                            ? text
                            : currentText + Environment.NewLine + text;
                    }

                    await ResolveAndEnqueueAsync(text);
                }
                finally
                {
                    _isCheckingClipboard = false;
                }
            };
            timer.Start();

            BtnResolveAndQueue.Click += async (_, _) =>
            {
                await ResolveAndEnqueueAsync(WorkshopUrlTextBox.Text ?? string.Empty);
            };

            BtnResolveAndQueue.AddHandler(InputElement.PointerPressedEvent, BtnResolveAndQueuePointerPressed, RoutingStrategies.Tunnel, true);

            WorkshopUrlTextBox.TextChanged += async (_, _) =>
            {
                if (!IsAutoResolveAndQueueEnabled) return;
                string text = WorkshopUrlTextBox.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text) || text == _lastResolvedInput) return;

                var ids = WorkshopUrlResolver.ParseUrls(text);
                if (ids.Count > 0)
                {
                    _lastResolvedInput = text;
                    await ResolveAndEnqueueAsync(text);
                }
            };

            BtnClearCompleted.Click += (_, _) =>
            {
                _queueManager.ClearCompleted();
            };

            BtnDockToggle.Click += (_, _) =>
            {
                DockToggleRequested?.Invoke(this, EventArgs.Empty);
            };

            BtnClose.Click += (_, _) =>
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            };
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
                var parsedIds = WorkshopUrlResolver.ParseUrls(rawText);
                if (parsedIds.Count == 0)
                    return string.Empty;

                return rawText;
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
            _statusCts = new CancellationTokenSource();
            var token = _statusCts.Token;

            Task.Delay(5000, token).ContinueWith(t =>
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

            var ids = WorkshopUrlResolver.ParseUrls(input);
            var nonExistingIds = new List<ulong>();
            int ignoredCount = 0;
            foreach (var id in ids)
            {
                if (_queueManager.ClassifyMod(id) == ModStatusClassification.AlreadyInstalled)
                {
                    ignoredCount++;
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
                return;

            var filteredInput = string.Join(" ", nonExistingIds);
            await _queueManager.ResolveAndEnqueueUrlsAsync(filteredInput, async (collectionMetadata) =>
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var ownerWindow = TopLevel.GetTopLevel(this) as Window;
                    if (ownerWindow != null)
                    {
                        var selected = await CollectionChecklistDialog.ShowAsync(ownerWindow, collectionMetadata, _queueManager);
                        if (selected != null && selected.Count > 0)
                        {
                            foreach (var meta in selected)
                            {
                                _queueManager.Enqueue(meta);
                            }
                        }
                    }
                });
            });
        }

        private void BtnResolveAndQueuePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(BtnResolveAndQueue).Properties.IsRightButtonPressed)
                return;

            e.Handled = true;
            IsAutoResolveAndQueueEnabled = !IsAutoResolveAndQueueEnabled;
        }
    }
}
