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

public partial class ModUpdateLogControl : UserControl, INotifyPropertyChanged, IModRefContextMenuProvider
{
    private readonly ModHearthManager? manager;
    private readonly ObservableCollection<ModUpdateLogItemViewModel> entries = new();
    public ObservableCollection<ModUpdateLogItemViewModel> Entries => entries;
    private readonly ListSelectionController<ModUpdateLogItemViewModel> selectionController = new();
    private ModRefControl? contextMenuHost;
    private IBrush backgroundColorBrush = Brushes.Transparent;

    public ModUpdateLogControl() : this(null) { }

    public ModUpdateLogControl(ModHearthManager? manager)
    {
        InitializeComponent();
        DataContext = this;
        InitializeModListAndContextMenu();

        if (manager != null)
        {
            this.manager = manager;
            LoadEntries();
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
        entries.Clear();

        List<ModUpdateLogEntry> logEntries = ModUpdateLogger.LoadEntries()
            .OrderByDescending(entry => entry.TimestampUtc)
            .ToList();

        HashSet<string> activeIds = manager == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(manager.enabledMods.Select(mod => mod.id), StringComparer.OrdinalIgnoreCase);

        IBrush defaultBrush = GetDefaultTextBrush();
        IBrush selectedBrush = GetSelectedBackgroundBrush();

        foreach (ModUpdateLogEntry entry in logEntries)
        {
            ModReference modref = BuildModReference(entry);
            bool isActive = activeIds.Contains(entry.ModId);
            IBrush brush = GetRowBrush(entry, defaultBrush, isActive);

            entries.Add(new ModUpdateLogItemViewModel(entry, modref, brush, selectedBrush, isActive));
        }

        selectionController.UpdateSelectionState(logList);
        ApplyDefaultSort();
    }

    private void ApplyDefaultSort()
    {
        if (logList.Columns.Count > 0)
            logList.Columns[0].Sort(ListSortDirection.Descending);
    }

    private static IBrush GetDefaultTextBrush()
    {
        if (Style.instance != null)
            return BrushCache.GetBrush(Style.instance.textColor.ToAvaloniaColor());
        return Brushes.White;
    }

    private static IBrush GetRowBrush(ModUpdateLogEntry entry, IBrush defaultBrush, bool isActive)
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
            DataGridRow.BackgroundProperty,
            new Binding(nameof(ModUpdateLogItemViewModel.BackgroundBrush)) { Mode = BindingMode.OneWay });

        if (e.Row.DataContext is ModUpdateLogItemViewModel vm)
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
