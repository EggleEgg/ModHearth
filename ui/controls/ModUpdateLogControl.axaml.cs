using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using ModHearth.Models;
using ModHearth.UI.Contracts;
using ModHearth.UI.Dialogs;
using ModHearth.UI.Helpers;
using ModHearth.UI.ModList;
using ModHearth.UI.Theme;
using ModHearth.UI.ViewModels;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI
{
    public partial class ModUpdateLogControl : UserControl, INotifyPropertyChanged, IModRefContextMenuProvider
    {
        private readonly ModHearthManager? manager;
        private readonly List<ModUpdateLogItemViewModel> allEntries = [];
        private readonly BulkObservableCollection<ModUpdateLogItemViewModel> entries = [];
        public ObservableCollection<ModUpdateLogItemViewModel> Entries => entries;
        private readonly ListSelectionController<ModUpdateLogItemViewModel> selectionController = new();
        private ModRefControl? contextMenuHost;
        private IBrush backgroundColorBrush = Brushes.Transparent;
        private readonly SemaphoreSlim loadGate = new(1, 1);
        private bool loadRerunRequested;
        private int loadedRawCount;

        public ModUpdateLogControl() : this(null) { }

        public ModUpdateLogControl(ModHearthManager? manager)
        {
            InitializeComponent();
            DataContext = this;
            InitializeModListAndContextMenu();

            if (manager != null)
            {
                this.manager = manager;
                _ = LoadEntriesAsync();
            }
        }

        private void InitializeModListAndContextMenu()
        {
            logList.SelectionChanged += LogListSelectionChanged;
            selectionController.RegisterList(logList);
            logList.LoadingRow += LogListLoadingRow;

            contextMenuHost = this.FindControl<ModRefControl>("ContextMenuHost");
            if (contextMenuHost != null)
                logList.ContextMenu = contextMenuHost.ContextMenu;

            modSearchBar.SearchTextChanged += (_, _) => ApplyFilter();
            modSearchBar.SearchModeChanged += (_, _) => ApplyFilter();
            modSearchBar.HideFilteredToggled += (_, _) => ApplyFilter();

            clearLogsButton.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is Window ownerWindow)
                {
                    bool confirm = await DialogService.ShowConfirmAsync(ownerWindow, "Are you sure you want to clear all update logs?", "Clear Logs");
                    if (!confirm)
                        return;
                }

                ModUpdateLogger.ClearEntries();
                allEntries.Clear();
                entries.Clear();
                loadedRawCount = 0;
                selectionController.UpdateSelectionState(logList);
            };
        }

        private void LogListSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not DataGrid list)
                return;

            if (selectionController.HandleSelectionChanged(list))
            {
                SyncContextMenuHostDataContext(list);
                return;
            }

            selectionController.UpdateSelectionState(list);
            SyncContextMenuHostDataContext(list);
        }

        private void SyncContextMenuHostDataContext(DataGrid list)
        {
            if (contextMenuHost == null)
                return;

            ModUpdateLogItemViewModel? selected = list.SelectedItem as ModUpdateLogItemViewModel
                ?? list.SelectedItems?.OfType<ModUpdateLogItemViewModel>().FirstOrDefault();
            if (selected != null)
            {
                contextMenuHost.DataContext = MainWindowModListBuilder.CreateViewModel(
                    selected.ModReference,
                    ConfigManager.GetModsPath(),
                    ConfigManager.GetVanillaModsPath());
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;

        public IBrush BackgroundColorBrush
        {
            get => backgroundColorBrush;
            set
            {
                if (Equals(backgroundColorBrush, value))
                    return;
                backgroundColorBrush = value;
                OnPropertyChanged();
            }
        }

        public void LoadEntries() => _ = LoadEntriesAsync();

        public async Task LoadEntriesAsync()
        {
            if (!await loadGate.WaitAsync(0))
            {
                loadRerunRequested = true;
                return;
            }

            try
            {
                do
                {
                    loadRerunRequested = false;

                    HashSet<string> activeIds = manager == null
                        ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(manager.enabledMods.Select(mod => mod.Id), StringComparer.OrdinalIgnoreCase);
                    int knownCount = loadedRawCount;
                    bool wasEmpty = allEntries.Count == 0;

                    (List<ModUpdateLogItemViewModel> viewModels, int rawCount, bool needsFullRebuild) =
                        await Task.Run(() => BuildFromDisk(activeIds, knownCount, wasEmpty));

                    ApplyViewModels(viewModels, rawCount, needsFullRebuild);
                }
                while (loadRerunRequested);
            }
            finally
            {
                _ = loadGate.Release();
            }
        }

        private static (List<ModUpdateLogItemViewModel> ViewModels, int RawCount, bool NeedsFullRebuild) BuildFromDisk(
            HashSet<string> activeIds, int knownCount, bool wasEmpty)
        {
            IReadOnlyList<ModUpdateLogEntry> rawEntries = ModUpdateLogger.LoadEntries();
            Loggers.Diag.Dev($"Total number of entries: {rawEntries.Count}");
            bool needsFullRebuild = wasEmpty || rawEntries.Count < knownCount;

            List<ModUpdateLogEntry> toMaterialize = [.. needsFullRebuild ? rawEntries : rawEntries.Skip(knownCount)];

            IBrush defaultBrush = GetDefaultTextBrush();
            IBrush selectedBrush = GetSelectedBackgroundBrush();

            List<ModUpdateLogItemViewModel> viewModels = BuildViewModels(toMaterialize, activeIds, defaultBrush, selectedBrush);
            if (needsFullRebuild)
                viewModels = [.. viewModels.OrderByDescending(vm => vm.Entry.TimestampUtc)];

            return (viewModels, rawEntries.Count, needsFullRebuild);
        }

        private const int ParallelThreshold = 64;

        private static List<ModUpdateLogItemViewModel> BuildViewModels(
            List<ModUpdateLogEntry> rawEntries, HashSet<string> activeIds, IBrush defaultBrush, IBrush selectedBrush)
        {
            ModUpdateLogItemViewModel[] results = new ModUpdateLogItemViewModel[rawEntries.Count];

            void Build(int i)
            {
                ModUpdateLogEntry entry = rawEntries[i];
                ModReference modref = BuildModReference(entry);
                bool isActive = activeIds.Contains(entry.ModId);
                IBrush brush = GetRowBrush(entry, defaultBrush, isActive);
                results[i] = new ModUpdateLogItemViewModel(entry, modref, brush, selectedBrush, isActive);
            }

            if (rawEntries.Count > ParallelThreshold)
            {
                _ = Parallel.For(0, rawEntries.Count, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, Build);
            }
            else
            {
                for (int i = 0; i < rawEntries.Count; i++)
                    Build(i);
            }

            return [.. results];
        }

        private void ApplyViewModels(List<ModUpdateLogItemViewModel> viewModels, int rawCount, bool needsFullRebuild)
        {
            if (needsFullRebuild)
            {
                allEntries.Clear();
                allEntries.AddRange(viewModels);
                entries.ReplaceAll(viewModels);
                selectionController.UpdateSelectionState(logList);
                ApplyDefaultSort();
            }
            else
            {
                allEntries.AddRange(viewModels);
                foreach (ModUpdateLogItemViewModel vm in viewModels)
                    entries.Add(vm);
            }

            loadedRawCount = rawCount;
        }

        private void ApplyFilter()
        {
            string filter = modSearchBar?.Text?.Trim() ?? string.Empty;
            SearchFilterMode searchMode = modSearchBar?.SearchMode ?? SearchFilterMode.Name;
            bool hideFiltered = modSearchBar?.HideFiltered ?? false;

            // Build the filter once, then visit every entry exactly once.
            SearchFilterContext context = new(filter, searchMode);
            List<ModUpdateLogItemViewModel> filtered = new(allEntries.Count);

            foreach (ModUpdateLogItemViewModel vm in allEntries)
            {
                bool match = !context.HasFilter || vm.MatchesFilter(context);
                vm.IsFilteredOut = context.HasFilter && !match;
                vm.IsVisible = !hideFiltered || match;
                if (vm.IsVisible)
                    filtered.Add(vm);
            }

            entries.ReplaceAll(filtered);
            selectionController.UpdateSelectionState(logList);
        }

        private void ApplyDefaultSort()
        {
            void DoSort()
            {
                if (logList.Columns.Count > 0)
                {
                    try
                    {
                        logList.Columns[0].Sort(ListSortDirection.Descending);
                    }
                    catch (Exception ex)
                    {
                        AppLogging.LogException("Failed to apply default sort on log list", ex);
                    }
                }
            }

            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (TopLevel.GetTopLevel(logList) != null)
                {
                    DoSort();
                }
                else
                {
                    void handler(object? s, VisualTreeAttachmentEventArgs e)
                    {
                        logList.AttachedToVisualTree -= handler;
                        DoSort();
                    }

                    logList.AttachedToVisualTree += handler;
                }
            });
        }

        private static IBrush GetDefaultTextBrush()
        {
            if (Style.instance != null)
                return BrushCache.GetBrush(Style.instance.TextColor.ToAvaloniaColor());
            return Brushes.White;
        }

        public static IBrush GetRowBrush(ModUpdateLogEntry entry, IBrush defaultBrush, bool isActive)
        {
            switch (entry.ChangeType)
            {
                case ModUpdateChangeType.Deleted:
                    return Brushes.Red;
                case ModUpdateChangeType.Updated:
                    return Brushes.DeepSkyBlue;
                case ModUpdateChangeType.Added:
                    return Brushes.LimeGreen;
            }

            if (isActive)
                return Brushes.LimeGreen;
            return defaultBrush;
        }

        private static IBrush GetSelectedBackgroundBrush()
        {
            if (Style.instance != null)
                return BrushCache.GetBrush(Style.instance.ModRefHighlightColor.ToAvaloniaColor());
            return Brushes.Transparent;
        }

        private static ModReference BuildModReference(ModUpdateLogEntry entry)
        {
            string id = entry.ModId ?? string.Empty;
            string name = string.IsNullOrWhiteSpace(entry.ModName) ? id : entry.ModName;
            string steamId = entry.SteamId ?? string.Empty;
            string path = entry.Path ?? string.Empty;
            return new ModReference
            {
                ID = id,
                NumericVersion = "0",
                Name = name,
                SteamID = steamId,
                Path = path,
                Source = string.IsNullOrWhiteSpace(steamId) ? ModSource.Local : ModSource.Steam
            };
        }

        public ModHearthManager? GetManager() => manager;

        public IEnumerable<ModReference> GetSelectedModReferences(ModRefViewModel contextVm) => logList.SelectedItems?.Cast<ModUpdateLogItemViewModel>().Select(item => item.ModReference)
                ?? [];

        public async void OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm)
        {
            if (manager == null)
                return;

            if (TopLevel.GetTopLevel(this) is not Window ownerWindow)
                return;

            await ModContextMenuSupport.DispatchDefaultActionAsync(ownerWindow, manager, item, vm.ModReference);
        }

        private static readonly Binding BackgroundBrushBinding = new(nameof(ModUpdateLogItemViewModel.BackgroundBrush)) { Mode = BindingMode.OneWay };

        private void LogListLoadingRow(object? sender, DataGridRowEventArgs e)
        {
            _ = e.Row.Bind(
                BackgroundProperty,
                BackgroundBrushBinding);

            if (e.Row.DataContext is ModUpdateLogItemViewModel vm)
            {
                Loggers.Diag.Dev($"Loading row for '{vm.ModName}' - Change: {vm.Entry.ChangeType}, Active: {vm.IsActive}, RowBrush: {vm.RowBrush}");
            }
        }

        public void ApplyCustomStyle(Style style)
        {
            BackgroundColorBrush = BrushCache.GetBrush(style.PanelColor.ToAvaloniaColor());

            if (logList != null)
            {
                logList.Background = BackgroundColorBrush;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }
}
