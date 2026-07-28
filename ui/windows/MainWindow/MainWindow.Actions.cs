using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public partial class MainWindow
{
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
            manager.GetModRelationshipRules(),
            modRefs,
            ModHearthManager.GetModRelationshipRulesPath(),
            rules =>
            {
                manager.SetModRelationshipRules(rules);
                manager.FindModlistProblems();
                RefreshModlistPanels();
            })
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

    private void InitializeDockingManagers()
    {
        bool initialDocked = ConfigManager.GetIsWorkshopDownloaderDocked();
        _workshopDockManager = new DockingManager<WorkshopDownloaderControl, WorkshopDownloaderWindow>(
            this,
            mainGrid,
            splitterColumnIndex: 4,
            contentColumnIndex: 5,
            workshopSplitter,
            workshopDockHost,
            workshopDockPreviewBorder,
            () =>
            {
                var ctrl = new WorkshopDownloaderControl(manager);
                ctrl.CloseRequested += (_, _) => _workshopDockManager?.Close();
                return ctrl;
            },
            control => new WorkshopDownloaderWindow(manager, control)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            },
            WorkshopDownloaderWindow.DefaultWidth,
            WorkshopDownloaderWindow.DefaultMinWidth,
            WorkshopDownloaderWindow.DefaultMaxWidth,
            splitterWidth: 7,
            initialDocked: initialDocked
        );
        _workshopDockManager.DockStateChanged += (_, _) =>
        {
            bool docked = _workshopDockManager.IsDocked;
            if (ConfigManager.GetIsWorkshopDownloaderDocked() != docked)
            {
                ConfigManager.SetIsWorkshopDownloaderDocked(docked);
                UpdateWorkshopDownloaderButtonModeImage();
            }
        };
        UpdateWorkshopDownloaderButtonModeImage();
    }

    private void OpenWorkshopDownloader()
    {
        _workshopDockManager?.Open();
    }

    private async Task ModSortAsync()
    {
        await manager.EnsureModRawDependencyCacheAsync();
        bool changed = await Task.Run(() => manager.ModSortEnabledMods());
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

        (bool success, string message) = await manager.RunDwarfFortressAsync();

        if (!success)
            await DialogService.ShowMessageAsync(this, message, "Launch Failed");
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
