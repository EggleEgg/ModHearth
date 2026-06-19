
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
using System.Text.RegularExpressions;
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
    private bool isBatchSelecting;
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
    private DispatcherTimer? dfHackStatusTimer;
    private DispatcherTimer? autoReloadTimer;
    private DispatcherTimer? searchDebounceTimer;
    private Flyout? reloadOptionsFlyout;
    private CheckBox? autoReloadEnabledCheckBox;
    private TextBox? autoReloadSecondsTextBox;
    private bool suppressAutoReloadUiEvents;
    private bool suppressSearchInputEvents;
    private bool isApplyingSearchFilter;
    private bool ensureSearchResultVisibleOnNextFilter;
    private bool bypassUnsavedClosePrompt;
    private bool unsavedClosePromptInFlight;
    private string transientStatusNotice = string.Empty;
    private DateTime transientStatusNoticeUntilUtc;
    private const int MinimumAutoReloadSeconds = 3;
    private static readonly TimeSpan DfHackStatusRefreshInterval = TimeSpan.FromSeconds(3);

    private IImage? currentPreview;
    private bool updateInProgress;
    private string? currentSelectedModId;
    private string? previousSelectedModId;


    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        SetPreviewImage(LoadFallbackPreview());
        ShowFallbackHelpText();

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
        KeyDown += MainWindowKeyDown;

        searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        searchDebounceTimer.Tick += (_, _) =>
        {
            searchDebounceTimer?.Stop();
            TempSearchLog($"DebounceTick left='{TrimForLog(leftSearchBar.Text)}' right='{TrimForLog(rightSearchBar.Text)}' leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");
            ApplySearchFilter();
        };

        leftSearchBar.SearchTextChanged += OnSearchInputChanged;
        rightSearchBar.SearchTextChanged += OnSearchInputChanged;
        leftSearchBar.HideFilteredToggled += OnHideFilteredChanged;
        rightSearchBar.HideFilteredToggled += OnHideFilteredChanged;
        leftSearchBar.SearchModeChanged += OnSearchModeChanged;
        rightSearchBar.SearchModeChanged += OnSearchModeChanged;

        saveButton.Click += async (_, _) => await SaveCurrentModpackAsync();
        undoChangesButton.Click += async (_, _) => await UndoChangesAsync();
        autoSortButton.Click += (_, _) => AutoSort();
        sortRulesButton.Click += async (_, _) => await OpenSortRulesAsync();
        clearInstalledModsButton.Click += async (_, _) => await ClearInstalledModsAsync();
        clearInstalledModsButton.AddHandler(InputElement.PointerPressedEvent, ClearInstalledModsPointerPressed, RoutingStrategies.Tunnel, true);
        reloadButton.Click += async (_, _) => await ReloadModpacksAsync();
        reloadButton.AddHandler(InputElement.PointerPressedEvent, ReloadButtonPointerPressed, RoutingStrategies.Tunnel, true);

        newListButton.Click += async (_, _) => await CreateNewModpackAsync();
        renameListButton.Click += async (_, _) => await RenameModpackAsync();
        deleteListButton.Click += async (_, _) => await DeleteModpackAsync();
        importButton.Click += async (_, _) => await ImportModpackAsync();
        exportButton.Click += async (_, _) => await ExportModpackAsync();

        warningIssuesButton.Click += (_, _) => JumpToNextProblem();
        redoConfigButton.Click += async (_, _) => await RedoConfigAsync();
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        updateLogButton.Click += (_, _) => OpenModUpdateLog();

        themeComboBox.ItemsSource = new[] { "light theme", "dark theme" };
        themeComboBox.SelectionChanged += async (_, _) => await OnThemeChangedAsync();

        modpackComboBox.SelectionChanged += (_, _) => OnModpackChanged();
        Opened += async (_, _) => await InitializeAsync();
        Closing += MainWindowClosing;
        Closed += (_, _) =>
        {
            modManagerWatcher?.Dispose();
            modManagerReloadTimer?.Stop();
            dfHackStatusTimer?.Stop();
            autoReloadTimer?.Stop();
            searchDebounceTimer?.Stop();
            if (currentPreview is IDisposable disposable)
                disposable.Dispose();
        };

        InitializeDfHackStatusTimer();
        InitializeAutoReloadTimer();
    }

    private void ShowFallbackHelpText()
    {
        modTitleLabel.Text = "Welcome to ModHearth!";
        modDescriptionLabel.Text = $"{BuildHelpTextFromReadme()}{Environment.NewLine}";
        buildVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
    }

    private static string BuildHelpTextFromReadme()
    {
        string? readmePath = FindReadmePath();
        if (string.IsNullOrWhiteSpace(readmePath) || !File.Exists(readmePath))
            return "README.md not found. Open README for instructions and shortcuts.";

        try
        {
            string markdown = File.ReadAllText(readmePath);
            string instructions = ExtractMarkdownSection(markdown, "Instructions");
            string controls = ExtractMarkdownSection(markdown, "Keyboard Shortcuts and Controls");

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(instructions))
                parts.Add($"### Instructions{Environment.NewLine}{instructions}");
            if (!string.IsNullOrWhiteSpace(controls))
                parts.Add($"### Keyboard Shortcuts and Controls{Environment.NewLine}{controls}");

            if (parts.Count > 0)
                return RenderBasicMarkdownToText(string.Join($"{Environment.NewLine}{Environment.NewLine}", parts));
        }
        catch
        {
            // Ignore README parsing failures and fall back to a short message.
        }

        return "Unable to read README sections. Open README for instructions and shortcuts.";
    }
    private static string RenderBasicMarkdownToText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        StringBuilder builder = new StringBuilder();
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                builder.AppendLine();
                continue;
            }

            Match headingMatch = Regex.Match(trimmed, @"^(?<level>#{1,6})\s+(?<title>.+)$");
            if (headingMatch.Success)
            {
                string heading = DecodeInlineMarkdown(headingMatch.Groups["title"].Value).Trim();
                int underlineLength = Math.Clamp(heading.Length, 3, 60);
                builder.AppendLine();
                builder.AppendLine(heading);
                builder.AppendLine(new string('-', underlineLength));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                builder.Append("- ");
                builder.AppendLine(DecodeInlineMarkdown(trimmed.Substring(2)).Trim());
                continue;
            }

            Match numberedMatch = Regex.Match(trimmed, @"^(?<num>\d+)\.\s+(?<text>.+)$");
            if (numberedMatch.Success)
            {
                builder.Append(numberedMatch.Groups["num"].Value);
                builder.Append(". ");
                builder.AppendLine(DecodeInlineMarkdown(numberedMatch.Groups["text"].Value).Trim());
                continue;
            }

            builder.AppendLine(DecodeInlineMarkdown(trimmed));
        }

        return builder.ToString().Trim();
    }

    private static string DecodeInlineMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string output = text;

        // [label](url) -> label (url)
        output = Regex.Replace(output, @"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)", "${label} (${url})");
        // `code` -> code
        output = Regex.Replace(output, @"`([^`]+)`", "$1");
        // **bold** / __bold__ -> bold
        output = Regex.Replace(output, @"\*\*(.+?)\*\*", "$1");
        output = Regex.Replace(output, @"__(.+?)__", "$1");
        // *italic* / _italic_ -> italic
        output = Regex.Replace(output, @"\*(.+?)\*", "$1");
        output = Regex.Replace(output, @"_(.+?)_", "$1");

        return output;
    }

    private static string? FindReadmePath()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "README.md"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "README.md")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "README.md"))
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string ExtractMarkdownSection(string markdown, string sectionTitle)
    {
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        StringBuilder builder = new StringBuilder();
        bool inSection = false;
        int sectionLevel = 0;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#')
                    level++;

                string title = trimmed.Substring(level).Trim();
                if (!inSection)
                {
                    if (string.Equals(title, sectionTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        sectionLevel = level;
                    }
                    continue;
                }

                if (level <= sectionLevel)
                    break;
            }

            if (!inSection)
                continue;

            builder.AppendLine(line);
        }

        return builder.ToString().Trim();
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
        if (!DevMode.IsEnabled)
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
        }
        else
        {
            try
            {
                manager.Initialize();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEV] Initialization failed in dev mode: {ex}");
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
        buildVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
        UpdateDfHackStatus();
        StartDfHackStatusTimer();
        SetChangesMade(false);
        if (!DevMode.IsEnabled)
            ResetModManagerWatcher();
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
        string modsFolderPath = manager.GetModsPath();
        string vanillaFolderPath = manager.GetVanillaModsPath();
        foreach (DFHMod dfm in manager.modPool)
        {
            ModReference modref = manager.GetModRef(dfm.ToString());
            ModRefViewModel vm = new ModRefViewModel(modref);
            (bool isVanillaMod, bool isLocalMod, bool isSteamMod) = ModSourceClassifier.Classify(
                modref,
                modsFolderPath,
                vanillaFolderPath);
            vm.IsVanillaModSource = isVanillaMod;
            vm.IsLocalModSource = isLocalMod;
            vm.IsSteamModSource = isSteamMod;
            vm.RefreshStyle();
            modViewMap[dfm.ToString()] = vm;
        }
    }

    private void RefreshModlistPanels()
    {
        RebuildPanelCollectionsFromManager();
        UpdateCachedIndicators();
        UpdateProblemIndicators();
        UpdateDuplicateWarningIndicators();
        UpdateModlistHeaders();
        ApplySearchFilter();
    }

    private void RebuildPanelCollectionsFromManager()
    {
        List<ModRefViewModel> newInactive = manager.disabledMods
            .OrderBy(m => manager.GetRefFromDFHMod(m).name ?? string.Empty)
            .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
            .Where(vm => vm != null)
            .Cast<ModRefViewModel>()
            .ToList();

        List<ModRefViewModel> newActive = manager.enabledMods
            .Select(m => modViewMap.TryGetValue(m.ToString(), out ModRefViewModel? vm) ? vm : null)
            .Where(vm => vm != null)
            .Cast<ModRefViewModel>()
            .ToList();

        ReplaceCollection(inactiveMods, newInactive);
        ReplaceCollection(activeMods, newActive);
    }

    private void SelectModsInList(bool destinationLeft, IEnumerable<DFHMod> mods)
    {
        ListBox list = destinationLeft ? leftModlist : rightModlist;
        ObservableCollection<ModRefViewModel> source = destinationLeft ? inactiveMods : activeMods;

        List<ModRefViewModel> toSelect = mods
            .Select(mod => source.FirstOrDefault(m => m.DfMod == mod))
            .Where(vm => vm != null)
            .Cast<ModRefViewModel>()
            .ToList();

        isBatchSelecting = true;
        try
        {
            list.SelectedItems?.Clear();
            foreach (ModRefViewModel vm in toSelect)
                list.SelectedItems?.Add(vm);
        }
        finally
        {
            isBatchSelecting = false;
        }

        modListController.UpdateSelectionState(list);

        // Single scroll + info update after all items are selected
        ModRefViewModel? primary = toSelect.FirstOrDefault();
        if (primary != null)
        {
            list.ScrollIntoView(primary);
            TrackSelectedMod(primary);
            ShowModInfo(primary.ModReference);
        }
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

        warningIssuesIcon.Source = ImageSourceLoader.LoadFromAssetUri(iconName)
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
        string leftFilter = leftSearchBar.Text.Trim();
        string rightFilter = rightSearchBar.Text.Trim();
        SearchFilterMode leftMode = leftSearchBar.SearchMode;
        SearchFilterMode rightMode = rightSearchBar.SearchMode;
        TempSearchLog(
            $"ApplySearchFilter start left='{TrimForLog(leftFilter)}' leftMode={DescribeSearchMode(leftMode)} " +
            $"right='{TrimForLog(rightFilter)}' rightMode={DescribeSearchMode(rightMode)} leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");
        bool ensureVisible = ensureSearchResultVisibleOnNextFilter;

        isApplyingSearchFilter = true;
        try
        {
            ApplyFilterFlags(
                inactiveMods,
                manager.disabledMods.OrderBy(m => manager.GetRefFromDFHMod(m).name ?? string.Empty),
                leftFilter,
                leftMode,
                leftSearchBar.HideFiltered,
                leftModlist);
            ApplyFilterFlags(
                activeMods,
                manager.enabledMods,
                rightFilter,
                rightMode,
                rightSearchBar.HideFiltered,
                rightModlist);
        }
        finally
        {
            isApplyingSearchFilter = false;
        }

        if (ensureVisible)
        {
            EnsureFirstVisibleSearchResultInView(leftModlist, inactiveMods, leftFilter, leftSearchBar.HideFiltered);
            EnsureFirstVisibleSearchResultInView(rightModlist, activeMods, rightFilter, rightSearchBar.HideFiltered);
            ensureSearchResultVisibleOnNextFilter = false;
            TempSearchLog("ApplySearchFilter ensureSearchResultVisibleOnNextFilter consumed");
        }

        TempSearchLog("ApplySearchFilter end");
        TempLogVisualFilterState("ApplySearchFilter");
    }

    private void OnSearchInputChanged(object? sender, EventArgs e)
    {
        if (suppressSearchInputEvents)
        {
            TempSearchLog($"OnSearchInputChanged suppressed source={DescribeSearchSender(sender)}");
            return;
        }

        TempSearchLog($"OnSearchInputChanged source={DescribeSearchSender(sender)} left='{TrimForLog(leftSearchBar.Text)}' right='{TrimForLog(rightSearchBar.Text)}'");

        ScheduleSearchFilter();
    }

    private void OnHideFilteredChanged(object? sender, EventArgs e)
    {
        if (suppressSearchInputEvents)
        {
            TempSearchLog($"OnHideFilteredChanged suppressed source={DescribeSearchSender(sender)}");
            return;
        }

        TempSearchLog($"OnHideFilteredChanged source={DescribeSearchSender(sender)} leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");

        if (sender is ModSearchBar searchBar &&
            searchBar.HideFiltered &&
            !string.IsNullOrWhiteSpace(searchBar.Text))
        {
            ensureSearchResultVisibleOnNextFilter = true;
            TempSearchLog($"OnHideFilteredChanged scheduled ensure-visible source={DescribeSearchSender(sender)}");
        }

        ApplySearchFilterImmediately();
    }

    private void OnSearchModeChanged(object? sender, EventArgs e)
    {
        if (suppressSearchInputEvents)
        {
            TempSearchLog($"OnSearchModeChanged suppressed source={DescribeSearchSender(sender)}");
            return;
        }

        TempSearchLog(
            $"OnSearchModeChanged source={DescribeSearchSender(sender)} leftMode={DescribeSearchMode(leftSearchBar.SearchMode)} " +
            $"rightMode={DescribeSearchMode(rightSearchBar.SearchMode)}");

        if (sender is ModSearchBar searchBar &&
            searchBar.HideFiltered &&
            !string.IsNullOrWhiteSpace(searchBar.Text))
        {
            ensureSearchResultVisibleOnNextFilter = true;
            TempSearchLog($"OnSearchModeChanged scheduled ensure-visible source={DescribeSearchSender(sender)}");
        }

        ApplySearchFilterImmediately();
    }

    private void ApplyFilterFlags(
        ObservableCollection<ModRefViewModel> targetCollection,
        IEnumerable<DFHMod> sourceMods,
        string filter,
        SearchFilterMode searchMode,
        bool hideFiltered,
        ListBox list)
    {
        bool hasFilter = !string.IsNullOrWhiteSpace(filter);
        List<ModRefViewModel> ordered = new List<ModRefViewModel>();
        int total = 0;
        int visible = 0;
        int filteredOut = 0;
        foreach (DFHMod mod in sourceMods)
        {
            if (!modViewMap.TryGetValue(mod.ToString(), out ModRefViewModel? vm) || vm == null)
                continue;

            total++;
            bool match = !hasFilter || vm.MatchesFilter(filter, searchMode);

            vm.IsFilteredOut = hasFilter && !match;
            vm.IsVisible = !hideFiltered || match;
            if (vm.IsFilteredOut)
                filteredOut++;
            if (vm.IsVisible)
                visible++;
            if (!vm.IsVisible)
                vm.IsJumpHighlighted = false;

            ordered.Add(vm);
        }

        // Preserve source/default modlist order in all modes.
        // When hideFiltered is enabled, only matching items stay visible.
        List<ModRefViewModel> displayItems = hideFiltered
            ? ordered.Where(vm => vm.IsVisible).ToList()
            : ordered;

        ReplaceCollection(targetCollection, displayItems);

        TempSearchLog(
            $"ApplyFilterFlags list={DescribeList(list)} filter='{TrimForLog(filter)}' mode={DescribeSearchMode(searchMode)} hideFiltered={hideFiltered} total={total} visible={visible} filteredOut={filteredOut}");

        DropNonDisplayedSelections(list, displayItems);
    }

    private void DropNonDisplayedSelections(ListBox list, IReadOnlyCollection<ModRefViewModel> displayItems)
    {
        if (list.SelectedItems == null || list.SelectedItems.Count == 0)
            return;

        HashSet<ModRefViewModel> visibleSet = new HashSet<ModRefViewModel>(displayItems);
        int before = list.SelectedItems.Count;
        List<ModRefViewModel> retained = list.SelectedItems
            .OfType<ModRefViewModel>()
            .Where(vm => visibleSet.Contains(vm))
            .ToList();

        if (retained.Count == list.SelectedItems.Count)
            return;

        list.SelectedItems.Clear();
        foreach (ModRefViewModel vm in retained)
            list.SelectedItems.Add(vm);

        modListController.UpdateSelectionState(list);
        TempSearchLog($"DropNonDisplayedSelections list={DescribeList(list)} before={before} after={retained.Count}");
    }

    private void ScheduleSearchFilter()
    {
        if (searchDebounceTimer == null)
        {
            TempSearchLog("ScheduleSearchFilter no-debounce -> immediate");
            ApplySearchFilter();
            return;
        }

        TempSearchLog("ScheduleSearchFilter restart timer");
        searchDebounceTimer.Stop();
        searchDebounceTimer.Start();
    }

    private void ApplySearchFilterImmediately()
    {
        TempSearchLog("ApplySearchFilterImmediately");
        searchDebounceTimer?.Stop();
        ApplySearchFilter();
    }

    private static void ReplaceCollection(ObservableCollection<ModRefViewModel> target, List<ModRefViewModel> items)
    {
        if (target.Count == items.Count)
        {
            bool same = true;
            for (int i = 0; i < target.Count; i++)
            {
                if (!ReferenceEquals(target[i], items[i]))
                {
                    same = false;
                    break;
                }
            }

            if (same)
                return;
        }

        target.Clear();
        foreach (ModRefViewModel vm in items)
            target.Add(vm);
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
        IBrush warningTextBrush = new SolidColorBrush(style.modRefTextWarningColor.ToAvaloniaColor());

        Background = formBrush;
        leftHeaderLabel.Foreground = textBrush;
        rightHeaderLabel.Foreground = textBrush;
        modTitleLabel.Foreground = textBrush;
        modDescriptionLabel.Foreground = textBrush;
        buildVersionLabel.Foreground = textBrush;
        dfhackStatusLabel.Foreground = warningTextBrush;
        modInfoTopBorder.Background = new SolidColorBrush(style.backgroundColor.ToAvaloniaColor());

        leftModlist.Background = panelBrush;
        rightModlist.Background = panelBrush;

        bool isDarkTheme = manager.GetTheme() == 1;
        IBrush inputTextBrush = isDarkTheme ? Brushes.White : Brushes.Black;

        leftSearchBar.ApplyStyle(style);
        rightSearchBar.ApplyStyle(style);

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
            updateLogButton,
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

        foreach (ModRefViewModel vm in modViewMap.Values)
            vm.RefreshStyle();

        int theme = manager.GetTheme();
        if (themeComboBox != null && themeComboBox.SelectedIndex != theme)
            themeComboBox.SelectedIndex = theme;

        RequestedThemeVariant = theme == 0 ? ThemeVariant.Light : ThemeVariant.Dark;
        WindowThemeManager.ApplyToOpenWindows(style);
    }
    private void ModlistSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isApplyingSearchFilter || isBatchSelecting)
        {
            if (sender is ListBox filteredList)
                modListController.UpdateSelectionState(filteredList);
            return;
        }

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
        {
            TrackSelectedMod(selected);
            ShowModInfo(selected.ModReference);
        }
    }

    private void TrackSelectedMod(ModRefViewModel selected)
    {
        string id = selected.ModReference.ID?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (string.Equals(currentSelectedModId, id, StringComparison.OrdinalIgnoreCase))
            return;

        previousSelectedModId = currentSelectedModId;
        currentSelectedModId = id;
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

        ContextMenuCoordinator.Activate(menu);

        Control? placementControl = menu.PlacementTarget as Control;
        ModRefViewModel? vm =
            placementControl?.DataContext as ModRefViewModel ??
            menu.DataContext as ModRefViewModel ??
            menu.Items.OfType<MenuItem>()
                .Select(item => item.DataContext)
                .OfType<ModRefViewModel>()
                .FirstOrDefault() ??
            rightModlist.SelectedItems?.OfType<ModRefViewModel>().FirstOrDefault() ??
            leftModlist.SelectedItems?.OfType<ModRefViewModel>().FirstOrDefault();
        if (vm == null)
            return;

        ListBox? list = GetListForMod(vm);
        if (list == null)
        {
            if (rightModlist.SelectedItems?.OfType<ModRefViewModel>().Contains(vm) == true)
                list = rightModlist;
            else if (leftModlist.SelectedItems?.OfType<ModRefViewModel>().Contains(vm) == true)
                list = leftModlist;
            else if (rightModlist.SelectedItems?.Count > 0)
                list = rightModlist;
            else if (leftModlist.SelectedItems?.Count > 0)
                list = leftModlist;
        }

        if (list != null)
        {
            modListController.TryRestoreContextSelection(list, vm);

            if (list.SelectedItems == null || list.SelectedItems.Count == 0 || !list.SelectedItems.Contains(vm))
            {
                list.SelectedItems?.Clear();
                list.SelectedItems?.Add(vm);
            }
        }

        List<ModRefViewModel> selected = list?.SelectedItems?.Cast<ModRefViewModel>().ToList()
            ?? new List<ModRefViewModel>();
        ModContextMenuState state = ModContextMenuSupport.BuildState(
            manager,
            vm.ModReference,
            selected.Select(item => item.ModReference));
        ModContextMenuSupport.ApplyState(menu, state);
    }

    private async void ModContextDeleteMod(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
            return;

        await DeleteSelectedModsAsync(selection);
    }

    private async Task DeleteSelectedModsAsync(List<ModRefViewModel> selection)
    {
        if (selection == null || selection.Count == 0)
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

        string? targetAfterDeleteId = previousSelectedModId;
        if (!string.IsNullOrWhiteSpace(targetAfterDeleteId) &&
            selection.Any(vm => string.Equals(vm.ModReference.ID, targetAfterDeleteId, StringComparison.OrdinalIgnoreCase)))
        {
            targetAfterDeleteId = null;
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

        try
        {
            ReloadModpacksFromDisk();
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Reload failed");
            return;
        }

        if (!TrySelectModById(targetAfterDeleteId))
            ShowFallbackInfo();
    }

    private async void ModContextUnsubscribeSteam(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
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
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
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
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
            return;

        await ModContextMenuSupport.OpenFolderAsync(this, selection.First().ModReference);
    }

    private async void MainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Key == Key.Escape)
        {
            if (HandleEscapeKey(e.Source))
                e.Handled = true;
            return;
        }

        if (e.Key != Key.Delete || !CanHandleDeleteKeyFromSource(e.Source))
            return;

        List<ModRefViewModel> selection = GetSelectedModsForDeletion();
        if (selection.Count == 0)
            return;

        e.Handled = true;
        await DeleteSelectedModsAsync(selection);
    }

    private bool HandleEscapeKey(object? source)
    {
        bool handled = false;
        if ((leftModlist.SelectedItems?.Count ?? 0) > 0 || (rightModlist.SelectedItems?.Count ?? 0) > 0)
        {
            ShowFallbackInfo();
            handled = true;
        }

        if (leftSearchBar.ClearSearchSelection())
            handled = true;
        if (rightSearchBar.ClearSearchSelection())
            handled = true;

        if (source is Control control && control.FindAncestorOfType<ModSearchBar>() != null)
        {
            Focus();
            handled = true;
        }

        return handled;
    }

    private static bool CanHandleDeleteKeyFromSource(object? source)
    {
        if (source is not Control control)
            return true;

        return control.FindAncestorOfType<TextBox>() == null &&
               control.FindAncestorOfType<ComboBox>() == null;
    }

    private List<ModRefViewModel> GetSelectedModsForDeletion()
    {
        if (rightModlist.SelectedItems != null && rightModlist.SelectedItems.Count > 0)
            return rightModlist.SelectedItems.OfType<ModRefViewModel>().ToList();

        if (leftModlist.SelectedItems != null && leftModlist.SelectedItems.Count > 0)
            return leftModlist.SelectedItems.OfType<ModRefViewModel>().ToList();

        return new List<ModRefViewModel>();
    }

    private bool TrySelectModById(string? modId)
    {
        string targetId = modId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        ModRefViewModel? vm = modViewMap.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.ModReference.ID, targetId, StringComparison.OrdinalIgnoreCase));
        if (vm == null)
            return false;

        ListBox? list = GetListForMod(vm);
        if (list?.SelectedItems == null)
            return false;

        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();
        list.SelectedItems.Add(vm);
        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
        list.ScrollIntoView(vm);
        TrackSelectedMod(vm);
        ShowModInfo(vm.ModReference);
        return true;
    }

    private void ShowFallbackInfo()
    {
        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();
        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
        currentSelectedModId = null;
        previousSelectedModId = null;
        SetPreviewImage(LoadFallbackPreview());
        ShowFallbackHelpText();
    }

    private ModSelectionSnapshot CaptureSelectionSnapshot()
    {
        List<ModSelectionToken> rightTokens = CaptureSelectionTokens(rightModlist);
        if (rightTokens.Count > 0)
            return new ModSelectionSnapshot(false, rightTokens, currentSelectedModId, previousSelectedModId);

        List<ModSelectionToken> leftTokens = CaptureSelectionTokens(leftModlist);
        if (leftTokens.Count > 0)
            return new ModSelectionSnapshot(true, leftTokens, currentSelectedModId, previousSelectedModId);

        return new ModSelectionSnapshot(null, new List<ModSelectionToken>(), currentSelectedModId, previousSelectedModId);
    }

    private SearchFilterStateSnapshot CaptureSearchFilterStateSnapshot()
    {
        SearchFilterStateSnapshot snapshot = new SearchFilterStateSnapshot(
            leftSearchBar.Text ?? string.Empty,
            leftSearchBar.HideFiltered,
            leftSearchBar.SearchMode,
            rightSearchBar.Text ?? string.Empty,
            rightSearchBar.HideFiltered,
            rightSearchBar.SearchMode);
        TempSearchLog(
            $"CaptureSearchFilterStateSnapshot left='{TrimForLog(snapshot.LeftText)}' leftHide={snapshot.LeftHideFiltered} leftMode={DescribeSearchMode(snapshot.LeftMode)} " +
            $"right='{TrimForLog(snapshot.RightText)}' rightHide={snapshot.RightHideFiltered} rightMode={DescribeSearchMode(snapshot.RightMode)}");
        return snapshot;
    }

    private void RestoreSearchFilterStateSnapshot(SearchFilterStateSnapshot snapshot)
    {
        TempSearchLog(
            $"RestoreSearchFilterStateSnapshot begin left='{TrimForLog(snapshot.LeftText)}' leftHide={snapshot.LeftHideFiltered} leftMode={DescribeSearchMode(snapshot.LeftMode)} " +
            $"right='{TrimForLog(snapshot.RightText)}' rightHide={snapshot.RightHideFiltered} rightMode={DescribeSearchMode(snapshot.RightMode)}");
        suppressSearchInputEvents = true;
        searchDebounceTimer?.Stop();
        try
        {
            if (!string.Equals(leftSearchBar.Text, snapshot.LeftText, StringComparison.Ordinal))
                leftSearchBar.Text = snapshot.LeftText;
            if (leftSearchBar.HideFiltered != snapshot.LeftHideFiltered)
                leftSearchBar.HideFiltered = snapshot.LeftHideFiltered;
            if (leftSearchBar.SearchMode != snapshot.LeftMode)
                leftSearchBar.SearchMode = snapshot.LeftMode;

            if (!string.Equals(rightSearchBar.Text, snapshot.RightText, StringComparison.Ordinal))
                rightSearchBar.Text = snapshot.RightText;
            if (rightSearchBar.HideFiltered != snapshot.RightHideFiltered)
                rightSearchBar.HideFiltered = snapshot.RightHideFiltered;
            if (rightSearchBar.SearchMode != snapshot.RightMode)
                rightSearchBar.SearchMode = snapshot.RightMode;
        }
        finally
        {
            suppressSearchInputEvents = false;
        }
        TempSearchLog(
            $"RestoreSearchFilterStateSnapshot end left='{TrimForLog(leftSearchBar.Text)}' leftHide={leftSearchBar.HideFiltered} leftMode={DescribeSearchMode(leftSearchBar.SearchMode)} " +
            $"right='{TrimForLog(rightSearchBar.Text)}' rightHide={rightSearchBar.HideFiltered} rightMode={DescribeSearchMode(rightSearchBar.SearchMode)}");
    }

    private static List<ModSelectionToken> CaptureSelectionTokens(ListBox list)
    {
        if (list.SelectedItems == null || list.SelectedItems.Count == 0)
            return new List<ModSelectionToken>();

        List<ModSelectionToken> tokens = new List<ModSelectionToken>();
        HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModRefViewModel vm in list.SelectedItems.OfType<ModRefViewModel>())
        {
            string key = vm.DfMod.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!seenKeys.Add(key))
                continue;

            string id = vm.ModReference.ID?.Trim() ?? string.Empty;
            tokens.Add(new ModSelectionToken(key, id));
        }

        return tokens;
    }

    private void RestoreSelectionSnapshot(ModSelectionSnapshot snapshot)
    {
        if (snapshot.IsLeftList == null || snapshot.Tokens.Count == 0)
            return;

        List<ModRefViewModel> leftMatches = ResolveSelectionTokens(inactiveMods, snapshot.Tokens);
        List<ModRefViewModel> rightMatches = ResolveSelectionTokens(activeMods, snapshot.Tokens);
        bool preferLeft = snapshot.IsLeftList == true;

        bool useLeft;
        List<ModRefViewModel> restored;
        if (preferLeft)
        {
            useLeft = leftMatches.Count > 0 || rightMatches.Count == 0;
            restored = useLeft ? leftMatches : rightMatches;
        }
        else
        {
            useLeft = !(rightMatches.Count > 0 || leftMatches.Count == 0);
            restored = useLeft ? leftMatches : rightMatches;
        }

        ListBox targetList = useLeft ? leftModlist : rightModlist;
        if (HasActiveHideFilter(targetList))
            restored = restored.Where(vm => vm.IsVisible).ToList();

        if (restored.Count == 0)
        {
            ShowFallbackInfo();
            return;
        }

        string targetId = snapshot.CurrentSelectedId?.Trim() ?? string.Empty;
        ModRefViewModel primary = restored.FirstOrDefault(vm =>
            string.Equals(vm.ModReference.ID, targetId, StringComparison.OrdinalIgnoreCase)) ?? restored[0];

        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();
        targetList.SelectedItems?.Add(primary);
        foreach (ModRefViewModel vm in restored)
        {
            if (!ReferenceEquals(vm, primary))
                targetList.SelectedItems?.Add(vm);
        }

        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
        targetList.ScrollIntoView(primary);
        currentSelectedModId = primary.ModReference.ID?.Trim();
        previousSelectedModId = snapshot.PreviousSelectedId;
        ShowModInfo(primary.ModReference);
    }

    private bool HasActiveHideFilter(ListBox list)
    {
        if (list == leftModlist)
            return leftSearchBar.HideFiltered && !string.IsNullOrWhiteSpace(leftSearchBar.Text);
        if (list == rightModlist)
            return rightSearchBar.HideFiltered && !string.IsNullOrWhiteSpace(rightSearchBar.Text);
        return false;
    }

    private static List<ModRefViewModel> ResolveSelectionTokens(
        IEnumerable<ModRefViewModel> candidates,
        IReadOnlyList<ModSelectionToken> tokens)
    {
        Dictionary<string, ModRefViewModel> byKey = new Dictionary<string, ModRefViewModel>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ModRefViewModel> byId = new Dictionary<string, ModRefViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (ModRefViewModel vm in candidates)
        {
            string key = vm.DfMod.ToString();
            if (!string.IsNullOrWhiteSpace(key) && !byKey.ContainsKey(key))
                byKey[key] = vm;

            string id = vm.ModReference.ID?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(id) && !byId.ContainsKey(id))
                byId[id] = vm;
        }

        List<ModRefViewModel> restored = new List<ModRefViewModel>();
        HashSet<ModRefViewModel> seen = new HashSet<ModRefViewModel>();
        foreach (ModSelectionToken token in tokens)
        {
            ModRefViewModel? vm = null;
            if (!string.IsNullOrWhiteSpace(token.DfModKey))
                byKey.TryGetValue(token.DfModKey, out vm);

            if (vm == null && !string.IsNullOrWhiteSpace(token.ModId))
                byId.TryGetValue(token.ModId, out vm);

            if (vm == null || !seen.Add(vm))
                continue;

            restored.Add(vm);
        }

        return restored;
    }

    private readonly record struct ModSelectionToken(string DfModKey, string ModId);
    private readonly record struct ModSelectionSnapshot(
        bool? IsLeftList,
        List<ModSelectionToken> Tokens,
        string? CurrentSelectedId,
        string? PreviousSelectedId);
    private readonly record struct SearchFilterStateSnapshot(
        string LeftText,
        bool LeftHideFiltered,
        SearchFilterMode LeftMode,
        string RightText,
        bool RightHideFiltered,
        SearchFilterMode RightMode);

    private async void ModContextCopyId(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
            return;

        await ModContextMenuSupport.CopyModIdAsync(this, selection.First().ModReference);
    }

    private async void ModContextOpenSteam(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContextSelection(sender, out List<ModRefViewModel> selection, out _))
            return;

        await ModContextMenuSupport.OpenSteamPageAsync(this, selection.First().ModReference);
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

        IImage? previewImage = null;
        string? previewSvgPath = ResolveFilePathCaseInsensitive(modref.path, "preview.svg");
        if (!string.IsNullOrWhiteSpace(previewSvgPath))
            previewImage = ImageSourceLoader.LoadFromFilePath(previewSvgPath);

        if (previewImage == null)
        {
            string? previewPath = ResolveFilePathCaseInsensitive(modref.path, "preview.png");
            if (!string.IsNullOrWhiteSpace(previewPath))
                previewImage = ImageSourceLoader.LoadFromFilePath(previewPath);
        }

        SetPreviewImage(previewImage ?? LoadFallbackPreview());
    }

    private static string? ResolveFilePathCaseInsensitive(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return null;
        if (!Directory.Exists(directory))
            return null;

        string exactPath = Path.Combine(directory, fileName);
        if (File.Exists(exactPath))
            return exactPath;

        try
        {
            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(file =>
                    string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private IImage LoadFallbackPreview()
    {
        IImage? fallback = ImageSourceLoader.LoadFromAssetUri("43G6tag.png");
        if (fallback != null)
            return fallback;

        try
        {
            Uri uri = new Uri("avares://ModHearth/resources/43G6tag.png");
            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            // Linux CI can be case-sensitive on embedded resource paths.
        }

        try
        {
            Uri uri = new Uri("avares://ModHearth/Resources/43G6tag.png");
            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            // If fallback preview cannot be loaded, return a tiny placeholder image.
        }

        return new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1, 1));
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
        ModHearthManager.ModpackSaveResult result = manager.SaveCurrentModpack();
        SetAndMarkChanges(false);
        ShowModpackSaveNotice(result);
        await Task.CompletedTask;
    }

    private void ShowModpackSaveNotice(ModHearthManager.ModpackSaveResult result)
    {
        if (!DevMode.IsEnabled)
            ResetModManagerWatcher();

        if (string.IsNullOrWhiteSpace(result.LiveReloadMessage))
            return;

        if (result.UsesFallbackStorage || !result.LiveReloadApplied)
            ShowTransientStatusNotice(result.LiveReloadMessage);
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
        foreach (DFHMod mod in manager.modPool)
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

        SortRulesWindow dialog = new SortRulesWindow(
            manager.GetSortRules(),
            modRefs,
            manager.GetModsPath(),
            manager.GetVanillaModsPath(),
            manager.GetSortRulesPath(),
            rules => manager.SetSortRules(rules))
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        await dialog.ShowDialog(this);
    }

    private void OpenModUpdateLog()
    {
        ModUpdateLogWindow dialog = new ModUpdateLogWindow(manager)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _ = dialog.ShowDialog(this);
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
            UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
                this,
                manager.SelectedModlist.name,
                "reload modpacks");
            if (choice == UnsavedChangesChoice.Cancel)
                return;

            if (choice == UnsavedChangesChoice.Save)
                await SaveCurrentModpackAsync();
            else
                SetAndMarkChanges(false);
        }

        ReloadModpacksFromDisk();
    }

    private void ReloadButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(reloadButton).Properties.IsRightButtonPressed)
            return;

        e.Handled = true;
        EnsureReloadOptionsFlyout();
        LoadAutoReloadMenuFromConfig();
        reloadOptionsFlyout?.ShowAt(reloadButton);
    }

    private void ReloadModpacksFromDisk()
    {
        TempSearchLog("ReloadModpacksFromDisk begin");
        searchDebounceTimer?.Stop();
        ModSelectionSnapshot selectionSnapshot = CaptureSelectionSnapshot();
        SearchFilterStateSnapshot filterStateSnapshot = CaptureSearchFilterStateSnapshot();
        ensureSearchResultVisibleOnNextFilter = true;
        TempSearchLog("ReloadModpacksFromDisk scheduled ensure-visible on next filter");
        string? preferredName = manager.modpacks.Count > 0
            ? manager.SelectedModlist?.name
            : null;

        TempSearchLog("Refreshing modlists from disk.");
        try
        {
            manager.Initialize(preferredName);
            BuildModViewModels();
            UpdateDfHackStatus();
            if (!DevMode.IsEnabled)
                ResetModManagerWatcher();
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

        TempSearchLog("ReloadModpacksFromDisk restoring snapshot + refresh");
        RestoreSearchFilterStateSnapshot(filterStateSnapshot);
        manager.RefreshInstalledCacheModIds();
        RefreshModlistPanels();
        SetAndMarkChanges(false);
        RestoreSelectionSnapshot(selectionSnapshot);
        ApplySearchFilterImmediately();
        TempSearchLog("ReloadModpacksFromDisk end");

        if (!string.IsNullOrWhiteSpace(manager.LastMissingModsMessage))
            _ = DialogService.ShowMessageAsync(this, manager.LastMissingModsMessage, "Missing Mods");
    }

    private void InitializeAutoReloadTimer()
    {
        autoReloadTimer = new DispatcherTimer();
        autoReloadTimer.Tick += AutoReloadTimerTick;
        int configured = NormalizeAutoReloadIntervalSeconds(manager.GetAutoReloadIntervalSeconds());
        if (configured != manager.GetAutoReloadIntervalSeconds())
            manager.SetAutoReloadIntervalSeconds(configured);
        ConfigureAutoReloadTimer(configured);
    }

    private void InitializeDfHackStatusTimer()
    {
        dfHackStatusTimer = new DispatcherTimer
        {
            Interval = DfHackStatusRefreshInterval
        };
        dfHackStatusTimer.Tick += (_, _) => UpdateDfHackStatus();
    }

    private void StartDfHackStatusTimer()
    {
        if (dfHackStatusTimer == null)
            return;

        dfHackStatusTimer.Stop();
        dfHackStatusTimer.Start();
    }

    private void AutoReloadTimerTick(object? sender, EventArgs e)
    {
        if (changesMade || manager.IsSavingModpacks || modifyingComboBox)
            return;

        ReloadModpacksFromDisk();
    }

    private void EnsureReloadOptionsFlyout()
    {
        if (reloadOptionsFlyout != null)
            return;

        autoReloadEnabledCheckBox = new CheckBox
        {
            Content = "Enable Auto-Reload",
            IsChecked = false
        };
        autoReloadEnabledCheckBox.IsCheckedChanged += AutoReloadEnabledChanged;

        autoReloadSecondsTextBox = new TextBox
        {
            Width = 90,
            Watermark = "seconds"
        };
        autoReloadSecondsTextBox.TextInput += AutoReloadSecondsTextInput;
        autoReloadSecondsTextBox.TextChanged += AutoReloadSecondsTextChanged;
        autoReloadSecondsTextBox.LostFocus += AutoReloadSecondsLostFocus;

        TextBlock label = new TextBlock
        {
            Text = $"Every (seconds):",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        StackPanel secondsRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8
        };
        secondsRow.Children.Add(label);
        secondsRow.Children.Add(autoReloadSecondsTextBox);

        StackPanel panel = new StackPanel
        {
            Margin = new Thickness(10),
            Spacing = 8,
            MinWidth = 220
        };
        panel.Children.Add(autoReloadEnabledCheckBox);
        panel.Children.Add(secondsRow);

        reloadOptionsFlyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = panel
        };
    }

    private void AutoReloadSecondsTextInput(object? sender, TextInputEventArgs e)
    {
        string text = e.Text ?? string.Empty;
        if (text.All(char.IsDigit))
            return;
        e.Handled = true;
    }

    private void LoadAutoReloadMenuFromConfig()
    {
        if (autoReloadEnabledCheckBox == null || autoReloadSecondsTextBox == null)
            return;

        int configured = NormalizeAutoReloadIntervalSeconds(manager.GetAutoReloadIntervalSeconds());
        if (configured != manager.GetAutoReloadIntervalSeconds())
            manager.SetAutoReloadIntervalSeconds(configured);

        bool enabled = configured >= 0;
        suppressAutoReloadUiEvents = true;
        autoReloadEnabledCheckBox.IsChecked = enabled;
        autoReloadSecondsTextBox.Text = enabled ? configured.ToString() : MinimumAutoReloadSeconds.ToString();
        suppressAutoReloadUiEvents = false;
        UpdateAutoReloadInputState();
    }

    private void UpdateAutoReloadInputState()
    {
        if (autoReloadEnabledCheckBox == null || autoReloadSecondsTextBox == null)
            return;

        bool enabled = autoReloadEnabledCheckBox.IsChecked == true;
        autoReloadSecondsTextBox.IsEnabled = enabled;
        autoReloadSecondsTextBox.Opacity = enabled ? 1.0 : 0.6;
    }

    private void AutoReloadEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (autoReloadEnabledCheckBox == null || autoReloadSecondsTextBox == null)
            return;

        UpdateAutoReloadInputState();
        if (suppressAutoReloadUiEvents)
            return;

        bool enabled = autoReloadEnabledCheckBox.IsChecked == true;
        if (!enabled)
        {
            manager.SetAutoReloadIntervalSeconds(-1);
            ConfigureAutoReloadTimer(-1);
            return;
        }

        int seconds = ParseAutoReloadSeconds(autoReloadSecondsTextBox.Text);
        ApplyAutoReloadInterval(seconds, normalizeText: true);
    }

    private void AutoReloadSecondsTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (suppressAutoReloadUiEvents || autoReloadEnabledCheckBox?.IsChecked != true || autoReloadSecondsTextBox == null)
            return;

        if (!int.TryParse(autoReloadSecondsTextBox.Text, out int parsed))
            return;
        if (parsed < MinimumAutoReloadSeconds)
            return;

        ApplyAutoReloadInterval(parsed, normalizeText: false);
    }

    private void AutoReloadSecondsLostFocus(object? sender, RoutedEventArgs e)
    {
        if (suppressAutoReloadUiEvents || autoReloadEnabledCheckBox?.IsChecked != true || autoReloadSecondsTextBox == null)
            return;

        int seconds = ParseAutoReloadSeconds(autoReloadSecondsTextBox.Text);
        ApplyAutoReloadInterval(seconds, normalizeText: true);
    }

    private void ApplyAutoReloadInterval(int seconds, bool normalizeText)
    {
        if (autoReloadSecondsTextBox == null)
            return;

        int normalized = NormalizeAutoReloadIntervalSeconds(seconds);
        manager.SetAutoReloadIntervalSeconds(normalized);
        ConfigureAutoReloadTimer(normalized);

        if (!normalizeText)
            return;

        string normalizedText = normalized.ToString();
        if (string.Equals(autoReloadSecondsTextBox.Text, normalizedText, StringComparison.Ordinal))
            return;

        suppressAutoReloadUiEvents = true;
        autoReloadSecondsTextBox.Text = normalizedText;
        suppressAutoReloadUiEvents = false;
    }

    private void ConfigureAutoReloadTimer(int configValue)
    {
        if (autoReloadTimer == null)
            return;

        if (configValue > 0)
        {
            autoReloadTimer.Interval = TimeSpan.FromSeconds(configValue);
            autoReloadTimer.Start();
            return;
        }

        autoReloadTimer.Stop();
    }

    private static int ParseAutoReloadSeconds(string? text)
    {
        if (!int.TryParse(text, out int parsed))
            return MinimumAutoReloadSeconds;
        if (parsed < MinimumAutoReloadSeconds)
            return MinimumAutoReloadSeconds;
        return parsed;
    }

    private static int NormalizeAutoReloadIntervalSeconds(int value)
    {
        if (value < 0)
            return -1;
        if (value < MinimumAutoReloadSeconds)
            return MinimumAutoReloadSeconds;
        return value;
    }

    private void UpdateDfHackStatus()
    {
        if (dfhackStatusLabel == null)
            return;

        if (TryShowTransientStatusNotice())
            return;

        bool dfRunning = manager.DwarfFortressRunning();
        bool hasDfhack = manager.HasDfhack();

        if (dfRunning && hasDfhack && manager.ActiveModpackBackend == ModHearthManager.ModpackStorageBackend.DFHackConfig)
        {
            dfhackStatusLabel.IsVisible = false;
            dfhackStatusLabel.Text = string.Empty;
            return;
        }

        if (!hasDfhack)
            dfhackStatusLabel.Text = "DFHack not found";
        else if (!dfRunning)
            dfhackStatusLabel.Text = "Dwarf Fortress not running";
        string dfStatus = dfRunning ? "Dwarf Fortress running" : "Dwarf Fortress not running";
        string dfhStatus = hasDfhack ? "DFHack found" : "DFHack not found";

        if (!hasDfhack || !dfRunning)
        {
            dfhackStatusLabel.Text = $"{dfStatus}, {dfhStatus}";
        }
        else
            dfhackStatusLabel.Text = "Using local modpacks; DFHack file unavailable";

        dfhackStatusLabel.IsVisible = true;
    }

    private void ShowTransientStatusNotice(string message)
    {
        transientStatusNotice = message;
        transientStatusNoticeUntilUtc = DateTime.UtcNow.AddSeconds(6);
        TryShowTransientStatusNotice();
    }

    private bool TryShowTransientStatusNotice()
    {
        if (string.IsNullOrWhiteSpace(transientStatusNotice))
            return false;

        if (DateTime.UtcNow >= transientStatusNoticeUntilUtc)
        {
            transientStatusNotice = string.Empty;
            return false;
        }

        dfhackStatusLabel.Text = transientStatusNotice;
        dfhackStatusLabel.IsVisible = true;
        return true;
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
        ModHearthManager.ModpackSaveResult saveResult = manager.SaveAllModpacks();
        ShowModpackSaveNotice(saveResult);

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

        ModHearthManager.ModpackSaveResult saveResult = manager.SaveCurrentModpack();
        ShowModpackSaveNotice(saveResult);
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
        ModHearthManager.ModpackSaveResult saveResult = manager.SaveAllModpacks();
        ShowModpackSaveNotice(saveResult);

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
        ContextMenuCoordinator.DismissActive();

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
        UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
            this,
            manager.SelectedModlist.name,
            "switch modpacks");

        if (choice == UnsavedChangesChoice.Save)
            await SaveCurrentModpackAsync();
        else if (choice == UnsavedChangesChoice.ExitWithoutSaving)
            SetAndMarkChanges(false);
        else
        {
            modifyingComboBox = true;
            modpackComboBox.SelectedIndex = lastIndex;
            modifyingComboBox = false;
            return;
        }

        SetAndRefreshModpack(modpackComboBox.SelectedIndex);
        lastIndex = modpackComboBox.SelectedIndex;
    }

    private async void MainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (bypassUnsavedClosePrompt || !changesMade)
            return;

        e.Cancel = true;
        if (unsavedClosePromptInFlight)
            return;

        unsavedClosePromptInFlight = true;
        try
        {
            UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
                this,
                manager.SelectedModlist.name,
                "exit");
            if (choice == UnsavedChangesChoice.Cancel)
                return;

            if (choice == UnsavedChangesChoice.Save)
                await SaveCurrentModpackAsync();
            else
                SetAndMarkChanges(false);

            bypassUnsavedClosePrompt = true;
            Close();
        }
        finally
        {
            bypassUnsavedClosePrompt = false;
            unsavedClosePromptInFlight = false;
        }
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

    private void ResetModManagerWatcher()
    {
        modManagerWatcher?.Dispose();
        modManagerWatcher = null;
        modManagerReloadTimer?.Stop();
        modManagerReloadTimer = null;
        SetupModManagerWatcher();
    }

    private void SetupModManagerWatcher()
    {
        string modManagerPath = manager.GetActiveModpackPath();
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

    private static void TempSearchLog(string message)
    {
        if (!DevMode.IsEnabled)
            return;

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TEMP][SearchFlow] {message}");
    }

    private string DescribeSearchSender(object? sender)
    {
        if (ReferenceEquals(sender, leftSearchBar))
            return "leftSearchBar";
        if (ReferenceEquals(sender, rightSearchBar))
            return "rightSearchBar";
        return sender?.GetType().Name ?? "<null>";
    }

    private string DescribeList(ListBox list)
    {
        if (ReferenceEquals(list, leftModlist))
            return "leftModlist";
        if (ReferenceEquals(list, rightModlist))
            return "rightModlist";
        return list.Name ?? "<unnamedList>";
    }

    private static string TrimForLog(string? value)
    {
        string text = value ?? string.Empty;
        text = text.Replace("\r", "\\r").Replace("\n", "\\n");
        return text.Length <= 80 ? text : text[..80] + "...";
    }

    private static string DescribeSearchMode(SearchFilterMode mode)
    {
        return mode switch
        {
            SearchFilterMode.Name => "name",
            SearchFilterMode.Id => "id",
            SearchFilterMode.SteamFileId => "steam_file_id",
            _ => "name"
        };
    }

    private void EnsureFirstVisibleSearchResultInView(
        ListBox list,
        IEnumerable<ModRefViewModel> source,
        string filter,
        bool hideFiltered)
    {
        if (!hideFiltered || string.IsNullOrWhiteSpace(filter))
            return;

        ModRefViewModel? firstVisible = source.FirstOrDefault(vm => vm.IsVisible);
        if (firstVisible == null)
            return;

        list.ScrollIntoView(firstVisible);
        TempSearchLog(
            $"EnsureFirstVisibleSearchResultInView list={DescribeList(list)} targetId='{TrimForLog(firstVisible.ModReference.ID)}'");
    }

    private void TempLogVisualFilterState(string phase)
    {
        if (!DevMode.IsEnabled)
            return;

        TempLogListVisualState(phase, leftModlist, inactiveMods, leftSearchBar.Text, leftSearchBar.SearchMode, leftSearchBar.HideFiltered);
        TempLogListVisualState(phase, rightModlist, activeMods, rightSearchBar.Text, rightSearchBar.SearchMode, rightSearchBar.HideFiltered);
    }

    private void TempLogListVisualState(
        string phase,
        ListBox list,
        IEnumerable<ModRefViewModel> source,
        string filter,
        SearchFilterMode mode,
        bool hideFiltered)
    {
        int total = 0;
        int vmVisible = 0;
        foreach (ModRefViewModel vm in source)
        {
            total++;
            if (vm.IsVisible)
                vmVisible++;
        }

        List<ListBoxItem> realizedItems = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        int realizedTotal = realizedItems.Count;
        int realizedVisible = realizedItems.Count(item => item.IsVisible);
        int realizedHidden = realizedTotal - realizedVisible;

        TempSearchLog(
            $"VisualState phase={phase} list={DescribeList(list)} filter='{TrimForLog(filter)}' mode={DescribeSearchMode(mode)} hideFiltered={hideFiltered} " +
            $"vmVisible={vmVisible}/{total} realizedVisible={realizedVisible}/{realizedTotal} realizedHidden={realizedHidden}");
    }
}
