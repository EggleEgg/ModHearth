using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using ModHearth.Utilities;

namespace ModHearth.UI;

public partial class MainWindow
{
    public const string AvaloniaUri = "avares://ModHearth/";
    private void ShowFallbackHelpText()
    {
        modTitleLabel.Text = "Welcome to ModHearth!";
        currentDescriptionBBCode = null;
        modDescriptionHtml.Text = BBCodeRenderer.PlainTextToHtml(
            MainWindowHelpContent.GetHelpText(), GetDescriptionTextColor(), "transparent");
        buildVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
    }

    private void SetWindowIcon()
    {
        try
        {
            Uri iconUri = new Uri($"{AvaloniaUri}/resources/modhearth_icon_v1.ico");
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

            await manager.EnsureModRawDependencyCacheAsync();
        }
        else
        {
            try
            {
                await Task.Run(() => manager.Initialize());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEV] Initialization failed in dev mode: {ex}");
            }

            await manager.EnsureModRawDependencyCacheAsync();
        }

        SetupModlistBox();
        try
        {
            ApplyStyle(ConfigManager.LoadStyle(false));
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Style load failed");
            Close();
            return;
        }

        BuildModViewModels();
        RefreshModlistPanels();
        clearInstalledModsButton.IsEnabled = Directory.Exists(ConfigManager.GetInstalledModsPath());
        buildVersionLabel.Text = $"Build {ModHearthManager.GetBuildVersionString()}";
        _ = UpdateDfHackStatusAsync();
        StartDfHackStatusTimer();
        SetChangesMade(false);
        ResetModManagerWatcher();
    }

    private async Task<bool> EnsureConfigAsync()
    {
        while (true)
        {
            IReadOnlyList<ModHearthManager.ConfigIssue> issues = ModHearthManager.GetConfigIssues();
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
}
