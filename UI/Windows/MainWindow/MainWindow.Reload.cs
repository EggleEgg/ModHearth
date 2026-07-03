using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
                SetAndMarkChanges(false);
        }

        ReloadModpacksFromDisk();
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

    private void ReloadModpacksFromDisk()
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
        try
        {
            manager.Initialize(preferredName);
            BuildModViewModels();
            _ = UpdateDfHackStatusAsync();
            if (!DevMode.IsEnabled)
                ResetModManagerWatcher();
        }
        catch (UserActionRequiredException ex)
        {
            _ = DialogService.ShowMessageAsync(this, ex.Message, "Dwarf Fortress required");
            return;
        }
        catch (Exception ex)
        {
            _ = DialogService.ShowMessageAsync(this, ex.Message, "Reload failed");
            return;
        }

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
        manager.RefreshInstalledCacheModIds();
        RefreshModlistPanels();
        SetAndMarkChanges(false);
        RestoreSelectionSnapshot(selectionSnapshot);
        ApplySearchFilterImmediately();
        SearchLogging.Log("ReloadModpacksFromDisk end");

        if (!string.IsNullOrWhiteSpace(manager.LastMissingModsMessage))
            _ = DialogService.ShowMessageAsync(this, manager.LastMissingModsMessage, "Missing Mods");

        ShowReloadFinishedPopup();
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

    private void AutoReloadTimerTick(object? sender, EventArgs e)
    {
        if (changesMade || manager.IsSavingModpacks || modifyingComboBox)
            return;

        ReloadModpacksFromDisk();
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
            Watermark = "seconds"
        };
        autoReloadSecondsTextBox.TextInput += AutoReloadSecondsTextInput;
        autoReloadSecondsTextBox.TextChanged += AutoReloadSecondsTextChanged;
        autoReloadSecondsTextBox.LostFocus += AutoReloadSecondsLostFocus;

        TextBlock label = new TextBlock
        {
            Text = "Every (seconds):",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        StackPanel secondsRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8
        };
        secondsRow.Children.Add(label);
        secondsRow.Children.Add(autoReloadSecondsTextBox);

        StackPanel panel = new StackPanel
        {
            Margin = new Thickness(10),
            Spacing = 8,
            MinWidth = 220
        };
        panel.Children.Add(autoReloadEnabledCheckBox);
        panel.Children.Add(secondsRow);

        reloadOptionsFlyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = panel
        };
    }

    private void AutoReloadSecondsTextInput(object? sender, TextInputEventArgs e)
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
            return;
        }

        autoReloadTimer.Stop();
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

        modManagerReloadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        modManagerReloadTimer.Tick += (_, _) =>
        {
            modManagerReloadTimer.Stop();
            if (manager.IsSavingModpacks)
                return;
            ReloadModpacksFromDisk();
        };

        modManagerWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        modManagerWatcher.Changed += (_, _) => RestartWatcherTimer();
        modManagerWatcher.Created += (_, _) => RestartWatcherTimer();
        modManagerWatcher.Renamed += (_, _) => RestartWatcherTimer();
        modManagerWatcher.EnableRaisingEvents = true;
    }

    private void RestartWatcherTimer()
    {
        if (modManagerReloadTimer == null || manager.IsSavingModpacks)
            return;

        modManagerReloadTimer.Stop();
        modManagerReloadTimer.Start();
    }

    private void ShowReloadFinishedPopup()
    {
        ShowNotification("Reload finished", "infoCircleWhiteIcon.svg");
    }

    public void ShowNotification(string message, string iconResourceName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var container = this.FindControl<StackPanel>("notificationContainer");
            if (container == null)
                return;

            // Limit to 3 notifications by removing the oldest (last in children list)
            while (container.Children.Count >= 3)
            {
                var oldest = container.Children[container.Children.Count - 1];
                if (oldest is Border b && b.Tag is System.Threading.CancellationTokenSource oldCts)
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
                container.Children.RemoveAt(container.Children.Count - 1);
            }

            // Create notification border and elements
            var notificationCts = new System.Threading.CancellationTokenSource();

            var border = new Border
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Padding = new Thickness(6, 5.5, 20, 5.5),
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                BorderThickness = new Thickness(0, 1, 1, 1),
                BoxShadow = BoxShadows.Parse("0 4 12 0 #40000000"),
                Tag = notificationCts
            };

            // Apply theme styling
            IBrush panelBrushClear;
            IBrush buttonOutlineBrush;
            IBrush textBrush;

            if (Style.instance != null)
            {
                panelBrushClear = new SolidColorBrush(Style.instance.modRefPanelColorClear.ToAvaloniaColor());
                buttonOutlineBrush = new SolidColorBrush(Style.instance.buttonOutlineColor.ToAvaloniaColor());
                textBrush = new SolidColorBrush(Style.instance.textColor.ToAvaloniaColor());
            }
            else
            {
                panelBrushClear = new SolidColorBrush(Avalonia.Media.Color.Parse("#2D2D30"));
                buttonOutlineBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#3F3F46"));
                textBrush = Brushes.White;
            }

            border.Background = panelBrushClear;
            border.BorderBrush = buttonOutlineBrush;

            var stackPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6
            };

            var image = new Image
            {
                Width = 16,
                Height = 16,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Source = ImageSourceLoader.LoadFromAssetUri(iconResourceName)
            };

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = textBrush
            };

            stackPanel.Children.Add(image);
            stackPanel.Children.Add(textBlock);
            border.Child = stackPanel;

            // Set up pointer entered to dismiss immediately
            border.PointerEntered += (s, e) =>
            {
                DismissNotification(border);
            };

            // Insert at top (index 0) so newest is on top, oldest is on bottom
            container.Children.Insert(0, border);

            // Timeout to dismiss after 3000ms
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000, notificationCts.Token);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!notificationCts.IsCancellationRequested)
                        {
                            DismissNotification(border);
                        }
                    });
                }
                catch (TaskCanceledException)
                {
                    // Graceful cancellation
                }
            });
        });
    }

    public void DismissNotification(Border border)
    {
        if (border.Tag is System.Threading.CancellationTokenSource cts)
        {
            cts.Cancel();
            cts.Dispose();
            border.Tag = null;
        }

        var container = this.FindControl<StackPanel>("notificationContainer");
        if (container != null)
        {
            container.Children.Remove(border);
        }
    }

    public void DismissAllNotifications()
    {
        var container = this.FindControl<StackPanel>("notificationContainer");
        if (container != null)
        {
            foreach (var child in container.Children)
            {
                if (child is Border b && b.Tag is System.Threading.CancellationTokenSource cts)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
            container.Children.Clear();
        }
    }
}
