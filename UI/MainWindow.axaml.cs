
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ModHearth.UI;

public partial class MainWindow : Window
{
    private const string DragDataKey = "ModHearth.ModRefs";
    private static readonly DataFormat<string> DragDataFormat =
        DataFormat.CreateStringApplicationFormat(DragDataKey);

    private readonly ObservableCollection<ModRefViewModel> inactiveMods = new();
    private readonly ObservableCollection<ModRefViewModel> activeMods = new();
    private readonly Dictionary<string, ModRefViewModel> modViewMap = new(StringComparer.OrdinalIgnoreCase);

    private ModHearthManager manager;
    private bool changesMade;
    private bool changesMarked;
    private bool redoAvailable;
    private bool isRedoing;
    private bool modifyingComboBox;
    private int lastIndex;
    private List<DFHMod> redoMods = new();
    private List<DFHMod> problemMods = new();
    private int problemModIndex;

    private DispatcherTimer? modManagerReloadTimer;
    private FileSystemWatcher? modManagerWatcher;

    private Point? dragStartPoint;
    private ListBox? dragSourceList;
    private List<ModRefViewModel>? dragSelectionSnapshot;
    private ModRefViewModel? dragHitItem;
    private bool dragPreserveSelection;
    private List<ModRefViewModel>? dragHighlightedItems;
    private Cursor? dragCursor;
    private Cursor? previousCursor;

    private IImage? currentPreview;


    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();

        manager = new ModHearthManager();

        leftModlist.ItemsSource = inactiveMods;
        rightModlist.ItemsSource = activeMods;
        DragDrop.SetAllowDrop(leftModlist, true);
        DragDrop.SetAllowDrop(rightModlist, true);

        leftModlist.SelectionChanged += ModlistSelectionChanged;
        rightModlist.SelectionChanged += ModlistSelectionChanged;
        leftModlist.DoubleTapped += (_, _) => MoveSelectedBetweenLists(true);
        rightModlist.DoubleTapped += (_, _) => MoveSelectedBetweenLists(false);

        leftModlist.AddHandler(InputElement.PointerPressedEvent, ModlistPointerPressed, RoutingStrategies.Tunnel, true);
        rightModlist.AddHandler(InputElement.PointerPressedEvent, ModlistPointerPressed, RoutingStrategies.Tunnel, true);
        leftModlist.AddHandler(InputElement.PointerMovedEvent, ModlistPointerMoved, RoutingStrategies.Tunnel, true);
        rightModlist.AddHandler(InputElement.PointerMovedEvent, ModlistPointerMoved, RoutingStrategies.Tunnel, true);

        leftModlist.AddHandler(DragDrop.DragOverEvent, ModlistDragOver);
        rightModlist.AddHandler(DragDrop.DragOverEvent, ModlistDragOver);
        leftModlist.AddHandler(DragDrop.DropEvent, ModlistDrop);
        rightModlist.AddHandler(DragDrop.DropEvent, ModlistDrop);
        leftModlist.AddHandler(DragDrop.DragLeaveEvent, ModlistDragLeave);
        rightModlist.AddHandler(DragDrop.DragLeaveEvent, ModlistDragLeave);

        leftSearchBox.TextChanged += (_, _) => ApplySearchFilter();
        rightSearchBox.TextChanged += (_, _) => ApplySearchFilter();
        leftSearchCloseButton.Click += (_, _) => leftSearchBox.Text = string.Empty;
        rightSearchCloseButton.Click += (_, _) => rightSearchBox.Text = string.Empty;

        saveButton.Click += async (_, _) => await SaveCurrentModpackAsync();
        undoChangesButton.Click += async (_, _) => await UndoChangesAsync();
        autoSortButton.Click += (_, _) => AutoSort();
        clearInstalledModsButton.Click += async (_, _) => await ClearInstalledModsAsync();
        reloadButton.Click += async (_, _) => await ReloadModpacksAsync();

        newListButton.Click += async (_, _) => await CreateNewModpackAsync();
        renameListButton.Click += async (_, _) => await RenameModpackAsync();
        deleteListButton.Click += async (_, _) => await DeleteModpackAsync();
        importButton.Click += async (_, _) => await ImportModpackAsync();
        exportButton.Click += async (_, _) => await ExportModpackAsync();

        warningIssuesButton.Click += (_, _) => JumpToNextProblem();
        redoConfigButton.Click += async (_, _) => await RedoConfigAsync();

        themeComboBox.ItemsSource = new[] { "light theme", "dark theme" };
        themeComboBox.SelectionChanged += (_, _) => OnThemeChanged();

        modpackComboBox.SelectionChanged += (_, _) => OnModpackChanged();

        KeyDown += OnKeyDown;
        Opened += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            modManagerWatcher?.Dispose();
            modManagerReloadTimer?.Stop();
            if (currentPreview is IDisposable disposable)
                disposable.Dispose();
        };
    }

    private void SetWindowIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "icons", "modhearth_icon_v1.ico");
            if (File.Exists(iconPath))
                Icon = new WindowIcon(iconPath);
        }
        catch
        {
            // Ignore icon load failures.
        }
    }

    private async Task InitializeAsync()
    {
        bool configReady = await EnsureConfigAsync();
        if (!configReady)
        {
            Close();
            return;
        }

        while (true)
        {
            try
            {
                manager.Initialize();
                break;
            }
            catch (UserActionRequiredException ex)
            {
                bool retry = await DialogService.ShowConfirmAsync(this, ex.Message, "Dwarf Fortress required");
                if (!retry)
                {
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageAsync(this, ex.Message, "Initialization failed");
                Close();
                return;
            }
        }

        SetupModlistBox();
        ApplyStyle(manager.LoadStyle());
        manager.RefreshInstalledCacheModIds();
        BuildModViewModels();
        RefreshModlistPanels();
        clearInstalledModsButton.IsEnabled = Directory.Exists(manager.GetInstalledModsPath());
        modVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
        SetChangesMade(false);
        SetupModManagerWatcher();
    }

    private async Task<bool> EnsureConfigAsync()
    {
        while (true)
        {
            IReadOnlyList<ModHearthManager.ConfigIssue> issues = manager.GetConfigIssues();
            if (issues.Count == 0)
                return true;

            bool handled = false;
            foreach (ModHearthManager.ConfigIssue issue in issues)
            {
                switch (issue.IssueType)
                {
                    case ModHearthManager.ConfigIssueType.MissingDwarfFortressPath:
                        handled = await PromptForDwarfFortressPathAsync();
                        if (!handled)
                            return false;
                        break;
                    case ModHearthManager.ConfigIssueType.MissingInstalledModsPath:
                        handled = await PromptForInstalledModsPathAsync();
                        if (!handled)
                            return false;
                        break;
                }
            }
        }
    }

    private async Task<bool> PromptForDwarfFortressPathAsync()
    {
        await DialogService.ShowMessageAsync(this,
            "Please select the Dwarf Fortress executable (df/df.exe) or the game folder.",
            "Dwarf Fortress Path");

        string? file = await DialogService.PickFileAsync(this, "Select Dwarf Fortress executable", GetExecutableFileTypes());
        if (!string.IsNullOrWhiteSpace(file))
        {
            manager.SetDwarfFortressExecutablePath(file);
            return true;
        }

        string? folder = await DialogService.PickFolderAsync(this, "Select Dwarf Fortress folder");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            manager.SetDwarfFortressFolderPath(folder);
            return true;
        }

        return false;
    }

    private async Task<bool> PromptForInstalledModsPathAsync()
    {
        string defaultPath = manager.GetInstalledModsPath();
        if (!string.IsNullOrWhiteSpace(defaultPath) && Directory.Exists(defaultPath))
        {
            manager.SetInstalledModsPath(defaultPath);
            return true;
        }

        await DialogService.ShowMessageAsync(this,
            "Please select your Dwarf Fortress installed_mods folder.",
            "installed_mods location");

        string? folder = await DialogService.PickFolderAsync(this, "Select installed_mods folder");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            manager.SetInstalledModsPath(folder);
            return true;
        }

        return false;
    }

    private static IEnumerable<FilePickerFileType> GetExecutableFileTypes()
    {
        if (OperatingSystem.IsWindows())
        {
            return new[]
            {
                new FilePickerFileType("Dwarf Fortress")
                {
                    Patterns = new[] { "*.exe" }
                }
            };
        }

        return new[] { FilePickerFileTypes.All };
    }
    private void SetupModlistBox()
    {
        modifyingComboBox = true;
        modpackComboBox.ItemsSource = manager.modpacks.Select(m => m.name).ToList();
        modpackComboBox.SelectedIndex = manager.selectedModlistIndex;
        lastIndex = manager.selectedModlistIndex;
        modifyingComboBox = false;
    }

    private void BuildModViewModels()
    {
        modViewMap.Clear();
        foreach (DFHMod dfm in manager.modPool)
        {
            ModReference modref = manager.GetModRef(dfm.ToString());
            ModRefViewModel vm = new ModRefViewModel(modref);
            vm.RefreshStyle();
            modViewMap[dfm.ToString()] = vm;
        }
    }

    private void RefreshModlistPanels()
    {
        List<DFHMod> selectedInactive = leftModlist.SelectedItems?
            .Cast<ModRefViewModel>()
            .Select(m => m.DfMod)
            .ToList() ?? new List<DFHMod>();
        List<DFHMod> selectedActive = rightModlist.SelectedItems?
            .Cast<ModRefViewModel>()
            .Select(m => m.DfMod)
            .ToList() ?? new List<DFHMod>();

        inactiveMods.Clear();
        foreach (DFHMod mod in manager.disabledMods.OrderBy(m => manager.GetRefFromDFHMod(m).name ?? string.Empty))
        {
            if (modViewMap.TryGetValue(mod.ToString(), out ModRefViewModel? vm) && vm != null)
                inactiveMods.Add(vm);
        }

        activeMods.Clear();
        foreach (DFHMod mod in manager.enabledMods)
        {
            if (modViewMap.TryGetValue(mod.ToString(), out ModRefViewModel? vm) && vm != null)
                activeMods.Add(vm);
        }

        UpdateCachedIndicators();
        UpdateProblemIndicators();
        UpdateModlistHeaders();
        ApplySearchFilter();
        RestoreSelections(selectedInactive, selectedActive);
    }

    private void RestoreSelections(IEnumerable<DFHMod> inactive, IEnumerable<DFHMod> active)
    {
        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();

        foreach (DFHMod mod in inactive)
        {
            ModRefViewModel? vm = inactiveMods.FirstOrDefault(m => m.DfMod == mod);
            if (vm != null)
                leftModlist.SelectedItems?.Add(vm);
        }

        foreach (DFHMod mod in active)
        {
            ModRefViewModel? vm = activeMods.FirstOrDefault(m => m.DfMod == mod);
            if (vm != null)
                rightModlist.SelectedItems?.Add(vm);
        }

        UpdateSelectionState(leftModlist, inactiveMods);
        UpdateSelectionState(rightModlist, activeMods);
    }

    private void SelectModsInList(bool destinationLeft, IEnumerable<DFHMod> mods)
    {
        ListBox list = destinationLeft ? leftModlist : rightModlist;
        ObservableCollection<ModRefViewModel> source = destinationLeft ? inactiveMods : activeMods;
        list.SelectedItems?.Clear();

        foreach (DFHMod mod in mods)
        {
            ModRefViewModel? vm = source.FirstOrDefault(m => m.DfMod == mod);
            if (vm != null)
                list.SelectedItems?.Add(vm);
        }

        UpdateSelectionState(list, source);
    }

    private void UpdateCachedIndicators()
    {
        HashSet<string> cachedIds = manager.GetInstalledCacheModIds();
        foreach (ModRefViewModel vm in modViewMap.Values)
        {
            vm.IsCached = cachedIds != null && cachedIds.Contains(vm.DfMod.id);
        }
    }

    private void UpdateProblemIndicators()
    {
        if (manager?.modproblems == null)
        {
            problemMods = new List<DFHMod>();
            problemModIndex = 0;
            warningIssuesButton.IsVisible = false;
            return;
        }

        Dictionary<string, List<ModProblem>> problemMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModProblem problem in manager.modproblems)
        {
            if (!problemMap.TryGetValue(problem.problemThrowerID, out List<ModProblem>? list))
            {
                list = new List<ModProblem>();
                problemMap[problem.problemThrowerID] = list;
            }
            list.Add(problem);
        }

        problemMods = manager.enabledMods
            .Where(m => problemMap.ContainsKey(m.id))
            .ToList();

        foreach (ModRefViewModel vm in modViewMap.Values)
        {
            if (problemMap.TryGetValue(vm.DfMod.id, out List<ModProblem>? problems))
            {
                vm.IsProblem = true;
                vm.ProblemTooltip = BuildProblemTooltip(problems);
            }
            else
            {
                vm.IsProblem = false;
                vm.ProblemTooltip = null;
            }
        }

        bool hasProblems = problemMods.Count > 0;
        warningIssuesButton.IsVisible = hasProblems;
        warningIssuesButton.IsEnabled = hasProblems;
    }

    private static string BuildProblemTooltip(List<ModProblem> problems)
    {
        StringBuilder builder = new StringBuilder("Problems:");
        foreach (ModProblem problem in problems)
            builder.AppendLine().Append(problem.ToString());
        return builder.ToString();
    }

    private void UpdateModlistHeaders()
    {
        leftHeaderLabel.Text = $"Inactive [{manager?.disabledMods?.Count ?? 0}]";
        rightHeaderLabel.Text = $"Active [{manager?.enabledMods?.Count ?? 0}]";
    }

    private void ApplySearchFilter()
    {
        string leftFilter = (leftSearchBox.Text ?? string.Empty).Trim().ToLowerInvariant();
        string rightFilter = (rightSearchBox.Text ?? string.Empty).Trim().ToLowerInvariant();

        foreach (ModRefViewModel vm in inactiveMods)
        {
            bool match = string.IsNullOrEmpty(leftFilter) ||
                (vm.ModReference.name?.ToLowerInvariant().Contains(leftFilter) ?? false) ||
                (vm.ModReference.ID?.ToLowerInvariant().Contains(leftFilter) ?? false);
            vm.IsFilteredOut = !match;
        }

        foreach (ModRefViewModel vm in activeMods)
        {
            bool match = string.IsNullOrEmpty(rightFilter) ||
                (vm.ModReference.name?.ToLowerInvariant().Contains(rightFilter) ?? false) ||
                (vm.ModReference.ID?.ToLowerInvariant().Contains(rightFilter) ?? false);
            vm.IsFilteredOut = !match;
        }
    }

    private void ApplyStyle(Style style)
    {
        if (style == null)
            return;

        Style.instance = style;
        IBrush formBrush = new SolidColorBrush(style.formColor.ToAvaloniaColor());
        IBrush textBrush = new SolidColorBrush(style.textColor.ToAvaloniaColor());
        IBrush panelBrush = new SolidColorBrush(style.modRefPanelColor.ToAvaloniaColor());
        IBrush buttonBrush = new SolidColorBrush(style.buttonColor.ToAvaloniaColor());
        IBrush buttonTextBrush = new SolidColorBrush(style.buttonTextColor.ToAvaloniaColor());
        IBrush buttonOutlineBrush = new SolidColorBrush(style.buttonOutlineColor.ToAvaloniaColor());

        Background = formBrush;
        leftHeaderLabel.Foreground = textBrush;
        rightHeaderLabel.Foreground = textBrush;
        modTitleLabel.Foreground = textBrush;
        modDescriptionLabel.Foreground = textBrush;
        modVersionLabel.Foreground = textBrush;

        leftModlist.Background = panelBrush;
        rightModlist.Background = panelBrush;

        bool isDarkTheme = manager.GetTheme() == 1;
        IBrush inputTextBrush = isDarkTheme ? Brushes.White : Brushes.Black;

        TextBox[] textBoxes =
        {
            leftSearchBox,
            rightSearchBox
        };

        foreach (TextBox textBox in textBoxes)
        {
            textBox.Background = panelBrush;
            textBox.Foreground = inputTextBrush;
        }

        ComboBox[] comboBoxes =
        {
            modpackComboBox,
            themeComboBox
        };

        foreach (ComboBox comboBox in comboBoxes)
        {
            comboBox.Background = panelBrush;
            comboBox.Foreground = inputTextBrush;
        }

        Button[] buttons =
        {
            saveButton,
            undoChangesButton,
            clearInstalledModsButton,
            reloadButton,
            newListButton,
            renameListButton,
            deleteListButton,
            importButton,
            exportButton,
            autoSortButton,
            redoConfigButton,
            warningIssuesButton
        };

        foreach (Button button in buttons)
        {
            button.Background = buttonBrush;
            button.Foreground = buttonTextBrush;
            button.BorderBrush = buttonOutlineBrush;
            button.BorderThickness = new Thickness(1);
        }

        foreach (ModRefViewModel vm in modViewMap.Values)
            vm.RefreshStyle();

        int theme = manager.GetTheme();
        if (themeComboBox != null && themeComboBox.SelectedIndex != theme)
            themeComboBox.SelectedIndex = theme;

        RequestedThemeVariant = theme == 0 ? ThemeVariant.Light : ThemeVariant.Dark;
    }
    private void ModlistSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender == leftModlist && leftModlist.SelectedItems?.Count > 0)
            rightModlist.SelectedItems?.Clear();
        if (sender == rightModlist && rightModlist.SelectedItems?.Count > 0)
            leftModlist.SelectedItems?.Clear();

        UpdateSelectionState(leftModlist, inactiveMods);
        UpdateSelectionState(rightModlist, activeMods);

        ModRefViewModel? selected = (sender as ListBox)?.SelectedItem as ModRefViewModel;
        if (selected != null)
            ShowModInfo(selected.ModReference);
    }

    private static void UpdateSelectionState(ListBox list, ObservableCollection<ModRefViewModel> items)
    {
        IEnumerable<ModRefViewModel> selectedItems = list.SelectedItems?.Cast<ModRefViewModel>()
            ?? Enumerable.Empty<ModRefViewModel>();
        HashSet<ModRefViewModel> selected = new HashSet<ModRefViewModel>(selectedItems);
        foreach (ModRefViewModel vm in items)
            vm.IsSelected = selected.Contains(vm);
    }

    private void ModlistPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        dragStartPoint = null;
        dragSourceList = null;
        dragSelectionSnapshot = null;
        dragHitItem = null;
        dragPreserveSelection = false;
        ClearDragHighlight();

        if (sender is not ListBox list)
            return;

        PointerPoint point = e.GetCurrentPoint(list);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        dragStartPoint = e.GetPosition(list);
        dragSourceList = list;
        dragHitItem = GetItemAtPoint(list, dragStartPoint.Value);

        bool hasModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!hasModifier && dragHitItem != null && list.SelectedItems?.Count > 1 && list.SelectedItems.Contains(dragHitItem))
        {
            dragPreserveSelection = true;
            dragSelectionSnapshot = list.SelectedItems.Cast<ModRefViewModel>().ToList();
        }
    }

    private async void ModlistPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragStartPoint == null || dragSourceList == null)
            return;

        if (!e.GetCurrentPoint(dragSourceList).Properties.IsLeftButtonPressed)
            return;

        Point current = e.GetPosition(dragSourceList);
        if (Math.Abs(current.X - dragStartPoint.Value.X) < 4 && Math.Abs(current.Y - dragStartPoint.Value.Y) < 4)
            return;

        List<ModRefViewModel> selected = dragSourceList.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
        ModRefViewModel? hit = dragHitItem ?? GetItemAtPoint(dragSourceList, current);

        if (dragPreserveSelection && dragSelectionSnapshot != null && dragSelectionSnapshot.Count > 0)
        {
            selected = new List<ModRefViewModel>(dragSelectionSnapshot);
            RestoreListSelection(dragSourceList, dragSelectionSnapshot);
        }

        if (hit != null && selected.Count > 0 && !selected.Contains(hit))
        {
            dragSourceList.SelectedItems?.Clear();
            dragSourceList.SelectedItems?.Add(hit);
            selected = new List<ModRefViewModel> { hit };
        }
        else if (selected.Count == 0 && hit != null)
        {
            dragSourceList.SelectedItems?.Clear();
            dragSourceList.SelectedItems?.Add(hit);
            selected.Add(hit);
        }

        if (selected.Count == 0)
            return;

        selected = OrderSelectionByList(dragSourceList, selected);

        SetDragHighlight(selected);
        try
        {
            string payload = SerializeDragData(selected);
            DataTransfer data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragDataFormat, payload));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            ClearDragHighlight();
            dragStartPoint = null;
            dragSourceList = null;
            dragSelectionSnapshot = null;
            dragHitItem = null;
            dragPreserveSelection = false;
        }
    }

    private void ModlistDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox list)
            return;

        if (!e.DataTransfer.Contains(DragDataFormat))
            return;

        e.DragEffects = DragDropEffects.Move;
        Point pos = e.GetPosition(list);
        UpdateDropHighlight(list, pos);
    }

    private void ModlistDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropHighlights();
    }

    private void ModlistDrop(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox list)
            return;
        if (!e.DataTransfer.Contains(DragDataFormat))
            return;

        ClearDropHighlights();
        ClearDragHighlight();
        Point pos = e.GetPosition(list);
        int index = GetInsertIndex(list, pos);

        string? payload = e.DataTransfer.TryGetValue(DragDataFormat);
        if (string.IsNullOrWhiteSpace(payload))
            return;

        List<ModRefViewModel> selected = DeserializeDragData(payload);
        if (selected.Count == 0)
            return;

        bool sourceLeft = dragSourceList == leftModlist;
        if (dragSourceList == null)
            sourceLeft = selected.Any(vm => inactiveMods.Contains(vm));
        bool destinationLeft = list == leftModlist;

        if (sourceLeft && destinationLeft)
        {
            dragSourceList = null;
            dragStartPoint = null;
            dragSelectionSnapshot = null;
            dragHitItem = null;
            dragPreserveSelection = false;
            return;
        }

        List<DFHMod> mods = selected.Select(vm => vm.DfMod).ToList();
        manager.MoveMods(mods, index, sourceLeft, destinationLeft);
        SetAndMarkChanges(true);
        RefreshModlistPanels();
        if (sourceLeft != destinationLeft)
            SelectModsInList(destinationLeft, mods);
        dragSourceList = null;
        dragStartPoint = null;
        dragSelectionSnapshot = null;
        dragHitItem = null;
        dragPreserveSelection = false;
    }

    private static string SerializeDragData(IEnumerable<ModRefViewModel> mods)
    {
        List<string> keys = mods.Select(vm => vm.DfMod.ToString()).ToList();
        return JsonSerializer.Serialize(keys);
    }

    private List<ModRefViewModel> DeserializeDragData(string payload)
    {
        List<string>? keys = JsonSerializer.Deserialize<List<string>>(payload);
        if (keys == null || keys.Count == 0)
            return new List<ModRefViewModel>();

        List<ModRefViewModel> mods = new List<ModRefViewModel>();
        foreach (string key in keys)
        {
            if (modViewMap.TryGetValue(key, out ModRefViewModel? vm) && vm != null)
                mods.Add(vm);
        }
        return mods;
    }

    private void UpdateDropHighlight(ListBox list, Point position)
    {
        ClearDropHighlights();
        if (list.ItemCount == 0)
            return;

        (int index, bool after) = GetDropTarget(list, position);
        if (list.ItemsSource is not IEnumerable<ModRefViewModel> items)
            return;

        List<ModRefViewModel> itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        if (index >= itemList.Count)
        {
            itemList[^1].ShowDropBelow = true;
            return;
        }

        ModRefViewModel target = itemList[index];
        if (after)
            target.ShowDropBelow = true;
        else
            target.ShowDropAbove = true;
    }

    private void ClearDropHighlights()
    {
        foreach (ModRefViewModel vm in modViewMap.Values)
        {
            vm.ShowDropAbove = false;
            vm.ShowDropBelow = false;
        }
    }

    private void SetDragHighlight(List<ModRefViewModel> items)
    {
        ClearDragHighlight();
        dragHighlightedItems = items;
        foreach (ModRefViewModel vm in items)
            vm.IsDragging = true;
        SetDragCursor(true);
    }

    private void ClearDragHighlight()
    {
        if (dragHighlightedItems == null)
            return;

        foreach (ModRefViewModel vm in dragHighlightedItems)
            vm.IsDragging = false;
        dragHighlightedItems = null;
        SetDragCursor(false);
    }

    private void SetDragCursor(bool active)
    {
        if (active)
        {
            dragCursor ??= new Cursor(StandardCursorType.Hand);
            previousCursor ??= Cursor;
            Cursor = dragCursor;
        }
        else
        {
            Cursor = previousCursor;
            previousCursor = null;
        }
    }

    private static ModRefViewModel? GetItemAtPoint(ListBox list, Point point)
    {
        IInputElement? element = list.InputHitTest(point) as IInputElement;
        Control? control = element as Control;
        ListBoxItem? item = control?.FindAncestorOfType<ListBoxItem>();
        return item?.DataContext as ModRefViewModel;
    }

    private void RestoreListSelection(ListBox list, IEnumerable<ModRefViewModel> selection)
    {
        if (list.SelectedItems == null)
            return;

        list.SelectedItems.Clear();
        foreach (ModRefViewModel vm in selection)
            list.SelectedItems.Add(vm);

        ObservableCollection<ModRefViewModel> items = list == leftModlist ? inactiveMods : activeMods;
        UpdateSelectionState(list, items);
    }

    private static List<ModRefViewModel> OrderSelectionByList(ListBox list, IEnumerable<ModRefViewModel> selection)
    {
        HashSet<ModRefViewModel> selectedSet = new HashSet<ModRefViewModel>(selection);
        if (list.ItemsSource is IEnumerable<ModRefViewModel> items)
            return items.Where(vm => selectedSet.Contains(vm)).ToList();

        return selection.ToList();
    }

    private static (int index, bool after) GetDropTarget(ListBox list, Point point)
    {
        for (int i = 0; i < list.ItemCount; i++)
        {
            if (list.ContainerFromIndex(i) is not Control container)
                continue;

            Point? topLeft = container.TranslatePoint(new Point(0, 0), list);
            if (topLeft == null)
                continue;

            double mid = topLeft.Value.Y + container.Bounds.Height / 2;
            if (point.Y <= mid)
                return (i, false);
        }

        return (list.ItemCount, true);
    }

    private static int GetInsertIndex(ListBox list, Point point)
    {
        (int index, bool after) = GetDropTarget(list, point);
        if (after && index < list.ItemCount)
            return index + 1;
        return index;
    }

    private void ModContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        if (menu.PlacementTarget is not Control control)
            return;

        if (control.DataContext is not ModRefViewModel vm)
            return;

        ListBox? list = GetListForMod(vm);
        if (list != null)
        {
            if (list.SelectedItems == null || list.SelectedItems.Count == 0 || !list.SelectedItems.Contains(vm))
            {
                list.SelectedItems?.Clear();
                list.SelectedItems?.Add(vm);
            }
        }

        bool canDelete = manager.CanDeleteModFromModsFolder(vm.ModReference);
        bool canOpenFolder = !string.IsNullOrWhiteSpace(vm.ModReference.path) &&
                             Directory.Exists(vm.ModReference.path);
        bool hasSteamId = !string.IsNullOrWhiteSpace(vm.ModReference.steamID) &&
                          long.TryParse(vm.ModReference.steamID, out _);

        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is string tag)
            {
                if (tag == "delete-mod")
                    item.IsEnabled = canDelete;
                else if (tag == "open")
                    item.IsEnabled = canOpenFolder;
                else if (tag == "open-steam")
                {
                    item.IsEnabled = hasSteamId;
                    item.IsVisible = hasSteamId;
                }
            }
        }
    }

    private async void ModContextDeleteMod(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
            return;

        List<ModRefViewModel> deletable = selection
            .Where(vm => manager.CanDeleteModFromModsFolder(vm.ModReference))
            .ToList();

        if (deletable.Count == 0)
        {
            await DialogService.ShowMessageAsync(this, "Selected mods cannot be deleted from the Mods folder.", "Delete Mod");
            return;
        }

        string prompt = deletable.Count == 1
            ? $"Delete '{deletable[0].DisplayName}' from the Mods folder?"
            : $"Delete {deletable.Count} mods from the Mods folder?";

        bool confirm = await DialogService.ShowConfirmAsync(this, prompt, "Delete Mod");
        if (!confirm)
            return;

        List<string> failures = new List<string>();
        foreach (ModRefViewModel vm in deletable)
        {
            if (!manager.DeleteModFromModsFolder(vm.ModReference, out string message))
                failures.Add(message);
        }

        if (failures.Count > 0)
        {
            await DialogService.ShowMessageAsync(this, string.Join(Environment.NewLine, failures), "Delete Mod");
        }

        try
        {
            manager.Initialize();
            BuildModViewModels();
            ReloadModpacksFromDisk();
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Reload failed");
        }
    }

    private async void ModContextOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
            return;

        ModRefViewModel vm = selection.First();
        string path = vm.ModReference.path;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            await DialogService.ShowMessageAsync(this, "Mod folder not found.", "Open Folder");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Open Folder");
        }
    }

    private async void ModContextCopyId(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
            return;

        string? id = selection.First().ModReference.ID;
        if (string.IsNullOrWhiteSpace(id))
            return;

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(id);
    }

    private async void ModContextOpenSteam(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
            return;

        string? steamId = selection.First().ModReference.steamID;
        if (string.IsNullOrWhiteSpace(steamId) || !long.TryParse(steamId, out _))
        {
            await DialogService.ShowMessageAsync(this, "Steam ID not available for this mod.", "Open Steam Page");
            return;
        }

        string url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={steamId}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Open Steam Page");
        }
    }

    private bool TryGetContextSelection(object? sender, out List<ModRefViewModel> selection, out bool isLeft)
    {
        selection = new List<ModRefViewModel>();
        isLeft = false;

        if (sender is not MenuItem menuItem)
            return false;
        if (menuItem.DataContext is not ModRefViewModel vm)
            return false;

        ListBox? list = GetListForMod(vm);
        if (list == null)
            return false;

        isLeft = list == leftModlist;

        List<ModRefViewModel> selected = list.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();

        if (selected.Count > 0 && selected.Contains(vm))
            selection = selected;
        else
            selection = new List<ModRefViewModel> { vm };

        return true;
    }

    private ListBox? GetListForMod(ModRefViewModel vm)
    {
        if (inactiveMods.Contains(vm))
            return leftModlist;
        if (activeMods.Contains(vm))
            return rightModlist;
        return null;
    }

    private void MoveSelectedBetweenLists(bool sourceLeft)
    {
        ListBox source = sourceLeft ? leftModlist : rightModlist;
        if (source.SelectedItems == null || source.SelectedItems.Count == 0)
            return;

        List<ModRefViewModel> selected = source.SelectedItems.Cast<ModRefViewModel>().ToList();
        List<DFHMod> mods = selected.Select(vm => vm.DfMod).ToList();
        int index = manager.enabledMods.Count;
        manager.MoveMods(mods, index, sourceLeft, !sourceLeft);
        SetAndMarkChanges(true);
        RefreshModlistPanels();
        SelectModsInList(!sourceLeft, mods);
    }
    private void ShowModInfo(ModReference modref)
    {
        modTitleLabel.Text = modref.name ?? string.Empty;
        modDescriptionLabel.Text = modref.description ?? string.Empty;
        modVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";

        IImage? previewImage = null;
        string previewSvgPath = Path.Combine(modref.path, "preview.svg");
        if (File.Exists(previewSvgPath))
            previewImage = ImageSourceLoader.LoadFromFilePath(previewSvgPath);

        if (previewImage == null)
        {
            string previewPath = Path.Combine(modref.path, "preview.png");
            if (File.Exists(previewPath))
                previewImage = ImageSourceLoader.LoadFromFilePath(previewPath);
        }

        SetPreviewImage(previewImage ?? LoadFallbackPreview());
    }

    private IImage LoadFallbackPreview()
    {
        IImage? fallback = ImageSourceLoader.LoadFromAssetUri("avares://ModHearth/Resources/43G6tag.png");
        if (fallback != null)
            return fallback;

        Uri uri = new Uri("avares://ModHearth/Resources/43G6tag.png");
        using Stream stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }

    private void SetPreviewImage(IImage? image)
    {
        if (currentPreview is IDisposable disposable)
            disposable.Dispose();
        currentPreview = image;
        modPreviewImage.Source = image;
    }

    private async Task SaveCurrentModpackAsync()
    {
        manager.SaveCurrentModpack();
        SetAndMarkChanges(false);
        await Task.CompletedTask;
    }

    private async Task UndoChangesAsync()
    {
        bool confirm = await DialogService.ShowConfirmAsync(this, "Are you sure you want to reset modlist changes?", "Undo changes");
        if (!confirm)
            return;

        UndoListChanges();
    }

    private void UndoListChanges()
    {
        redoMods = new List<DFHMod>(manager.enabledMods);
        redoAvailable = true;

        manager.SetSelectedModpack(lastIndex);
        RefreshModlistPanels();
        SetAndMarkChanges(false);
    }

    private void RedoListChanges()
    {
        if (!redoAvailable || redoMods.Count == 0)
            return;

        isRedoing = true;
        manager.SetActiveMods(new List<DFHMod>(redoMods));
        RefreshModlistPanels();
        SetAndMarkChanges(true);
        isRedoing = false;

        redoAvailable = false;
        redoMods.Clear();
    }

    private void ClearRedo()
    {
        redoAvailable = false;
        redoMods.Clear();
    }

    private void AutoSort()
    {
        bool changed = manager.AutoSortEnabledMods();
        if (changed)
            SetAndMarkChanges(true);
        RefreshModlistPanels();
    }

    private async Task ClearInstalledModsAsync()
    {
        string installedModsPath = manager.GetInstalledModsPath();
        bool confirm = await DialogService.ShowConfirmAsync(this,
            $"Clear installed mods cache?\n{installedModsPath}",
            "Clear installed mods");
        if (!confirm)
            return;

        bool success = manager.ClearInstalledModsFolder(out string message);
        await DialogService.ShowMessageAsync(this, message, success ? "Installed mods cleared" : "Clear failed");

        clearInstalledModsButton.IsEnabled = Directory.Exists(installedModsPath);
        if (success)
        {
            manager.RefreshInstalledCacheModIds();
            RefreshModlistPanels();
        }
    }

    private async Task ReloadModpacksAsync()
    {
        if (changesMade)
        {
            bool confirm = await DialogService.ShowConfirmAsync(this,
                $"You have unsaved changes to '{manager.SelectedModlist.name}'. Reloading will discard them. Continue?",
                "Reload modlists");
            if (!confirm)
                return;
        }

        ReloadModpacksFromDisk();
    }

    private void ReloadModpacksFromDisk()
    {
        string? preferredName = manager.modpacks.Count > 0
            ? manager.SelectedModlist?.name
            : null;

        if (!manager.ReloadModpacksFromDisk(preferredName))
            return;

        modifyingComboBox = true;
        modpackComboBox.ItemsSource = manager.modpacks.Select(m => m.name).ToList();

        if (manager.selectedModlistIndex >= 0 && manager.selectedModlistIndex < manager.modpacks.Count)
        {
            modpackComboBox.SelectedIndex = manager.selectedModlistIndex;
            lastIndex = manager.selectedModlistIndex;
        }
        else
        {
            modpackComboBox.SelectedIndex = -1;
            lastIndex = -1;
        }

        modifyingComboBox = false;

        manager.RefreshInstalledCacheModIds();
        RefreshModlistPanels();
        SetAndMarkChanges(false);

        if (!string.IsNullOrWhiteSpace(manager.LastMissingModsMessage))
            _ = DialogService.ShowMessageAsync(this, manager.LastMissingModsMessage, "Missing Mods");
    }
    private async Task CreateNewModpackAsync()
    {
        string? newName = await DialogService.ShowInputAsync(this,
            "Please enter a name for the new modpack",
            "New Modpack Name",
            string.Empty);

        if (string.IsNullOrWhiteSpace(newName))
            return;

        DFHModpack newPack = new DFHModpack(false, manager.GenerateVanillaModlist(), newName);
        RegisterNewModpack(newPack);
    }

    private void RegisterNewModpack(DFHModpack newList)
    {
        modifyingComboBox = true;

        manager.modpacks.Add(newList);
        manager.SaveAllModpacks();

        modpackComboBox.ItemsSource = manager.modpacks.Select(m => m.name).ToList();
        modpackComboBox.SelectedIndex = manager.modpacks.Count - 1;

        manager.SetSelectedModpack(modpackComboBox.SelectedIndex);
        RefreshModlistPanels();
        SetAndMarkChanges(false);

        modifyingComboBox = false;
    }

    private async Task RenameModpackAsync()
    {
        string? newName = await DialogService.ShowInputAsync(this,
            "Please enter a new name for the modpack",
            "New Modpack Name",
            manager.SelectedModlist.name);

        if (string.IsNullOrWhiteSpace(newName))
            return;

        modifyingComboBox = true;

        manager.SelectedModlist.name = newName;
        modpackComboBox.ItemsSource = manager.modpacks.Select(m => m.name).ToList();
        modpackComboBox.SelectedIndex = manager.selectedModlistIndex;

        manager.SaveCurrentModpack();
        SetAndMarkChanges(false);

        modifyingComboBox = false;
    }

    private async Task DeleteModpackAsync()
    {
        bool confirm = await DialogService.ShowConfirmAsync(this,
            $"Are you sure you want to delete {manager.SelectedModlist.name}? This is final.",
            "Delete modlist");
        if (!confirm)
            return;

        SetAndMarkChanges(false);

        if (manager.modpacks.Count == 1)
        {
            await DialogService.ShowMessageAsync(this, "You cannot delete the last modlist.", "Failed");
            return;
        }

        modifyingComboBox = true;

        int removeIndex = manager.selectedModlistIndex;
        manager.modpacks.RemoveAt(removeIndex);
        manager.SaveAllModpacks();

        modpackComboBox.ItemsSource = manager.modpacks.Select(m => m.name).ToList();
        manager.SetSelectedModpack(0);
        modpackComboBox.SelectedIndex = 0;
        lastIndex = 0;
        RefreshModlistPanels();

        modifyingComboBox = false;
    }

    private async Task ImportModpackAsync()
    {
        string? filePath = await DialogService.PickFileAsync(this,
            "Select a Modpack JSON File",
            new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } });

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            string importedString = File.ReadAllText(filePath);
            DFHModpack? importedList = JsonSerializer.Deserialize<DFHModpack>(importedString);
            if (importedList == null)
                throw new InvalidOperationException("Invalid modpack file.");

            for (int i = 0; i < manager.modpacks.Count; i++)
            {
                DFHModpack otherModlist = manager.modpacks[i];
                if (otherModlist.name == importedList.name)
                {
                    bool overwrite = await DialogService.ShowConfirmAsync(this,
                        $"A modpack with the name {otherModlist.name} is already present. Would you like to overwrite it?",
                        "Modlist Already Present");
                    if (!overwrite)
                        return;

                    modifyingComboBox = true;
                    modpackComboBox.SelectedIndex = i;
                    lastIndex = i;
                    modifyingComboBox = false;

                    manager.SetSelectedModpack(i);
                    manager.SetActiveMods(importedList.modlist);
                    RefreshModlistPanels();

                    SetChangesMade(true);
                    MarkChanges(i);
                    return;
                }
            }

            RegisterNewModpack(importedList);
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, "Error: " + ex.Message, "Error");
        }
    }

    private async Task ExportModpackAsync()
    {
        string? filePath = await DialogService.PickSaveFileAsync(this,
            "Save Modpack JSON File",
            "modpack.json",
            new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } });

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
            string exportString = JsonSerializer.Serialize(manager.SelectedModlist, options);
            File.WriteAllText(filePath, exportString);
            await DialogService.ShowMessageAsync(this, "File saved successfully.", "Success");
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, "Error: " + ex.Message, "Error");
        }
    }

    private async Task RedoConfigAsync()
    {
        bool confirm = await DialogService.ShowConfirmAsync(this,
            "Are you sure you want to reset config file? Application will restart.",
            "Redo Config");
        if (!confirm)
            return;

        manager.DestroyConfig();
        RestartApplication();
    }

    private void JumpToNextProblem()
    {
        if (problemMods == null || problemMods.Count == 0)
            return;

        if (problemModIndex >= problemMods.Count)
            problemModIndex = 0;

        DFHMod target = problemMods[problemModIndex];
        problemModIndex = (problemModIndex + 1) % problemMods.Count;

        ModRefViewModel? vm = activeMods.FirstOrDefault(m => m.DfMod == target);
        if (vm == null)
            return;

        foreach (ModRefViewModel other in activeMods)
            other.IsJumpHighlighted = false;

        vm.IsJumpHighlighted = true;
        rightModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Add(vm);
        rightModlist.ScrollIntoView(vm);
        ShowModInfo(vm.ModReference);
    }

    private void OnThemeChanged()
    {
        if (themeComboBox.SelectedIndex < 0)
            return;

        manager.SetTheme(themeComboBox.SelectedIndex);
        ApplyStyle(manager.LoadStyle());
        UpdateProblemIndicators();
    }

    private void OnModpackChanged()
    {
        if (modifyingComboBox)
            return;

        if (changesMade)
        {
            _ = HandleModpackChangeWithUnsavedAsync();
            return;
        }

        SetAndRefreshModpack(modpackComboBox.SelectedIndex);
        lastIndex = modpackComboBox.SelectedIndex;
    }

    private async Task HandleModpackChangeWithUnsavedAsync()
    {
        bool save = await DialogService.ShowConfirmAsync(this,
            $"Do you want to save changes to '{manager.SelectedModlist.name}'?",
            "Save changes");

        if (save)
        {
            await SaveCurrentModpackAsync();
        }
        else
        {
            bool discard = await DialogService.ShowConfirmAsync(this,
                "Discard changes and switch modpack?",
                "Discard changes");
            if (!discard)
            {
                modifyingComboBox = true;
                modpackComboBox.SelectedIndex = lastIndex;
                modifyingComboBox = false;
                return;
            }
            SetAndMarkChanges(false);
        }

        SetAndRefreshModpack(modpackComboBox.SelectedIndex);
        lastIndex = modpackComboBox.SelectedIndex;
    }

    private void SetAndRefreshModpack(int index)
    {
        manager.SetSelectedModpack(index);
        RefreshModlistPanels();
    }

    private void MarkChanges(int index)
    {
        if (changesMarked)
            return;
        if (index < 0 || index >= manager.modpacks.Count)
            return;

        List<string> names = manager.modpacks.Select(m => m.name).ToList();
        names[index] = names[index] + "*";
        modifyingComboBox = true;
        modpackComboBox.ItemsSource = names;
        modpackComboBox.SelectedIndex = index;
        modifyingComboBox = false;
        changesMarked = true;
    }

    private void UnmarkChanges(int index)
    {
        if (!changesMarked)
            return;
        if (index < 0 || index >= manager.modpacks.Count)
        {
            changesMarked = false;
            return;
        }

        List<string> names = manager.modpacks.Select(m => m.name).ToList();
        if (names[index].EndsWith("*", StringComparison.Ordinal))
            names[index] = names[index][..^1];

        modifyingComboBox = true;
        modpackComboBox.ItemsSource = names;
        modpackComboBox.SelectedIndex = index;
        modifyingComboBox = false;
        changesMarked = false;
    }

    private void SetChangesMade(bool made)
    {
        changesMade = made;
        undoChangesButton.IsEnabled = made;
        renameListButton.IsEnabled = !made;
        importButton.IsEnabled = !made;
        exportButton.IsEnabled = !made;
        newListButton.IsEnabled = !made;
    }

    private void SetAndMarkChanges(bool made)
    {
        if (made && !isRedoing)
            ClearRedo();
        SetChangesMade(made);
        if (made)
            MarkChanges(lastIndex);
        else
            UnmarkChanges(lastIndex);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
        {
            if (undoChangesButton.IsEnabled)
                _ = UndoChangesAsync();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
        {
            RedoListChanges();
            e.Handled = true;
        }
    }
    private void SetupModManagerWatcher()
    {
        string modManagerPath = manager.GetModManagerConfigPath();
        if (string.IsNullOrWhiteSpace(modManagerPath))
            return;

        string? directory = Path.GetDirectoryName(modManagerPath);
        string? fileName = Path.GetFileName(modManagerPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            return;

        modManagerReloadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        modManagerReloadTimer.Tick += (_, _) =>
        {
            modManagerReloadTimer.Stop();
            if (manager.IsSavingModpacks)
                return;
            ReloadModpacksFromDisk();
        };

        modManagerWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        modManagerWatcher.Changed += (_, _) => RestartWatcherTimer();
        modManagerWatcher.Created += (_, _) => RestartWatcherTimer();
        modManagerWatcher.Renamed += (_, _) => RestartWatcherTimer();
        modManagerWatcher.EnableRaisingEvents = true;
    }

    private void RestartWatcherTimer()
    {
        if (modManagerReloadTimer == null || manager.IsSavingModpacks)
            return;

        modManagerReloadTimer.Stop();
        modManagerReloadTimer.Start();
    }

    private void RestartApplication()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        catch
        {
            // Ignore restart failures.
        }

        Close();
    }

}
