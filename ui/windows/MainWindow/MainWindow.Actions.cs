using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

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

    private IReadOnlyDictionary<DockSide, DockingTarget> CreateDockSideTargets()
    {
        return new Dictionary<DockSide, DockingTarget>
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
    }



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

    private void InitializeDockingManagers()
    {
        var sideTargets = CreateDockSideTargets();

        bool workshopInitialDocked = ConfigManager.GetIsWorkshopDownloaderDocked();
        _workshopDockManager = new DockingManager<WorkshopDownloaderControl, WorkshopDownloaderWindow>(
            this,
            sideTargets,
            DockSide.Right,
            () =>
            {
                return new WorkshopDownloaderControl(manager);
            },
            control => new WorkshopDownloaderWindow(manager, control)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            },
            WorkshopDownloaderWindow.DefaultWidth,
            WorkshopDownloaderWindow.DefaultMinWidth,
            WorkshopDownloaderWindow.DefaultMaxWidth,
            splitterSize: 7,
            initialDocked: workshopInitialDocked,
            onSideAcquired: side => AcquireDockSide(side, _workshopDockManager!),
            proportionLoader: side => ConfigManager.GetDockSplitterProportion($"WorkshopDownloaderControl_{side}", 0.0),
            proportionSaver: (side, proportion) => ConfigManager.SetDockSplitterProportion($"WorkshopDownloaderControl_{side}", proportion),
            sideLoader: () => (DockSide)ConfigManager.GetDockSide("WorkshopDownloaderControl_Side", (int)DockSide.Right),
            sideSaver: side => ConfigManager.SetDockSide("WorkshopDownloaderControl_Side", (int)side)
        );
        _workshopDockManager.DockStateChanged += (_, _) =>
        {
            bool docked = _workshopDockManager.IsDocked;
            if (ConfigManager.GetIsWorkshopDownloaderDocked() != docked)
            {
                ConfigManager.SetIsWorkshopDownloaderDocked(docked);
                UpdateDockingButtonModeImages();
            }
        };

        _updateLogDockManager = new DockingManager<ModUpdateLogControl, ModUpdateLogWindow>(
            this,
            sideTargets,
            DockSide.Bottom,
            () =>
            {
                return new ModUpdateLogControl(manager);
            },
            control => new ModUpdateLogWindow(manager, control)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            },
            ModUpdateLogWindow.DefaultHeight,
            ModUpdateLogWindow.DefaultMinHeight,
            ModUpdateLogWindow.DefaultMaxHeight,
            splitterSize: 7,
            initialDocked: ConfigManager.GetIsModUpdateLogDocked(),
            onSideAcquired: side => AcquireDockSide(side, _updateLogDockManager!),
            proportionLoader: side => ConfigManager.GetDockSplitterProportion($"ModUpdateLogControl_{side}", 0.0),
            proportionSaver: (side, proportion) => ConfigManager.SetDockSplitterProportion($"ModUpdateLogControl_{side}", proportion),
            sideLoader: () => (DockSide)ConfigManager.GetDockSide("ModUpdateLogControl_Side", (int)DockSide.Bottom),
            sideSaver: side => ConfigManager.SetDockSide("ModUpdateLogControl_Side", (int)side)
        );

        _updateLogDockManager.DockStateChanged += (_, _) =>
        {
            bool docked = _updateLogDockManager.IsDocked;
            if (ConfigManager.GetIsModUpdateLogDocked() != docked)
            {
                ConfigManager.SetIsModUpdateLogDocked(docked);
                UpdateDockingButtonModeImages();
            }
        };

        bool sortRulesInitialDocked = ConfigManager.GetIsSortRulesDocked();
        _sortRulesDockManager = new DockingManager<SortRulesControl, SortRulesWindow>(
            this,
            sideTargets,
            DockSide.Left,
            () =>
            {
                List<ModReference> modRefs = manager.modPool
                    .Select(mod => manager.GetRefFromDFHMod(mod))
                    .Where(modref => modref != null && !string.IsNullOrWhiteSpace(modref.ID))
                    .ToList();

                var ctrl = new SortRulesControl(
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
            control => new SortRulesWindow(
                manager.GetModRelationshipRules(),
                manager.modPool.Select(mod => manager.GetRefFromDFHMod(mod)).Where(m => m != null && !string.IsNullOrWhiteSpace(m.ID)),
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
            SortRulesWindow.DefaultWidth,
            SortRulesWindow.DefaultMinWidth,
            SortRulesWindow.DefaultMaxWidth,
            splitterSize: 7,
            initialDocked: sortRulesInitialDocked,
            onSideAcquired: side => AcquireDockSide(side, _sortRulesDockManager!),
            proportionLoader: side => ConfigManager.GetDockSplitterProportion($"SortRulesControl_{side}", 0.0),
            proportionSaver: (side, proportion) => ConfigManager.SetDockSplitterProportion($"SortRulesControl_{side}", proportion),
            sideLoader: () => (DockSide)ConfigManager.GetDockSide("SortRulesControl_Side", (int)DockSide.Left),
            sideSaver: side => ConfigManager.SetDockSide("SortRulesControl_Side", (int)side)
        );

        _sortRulesDockManager.DockStateChanged += (_, _) =>
        {
            bool docked = _sortRulesDockManager.IsDocked;
            if (ConfigManager.GetIsSortRulesDocked() != docked)
            {
                ConfigManager.SetIsSortRulesDocked(docked);
                UpdateDockingButtonModeImages();
            }
        };

        InitializeDockingButtons();
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
            await SetAndMarkChangesAsync(true, skipSort: true);
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

        ConfigManager.DestroyConfig();
        RestartApplication();
    }

    private void RestartApplication()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath))
                _ = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
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
