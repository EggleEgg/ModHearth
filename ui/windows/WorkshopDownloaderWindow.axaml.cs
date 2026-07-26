using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ModHearth.Utilities.Workshop;

namespace ModHearth.UI
{
    public partial class WorkshopDownloaderWindow : Window, IStyleAwareWindow
    {
        private readonly WorkshopQueueManager _queueManager;

        public WorkshopDownloaderWindow() : this(null!) { }

        public WorkshopDownloaderWindow(ModHearthManager manager)
        {
            InitializeComponent();
            WindowThemeManager.Register(this);

            _queueManager = new WorkshopQueueManager(manager);

            ProviderComboBox.ItemsSource = _queueManager.Providers;
            ProviderComboBox.SelectedItem = _queueManager.SelectedProvider;
            ProviderComboBox.SelectionChanged += (_, _) => _queueManager.SelectedProvider = ProviderComboBox.SelectedItem as IWorkshopDownloadProvider;

            DownloadQueueList.ItemsSource = _queueManager.Queue;

            BtnDownloadFromClipboard.Click += async (_, _) =>
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopDownloaderWindow: Download from Clipboard clicked.");
                await DownloadFromClipboardAsync();
            };

            BtnResolveAndQueue.Click += async (_, _) =>
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Resolve & Queue clicked. Input: {WorkshopUrlTextBox.Text}");
                await ResolveAndEnqueueAsync(WorkshopUrlTextBox.Text);
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

        private async Task DownloadFromClipboardAsync()
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopDownloaderWindow: Clipboard not available.");
                return;
            }

            string text = await clipboard.TryGetTextAsync() ?? string.Empty;
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Clipboard text: {text}");

            if (!string.IsNullOrWhiteSpace(text))
            {
                await ResolveAndEnqueueAsync(text);
            }
        }

        private async Task ResolveAndEnqueueAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                if (DevMode.IsEnabled) InfoLogger.LogRunDf("WorkshopDownloaderWindow: ResolveAndEnqueueAsync called with empty input.");
                return;
            }

            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopDownloaderWindow: Resolving input: {input}");
            await _queueManager.ResolveAndEnqueueUrlsAsync(input, async (collectionMetadata) =>
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
