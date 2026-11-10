using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using ModHearth.Models;
using ModHearth.UI.Dialogs;
using ModHearth.UI.Rendering;
using ModHearth.UI.Theme;
using ModHearth.UI.Update;
using ModHearth.UI.Windows.MainWindow;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        public const string AvaloniaUri = "avares://ModHearth/";
        private void ShowFallbackHelpText()
        {
            modTitleRun.Text = "Welcome to ModHearth!" + " ";
            currentDescriptionBBCode = null;
            modDescriptionPanelViewModel.DescriptionHtml = BBCodeRenderer.PlainTextToHtml(
                MainWindowHelpContent.GetHelpText(), GetDescriptionTextColor(), "transparent");
            buildVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
        }

        private void SetWindowIcon()
        {
            try
            {
                Uri iconUri = new($"{AvaloniaUri}/resources/modhearth_icon_v2.ico");
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
            BeginNotificationLogPopulation();
            try
            {
                await InitializeCoreAsync();
            }
            finally
            {
                EndNotificationLogPopulation();
            }
        }

        private async Task InitializeCoreAsync()
        {
            UpdateService.CheckPendingUpdateStatus(this);

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
                        bool didInitialize = await Task.Run(() =>
                        {
                            bool result = manager.Initialize();
                            if (result)
                                manager.RefreshInstalledCacheModIds();
                            return result;
                        });
                        if (!didInitialize)
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        await UpdateDfHackStatusAsync();
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

                await Task.Run(manager.RefreshRawAnalysisAsync);
            }
            else
            {
                try
                {
                    _ = await Task.Run(() => manager.Initialize());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEV] Initialization failed in dev mode: {ex}");
                }

                await Task.Run(manager.RefreshRawAnalysisAsync);
            }

            SetupModlistBox();
            try
            {
                ApplyStyle(StyleLoader.LoadStyle(false));
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageAsync(this, ex.Message, "Style load failed");
                Close();
                return;
            }

            BuildModViewModels();
            RefreshModlistPanels();
            buildVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
            _ = RefreshVersionLabelsAsync();
            InitializeUpdateChecking();
            _ = UpdateDfHackStatusAsync();
            StartDfHackStatusTimer();
            SetChangesMade(false);
            ResetModManagerWatcher();
        }

        private async Task<bool> EnsureConfigAsync()
        {
            while (true)
            {
                IReadOnlyList<ModHearthManager.ConfigIssue> issues = ConfigManager.GetConfigIssues();
                if (issues.Count == 0)
                    return true;
                foreach (ModHearthManager.ConfigIssue issue in issues)
                {
                    bool handled;
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
                        case ModHearthManager.ConfigIssueType.MissingDFHackPath:
                            handled = await PromptForDFHackFolderPathAsync();
                            if (!handled)
                                return false;
                            break;
                    }
                }
            }
        }

        private async Task<bool> PromptForDFHackFolderPathAsync()
        {
            await DialogService.ShowMessageAsync(this,
                "Please select the DFHack installation folder (containing dfhack-run).",
                "DFHack Folder Path");

            string? folder = await DialogService.PickFolderAsync(this, "Select DFHack folder");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                ConfigManager.SetDFHackFolderPath(folder);
                return true;
            }

            return false;
        }

        private async Task<bool> PromptForDwarfFortressPathAsync()
        {
            await DialogService.ShowMessageAsync(this,
                "Please select the Dwarf Fortress executable (df/df.exe) or the game folder.",
                "Dwarf Fortress Path");

            string? file = await DialogService.PickFileAsync(this, "Select Dwarf Fortress executable", GetExecutableFileTypes());
            if (!string.IsNullOrWhiteSpace(file))
            {
                ConfigManager.SetDwarfFortressExecutablePath(file);
                return true;
            }

            string? folder = await DialogService.PickFolderAsync(this, "Select Dwarf Fortress folder");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                ConfigManager.SetDwarfFortressFolderPath(folder);
                return true;
            }

            return false;
        }

        private async Task<bool> PromptForInstalledModsPathAsync()
        {
            string defaultPath = ConfigManager.GetInstalledModsPath();
            if (!string.IsNullOrWhiteSpace(defaultPath) && Directory.Exists(defaultPath))
            {
                ConfigManager.SetInstalledModsPath(defaultPath);
                return true;
            }

            await DialogService.ShowMessageAsync(this,
                "Please select your Dwarf Fortress installed_mods folder.",
                "installed_mods location");

            string? folder = await DialogService.PickFolderAsync(this, "Select installed_mods folder");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                ConfigManager.SetInstalledModsPath(folder);
                return true;
            }

            return false;
        }

        private static IEnumerable<FilePickerFileType> GetExecutableFileTypes()
        {
            if (OperatingSystem.IsWindows())
            {
                return
                [
                    new FilePickerFileType("Dwarf Fortress")
                    {
                        Patterns = ["*.exe"]
                    }
                ];
            }

            return [FilePickerFileTypes.All];
        }

        private void SetupModlistBox()
        {
            int index = manager.selectedModlistIndex;
            RefreshModpackCombo(index);
            lastIndex = index;
        }
    }
}
