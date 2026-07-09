using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public partial class ModUpdateLogWindow : Window, IStyleAwareWindow, INotifyPropertyChanged
{
    private readonly ModHearthManager? manager;
    private readonly ObservableCollection<ModUpdateLogItemViewModel> entries = new();
    private readonly ListSelectionController<ModUpdateLogItemViewModel> selectionController = new();
    private IBrush backgroundColorBrush = Brushes.Transparent;

    public ModUpdateLogWindow()
    {
        InitializeComponent();
        WindowThemeManager.Register(this);

        logList.ItemsSource = entries;
        logList.SelectionChanged += LogListSelectionChanged;
        selectionController.RegisterList(logList);
        AddHandler(InputElement.PointerPressedEvent, WindowPointerPressed, RoutingStrategies.Tunnel, true);
    }

    public ModUpdateLogWindow(ModHearthManager manager)
    {
        InitializeComponent();
        WindowThemeManager.Register(this);

        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        logList.ItemsSource = entries;
        logList.SelectionChanged += LogListSelectionChanged;
        selectionController.RegisterList(logList);
        AddHandler(InputElement.PointerPressedEvent, WindowPointerPressed, RoutingStrategies.Tunnel, true);
        LoadEntries();
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
            return new SolidColorBrush(Style.instance.textColor.ToAvaloniaColor());
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
            return new SolidColorBrush(Style.instance.modRefHighlightColor.ToAvaloniaColor());
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

    private void ModContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (sender is not ContextMenu menu)
            return;

        ModUpdateLogItemViewModel? contextVm = menu.DataContext as ModUpdateLogItemViewModel;
        if (contextVm != null && (logList.SelectedItems == null || !logList.SelectedItems.Contains(contextVm)))
        {
            contextVm = null;
        }

        ModUpdateLogItemViewModel? vm =
            (menu.PlacementTarget as Control)?.DataContext as ModUpdateLogItemViewModel ??
            contextVm ??
            menu.Items.OfType<MenuItem>()
                .Select(item => item.DataContext)
                .OfType<ModUpdateLogItemViewModel>()
                .FirstOrDefault() ??
            logList.SelectedItems?.OfType<ModUpdateLogItemViewModel>().FirstOrDefault();

        if (vm == null)
            return;

        // Ensure the ContextMenu itself has the DataContext so MenuItems can find it if needed
        menu.DataContext = vm;

        if (logList.SelectedItems == null)
            return;

        selectionController.TryRestoreContextSelection(logList, vm);
        ModContextMenuSupport.EnsureContextItemSelected(logList.SelectedItems, vm);
        selectionController.UpdateSelectionState(logList);

        List<ModUpdateLogItemViewModel> selected = logList.SelectedItems.Cast<ModUpdateLogItemViewModel>().ToList();
        ModContextMenuSupport.PrepareContextMenu(
            menu,
            manager,
            vm.ModReference,
            selected.Select(item => item.ModReference));
    }

    private void LogListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid list)
            return;

        if (selectionController.HandleSelectionChanged(list))
            return;

        selectionController.UpdateSelectionState(list);
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
            Console.WriteLine($"[ModUpdateLog] Loading row for '{vm.ModName}' - Change: {vm.Entry.ChangeType}, Active: {vm.IsActive}, RowBrush: {vm.RowBrush}");
        }
    }

    private void WindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ContextMenuCoordinator.DismissActive();
    }

    private async void ModContextDeleteMod(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!ModContextMenuSupport.TryGetContextModReferences<ModUpdateLogItemViewModel>(
                sender,
                logList.SelectedItems,
                vm => vm.ModReference,
                out List<ModReference> modReferences))
            return;

        await ModContextMenuSupport.DeleteLocalModsWithConfirmAsync(this, manager, modReferences);
    }

    private async void ModContextUnsubscribeSteam(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!ModContextMenuSupport.TryGetContextModReferences<ModUpdateLogItemViewModel>(
                sender,
                logList.SelectedItems,
                vm => vm.ModReference,
                out List<ModReference> modReferences))
            return;

        await ModContextMenuSupport.UnsubscribeSteamWithConfirmAsync(this, manager, modReferences);
    }

    private async void ModContextRedownloadSteam(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!ModContextMenuSupport.TryGetContextModReferences<ModUpdateLogItemViewModel>(
                sender,
                logList.SelectedItems,
                vm => vm.ModReference,
                out List<ModReference> modReferences))
            return;

        await ModContextMenuSupport.RedownloadSteamWithConfirmAsync(this, manager, modReferences);
    }

    private async void ModContextOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;

        await ModContextMenuSupport.OpenFolderFromContextMenuAsync(
            sender,
            this,
            logList.SelectedItems,
            (ModUpdateLogItemViewModel vm) => vm.ModReference);
    }

    private async void ModContextCopyId(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;

        await ModContextMenuSupport.CopyModIdFromContextMenuAsync(
            sender,
            this,
            logList.SelectedItems,
            (ModUpdateLogItemViewModel vm) => vm.ModReference);
    }

    private async void ModContextOpenSteam(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;

        await ModContextMenuSupport.OpenSteamPageFromContextMenuAsync(
            sender,
            this,
            logList.SelectedItems,
            (ModUpdateLogItemViewModel vm) => vm.ModReference);
    }

    public void ApplyCustomStyle(Style style)
    {
        BackgroundColorBrush = new SolidColorBrush(style.backgroundColor.ToAvaloniaColor());

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