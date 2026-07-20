using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI;

public partial class MainWindow
{
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
                await SetAndMarkChangesAsync(true);
        }

        await ReloadModpacksFromDisk();
        await manager.EnsureModRawDependencyCacheAsync();
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

    private async Task ReloadModpacksFromDisk()
    {
        DismissAllNotifications();

        SearchLogging.Log("ReloadModpacksFromDisk begin");
        searchDebounceTimer?.Stop();
        ModSelectionSnapshot selectionSnapshot = CaptureSelectionSnapshot();
        SearchFilterStateSnapshot filterStateSnapshot = CaptureSearchFilterStateSnapshot();
        ensureSearchResultVisibleOnNextFilter = true;
        SearchLogging.Log("ReloadModpacksFromDisk scheduled ensure-visible on next filter");
        string? preferredName = manager.modpacks.Count > 0
            ? manager.SelectedModlist?.name
            : null;

        SearchLogging.Log("Refreshing modlists from disk.");
        bool didReload;
        try
        {
            didReload = await Task.Run(() =>
            {
                bool result = manager.Initialize(preferredName);
                if (result)
                    manager.RefreshInstalledCacheModIds();
                return result;
            });
        }
        catch (UserActionRequiredException ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Dwarf Fortress required");
            return;
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, ex.Message, "Reload failed");
            return;
        }

        if (!didReload)
        {
            SearchLogging.Log("ReloadModpacksFromDisk skipped: a reload was already in progress");
            return;
        }

        BuildModViewModels();
        _ = UpdateDfHackStatusAsync();
        ResetModManagerWatcher();

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

        SearchLogging.Log("ReloadModpacksFromDisk restoring snapshot + refresh");

        RestoreSearchFilterStateSnapshot(filterStateSnapshot);
        RefreshModlistPanels();
        await SetAndMarkChangesAsync(false);
        RestoreSelectionSnapshot(selectionSnapshot);

        SearchLogging.Log("ReloadModpacksFromDisk end");

        if (!string.IsNullOrWhiteSpace(manager.LastMissingModsMessage))
            _ = DialogService.ShowMessageAsync(this, manager.LastMissingModsMessage, "Missing Mods");

        ShowNotification("Reload finished", "infoCircleWhiteIcon.svg");
    }

    private void InitializeAutoReloadTimer()
    {
        autoReloadTimer = new DispatcherTimer();
        autoReloadTimer.Tick += AutoReloadTimerTick;
        int configured = NormalizeAutoReloadIntervalSeconds(ConfigManager.GetAutoReloadIntervalSeconds());
        if (configured != ConfigManager.GetAutoReloadIntervalSeconds())
            ConfigManager.SetAutoReloadIntervalSeconds(configured);
        ConfigureAutoReloadTimer(configured);
    }

    private async void AutoReloadTimerTick(object? sender, EventArgs e)
    {
        if (changesMade || manager.IsSavingModpacks || modifyingComboBox)
            return;

        await ReloadModpacksFromDisk();
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
            PlaceholderText = "seconds"
        };
        autoReloadSecondsTextBox.TextInput += AutoReloadSecondsTextInput;
        autoReloadSecondsTextBox.TextChanged += AutoReloadSecondsTextChanged;
        autoReloadSecondsTextBox.LostFocus += AutoReloadSecondsLostFocus;

        TextBlock label = new TextBlock
        {
            Text = "Every (seconds):",
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel secondsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        secondsRow.Children.Add(label);
        secondsRow.Children.Add(autoReloadSecondsTextBox);

        StackPanel panel = new StackPanel
        {
            Margin = new Thickness(2),
            Spacing = 8,
        };
        panel.Children.Add(autoReloadEnabledCheckBox);
        panel.Children.Add(secondsRow);

        reloadOptionsFlyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = panel
        };
    }

    private static void AutoReloadSecondsTextInput(object? sender, TextInputEventArgs e)
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

        int configured = NormalizeAutoReloadIntervalSeconds(ConfigManager.GetAutoReloadIntervalSeconds());
        if (configured != ConfigManager.GetAutoReloadIntervalSeconds())
            ConfigManager.SetAutoReloadIntervalSeconds(configured);

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
            ConfigManager.SetAutoReloadIntervalSeconds(-1);
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
        ConfigManager.SetAutoReloadIntervalSeconds(normalized);
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
            IsAutoReloadEnabled = true;
            return;
        }

        autoReloadTimer.Stop();
        IsAutoReloadEnabled = false;
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

        DispatcherTimer newReloadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        newReloadTimer.Tick += async (_, _) =>
        {
            newReloadTimer.Stop();
            if (manager.IsSavingModpacks)
                return;
            await ReloadModpacksFromDisk();
        };
        modManagerReloadTimer = newReloadTimer;

        modManagerWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        modManagerWatcher.Changed += (_, _) => Dispatcher.UIThread.Post(RestartWatcherTimer);
        modManagerWatcher.Created += (_, _) => Dispatcher.UIThread.Post(RestartWatcherTimer);
        modManagerWatcher.Renamed += (_, _) => Dispatcher.UIThread.Post(RestartWatcherTimer);
        modManagerWatcher.EnableRaisingEvents = true;
    }

    private void RestartWatcherTimer()
    {
        if (modManagerReloadTimer == null || manager.IsSavingModpacks)
            return;

        modManagerReloadTimer.Stop();
        modManagerReloadTimer.Start();
    }
}
