using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ModHearth.Config;
using ModHearth.Manager;
using ModHearth.Models;
using ModHearth.UI.Behaviors;
using ModHearth.UI.Docking;
using ModHearth.UI.Helpers;
using ModHearth.UI.ModList;
using ModHearth.UI.Theme;
using ModHearth.UI.ViewModels;
using ModHearth.Utils.Logging;

namespace ModHearth.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
    {
        public new event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void NotifyOfPropertyChange([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            NotifyOfPropertyChange(propertyName);
            return true;
        }

        /// <summary>Overall Steam workshop download progress (0-100) across the current run.</summary>
        public double WorkshopDownloadProgress
        {
            get;
            private set
            {
                // Ignore sub-percent jitter so the progress bar is not re-rendered for noise.
                if (Math.Abs(field - value) < 0.01)
                    return;
                field = value;
                NotifyOfPropertyChange();
            }
        }

        /// <summary>True while at least one Steam workshop download is in flight.</summary>
        public bool IsWorkshopDownloadActive
        {
            get;
            private set => SetField(ref field, value);
        }

        /// <summary>Number of Steam workshop downloads currently in flight.</summary>
        public int WorkshopActiveDownloadCount
        {
            get;
            private set => SetField(ref field, value);
        }

        /// <summary>Text shown next to the aggregate workshop progress bar.</summary>
        public string WorkshopDownloadStatusText
        {
            get;
            private set => SetField(ref field, value);
        } = string.Empty;

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

        public bool IsAutoReloadEnabled
        {
            get;
            set
            {
                if (field != value)
                {
                    field = value;
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
        private bool modifyingComboBox;
        private int lastIndex;
        private readonly ModListHistory modlistHistory = new();
        private DfhModpack? modlistHistoryOwner;
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
            // The drawer banner binds to MainWindow.IsNotificationLogPopulating.
            notificationPopulatingNotice.DataContext = this;
            AttachNotificationLogDiagnostics();
            toastHost.ItemsSource = toasts;
            WindowThemeManager.Register(this);
            InitializeModInfoDock();
            InitializeDockingManagers();
            saveButton.DataContext = this;
            sortButton.DataContext = this;
            reloadButton.DataContext = this;
            runDwarfFortressButton.DataContext = this;
            SetWindowIcon();
            SetPreviewImage(LoadFallbackPreview("modhearth_icon_v2.ico"));
            ShowFallbackHelpText();
            HorizontalScrollHelper.EnableSidewaysScrolling(dfhackStatusScrollViewer);
            HorizontalScrollHelper.EnableSidewaysScrolling(dfVersionScrollViewer);
            HorizontalScrollHelper.EnableSidewaysScrolling(dfhackVersionScrollViewer);

            manager = new ModHearthManager();
            manager.RequestUIReload += () => Dispatcher.UIThread.Post(async () => await ReloadModpacksFromDisk());
            manager.RequestNotification += (msg, icon) => ShowNotification(msg, icon);
            manager.WorkshopDownloads.Changed += OnWorkshopDownloadsChanged;
            modListController = new ModListDragDropController(this);
            modListController.Dropped += ModlistDropped;

            shortcutKeyHandler = new ShortcutKeyHandler(
                CanUndoModlistChanges,
                UndoChangesAsync,
                CanRedoModlistChanges,
                RedoListChangesAsync,
                saveAsync: SaveCurrentModpackAsync,
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
            ButtonHelpers.AttachRightClickHandler(runDwarfFortressButton, () =>
            {
                EnsureRunDwarfFortressOptionsFlyout();
                SyncAutoOpenNewWorldSetupCheckBox();
                runDwarfFortressOptionsFlyout?.ShowAt(runDwarfFortressButton);
            });
            undoChangesButton.Click += async (_, _) => await UndoChangesAsync();
            sortButton.Click += async (_, _) => await ModSortAsync();
            sortButton.AddHandler(PointerPressedEvent, SortButtonPointerPressed, RoutingStrategies.Tunnel, true);
            sortRulesButton.Click += async (_, _) => await OpenSortRulesAsync();
            installedModsMenuButton.Click += (s, e) =>
            {
                e.Handled = true;
                EnsureInstalledModsOptionsFlyout();
                installedModsOptionsFlyout?.ShowAt(installedModsMenuButton);
            };
            reloadButton.Click += async (_, _) => await ReloadModpacksAsync();
            ButtonHelpers.AttachRightClickHandler(reloadButton, () =>
            {
                EnsureReloadOptionsFlyout();
                LoadAutoReloadMenuFromConfig();
                reloadOptionsFlyout?.ShowAt(reloadButton);
            });

            newListButton.Click += async (_, _) => await CreateNewModpackAsync();
            renameListButton.Click += async (_, _) => await RenameModpackAsync();
            deleteListButton.Click += async (_, _) => await DeleteModpackAsync();
            importButton.Click += async (_, _) => await ImportModpackAsync();
            exportButton.Click += async (_, _) => await ExportModpackAsync();
            clearModlistButton.Click += async (_, _) => await ClearModlistAsync();

            warningIssuesButton.Click += (_, _) => JumpToNextProblem();
            ButtonHelpers.AttachRightClickHandler(warningIssuesButton, () =>
            {
                EnsureIssueSearchFlyout();
                issueSearchFlyout?.ShowAt(warningIssuesButton);
            });
            modIssuesOverlayLeft.JumpRequested += (_, tier) =>
                JumpToNextIssueTier(inactiveMods, leftModlist, tier, inactiveIssueCursors);
            modIssuesOverlayRight.JumpRequested += (_, tier) =>
                JumpToNextIssueTier(activeMods, rightModlist, tier, activeIssueCursors);
            modIssuesOverlayRight.RemoveRequested += async (_, tier) =>
                await RemoveIssueTierFromListAsync(activeMods, tier);
            settingsButton.Click += async (_, _) => await OpenSettingsDialogAsync();
            updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
            updateLogButton.Click += async (_, _) => await OpenModUpdateLog();
            workshopDownloaderButton.Click += async (_, _) => await OpenWorkshopDownloaderAsync();

            leftDockSplitterCloseButton.Click += (_, _) => CloseDockedWindowOnSide(DockSide.Left);
            rightDockSplitterCloseButton.Click += (_, _) => CloseDockedWindowOnSide(DockSide.Right);
            bottomDockSplitterCloseButton.Click += (_, _) => CloseDockedWindowOnSide(DockSide.Bottom);

            themeComboBox.ItemsSource = new[]
            {
                new ThemeOption("light theme", "themeLightIcon", BrushCache.GetBrush(Color.Parse("#000000"))),
                new ThemeOption("dark theme", "themeDarkIcon", BrushCache.GetBrush(Color.Parse("#ffffff")))
            };
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
                Dispose();
            };

            InitializeDfHackStatusTimer();
            InitializeAutoReloadTimer();
            InitializeRestartGuards();
        }

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _workshopDockManager?.Dispose();
                _updateLogDockManager?.Dispose();
                _sortRulesDockManager?.Dispose();
                modManagerWatcher?.Dispose();
                autoActionGate?.Dispose();
                dropProcessingGate?.Dispose();
                autoNewWorldCancellation.Cancel();

                modManagerReloadTimer?.Stop();
                dfHackStatusTimer?.Stop();
                autoReloadTimer?.Stop();
                searchDebounceTimer?.Stop();
                searchStateSaveTimer?.Stop();

                SetPreviewImage(null);
                ClearPreviewCache();
            }

            _disposed = true;
        }
    }
}
