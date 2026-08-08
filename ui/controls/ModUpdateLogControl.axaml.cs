using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public partial class ModUpdateLogControl : UserControl, INotifyPropertyChanged, IModRefContextMenuProvider, IStyleAwareWindow
{
    private readonly ModHearthManager? manager;
    private readonly List<ModUpdateLogItemViewModel> allEntries = [];
    private readonly BulkObservableCollection<ModUpdateLogItemViewModel> entries = [];
    public ObservableCollection<ModUpdateLogItemViewModel> Entries => entries;
    private readonly ListSelectionController<ModUpdateLogItemViewModel> selectionController = new();
    private ModRefControl? contextMenuHost;
    public ModRefControl? GetContextMenuHost() => contextMenuHost;
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
            var ownerWindow = TopLevel.GetTopLevel(this) as Window;
            if (ownerWindow != null)
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

    public void LoadEntries()
    {
        _ = LoadEntriesAsync();
    }

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
                    : new HashSet<string>(manager.enabledMods.Select(mod => mod.id), StringComparer.OrdinalIgnoreCase);
                int knownCount = loadedRawCount;
                bool wasEmpty = allEntries.Count == 0;

                (List<ModUpdateLogItemViewModel> viewModels, int rawCount, bool needsFullRebuild) result =
                    await Task.Run(() => BuildFromDisk(activeIds, knownCount, wasEmpty));

                ApplyViewModels(result.viewModels, result.rawCount, result.needsFullRebuild);
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
        Console.WriteLine($"[ModUpdateLog] Total number of entries: {rawEntries.Count}");
        bool needsFullRebuild = wasEmpty || rawEntries.Count < knownCount;

        List<ModUpdateLogEntry> toMaterialize = (needsFullRebuild ? rawEntries : rawEntries.Skip(knownCount)).ToList();

        IBrush defaultBrush = GetDefaultTextBrush();
        IBrush selectedBrush = GetSelectedBackgroundBrush();

        List<ModUpdateLogItemViewModel> viewModels = BuildViewModels(toMaterialize, activeIds, defaultBrush, selectedBrush);
        if (needsFullRebuild)
            viewModels = viewModels.OrderByDescending(vm => vm.Entry.TimestampUtc).ToList();

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

        switch (rawEntries.Count)
        {
            case > ParallelThreshold:
                _ = Parallel.For(0, rawEntries.Count, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, Build);
                break;
            default:
                {
                    for (int i = 0; i < rawEntries.Count; i++)
                        Build(i);
                    break;
                }

        }

        return results.ToList();
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
        bool hasFilter = !string.IsNullOrWhiteSpace(filter);

        List<ModUpdateLogItemViewModel> filtered = allEntries.Where(vm =>
        {
            bool match = !hasFilter || vm.MatchesFilter(filter, searchMode);
            vm.IsFilteredOut = hasFilter && !match;
            vm.IsVisible = !hideFiltered || match;
            return vm.IsVisible;
        }).ToList();

        entries.Clear();
        foreach (var vm in filtered)
        {
            entries.Add(vm);
        }

        selectionController.UpdateSelectionState(logList);
    }

    private void ApplyDefaultSort()
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (logList.Columns.Count > 0)
                logList.Columns[0].Sort(ListSortDirection.Descending);
        });
    }

    private static IBrush GetDefaultTextBrush()
    {
        if (Style.instance != null)
            return BrushCache.GetBrush(Style.instance.textColor.ToAvaloniaColor());
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
            return BrushCache.GetBrush(Style.instance.modRefHighlightColor.ToAvaloniaColor());
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
            numericVersion = "0",
            name = name,
            steamID = steamId,
            path = path,
            Source = string.IsNullOrWhiteSpace(steamId) ? ModSource.Local : ModSource.Steam
        };
    }

    public void OnModRefContextMenuOpened(ContextMenu menu, ModRefViewModel vm) { }

    public ModHearthManager? GetManager() => manager;

    public IEnumerable<ModReference> GetSelectedModReferences(ModRefViewModel contextVm)
    {
        return logList.SelectedItems?.Cast<ModUpdateLogItemViewModel>().Select(item => item.ModReference)
            ?? Enumerable.Empty<ModReference>();
    }

    public async void OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm)
    {
        if (manager == null)
            return;

        var ownerWindow = TopLevel.GetTopLevel(this) as Window;
        if (ownerWindow == null)
            return;

        switch (item.Tag?.ToString())
        {
            case ModContextMenuSupport.DeleteTag:
                _ = await ModContextMenuSupport.DeleteLocalModsWithConfirmAsync(ownerWindow, manager, [vm.ModReference]);
                break;
            case ModContextMenuSupport.UnsubscribeTag:
                await ModContextMenuSupport.UnsubscribeSteamWithConfirmAsync(ownerWindow, manager, [vm.ModReference]);
                break;
            case ModContextMenuSupport.RedownloadTag:
                await ModContextMenuSupport.RedownloadSteamWithConfirmAsync(ownerWindow, manager, [vm.ModReference]);
                break;
            case ModContextMenuSupport.OpenFolderTag:
                await ModContextMenuSupport.OpenFolderAsync(ownerWindow, vm.ModReference);
                break;
            case ModContextMenuSupport.OpenSteamTag:
                await ModContextMenuSupport.OpenSteamPageAsync(ownerWindow, vm.ModReference);
                break;
            case ModContextMenuSupport.CopyIdTag:
                await ModContextMenuSupport.CopyModIdAsync(ownerWindow, vm.ModReference);
                break;
        }
    }

    private static readonly Binding BackgroundBrushBinding = new(nameof(ModUpdateLogItemViewModel.BackgroundBrush)) { Mode = BindingMode.OneWay };

    private void LogListLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        _ = e.Row.Bind(
            BackgroundProperty,
            BackgroundBrushBinding);

        if (e.Row.DataContext is ModUpdateLogItemViewModel vm && DevMode.IsEnabled)
            Console.WriteLine($"[ModUpdateLog] Loading row for '{vm.ModName}' - Change: {vm.Entry.ChangeType}, Active: {vm.IsActive}, RowBrush: {vm.RowBrush}");
    }

    public void ApplyCustomStyle(Style style)
    {
        BackgroundColorBrush = BrushCache.GetBrush(style.panelColor.ToAvaloniaColor());

        if (logList != null)
        {
            logList.Background = BackgroundColorBrush;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
