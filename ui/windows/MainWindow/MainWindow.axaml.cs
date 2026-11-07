using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ModHearth.Models;
using ModHearth.UI.Behaviors;
using ModHearth.UI.Docking;
using ModHearth.UI.ModList;
using ModHearth.UI.Theme;
using ModHearth.UI.ViewModels;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public new event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void NotifyOfPropertyChange([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private double _workshopDownloadProgress;
        /// <summary>Overall Steam workshop download progress (0-100) across the current run.</summary>
        public double WorkshopDownloadProgress
        {
            get => _workshopDownloadProgress;
            private set
            {
                if (Math.Abs(_workshopDownloadProgress - value) < 0.01)
                    return;
                _workshopDownloadProgress = value;
                NotifyOfPropertyChange();
            }
        }

        private bool _isWorkshopDownloadActive;
        /// <summary>True while at least one Steam workshop download is in flight.</summary>
        public bool IsWorkshopDownloadActive
        {
            get => _isWorkshopDownloadActive;
            private set
            {
                if (_isWorkshopDownloadActive == value)
                    return;
                _isWorkshopDownloadActive = value;
                NotifyOfPropertyChange();
            }
        }

        private int _workshopActiveDownloadCount;
        /// <summary>Number of Steam workshop downloads currently in flight.</summary>
        public int WorkshopActiveDownloadCount
        {
            get => _workshopActiveDownloadCount;
            private set
            {
                if (_workshopActiveDownloadCount == value)
                    return;
                _workshopActiveDownloadCount = value;
                NotifyOfPropertyChange();
            }
        }

        private string _workshopDownloadStatusText = string.Empty;
        /// <summary>Text shown next to the aggregate workshop progress bar.</summary>
        public string WorkshopDownloadStatusText
        {
            get => _workshopDownloadStatusText;
            private set
            {
                if (string.Equals(_workshopDownloadStatusText, value, StringComparison.Ordinal))
                    return;
                _workshopDownloadStatusText = value;
                NotifyOfPropertyChange();
            }
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

        private readonly BulkObservableCollection<ModRefViewModel> inactiveMods = [];
        private readonly BulkObservableCollection<ModRefViewModel> activeMods = [];
        private readonly Dictionary<string, ModRefViewModel> modViewMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly ModListDragDropController modListController;
        private readonly ShortcutKeyHandler shortcutKeyHandler;
        private readonly SemaphoreSlim autoActionGate = new(1, 1); // lives with SetAndMarkChangesAsync's other state
        private readonly SemaphoreSlim dropProcessingGate = new(1, 1); // serializes drop tails so a later drop's scan can't be overwritten by an earlier one
        private bool autoActionRerunRequested;

        private DockingManager<WorkshopDownloaderControl, WorkshopDownloaderWindow>? _workshopDockManager;
        private DockingManager<ModUpdateLogControl, ModUpdateLogWindow>? _updateLogDockManager;
        private DockingManager<SortRulesControl, SortRulesWindow>? _sortRulesDockManager;
        private readonly ModHearthManager manager;
        private bool changesMade;
        private bool isBatchSelecting;
        private bool changesMarked;
        private bool redoAvailable;
        private bool isRedoing;
        private bool modifyingComboBox;
        private int lastIndex;
        private List<DfhMod> redoMods = [];
        private List<DfhMod> problemMods = [];
        private int problemModIndex;
        private List<DfhMod> duplicateWarningMods = [];
        private int duplicateWarningIndex;

        private DispatcherTimer? modManagerReloadTimer;
        private FileSystemWatcher? modManagerWatcher;
        private DispatcherTimer? dfHackStatusTimer;
        private DispatcherTimer? autoReloadTimer;
        private readonly DispatcherTimer? searchDebounceTimer;
        private readonly DispatcherTimer? searchStateSaveTimer;
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
            previewCache = new(MaxCachedPreviews, StringComparer.OrdinalIgnoreCase);
            NotificationItemsControl.ItemsSource = notificationRecords;
            toastHost.ItemsSource = toasts;
            WindowThemeManager.Register(this);
            InitializeModInfoDock();
            InitializeDockingManagers();
            saveButton.DataContext = this;
            sortButton.DataContext = this;
            reloadButton.DataContext = this;
            SetWindowIcon();
            SetPreviewImage(LoadFallbackPreview("modhearth_icon_v2.ico"));
            ShowFallbackHelpText();
            HorizontalScrollHelper.EnableSidewaysScrolling(dfhackStatusScrollViewer);

            manager = new ModHearthManager();
            manager.RequestUIReload += () => Dispatcher.UIThread.Post(async () => await ReloadModpacksFromDisk());
            manager.RequestNotification += (msg, icon) => ShowNotification(msg, icon);
            manager.WorkshopDownloads.Changed += OnWorkshopDownloadsChanged;
            modListController = new ModListDragDropController(this);
            modListController.Dropped += ModlistDropped;

            shortcutKeyHandler = new ShortcutKeyHandler(
                () => undoChangesButton.IsEnabled,
                UndoChangesAsync,
                () => redoAvailable,
                RedoListChangesAsync,
                saveAsync: () => SaveCurrentModpackAsync(),
                moveLeftAsync: () => MoveSelectedBetweenListsAsync(false),
                moveRightAsync: () => MoveSelectedBetweenListsAsync(true));
            shortcutKeyHandler.Attach(this);

            leftModlist.ItemsSource = inactiveMods;
            rightModlist.ItemsSource = activeMods;
            modListController.RegisterList(leftModlist, allowReorder: false);
            modListController.RegisterList(rightModlist, allowReorder: true);

            double mainRatio = ConfigManager.GetMainWindowGridSplitterRatio();
            if (mainRatio > 0 && mainRatio < 1 && mainGrid != null && mainGrid.ColumnDefinitions.Count >= 5)
            {
                //Change these if MainWindow layouts change!
                mainGrid.ColumnDefinitions[2].Width = new GridLength(mainRatio, GridUnitType.Star);
                mainGrid.ColumnDefinitions[4].Width = new GridLength(1.0 - mainRatio, GridUnitType.Star);
            }

            leftModlist.SelectionChanged += ModlistSelectionChanged;
            rightModlist.SelectionChanged += ModlistSelectionChanged;
            leftModlist.DoubleTapped += async (_, _) => await MoveSelectedBetweenListsAsync(true);
            rightModlist.DoubleTapped += async (_, _) => await MoveSelectedBetweenListsAsync(false);
            AddHandler(PointerPressedEvent, WindowPointerPressed, RoutingStrategies.Tunnel, true);
            KeyDown += MainWindowKeyDown;

            searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(140)
            };
            searchDebounceTimer.Tick += (_, _) =>
            {
                searchDebounceTimer?.Stop();
                Loggers.Search.Dev($"DebounceTick left='{TrimForLog(leftSearchBar.Text)}' right='{TrimForLog(rightSearchBar.Text)}' leftHide={leftSearchBar.HideFiltered} rightHide={rightSearchBar.HideFiltered}");
                ApplySearchFilter();
            };

            searchStateSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            searchStateSaveTimer.Tick += (_, _) =>
            {
                searchStateSaveTimer?.Stop();
                SaveSearchBarStates();
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

            if (notificationSearchBar != null)
            {
                notificationSearchBar.SearchMode = SearchFilterMode.ModifiedTime;
                notificationSearchBar.SortDescending = true;
                notificationSearchBar.SearchTextChanged += (_, _) => ApplyNotificationFilterAndSort();
                notificationSearchBar.SearchModeChanged += (_, _) => ApplyNotificationFilterAndSort();
                notificationSearchBar.SortOrderChanged += (_, _) => ApplyNotificationFilterAndSort();
                notificationSearchBar.HideFilteredToggled += (_, _) => ApplyNotificationFilterAndSort();
            }

            saveButton.Click += async (_, _) => await SaveCurrentModpackAsync();
            saveButton.AddHandler(PointerPressedEvent, SaveButtonPointerPressed, RoutingStrategies.Tunnel, true);
            runDwarfFortressButton.Click += async (_, _) => await RunDwarfFortressAsync();
            undoChangesButton.Click += async (_, _) => await UndoChangesAsync();
            sortButton.Click += async (_, _) => await ModSortAsync();
            sortButton.AddHandler(PointerPressedEvent, SortButtonPointerPressed, RoutingStrategies.Tunnel, true);
            sortRulesButton.Click += async (_, _) => await OpenSortRulesAsync();
            clearInstalledModsButton.Click += async (_, _) => await ClearInstalledModsAsync();
            clearInstalledModsButton.AddHandler(PointerPressedEvent, ClearInstalledModsPointerPressed, RoutingStrategies.Tunnel, true);
            reloadButton.Click += async (_, _) => await ReloadModpacksAsync();
            reloadButton.AddHandler(PointerPressedEvent, ReloadButtonPointerPressed, RoutingStrategies.Tunnel, true);

            newListButton.Click += async (_, _) => await CreateNewModpackAsync();
            renameListButton.Click += async (_, _) => await RenameModpackAsync();
            deleteListButton.Click += async (_, _) => await DeleteModpackAsync();
            importButton.Click += async (_, _) => await ImportModpackAsync();
            exportButton.Click += async (_, _) => await ExportModpackAsync();
            clearModlistButton.Click += async (_, _) => await ClearModlistAsync();

            warningIssuesButton.Click += (_, _) => JumpToNextProblem();
            modIssuesOverlayLeft.JumpRequested += (_, tier) =>
                JumpToNextIssueTier(inactiveMods, leftModlist, tier, inactiveIssueCursors);
            modIssuesOverlayRight.JumpRequested += (_, tier) =>
                JumpToNextIssueTier(activeMods, rightModlist, tier, activeIssueCursors);
            redoConfigButton.Click += async (_, _) => await RedoConfigAsync();
            updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
            updateLogButton.Click += async (_, _) => await OpenModUpdateLog();
            workshopDownloaderButton.Click += async (_, _) => await OpenWorkshopDownloaderAsync();

            leftDockSplitterCloseButton.Click += (_, _) => CloseDockedWindowOnSide(DockSide.Left);
            rightDockSplitterCloseButton.Click += (_, _) => CloseDockedWindowOnSide(DockSide.Right);
            bottomDockSplitterCloseButton.Click += (_, _) => CloseDockedWindowOnSide(DockSide.Bottom);

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
                manager.WorkshopDownloads.Changed -= OnWorkshopDownloadsChanged;
                _workshopDockManager?.Dispose();
                _updateLogDockManager?.Dispose();
                _sortRulesDockManager?.Dispose();
                modManagerWatcher?.Dispose();
                modManagerReloadTimer?.Stop();
                dfHackStatusTimer?.Stop();
                autoReloadTimer?.Stop();
                searchDebounceTimer?.Stop();
                searchStateSaveTimer?.Stop();
                SetPreviewImage(null);
                ClearPreviewCache();
            };

            InitializeDfHackStatusTimer();
            InitializeAutoReloadTimer();
        }
    }
}
