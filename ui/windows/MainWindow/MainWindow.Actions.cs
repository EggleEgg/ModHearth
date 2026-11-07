using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ModHearth.Models;
using ModHearth.UI.Dialogs;
using ModHearth.UI.Docking;
using ModHearth.UI.Update;
using ModHearth.Utilities.Workshop;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private void CloseDockedWindowOnSide(DockSide side)
        {
            if (_workshopDockManager != null && _workshopDockManager.IsDocked && _workshopDockManager.ActiveSide == side)
            {
                _workshopDockManager.Close();
            }
            if (_updateLogDockManager != null && _updateLogDockManager.IsDocked && _updateLogDockManager.ActiveSide == side)
            {
                _updateLogDockManager.Close();
            }
            if (_sortRulesDockManager != null && _sortRulesDockManager.IsDocked && _sortRulesDockManager.ActiveSide == side)
            {
                _sortRulesDockManager.Close();
            }
        }

        private async Task OpenSortRulesAsync()
        {
            if (_sortRulesDockManager?.IsOpen == true && _sortRulesDockManager.IsDocked)
            {
                _sortRulesDockManager.Close();
                return;
            }

            _sortRulesDockManager?.Open();
            await Task.CompletedTask;
        }

        private async Task OpenModUpdateLog()
        {
            if (_updateLogDockManager?.IsOpen == true && _updateLogDockManager.IsDocked)
            {
                _updateLogDockManager.Close();
                return;
            }

            _updateLogDockManager?.Open();
            if (_updateLogDockManager?.SharedControl != null)
            {
                await _updateLogDockManager.SharedControl.LoadEntriesAsync();
            }
        }

        private Dictionary<DockSide, DockingTarget> CreateDockSideTargets() => new()
        {
            [DockSide.Left] = new DockingTarget
            {
                MainGrid = mainGrid,
                Side = DockSide.Left,
                SplitterIndex = 1,
                ContentIndex = 0,
                SplitterControl = leftDockSplitter,
                DockHostControl = leftDockHost,
                DockHostBorder = leftDockHostLine,
                PreviewBorder = leftDockPreviewBorder
            },
            [DockSide.Right] = new DockingTarget
            {
                MainGrid = mainGrid,
                Side = DockSide.Right,
                SplitterIndex = 6,
                ContentIndex = 7,
                SplitterControl = rightDockSplitter,
                DockHostControl = rightDockHost,
                DockHostBorder = rightDockHostLine,
                PreviewBorder = rightDockPreviewBorder
            },
            [DockSide.Bottom] = new DockingTarget
            {
                MainGrid = mainGrid,
                Side = DockSide.Bottom,
                SplitterIndex = 1,
                ContentIndex = 2,
                SplitterControl = bottomDockSplitter,
                DockHostControl = bottomDockHost,
                DockHostBorder = bottomDockHostLine,
                PreviewBorder = bottomDockPreviewBorder
            }
        };

        private void AcquireDockSide(DockSide side, object acquiringManager)
        {
            UndockIfOccupyingSide(_workshopDockManager, side, acquiringManager);
            UndockIfOccupyingSide(_updateLogDockManager, side, acquiringManager);
            UndockIfOccupyingSide(_sortRulesDockManager, side, acquiringManager);
        }

        private static void UndockIfOccupyingSide<TControl, TWindow>(
            DockingManager<TControl, TWindow>? manager,
            DockSide side,
            object acquiringManager)
            where TControl : UserControl
            where TWindow : Window
        {
            if (manager != null
                && !ReferenceEquals(manager, acquiringManager)
                && manager.IsDocked
                && manager.ActiveSide == side)
            {
                manager.ForceUndockForSideHandoff();
            }
        }

        private DockingManager<TControl, TWindow> CreateDockingManager<TControl, TWindow>(
            string configKeyPrefix,
            Func<bool> getInitialDocked,
            Action<bool> setInitialDocked,
            DockingManagerOptions<TControl, TWindow> options)
            where TControl : UserControl
            where TWindow : Window
        {
            DockingManager<TControl, TWindow>? managerRef = null;
            options.InitialDocked = getInitialDocked();
            options.OnSideAcquired = side => AcquireDockSide(side, managerRef!);
            options.ProportionLoader = side => ConfigManager.GetDockSplitterProportion($"{configKeyPrefix}_{side}", 0.0);
            options.ProportionSaver = (side, proportion) => ConfigManager.SetDockSplitterProportion($"{configKeyPrefix}_{side}", proportion);
            options.SideLoader = () => (DockSide)ConfigManager.GetDockSide($"{configKeyPrefix}_Side", (int)options.DefaultSide);
            options.SideSaver = side => ConfigManager.SetDockSide($"{configKeyPrefix}_Side", (int)side);

            DockingManager<TControl, TWindow> dockingManager = new(this, options);
            managerRef = dockingManager;

            dockingManager.DockStateChanged += (_, _) =>
            {
                bool docked = dockingManager.IsDocked;
                if (getInitialDocked() != docked)
                {
                    setInitialDocked(docked);
                    UpdateDockingButtonModeImages();
                }
            };

            return dockingManager;
        }

        private void InitializeDockingManagers()
        {
            IReadOnlyDictionary<DockSide, DockingTarget> sideTargets = CreateDockSideTargets();

            _workshopDockManager = CreateDockingManager(
                "WorkshopDownloaderControl",
                ConfigManager.GetIsWorkshopDownloaderDocked,
                ConfigManager.SetIsWorkshopDownloaderDocked,
                new DockingManagerOptions<WorkshopDownloaderControl, WorkshopDownloaderWindow>
                {
                    SideTargets = sideTargets,
                    DefaultSide = DockSide.Right,
                    ControlCreator = () => new WorkshopDownloaderControl(manager),
                    WindowCreator = control => new WorkshopDownloaderWindow(manager, control)
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    },
                    DefaultSize = WorkshopDownloaderWindow.DefaultWidth,
                    MinSize = WorkshopDownloaderWindow.DefaultMinWidth,
                    MaxSize = WorkshopDownloaderWindow.DefaultMaxWidth,
                    SplitterSize = 7
                }
            );

            _updateLogDockManager = CreateDockingManager(
                "ModUpdateLogControl",
                ConfigManager.GetIsModUpdateLogDocked,
                ConfigManager.SetIsModUpdateLogDocked,
                new DockingManagerOptions<ModUpdateLogControl, ModUpdateLogWindow>
                {
                    SideTargets = sideTargets,
                    DefaultSide = DockSide.Bottom,
                    ControlCreator = () => new ModUpdateLogControl(manager),
                    WindowCreator = control => new ModUpdateLogWindow(manager, control)
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    },
                    DefaultSize = ModUpdateLogWindow.DefaultHeight,
                    MinSize = ModUpdateLogWindow.DefaultMinHeight,
                    MaxSize = ModUpdateLogWindow.DefaultMaxHeight,
                    SplitterSize = 7
                }
            );

            _sortRulesDockManager = CreateDockingManager(
                "SortRulesControl",
                ConfigManager.GetIsSortRulesDocked,
                ConfigManager.SetIsSortRulesDocked,
                new DockingManagerOptions<SortRulesControl, SortRulesWindow>
                {
                    SideTargets = sideTargets,
                    DefaultSide = DockSide.Left,
                    ControlCreator = () =>
                    {
                        List<ModReference> modRefs = [.. manager.modPool
                            .Select(manager.FindModRef)
                            .OfType<ModReference>()
                            .Where(modref => !string.IsNullOrWhiteSpace(modref.ID))];

                        SortRulesControl ctrl = new(
                            manager.GetModRelationshipRules(),
                            modRefs,
                            ModHearthManager.GetModRelationshipRulesPath(),
                            rules =>
                            {
                                manager.SetModRelationshipRules(rules);
                                manager.FindModlistProblems();
                                RefreshModlistPanels();
                            });
                        ctrl.CloseRequested += (_, _) => _sortRulesDockManager?.Close();
                        return ctrl;
                    },
                    WindowCreator = control => new SortRulesWindow(
                        manager.GetModRelationshipRules(),
                        manager.modPool.Select(manager.FindModRef).OfType<ModReference>().Where(m => !string.IsNullOrWhiteSpace(m.ID)),
                        ModHearthManager.GetModRelationshipRulesPath(),
                        rules =>
                        {
                            manager.SetModRelationshipRules(rules);
                            manager.FindModlistProblems();
                            RefreshModlistPanels();
                        })
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    },
                    DefaultSize = SortRulesWindow.DefaultWidth,
                    MinSize = SortRulesWindow.DefaultMinWidth,
                    MaxSize = SortRulesWindow.DefaultMaxWidth,
                    SplitterSize = 7
                }
            );

            InitializeDockingButtons();
        }

        private void OnWorkshopDownloadsChanged(object? sender, EventArgs e)
        {
            if (sender is not WorkshopDownloadProgressTracker tracker)
                return;

            if (Dispatcher.UIThread.CheckAccess())
                ApplyWorkshopDownloadProgress(tracker);
            else
                Dispatcher.UIThread.Post(() => ApplyWorkshopDownloadProgress(tracker));
        }

        private void ApplyWorkshopDownloadProgress(WorkshopDownloadProgressTracker tracker)
        {
            WorkshopActiveDownloadCount = tracker.ActiveDownloadCount;
            WorkshopDownloadProgress = tracker.OverallProgress;
            WorkshopDownloadStatusText = tracker.StatusText;
            IsWorkshopDownloadActive = tracker.ActiveDownloadCount > 0;
        }

        private async Task OpenWorkshopDownloaderAsync()
        {
            if (_workshopDockManager?.IsOpen == true && _workshopDockManager.IsDocked)
            {
                _workshopDockManager.Close();
                return;
            }

            _workshopDockManager?.Open();
            if (_workshopDockManager?.SharedControl != null)
            {
                await _workshopDockManager.SharedControl.CheckProviderSetupAsync();
            }
        }

        private async Task ModSortAsync()
        {
            await manager.EnsureModRawDependencyCacheAsync();
            bool changed = await Task.Run(manager.ModSortEnabledMods);
            if (changed)
                _ = await SetAndMarkChangesAsync(true, skipSort: true);
            RefreshModlistPanels();
        }

        private async Task RunDwarfFortressAsync()
        {
            if (ModHearthManager.DwarfFortressRunning())
            {
                await DialogService.ShowMessageAsync(this, "Dwarf Fortress is already running.", "Already Running");
                return;
            }

            (bool success, bool isSteam, string message) = await ModHearthManager.RunDwarfFortressAsync();

            if (!success)
            {
                await DialogService.ShowMessageAsync(this, message, "Launch Failed");
            }
            else
            {
                if (isSteam)
                    dfSteamLaunched = true;
                else
                    dfLaunched = true;
            }
        }

        private async Task ClearInstalledModsAsync()
        {
            string installedModsPath = ConfigManager.GetInstalledModsPath();
            bool success = await DialogService.RunConfirmedActionAsync(this,
                $"Clear installed mods cache?\n{installedModsPath}",
                "Clear installed mods",
                () =>
                {
                    bool res = manager.ClearInstalledModsFolder(out string msg);
                    return (res, msg);
                },
                "Installed mods cleared",
                "Clear failed");

            clearInstalledModsButton.IsEnabled = Directory.Exists(installedModsPath);
            if (success)
            {
                await Task.Run(manager.RefreshInstalledCacheModIds);
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

        private void SaveButtonPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(saveButton).Properties.IsRightButtonPressed)
                return;

            e.Handled = true;
            IsAutoSaveEnabled = !IsAutoSaveEnabled;

            if (IsAutoSaveEnabled)
            {
                ShowNotification("Autosave enabled", "saveIcon.svg");
                if (changesMade)
                {
                    _ = SaveCurrentModpackAsync();
                }
            }
            else
            {
                ShowNotification("Autosave disabled", "saveCancelIcon.svg");
            }
        }

        private void SortButtonPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(sortButton).Properties.IsRightButtonPressed)
                return;

            e.Handled = true;
            IsAutoSortEnabled = !IsAutoSortEnabled;

            if (IsAutoSortEnabled)
            {
                ShowNotification("Autosort enabled", "sortBorderIcon.svg");
                _ = ModSortAsync();
            }
            else
            {
                ShowNotification("Autosort disabled", "sortCancelIcon.svg");
            }
        }

        private async Task RevealInstalledModsFolderAsync()
        {
            string installedModsPath = ConfigManager.GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath) || !Directory.Exists(installedModsPath))
            {
                await DialogService.ShowMessageAsync(this, "Installed mods folder not found.", "Open Folder");
                return;
            }

            try
            {
                using Process? process = Process.Start(new ProcessStartInfo
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

        private async Task RedoConfigAsync()
        {
            bool confirm = await DialogService.ShowConfirmAsync(this,
                "Are you sure you want to reset the config file? Application will restart.",
                "Redo Config");
            if (!confirm)
                return;

            // Destroy the config only after the restart guards approve, so cancelling the
            // unsaved-changes prompt leaves the existing config untouched.
            _ = await RestartApplicationAsync(() =>
            {
                ConfigManager.DestroyConfig();
                return Task.CompletedTask;
            });
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
}
