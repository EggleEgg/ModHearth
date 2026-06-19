using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public partial class ModUpdateLogWindow : Window, IStyleAwareWindow, INotifyPropertyChanged
{
    private readonly ModHearthManager? manager;
    private readonly ObservableCollection<ModUpdateLogItemViewModel> entries = new();
    private readonly List<ModUpdateLogItemViewModel> allEntries = new();
    private readonly ListSelectionController<ModUpdateLogItemViewModel> selectionController = new();
    private ModUpdateSortColumn sortColumn = ModUpdateSortColumn.Date;
    private bool sortDescending = true;

    private GridLength dateColumnWidth = new GridLength(160);
    private GridLength modColumnWidth = new GridLength(220);
    private GridLength typeColumnWidth = new GridLength(120);
    private GridLength stateColumnWidth = new GridLength(120);
    private GridLength activeColumnWidth = new GridLength(80);
    private GridLength pathColumnWidth = new GridLength(1, GridUnitType.Star);
    private IBrush backgroundColorBrush = Brushes.Transparent;

    // Parameterless constructor required for XAML loader/design-time tools.
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

    public GridLength DateColumnWidth
    {
        get => dateColumnWidth;
        set
        {
            if (dateColumnWidth == value)
                return;
            dateColumnWidth = value;
            OnPropertyChanged();
        }
    }

    public GridLength ModColumnWidth
    {
        get => modColumnWidth;
        set
        {
            if (modColumnWidth == value)
                return;
            modColumnWidth = value;
            OnPropertyChanged();
        }
    }

    public GridLength TypeColumnWidth
    {
        get => typeColumnWidth;
        set
        {
            if (typeColumnWidth == value)
                return;
            typeColumnWidth = value;
            OnPropertyChanged();
        }
    }

    public GridLength StateColumnWidth
    {
        get => stateColumnWidth;
        set
        {
            if (stateColumnWidth == value)
                return;
            stateColumnWidth = value;
            OnPropertyChanged();
        }
    }

    public GridLength ActiveColumnWidth
    {
        get => activeColumnWidth;
        set
        {
            if (activeColumnWidth == value)
                return;
            activeColumnWidth = value;
            OnPropertyChanged();
        }
    }

    public GridLength PathColumnWidth
    {
        get => pathColumnWidth;
        set
        {
            if (pathColumnWidth == value)
                return;
            pathColumnWidth = value;
            OnPropertyChanged();
        }
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
        allEntries.Clear();

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
            allEntries.Add(new ModUpdateLogItemViewModel(entry, modref, brush, selectedBrush, isActive));
        }

        ApplySort();
        UpdateSortIndicators();
        selectionController.UpdateSelectionState(logList);
    }

    private static IBrush GetDefaultTextBrush()
    {
        if (Style.instance != null)
            return new SolidColorBrush(Style.instance.textColor.ToAvaloniaColor());
        return Brushes.White;
    }

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
            path);
    }

    private void SortHeaderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;
        if (button.Tag is not string tag)
            return;
        if (!Enum.TryParse(tag, out ModUpdateSortColumn column))
            return;

        if (sortColumn == column)
            sortDescending = !sortDescending;
        else
        {
            sortColumn = column;
            sortDescending = true;
        }

        ApplySort();
        UpdateSortIndicators();
    }

    private void ApplySort()
    {
        IEnumerable<ModUpdateLogItemViewModel> source = allEntries;
        IOrderedEnumerable<ModUpdateLogItemViewModel> ordered = sortColumn switch
        {
            ModUpdateSortColumn.Date => sortDescending
                ? source.OrderByDescending(vm => vm.Entry.TimestampUtc)
                : source.OrderBy(vm => vm.Entry.TimestampUtc),
            ModUpdateSortColumn.Mod => sortDescending
                ? source.OrderByDescending(vm => vm.ModName, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(vm => vm.ModName, StringComparer.OrdinalIgnoreCase),
            ModUpdateSortColumn.Type => sortDescending
                ? source.OrderByDescending(vm => vm.SourceType, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(vm => vm.SourceType, StringComparer.OrdinalIgnoreCase),
            ModUpdateSortColumn.State => sortDescending
                ? source.OrderByDescending(vm => GetStateRank(vm.Entry.ChangeType))
                : source.OrderBy(vm => GetStateRank(vm.Entry.ChangeType)),
            ModUpdateSortColumn.Active => sortDescending
                ? source.OrderByDescending(vm => vm.IsActive)
                : source.OrderBy(vm => vm.IsActive),
            ModUpdateSortColumn.Path => sortDescending
                ? source.OrderByDescending(vm => vm.Path, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(vm => vm.Path, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderByDescending(vm => vm.Entry.TimestampUtc)
        };

        ordered = ordered.ThenBy(vm => vm.ModName, StringComparer.OrdinalIgnoreCase);

        entries.Clear();
        foreach (ModUpdateLogItemViewModel vm in ordered)
            entries.Add(vm);
    }

    private void UpdateSortIndicators()
    {
        UpdateSortIndicator(dateSortArrow, sortColumn == ModUpdateSortColumn.Date);
        UpdateSortIndicator(modSortArrow, sortColumn == ModUpdateSortColumn.Mod);
        UpdateSortIndicator(typeSortArrow, sortColumn == ModUpdateSortColumn.Type);
        UpdateSortIndicator(stateSortArrow, sortColumn == ModUpdateSortColumn.State);
        UpdateSortIndicator(activeSortArrow, sortColumn == ModUpdateSortColumn.Active);
        UpdateSortIndicator(pathSortArrow, sortColumn == ModUpdateSortColumn.Path);
    }

    private void UpdateSortIndicator(TextBlock? arrow, bool isActive)
    {
        if (arrow == null)
            return;

        arrow.IsVisible = isActive;
        if (!isActive)
            return;

        arrow.Text = sortDescending ? "  \u2193" : "  \u2191";
    }

    private static int GetStateRank(ModUpdateChangeType changeType)
    {
        return changeType switch
        {
            ModUpdateChangeType.Updated => 2,
            ModUpdateChangeType.Added => 1,
            ModUpdateChangeType.Deleted => 0,
            _ => 0
        };
    }

    private void ModContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (sender is not ContextMenu menu)
            return;

        ContextMenuCoordinator.Activate(menu);

        ModUpdateLogItemViewModel? vm =
            (menu.PlacementTarget as Control)?.DataContext as ModUpdateLogItemViewModel ??
            menu.DataContext as ModUpdateLogItemViewModel ??
            menu.Items.OfType<MenuItem>()
                .Select(item => item.DataContext)
                .OfType<ModUpdateLogItemViewModel>()
                .FirstOrDefault() ??
            logList.SelectedItems?.OfType<ModUpdateLogItemViewModel>().FirstOrDefault();
        if (vm == null)
            return;

        if (logList.SelectedItems == null)
            return;

        selectionController.TryRestoreContextSelection(logList, vm);

        if (logList.SelectedItems.Count == 0 || !logList.SelectedItems.Contains(vm))
        {
            logList.SelectedItems.Clear();
            logList.SelectedItems.Add(vm);
        }

        selectionController.UpdateSelectionState(logList);

        List<ModUpdateLogItemViewModel> selected = logList.SelectedItems.Cast<ModUpdateLogItemViewModel>().ToList();
        ModContextMenuState state = ModContextMenuSupport.BuildState(
            manager,
            vm.ModReference,
            selected.Select(item => item.ModReference));
        ModContextMenuSupport.ApplyState(menu, state);
    }

    private void LogListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list)
            return;

        if (selectionController.HandleSelectionChanged(list))
            return;

        selectionController.UpdateSelectionState(list);
    }

    private void WindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ContextMenuCoordinator.DismissActive();
    }

    private async void ModContextDeleteMod(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!TryGetContextSelection(sender, out List<ModUpdateLogItemViewModel> selection))
            return;

        manager.SplitActionableMods(
            selection.Select(vm => vm.ModReference),
            out List<ModReference> localTargets,
            out _);

        if (localTargets.Count == 0)
        {
            await DialogService.ShowMessageAsync(this, "Selected mods cannot be deleted from the Mods folder.", "Delete Mod");
            return;
        }

        string prompt = ModContextMenuSupport.BuildDeletePrompt(localTargets);

        bool confirm = await DialogService.ShowConfirmAsync(this, prompt, "Delete Mod");
        if (!confirm)
            return;

        List<string> failures = ModContextMenuSupport.DeleteLocalMods(manager, localTargets);

        if (failures.Count > 0)
        {
            await DialogService.ShowMessageAsync(this, string.Join(Environment.NewLine, failures), "Delete Mod");
        }
    }

    private async void ModContextUnsubscribeSteam(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!TryGetContextSelection(sender, out List<ModUpdateLogItemViewModel> selection))
            return;

        manager.SplitActionableMods(
            selection.Select(vm => vm.ModReference),
            out _,
            out List<ModReference> steamTargets);

        if (steamTargets.Count == 0)
        {
            await DialogService.ShowMessageAsync(this, "Selected mods are not Steam Workshop mods.", "Unsubscribe Steam Mod");
            return;
        }

        string prompt = ModContextMenuSupport.BuildUnsubscribePrompt(steamTargets);

        bool confirm = await DialogService.ShowConfirmAsync(this, prompt, "Unsubscribe Steam Mod");
        if (!confirm)
            return;

        List<string> failures = await Task.Run(() =>
            ModContextMenuSupport.UnsubscribeSteamMods(manager, steamTargets));

        if (failures.Count > 0)
            await DialogService.ShowMessageAsync(this, string.Join(Environment.NewLine, failures), "Unsubscribe Steam Mod");
    }

    private async void ModContextRedownloadSteam(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!TryGetContextSelection(sender, out List<ModUpdateLogItemViewModel> selection))
            return;

        manager.SplitActionableMods(
            selection.Select(vm => vm.ModReference),
            out _,
            out List<ModReference> steamTargets);

        if (steamTargets.Count == 0)
        {
            await DialogService.ShowMessageAsync(this, "Selected mods are not Steam Workshop mods.", "Redownload Steam Mod");
            return;
        }

        string prompt = ModContextMenuSupport.BuildRedownloadPrompt(steamTargets);

        bool confirm = await DialogService.ShowConfirmAsync(this, prompt, "Redownload Steam Mod");
        if (!confirm)
            return;

        List<string> failures = await Task.Run(() =>
            ModContextMenuSupport.RedownloadSteamMods(manager, steamTargets));

        if (failures.Count > 0)
            await DialogService.ShowMessageAsync(this, string.Join(Environment.NewLine, failures), "Redownload Steam Mod");
    }

    private async void ModContextOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!TryGetContextSelection(sender, out List<ModUpdateLogItemViewModel> selection))
            return;

        await ModContextMenuSupport.OpenFolderAsync(this, selection.First().ModReference);
    }

    private async void ModContextCopyId(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!TryGetContextSelection(sender, out List<ModUpdateLogItemViewModel> selection))
            return;

        await ModContextMenuSupport.CopyModIdAsync(this, selection.First().ModReference);
    }

    private async void ModContextOpenSteam(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
            return;
        if (!TryGetContextSelection(sender, out List<ModUpdateLogItemViewModel> selection))
            return;

        await ModContextMenuSupport.OpenSteamPageAsync(this, selection.First().ModReference);
    }

    private bool TryGetContextSelection(object? sender, out List<ModUpdateLogItemViewModel> selection)
    {
        selection = new List<ModUpdateLogItemViewModel>();

        if (sender is not MenuItem menuItem)
            return false;
        if (menuItem.DataContext is not ModUpdateLogItemViewModel vm)
            return false;

        List<ModUpdateLogItemViewModel> selected = logList.SelectedItems?.Cast<ModUpdateLogItemViewModel>().ToList()
            ?? new List<ModUpdateLogItemViewModel>();

        if (selected.Count > 0 && selected.Contains(vm))
            selection = selected;
        else
            selection = new List<ModUpdateLogItemViewModel> { vm };

        return true;
    }

    private enum ModUpdateSortColumn
    {
        Date,
        Mod,
        Type,
        State,
        Active,
        Path
    }

    public void ApplyCustomStyle(Style style)
    {
        if (headerBorder == null)
            return;

        BackgroundColorBrush = headerBorder.Background = new SolidColorBrush(style.backgroundColor.ToAvaloniaColor());
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        propertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
