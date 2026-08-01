using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ModHearth.Utilities.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public partial class ModUpdateLogControl : UserControl, INotifyPropertyChanged, IModRefContextMenuProvider, IStyleAwareWindow
{
    private readonly ModHearthManager? manager;
    private readonly List<ModUpdateLogItemViewModel> allEntries = new();
    private readonly ObservableCollection<ModUpdateLogItemViewModel> entries = new();
    public ObservableCollection<ModUpdateLogItemViewModel> Entries => entries;
    private readonly ListSelectionController<ModUpdateLogItemViewModel> selectionController = new();
    private ModRefControl? contextMenuHost;
    private IBrush backgroundColorBrush = Brushes.Transparent;
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
            _ = LoadEntriesInitialAsync();
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
        var entries = ModUpdateLogger.LoadEntries();
        Console.WriteLine($"[ModUpdateLog] Total number of entries: {entries.Count}");
        ApplyRawEntries(entries);
    }

    // Used only for the very first population (opening the panel for the first time this session), so the initial file read + JSON parse doesn't block the UI thread. 
    // Every reload after that goes through the synchronous LoadEntries(); cheap by then, since ApplyRawEntries only processes what's actually new.
    private async Task LoadEntriesInitialAsync()
    {
        try
        {
            IReadOnlyList<ModUpdateLogEntry> rawEntries = await Task.Run(() => ModUpdateLogger.LoadEntries());
            Console.WriteLine($"[ModUpdateLog] Total number of entries: {rawEntries.Count}");
            ApplyRawEntries(rawEntries);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModUpdateLog] Initial load failed: {ex.Message}");
        }
    }

    // Builds view models only for entries not already represented in `entries`, instead of tearing down and rebuilding on every call. 
    // New log entries are always appended on disk (see ModUpdateLogger.AppendEntries), so anything past loadedRawCount is new.
    private void ApplyRawEntries(IReadOnlyList<ModUpdateLogEntry> rawEntries)
    {
        bool needsFullRebuild = allEntries.Count == 0 || rawEntries.Count < loadedRawCount;
        IEnumerable<ModUpdateLogEntry> toMaterialize = needsFullRebuild
            ? rawEntries
            : rawEntries.Skip(loadedRawCount);

        HashSet<string> activeIds = manager == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(manager.enabledMods.Select(mod => mod.id), StringComparer.OrdinalIgnoreCase);

        IBrush defaultBrush = GetDefaultTextBrush();
        IBrush selectedBrush = GetSelectedBackgroundBrush();

        List<ModUpdateLogItemViewModel> newViewModels = new();
        foreach (ModUpdateLogEntry entry in toMaterialize)
        {
            ModReference modref = BuildModReference(entry);
            bool isActive = activeIds.Contains(entry.ModId);
            IBrush brush = GetRowBrush(entry, defaultBrush, isActive);
            newViewModels.Add(new ModUpdateLogItemViewModel(entry, modref, brush, selectedBrush, isActive));
        }

        if (needsFullRebuild)
        {
            allEntries.Clear();
            foreach (ModUpdateLogItemViewModel vm in newViewModels.OrderByDescending(vm => vm.Entry.TimestampUtc))
                allEntries.Add(vm);

            ApplyFilter();
            ApplyDefaultSort();
        }
        else
        {
            foreach (ModUpdateLogItemViewModel vm in newViewModels)
                allEntries.Add(vm);
            ApplyFilter();
        }

        loadedRawCount = rawEntries.Count;
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
        Dispatcher.UIThread.InvokeAsync(() =>
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
                await ModContextMenuSupport.DeleteLocalModsWithConfirmAsync(ownerWindow, manager, new[] { vm.ModReference });
                break;
            case ModContextMenuSupport.UnsubscribeTag:
                await ModContextMenuSupport.UnsubscribeSteamWithConfirmAsync(ownerWindow, manager, new[] { vm.ModReference });
                break;
            case ModContextMenuSupport.RedownloadTag:
                await ModContextMenuSupport.RedownloadSteamWithConfirmAsync(ownerWindow, manager, new[] { vm.ModReference });
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

    private void LogListLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Bind(
            BackgroundProperty,
            new Binding(nameof(ModUpdateLogItemViewModel.BackgroundBrush)) { Mode = BindingMode.OneWay });

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

        foreach (var vm in allEntries)
        {
            vm.RefreshStyle(style);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
