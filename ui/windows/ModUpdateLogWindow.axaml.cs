using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public partial class ModUpdateLogWindow : Window, IStyleAwareWindow, INotifyPropertyChanged, IModRefContextMenuProvider
{
    private readonly ModHearthManager? manager;
    private readonly ObservableCollection<ModUpdateLogItemViewModel> entries = new();
    public ObservableCollection<ModUpdateLogItemViewModel> Entries => entries;
    private readonly ListSelectionController<ModUpdateLogItemViewModel> selectionController = new();
    private ModRefControl? contextMenuHost;
    private IBrush backgroundColorBrush = Brushes.Transparent;


    // Just so avalonia doesnt complain
    public ModUpdateLogWindow() : this(null) { }
    public ModUpdateLogWindow(ModHearthManager? manager)
    {
        InitializeComponent();
        DataContext = this;
        WindowThemeManager.Register(this);
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
        AddHandler(InputElement.PointerPressedEvent, WindowPointerPressed, RoutingStrategies.Tunnel, true);

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
            contextMenuHost.DataContext = new ModRefViewModel(selected.ModReference);
    }

    private void LogListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        ModUpdateLogItemViewModel? contextLogItemVm = grid.SelectedItem as ModUpdateLogItemViewModel;
        if (contextLogItemVm == null)
        {
            contextLogItemVm = grid.SelectedItems?.OfType<ModUpdateLogItemViewModel>().FirstOrDefault();
        }

        if (contextLogItemVm == null)
            return;

        selectionController.TryRestoreContextSelection(grid, contextLogItemVm);
        ModContextMenuSupport.EnsureContextItemSelected(grid.SelectedItems, contextLogItemVm);
        selectionController.UpdateSelectionState(grid);

        if (contextMenuHost != null)
        {
            contextMenuHost.DataContext = new ModRefViewModel(contextLogItemVm.ModReference);
        }
    }

    private event PropertyChangedEventHandler? propertyChanged;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => propertyChanged += value;
        remove => propertyChanged -= value;
    }

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

    private void LoadEntries()
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

    // non configurable for now
    private static IBrush GetRowBrush(ModUpdateLogEntry entry, IBrush defaultBrush, bool isActive)
    {
        if (entry.ChangeType == ModUpdateChangeType.Deleted)
            return Brushes.Red;
        if (entry.ChangeType == ModUpdateChangeType.Updated)
            return Brushes.DeepSkyBlue;
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
        return new ModReference(
            id,
            "0",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            name,
            string.Empty,
            string.Empty,
            string.Empty,
            steamId,
            path,
            string.IsNullOrWhiteSpace(steamId) ? ModSource.Local : ModSource.Steam);
    }

    public void OnModRefContextMenuOpened(ContextMenu menu, ModRefViewModel vm)
    {
        if (manager == null)
            return;

        foreach (Control item in menu.Items.OfType<Control>())
        {
            if (item is MenuItem menuItem)
            {
                string tag = menuItem.Tag?.ToString() ?? string.Empty;
                if (string.Equals(tag, "set-mod-color-root", StringComparison.Ordinal))
                {
                    menuItem.IsVisible = false;
                }
            }
        }

        if (logList.SelectedItems == null)
            return;

        List<ModUpdateLogItemViewModel> selected = logList.SelectedItems.Cast<ModUpdateLogItemViewModel>().ToList();
        ModContextMenuSupport.PrepareContextMenu(
            menu,
            manager,
            vm.ModReference,
            selected.Select(item => item.ModReference));
    }

    public async void OnModRefContextMenuItemClicked(MenuItem item, ModRefViewModel vm)
    {
        if (manager == null)
            return;

        switch (item.Tag?.ToString())
        {
            case ModContextMenuSupport.DeleteTag:
                await ModContextMenuSupport.DeleteLocalModsWithConfirmAsync(this, manager, new[] { vm.ModReference });
                break;
            case ModContextMenuSupport.UnsubscribeTag:
                await ModContextMenuSupport.UnsubscribeSteamWithConfirmAsync(this, manager, new[] { vm.ModReference });
                break;
            case ModContextMenuSupport.RedownloadTag:
                await ModContextMenuSupport.RedownloadSteamWithConfirmAsync(this, manager, new[] { vm.ModReference });
                break;
            case ModContextMenuSupport.OpenFolderTag:
                await ModContextMenuSupport.OpenFolderAsync(this, vm.ModReference);
                break;
            case ModContextMenuSupport.OpenSteamTag:
                await ModContextMenuSupport.OpenSteamPageAsync(this, vm.ModReference);
                break;
            case ModContextMenuSupport.CopyIdTag:
                await ModContextMenuSupport.CopyModIdAsync(this, vm.ModReference);
                break;
        }
    }

    private void LogListLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Bind(
            DataGridRow.BackgroundProperty,
            new Binding(nameof(ModUpdateLogItemViewModel.BackgroundBrush)) { Mode = BindingMode.OneWay });
        e.Row.Bind(
            DataGridRow.ForegroundProperty,
            new Binding(nameof(ModUpdateLogItemViewModel.RowBrush)) { Mode = BindingMode.OneWay });

        if (e.Row.DataContext is ModUpdateLogItemViewModel vm)
        {
            if (DevMode.IsEnabled)
                Console.WriteLine($"[ModUpdateLog] Loading row for '{vm.ModName}' - Change: {vm.Entry.ChangeType}, Active: {vm.IsActive}, RowBrush: {vm.RowBrush}");
        }
    }

    private void WindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ContextMenuCoordinator.DismissActive();
    }

    public void ApplyCustomStyle(Style style)
    {
        BackgroundColorBrush = BrushCache.GetBrush(style.backgroundColor.ToAvaloniaColor());

        if (logList != null)
        {
            logList.Background = BackgroundColorBrush;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        propertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
