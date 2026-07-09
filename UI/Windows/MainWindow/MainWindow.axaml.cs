
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void NotifyOfPropertyChange([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public bool IsAutoSaveEnabled
    {
        get => ConfigManager.IsAutoSaveEnabled();
        set
        {
            if (value != ConfigManager.IsAutoSaveEnabled())
            {
                ConfigManager.SetAutoSaveEnabled(value);
                NotifyOfPropertyChange();
            }
        }
    }

    private readonly ObservableCollection<ModRefViewModel> inactiveMods = new();
    private readonly ObservableCollection<ModRefViewModel> activeMods = new();
    private readonly Dictionary<string, ModRefViewModel> modViewMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ModListDragDropController modListController;
    private readonly ShortcutKeyHandler shortcutKeyHandler;

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
    private string? currentDescriptionBBCode;
    private (bool dfRunning, bool dfFound, bool hasDfhackExecutable, bool isDfHackRpcRunning, bool isDfHackInstalled) lastDfHackStatus;

    public MainWindow()
    {
        InitializeComponent();
        InitializeModInfoDock();
        saveButton.DataContext = this;
        SetWindowIcon();
        SetPreviewImage(LoadFallbackPreview());
        ShowFallbackHelpText();

        manager = new ModHearthManager();
        manager.RequestUIReload += () => Dispatcher.UIThread.Post(async () => await ReloadModpacksFromDisk());
        modListController = new ModListDragDropController(
            this,
            () => modViewMap.Values,
            key => modViewMap.TryGetValue(key, out ModRefViewModel? vm) ? vm : null,
            vm => vm.DfMod.ToString());
        modListController.Dropped += ModlistDropped;

        shortcutKeyHandler = new ShortcutKeyHandler(
            () => undoChangesButton.IsEnabled,
            () => UndoChangesAsync(),
            () => redoAvailable,
            () => RedoListChanges(),
            saveAsync: () => SaveCurrentModpackAsync());
        shortcutKeyHandler.Attach(this);

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
            SearchLogging.Log($"DebounceTick left='{TrimForLog(leftSearchBar.Text)}' right='{TrimForLog(rightSearchBar.Text)}' leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");
            ApplySearchFilter();
        };

        leftSearchBar.HideFiltered = true;
        rightSearchBar.HideFiltered = true;
        leftSearchBar.SearchTextChanged += OnSearchInputChanged;
        rightSearchBar.SearchTextChanged += OnSearchInputChanged;
        leftSearchBar.HideFilteredToggled += OnHideFilteredChanged;
        rightSearchBar.HideFilteredToggled += OnHideFilteredChanged;
        leftSearchBar.SearchModeChanged += OnSearchModeChanged;
        rightSearchBar.SearchModeChanged += OnSearchModeChanged;

        saveButton.Click += async (_, _) => await SaveCurrentModpackAsync();
        saveButton.AddHandler(InputElement.PointerPressedEvent, SaveButtonPointerPressed, RoutingStrategies.Tunnel, true);
        runDwarfFortressButton.Click += async (_, _) => await RunDwarfFortressAsync();
        undoChangesButton.Click += async (_, _) => await UndoChangesAsync();
        autoSortButton.Click += async (_, _) => await AutoSortAsync();
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
            SaveModDataPanelLayout();
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
}
