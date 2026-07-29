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
using System.Collections.Generic;
using System.Linq;
using ModHearth.Models;
using ModHearth.Utilities;
using ModHearth.UI;

namespace ModHearth.UI;

public partial class MainWindow : Window, INotifyPropertyChanged, IStyleAwareWindow
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

    public bool IsAutoSortEnabled
    {
        get => ConfigManager.IsAutoSortEnabled();
        set
        {
            if (value != ConfigManager.IsAutoSortEnabled())
            {
                ConfigManager.SetAutoSortEnabled(value);
                NotifyOfPropertyChange();
            }
        }
    }

    private bool _isAutoReloadEnabled;
    public bool IsAutoReloadEnabled
    {
        get => _isAutoReloadEnabled;
        set
        {
            if (_isAutoReloadEnabled != value)
            {
                _isAutoReloadEnabled = value;
                NotifyOfPropertyChange();
            }
        }
    }

    public bool IsOpenSteamInClientEnabled
    {
        get => ConfigManager.GetOpenSteamInClient();
        set
        {
            if (value != ConfigManager.GetOpenSteamInClient())
            {
                ConfigManager.SetOpenSteamInClient(value);
                NotifyOfPropertyChange();
            }
        }
    }

    public bool IsOpenSteamFolderEnabled
    {
        get => ConfigManager.GetOpenSteamFolder();
        set
        {
            if (value != ConfigManager.GetOpenSteamFolder())
            {
                ConfigManager.SetOpenSteamFolder(value);
                NotifyOfPropertyChange();
            }
        }
    }

    public bool IsCopySteamFileIdEnabled
    {
        get => ConfigManager.GetCopySteamFileId();
        set
        {
            if (value != ConfigManager.GetCopySteamFileId())
            {
                ConfigManager.SetCopySteamFileId(value);
                NotifyOfPropertyChange();
            }
        }
    }

    private readonly ObservableCollection<ModRefViewModel> inactiveMods = new();
    private readonly ObservableCollection<ModRefViewModel> activeMods = new();
    private readonly Dictionary<string, ModRefViewModel> modViewMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ModListDragDropController modListController;
    private readonly ShortcutKeyHandler shortcutKeyHandler;
    private readonly SemaphoreSlim autoActionGate = new SemaphoreSlim(1, 1); // lives with SetAndMarkChangesAsync's other state
    private bool autoActionRerunRequested;

    private DockingManager<WorkshopDownloaderControl, WorkshopDownloaderWindow>? _workshopDockManager;
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
    private bool pendingReloadQueued;
    private string? currentSelectedModId;
    private string? previousSelectedModId;
    private string? currentDescriptionBBCode;
    private (bool dfRunning, bool dfFound, bool hasDfhackExecutable, bool isDfHackRpcRunning, bool isDfHackInstalled) lastDfHackStatus;

    public MainWindow()
    {
        InitializeComponent();
        WindowThemeManager.Register(this);
        InitializeModInfoDock();
        InitializeDockingManagers();
        saveButton.DataContext = this;
        sortButton.DataContext = this;
        reloadButton.DataContext = this;
        SetWindowIcon();
        SetPreviewImage(LoadFallbackPreview("modhearth_icon_v2.ico"));
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
            () => RedoListChangesAsync(),
            saveAsync: () => SaveCurrentModpackAsync(),
            moveLeftAsync: () => MoveSelectedBetweenListsAsync(false),
            moveRightAsync: () => MoveSelectedBetweenListsAsync(true));
        shortcutKeyHandler.Attach(this);

        leftModlist.ItemsSource = inactiveMods;
        rightModlist.ItemsSource = activeMods;
        modListController.RegisterList(leftModlist, allowReorder: false);
        modListController.RegisterList(rightModlist, allowReorder: true);

        double mainRatio = ConfigManager.GetMainWindowGridSplitterRatio();
        if (mainRatio > 0 && mainRatio < 1 && mainGrid != null && mainGrid.ColumnDefinitions.Count >= 3)
        {
            mainGrid.ColumnDefinitions[0].Width = new GridLength(mainRatio, GridUnitType.Star);
            mainGrid.ColumnDefinitions[2].Width = new GridLength(1.0 - mainRatio, GridUnitType.Star);
        }

        leftModlist.SelectionChanged += ModlistSelectionChanged;
        rightModlist.SelectionChanged += ModlistSelectionChanged;
        leftModlist.DoubleTapped += async (_, _) => await MoveSelectedBetweenListsAsync(true);
        rightModlist.DoubleTapped += async (_, _) => await MoveSelectedBetweenListsAsync(false);
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

        leftSearchBar.SetStringState(ConfigManager.GetLeftSearchBarState());
        rightSearchBar.SetStringState(ConfigManager.GetRightSearchBarState());

        leftSearchBar.SearchTextChanged += (_, _) => OnSearchBarStateChanged();
        leftSearchBar.HideFilteredToggled += (_, _) => OnSearchBarStateChanged();
        leftSearchBar.SearchModeChanged += (_, _) => OnSearchBarStateChanged();
        leftSearchBar.SortOrderChanged += (_, _) => OnSearchBarStateChanged();

        rightSearchBar.SearchTextChanged += (_, _) => OnSearchBarStateChanged();
        rightSearchBar.HideFilteredToggled += (_, _) => OnSearchBarStateChanged();
        rightSearchBar.SearchModeChanged += (_, _) => OnSearchBarStateChanged();
        rightSearchBar.SortOrderChanged += (_, _) => OnSearchBarStateChanged();

        leftSearchBar.HideFiltered = true;
        rightSearchBar.HideFiltered = true;
        rightSearchBar.IsSortingEnabled = false;
        leftSearchBar.SearchTextChanged += OnSearchInputChanged;
        rightSearchBar.SearchTextChanged += OnSearchInputChanged;
        leftSearchBar.HideFilteredToggled += OnHideFilteredChanged;
        rightSearchBar.HideFilteredToggled += OnHideFilteredChanged;
        leftSearchBar.SearchModeChanged += OnSearchModeChanged;
        rightSearchBar.SearchModeChanged += OnSearchModeChanged;
        leftSearchBar.SortOrderChanged += OnSearchModeChanged;
        rightSearchBar.SortOrderChanged += OnSearchModeChanged;



        saveButton.Click += async (_, _) => await SaveCurrentModpackAsync();
        saveButton.AddHandler(InputElement.PointerPressedEvent, SaveButtonPointerPressed, RoutingStrategies.Tunnel, true);
        runDwarfFortressButton.Click += async (_, _) => await RunDwarfFortressAsync();
        undoChangesButton.Click += async (_, _) => await UndoChangesAsync();
        sortButton.Click += async (_, _) => await ModSortAsync();
        sortButton.AddHandler(InputElement.PointerPressedEvent, SortButtonPointerPressed, RoutingStrategies.Tunnel, true);
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
        clearModlistButton.Click += async (_, _) => await ClearModlistAsync();

        warningIssuesButton.Click += (_, _) => JumpToNextProblem();
        redoConfigButton.Click += async (_, _) => await RedoConfigAsync();
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        updateLogButton.Click += (_, _) => OpenModUpdateLog();
        workshopDownloaderButton.Click += (_, _) => OpenWorkshopDownloader();
        workshopDownloaderButton.AddHandler(InputElement.PointerPressedEvent, WorkshopDownloaderButtonPointerPressed, RoutingStrategies.Tunnel, true);

        themeComboBox.ItemsSource = new[] { "light theme", "dark theme" };
        themeComboBox.SelectionChanged += async (_, _) => await OnThemeChangedAsync();

        modpackComboBox.SelectionChanged += (_, _) => OnModpackChanged();
        Opened += async (_, _) => await InitializeAsync();
        Closing += MainWindowClosing;
        Closed += (_, _) =>
        {
            SaveMainWindowGridSplitterRatio();
            SaveModInfoDockLayout();
            SaveSearchBarStates();
            _workshopDockManager?.Close();
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

    private void SaveMainWindowGridSplitterRatio()
    {
        if (mainGrid != null && mainGrid.ColumnDefinitions.Count >= 3)
        {
            double w0 = mainGrid.ColumnDefinitions[0].ActualWidth;
            double w2 = mainGrid.ColumnDefinitions[2].ActualWidth;
            if (w0 + w2 > 0)
            {
                double ratio = w0 / (w0 + w2);
                ratio = Math.Clamp(ratio, 0.05, 0.95);
                ConfigManager.SetMainWindowGridSplitterRatio(ratio);
            }
        }
    }

    private void OnSearchBarStateChanged()
    {
        SaveSearchBarStates();
    }

    private void SaveSearchBarStates()
    {
        ConfigManager.SetLeftSearchBarState(leftSearchBar.GetStringState());
        ConfigManager.SetRightSearchBarState(rightSearchBar.GetStringState());
    }
}
