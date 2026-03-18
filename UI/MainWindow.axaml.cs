
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly ObservableCollection<ModRefViewModel> inactiveMods = new();
    private readonly ObservableCollection<ModRefViewModel> activeMods = new();
    private readonly Dictionary<string, ModRefViewModel> modViewMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ModListDragDropController modListController;
    private readonly UndoRedoKeyHandler undoRedoHandler;

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
    private List<DFHMod> duplicateWarningMods = new();
    private int duplicateWarningIndex;

    private DispatcherTimer? modManagerReloadTimer;
    private FileSystemWatcher? modManagerWatcher;

    private IImage? currentPreview;
    private bool updateInProgress;
    private bool hideFilteredLeft;
    private bool hideFilteredRight;


    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();

        manager = new ModHearthManager();
        modListController = new ModListDragDropController(
            this,
            () => modViewMap.Values,
            key => modViewMap.TryGetValue(key, out ModRefViewModel? vm) ? vm : null,
            vm => vm.DfMod.ToString());
        modListController.Dropped += ModlistDropped;

        undoRedoHandler = new UndoRedoKeyHandler(
            () => undoChangesButton.IsEnabled,
            () => UndoChangesAsync(),
            () => redoAvailable,
            () => RedoListChanges());
        undoRedoHandler.Attach(this);

        leftModlist.ItemsSource = inactiveMods;
        rightModlist.ItemsSource = activeMods;
        modListController.RegisterList(leftModlist, allowReorder: false);
        modListController.RegisterList(rightModlist, allowReorder: true);

        leftModlist.SelectionChanged += ModlistSelectionChanged;
        rightModlist.SelectionChanged += ModlistSelectionChanged;
        leftModlist.DoubleTapped += (_, _) => MoveSelectedBetweenLists(true);
        rightModlist.DoubleTapped += (_, _) => MoveSelectedBetweenLists(false);
        AddHandler(InputElement.PointerPressedEvent, WindowPointerPressed, RoutingStrategies.Tunnel, true);

        leftSearchBox.TextChanged += (_, _) => ApplySearchFilter();
        rightSearchBox.TextChanged += (_, _) => ApplySearchFilter();
        leftSearchCloseButton.Click += (_, _) => leftSearchBox.Text = string.Empty;
        rightSearchCloseButton.Click += (_, _) => rightSearchBox.Text = string.Empty;
        leftSearchToggleButton.Click += (_, _) => ToggleSearchFilterVisibility(true);
        rightSearchToggleButton.Click += (_, _) => ToggleSearchFilterVisibility(false);

        saveButton.Click += async (_, _) => await SaveCurrentModpackAsync();
        undoChangesButton.Click += async (_, _) => await UndoChangesAsync();
        autoSortButton.Click += (_, _) => AutoSort();
        sortRulesButton.Click += async (_, _) => await OpenSortRulesAsync();
        clearInstalledModsButton.Click += async (_, _) => await ClearInstalledModsAsync();
        clearInstalledModsButton.AddHandler(InputElement.PointerPressedEvent, ClearInstalledModsPointerPressed, RoutingStrategies.Tunnel, true);
        reloadButton.Click += async (_, _) => await ReloadModpacksAsync();

        newListButton.Click += async (_, _) => await CreateNewModpackAsync();
        renameListButton.Click += async (_, _) => await RenameModpackAsync();
        deleteListButton.Click += async (_, _) => await DeleteModpackAsync();
        importButton.Click += async (_, _) => await ImportModpackAsync();
        exportButton.Click += async (_, _) => await ExportModpackAsync();

        warningIssuesButton.Click += (_, _) => JumpToNextProblem();
        redoConfigButton.Click += async (_, _) => await RedoConfigAsync();
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync();

        themeComboBox.ItemsSource = new[] { "light theme", "dark theme" };
        themeComboBox.SelectionChanged += async (_, _) => await OnThemeChangedAsync();

        modpackComboBox.SelectionChanged += (_, _) => OnModpackChanged();
        Opened += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            modManagerWatcher?.Dispose();
            modManagerReloadTimer?.Stop();
            if (currentPreview is IDisposable disposable)
                disposable.Dispose();
        };

        UpdateSearchToggleIcons();
    }

    private void SetWindowIcon()
    {
        try
        {
            Uri iconUri = new Uri("avares://ModHearth/icons/modhearth_icon_v1.ico");
            using Stream stream = AssetLoader.Open(iconUri);
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // Ignore icon load failures.
        }
    }

    private async Task InitializeAsync()
    {
        if (IsTestMode())
        {
            SetupModlistBox();
            ApplyStyle(manager.LoadStyle());
            BuildModViewModels();
            RefreshModlistPanels();
            clearInstalledModsButton.IsEnabled = Directory.Exists(manager.GetInstalledModsPath());
            modVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
            SetChangesMade(false);
            return;
        }

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
                UpdateDfHackStatus();
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
        try
        {
            ApplyStyle(manager.LoadStyle());
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Style load failed");
            Close();
            return;
        }
        manager.RefreshInstalledCacheModIds();
        BuildModViewModels();
        RefreshModlistPanels();
        clearInstalledModsButton.IsEnabled = Directory.Exists(manager.GetInstalledModsPath());
        modVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
        UpdateDfHackStatus();
        SetChangesMade(false);
        SetupModManagerWatcher();
    }

    private static bool IsTestMode()
    {
        string? value = Environment.GetEnvironmentVariable("MODHEARTH_TEST_MODE");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
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
        UpdateCachedIndicators();
        UpdateProblemIndicators();
        UpdateDuplicateWarningIndicators();
        UpdateModlistHeaders();
        ApplySearchFilter();
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

        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
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

        modListController.UpdateSelectionState(list);
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
            UpdateWarningIssuesButton();
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

    }

    private void UpdateDuplicateWarningIndicators()
    {
        IReadOnlyDictionary<string, List<string>> duplicateMap = manager.GetDuplicateWarningMap();

        duplicateWarningMods = manager.enabledMods
            .Where(m => duplicateMap.ContainsKey(m.id))
            .ToList();

        foreach (ModRefViewModel vm in modViewMap.Values)
        {
            if (duplicateMap.TryGetValue(vm.ModReference.ID, out List<string>? duplicates) &&
                duplicates.Count > 0)
            {
                vm.IsDuplicateWarning = true;
                vm.DuplicateWarningTooltip = BuildDuplicateWarningTooltip(duplicates);
            }
            else
            {
                vm.IsDuplicateWarning = false;
                vm.DuplicateWarningTooltip = null;
            }
        }

        UpdateWarningIssuesButton();
    }

    private static string BuildProblemTooltip(List<ModProblem> problems)
    {
        StringBuilder builder = new StringBuilder("Problems:");
        foreach (ModProblem problem in problems)
            builder.AppendLine().Append(problem.ToString());
        return builder.ToString();
    }

    private string BuildDuplicateWarningTooltip(IEnumerable<string> duplicates)
    {
        string errorLogPath = manager?.GetErrorLogPath() ?? "errorlog.txt";
        StringBuilder builder = new StringBuilder($"Duplicate raw definitions ({errorLogPath}):");
        foreach (string entry in duplicates)
            builder.AppendLine().Append(entry);
        return builder.ToString();
    }

    private void UpdateWarningIssuesButton()
    {
        int problemCount = problemMods?.Count ?? 0;
        int duplicateCount = duplicateWarningMods?.Count ?? 0;
        bool hasProblems = problemCount > 0;
        bool hasDuplicates = duplicateCount > 0;
        bool hasIssues = hasProblems || hasDuplicates;

        warningIssuesButton.IsVisible = hasIssues;
        warningIssuesButton.IsEnabled = hasIssues;
        ToolTip.SetTip(warningIssuesButton, BuildWarningIssuesTooltip(problemCount, duplicateCount));

        if (!hasIssues || warningIssuesIcon == null)
            return;

        string iconName = hasProblems && hasDuplicates
            ? "warningErrorIcon.svg"
            : hasProblems
                ? "errorIcon.svg"
                : "warningIcon.svg";

        warningIssuesIcon.Source = ImageSourceLoader.LoadFromAssetUri($"avares://ModHearth/resources/{iconName}")
            ?? warningIssuesIcon.Source;
    }

    private static string? BuildWarningIssuesTooltip(int problemCount, int duplicateCount)
    {
        if (problemCount <= 0 && duplicateCount <= 0)
            return null;

        string problemText = problemCount > 0
            ? $"{problemCount} mod{(problemCount == 1 ? string.Empty : "s")} with issues"
            : string.Empty;

        string duplicateText = duplicateCount > 0
            ? $"{duplicateCount} mod{(duplicateCount == 1 ? string.Empty : "s")} with duplicate raws"
            : string.Empty;

        if (string.IsNullOrEmpty(problemText))
            return duplicateText;
        if (string.IsNullOrEmpty(duplicateText))
            return problemText;

        return $"{problemText}{Environment.NewLine}{duplicateText}";
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

        List<DFHMod> selectedInactive = leftModlist.SelectedItems?
            .Cast<ModRefViewModel>()
            .Select(m => m.DfMod)
            .ToList() ?? new List<DFHMod>();
        List<DFHMod> selectedActive = rightModlist.SelectedItems?
            .Cast<ModRefViewModel>()
            .Select(m => m.DfMod)
            .ToList() ?? new List<DFHMod>();

        RebuildFilteredList(
            inactiveMods,
            manager.disabledMods.OrderBy(m => manager.GetRefFromDFHMod(m).name ?? string.Empty),
            leftFilter,
            hideFilteredLeft);

        RebuildFilteredList(
            activeMods,
            manager.enabledMods,
            rightFilter,
            hideFilteredRight);

        RestoreSelections(selectedInactive, selectedActive);
    }

    private void RebuildFilteredList(
        ObservableCollection<ModRefViewModel> target,
        IEnumerable<DFHMod> mods,
        string filter,
        bool hideFiltered)
    {
        target.Clear();
        foreach (DFHMod mod in mods)
        {
            if (!modViewMap.TryGetValue(mod.ToString(), out ModRefViewModel? vm) || vm == null)
                continue;

            bool match = string.IsNullOrEmpty(filter) ||
                (vm.ModReference.name?.ToLowerInvariant().Contains(filter) ?? false) ||
                (vm.ModReference.ID?.ToLowerInvariant().Contains(filter) ?? false);

            vm.IsFilteredOut = !match;
            vm.IsVisible = !hideFiltered || match;
            if (!vm.IsVisible)
                vm.IsJumpHighlighted = false;

            if (!hideFiltered || match)
                target.Add(vm);
        }
    }

    private void ToggleSearchFilterVisibility(bool isLeft)
    {
        if (isLeft)
            hideFilteredLeft = !hideFilteredLeft;
        else
            hideFilteredRight = !hideFilteredRight;

        UpdateSearchToggleIcons();
        ApplySearchFilter();
    }

    private void UpdateSearchToggleIcons()
    {
        UpdateSearchToggleIcon(leftSearchToggleIcon, leftSearchToggleButton, hideFilteredLeft);
        UpdateSearchToggleIcon(rightSearchToggleIcon, rightSearchToggleButton, hideFilteredRight);
    }

    private static void UpdateSearchToggleIcon(Image? icon, Button? button, bool isHidden)
    {
        if (icon == null)
            return;

        string iconName = isHidden ? "hideEyeIcon.svg" : "viewEyeIcon.svg";
        icon.Source = ImageSourceLoader.LoadFromAssetUri($"avares://ModHearth/resources/{iconName}") ?? icon.Source;

        if (button != null)
        {
            ToolTip.SetTip(button, isHidden
                ? "Show mismatched mods"
                : "Hide mismatched mods");
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
        IBrush searchButtonBrush = new SolidColorBrush(style.searchButtonColor.ToAvaloniaColor());
        IBrush warningTextBrush = new SolidColorBrush(style.modRefTextWarningColor.ToAvaloniaColor());

        Background = formBrush;
        leftHeaderLabel.Foreground = textBrush;
        rightHeaderLabel.Foreground = textBrush;
        modTitleLabel.Foreground = textBrush;
        modDescriptionLabel.Foreground = textBrush;
        modVersionLabel.Foreground = textBrush;
        dfhackStatusLabel.Foreground = warningTextBrush;

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
            sortRulesButton,
            redoConfigButton,
            warningIssuesButton,
            updateButton,
        };

        foreach (Button button in buttons)
        {
            button.Background = buttonBrush;
            button.Foreground = buttonTextBrush;
            button.BorderBrush = buttonOutlineBrush;
            button.BorderThickness = new Thickness(1);
        }

        Button[] searchButtons =
        {
            leftSearchToggleButton,
            leftSearchCloseButton,
            rightSearchToggleButton,
            rightSearchCloseButton
        };

        foreach (Button button in searchButtons)
        {
            button.Background = searchButtonBrush;
            button.Foreground = buttonTextBrush;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
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
        if (sender is ListBox list && modListController.HandleSelectionChanged(list))
            return;

        if (sender == leftModlist && leftModlist.SelectedItems?.Count > 0)
            rightModlist.SelectedItems?.Clear();
        if (sender == rightModlist && rightModlist.SelectedItems?.Count > 0)
            leftModlist.SelectedItems?.Clear();

        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);

        ModRefViewModel? selected = (sender as ListBox)?.SelectedItem as ModRefViewModel;
        if (selected != null)
            ShowModInfo(selected.ModReference);
    }

    private void ModlistDropped(ModListDropContext context)
    {
        if (context.Items.Count == 0)
            return;

        bool sourceLeft = context.SourceList == leftModlist;
        if (context.SourceList == null)
            sourceLeft = context.Items.Any(vm => inactiveMods.Contains(vm));
        bool destinationLeft = context.DestinationList == leftModlist;

        if (sourceLeft && destinationLeft)
            return;

        List<DFHMod> mods = context.Items.Select(vm => vm.DfMod).ToList();
        manager.MoveMods(mods, context.InsertIndex, sourceLeft, destinationLeft);
        SetAndMarkChanges(true);
        RefreshModlistPanels();
        if (sourceLeft != destinationLeft)
            SelectModsInList(destinationLeft, mods);
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
            modListController.TryRestoreContextSelection(list, vm);

            if (list.SelectedItems == null || list.SelectedItems.Count == 0 || !list.SelectedItems.Contains(vm))
            {
                list.SelectedItems?.Clear();
                list.SelectedItems?.Add(vm);
            }
        }

        bool canOpenFolder = !string.IsNullOrWhiteSpace(vm.ModReference.path) &&
                             Directory.Exists(vm.ModReference.path);
        bool hasSteamId = !string.IsNullOrWhiteSpace(vm.ModReference.steamID) &&
                          long.TryParse(vm.ModReference.steamID, out _);

        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is string tag)
            {
                if (tag == "delete-mod")
                {
                    int deletableCount = list?.SelectedItems?.Cast<ModRefViewModel>()
                        .Count(mod => manager.CanDeleteModFromModsFolder(mod.ModReference)) ?? 0;
                    item.IsEnabled = deletableCount > 0;
                    item.Header = deletableCount > 1
                        ? $"Delete Mods ({deletableCount})"
                        : "Delete Mod";
                }
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
        IImage? fallback = ImageSourceLoader.LoadFromAssetUri("avares://ModHearth/resources/43G6tag.png");
        if (fallback != null)
            return fallback;

        Uri uri = new Uri("avares://ModHearth/resources/43G6tag.png");
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

    private async Task OpenSortRulesAsync()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<ModReference> modRefs = new();
        foreach (DFHMod mod in manager.enabledMods)
        {
            ModReference modref = manager.GetRefFromDFHMod(mod);
            if (modref == null)
                continue;
            string id = modref.ID?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (seen.Add(id))
                modRefs.Add(modref);
        }

        SortRulesWindow dialog = new SortRulesWindow(manager.GetSortRules(), modRefs)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        List<ModSortRule>? result = await dialog.ShowDialog<List<ModSortRule>?>(this);
        if (result == null)
            return;

        manager.SetSortRules(result);
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

    private async void ClearInstalledModsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(clearInstalledModsButton).Properties.IsRightButtonPressed)
            return;

        e.Handled = true;
        await RevealInstalledModsFolderAsync();
    }

    private async Task RevealInstalledModsFolderAsync()
    {
        string installedModsPath = manager.GetInstalledModsPath();
        if (string.IsNullOrWhiteSpace(installedModsPath) || !Directory.Exists(installedModsPath))
        {
            await DialogService.ShowMessageAsync(this, "Installed mods folder not found.", "Open Folder");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installedModsPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Open Folder");
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

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Refreshing modlists from disk.");
        try
        {
            manager.Initialize(preferredName);
            BuildModViewModels();
            UpdateDfHackStatus();
        }
        catch (UserActionRequiredException ex)
        {
            _ = DialogService.ShowMessageAsync(this, ex.Message, "Dwarf Fortress required");
            return;
        }
        catch (Exception ex)
        {
            _ = DialogService.ShowMessageAsync(this, ex.Message, "Reload failed");
            return;
        }

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

    private void UpdateDfHackStatus()
    {
        if (dfhackStatusLabel == null)
            return;

        bool dfRunning = manager.DwarfFortressRunning();
        bool hasDfhack = manager.HasDfhack();

        if (dfRunning && hasDfhack)
        {
            dfhackStatusLabel.IsVisible = false;
            dfhackStatusLabel.Text = string.Empty;
            return;
        }

        dfhackStatusLabel.Text = dfRunning
            ? "DFHack not found"
            : "Dwarf Fortress not running";
        dfhackStatusLabel.IsVisible = true;
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
        if (problemMods != null && problemMods.Count > 0)
        {
            JumpToNextIssue(problemMods, ref problemModIndex);
            return;
        }

        if (duplicateWarningMods != null && duplicateWarningMods.Count > 0)
            JumpToNextIssue(duplicateWarningMods, ref duplicateWarningIndex);
    }

    private void JumpToNextIssue(List<DFHMod> issues, ref int index)
    {
        if (issues.Count == 0)
            return;

        if (index >= issues.Count)
            index = 0;

        DFHMod target = issues[index];
        index = (index + 1) % issues.Count;

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

    private void WindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!HasJumpHighlights())
            return;

        if (e.Source is Control control)
        {
            if (warningIssuesButton != null &&
                (control == warningIssuesButton || control.FindAncestorOfType<Button>() == warningIssuesButton))
                return;

            ListBoxItem? item = control.FindAncestorOfType<ListBoxItem>();
            if (item?.DataContext is ModRefViewModel vm && vm.IsJumpHighlighted)
                return;
        }

        ClearJumpHighlights();
    }

    private bool HasJumpHighlights()
    {
        return modViewMap.Values.Any(vm => vm.IsJumpHighlighted);
    }

    private void ClearJumpHighlights()
    {
        foreach (ModRefViewModel vm in modViewMap.Values)
            vm.IsJumpHighlighted = false;
    }

    private async Task OnThemeChangedAsync()
    {
        if (themeComboBox.SelectedIndex < 0)
            return;

        manager.SetTheme(themeComboBox.SelectedIndex);
        try
        {
            ApplyStyle(manager.LoadStyle());
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Style load failed");
            Close();
            return;
        }
        UpdateProblemIndicators();
        UpdateDuplicateWarningIndicators();
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

    private async Task CheckForUpdatesAsync()
    {
        if (updateInProgress)
            return;

        updateInProgress = true;
        updateButton.IsEnabled = false;

        try
        {
            string currentBuild = ModHearthManager.GetBuildVersionString().Trim();
            bool shouldRestart = await UpdateService.TryRunUpdateAsync(this, currentBuild);
            if (shouldRestart)
                Close();
        }
        finally
        {
            updateInProgress = false;
            updateButton.IsEnabled = true;
        }
    }
}
