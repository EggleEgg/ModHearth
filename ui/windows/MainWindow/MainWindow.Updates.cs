using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set
            {
                if (_isUpdateAvailable != value)
                {
                    _isUpdateAvailable = value;
                    NotifyOfPropertyChange();
                }
            }
        }

        private Flyout? updateOptionsFlyout;
        private CheckBox? autoCheckUpdatesOnStartupCheckBox;
        private Button? ignoreVersionButton;
        private string? latestAvailableVersion;
        private bool suppressUpdateUiEvents;

        private void InitializeUpdateChecking()
        {
            updateButton.DataContext = this;
            updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
            updateButton.AddHandler(PointerPressedEvent, UpdateButtonPointerPressed, RoutingStrategies.Tunnel, true);

            if (ConfigManager.GetAutoCheckForReleasesOnStartup())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000); // Wait 2s after startup before checking
                        await Dispatcher.UIThread.InvokeAsync(async () => await CheckForUpdatesQuietlyAsync());
                    }
                    catch (Exception ex)
                    {
                        if (DevMode.IsEnabled)
                            Console.WriteLine($"[UpdateCheck] Startup check failed: {ex.Message}");
                    }
                });
            }
        }

        private void UpdateButtonPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(updateButton).Properties.IsRightButtonPressed)
                return;

            e.Handled = true;
            EnsureUpdateOptionsFlyout();
            LoadUpdateMenuFromConfig();
            updateOptionsFlyout?.ShowAt(updateButton);
        }

        private async Task CheckForUpdatesQuietlyAsync()
        {
            try
            {
                List<GitHubRelease> releases = await UpdateService.FetchRecentBuildsAsync(5);
                EvaluateAvailableUpdate(releases);
            }
            catch (Exception ex)
            {
                if (DevMode.IsEnabled)
                    Console.WriteLine($"[UpdateCheck] Quiet check failed: {ex.Message}");
            }
        }

        private void EvaluateAvailableUpdate(List<GitHubRelease> releases)
        {
            if (releases == null || releases.Count == 0)
            {
                IsUpdateAvailable = false;
                latestAvailableVersion = null;
                return;
            }

            string currentBuild = ModHearthManager.GetBuildVersionString().Trim();
            string ignoredVersion = ConfigManager.GetIgnoredUpdateVersion().Trim();

            GitHubRelease? latestRelease = releases.FirstOrDefault();
            if (latestRelease == null)
            {
                IsUpdateAvailable = false;
                latestAvailableVersion = null;
                return;
            }

            string fetchedVersion = UpdateHelpers.TryGetBuildNumber(latestRelease) ?? latestRelease.TagName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fetchedVersion))
            {
                IsUpdateAvailable = false;
                latestAvailableVersion = null;
                return;
            }

            latestAvailableVersion = fetchedVersion;

            bool isNewer = UpdateHelpers.IsNewerVersion(fetchedVersion, currentBuild);
            bool isIgnored = !string.IsNullOrWhiteSpace(ignoredVersion) && UpdateHelpers.IsCurrentVersion(fetchedVersion, ignoredVersion);
            IsUpdateAvailable = isNewer && !isIgnored;

            if (DevMode.IsEnabled)
                Console.WriteLine($"[UpdateCheck] EvaluateAvailableUpdate: fetchedVersion='{fetchedVersion}', currentBuild='{currentBuild}', ignoredVersion='{ignoredVersion}', isNewer={isNewer}, isIgnored={isIgnored}, IsUpdateAvailable={IsUpdateAvailable}");

            UpdateIgnoreButtonLabel();
        }

        private void EnsureUpdateOptionsFlyout()
        {
            if (updateOptionsFlyout != null)
                return;

            autoCheckUpdatesOnStartupCheckBox = new CheckBox
            {
                Content = "Check for updates on startup",
                IsChecked = ConfigManager.GetAutoCheckForReleasesOnStartup()
            };
            autoCheckUpdatesOnStartupCheckBox.IsCheckedChanged += AutoCheckUpdatesOnStartupChanged;

            ignoreVersionButton = new Button
            {
                Tag = "Themed",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ignoreVersionButton.Click += IgnoreVersionClicked;

            StackPanel panel = new()
            {
                Margin = new Thickness(6),
                Spacing = 8
            };
            panel.Children.Add(autoCheckUpdatesOnStartupCheckBox);
            panel.Children.Add(ignoreVersionButton);

            updateOptionsFlyout = new Flyout
            {
                FlyoutPresenterClasses = { "compact-flyout" },
                Placement = PlacementMode.Bottom,
                Content = panel
            };

            UpdateIgnoreButtonLabel();
        }

        private void LoadUpdateMenuFromConfig()
        {
            if (autoCheckUpdatesOnStartupCheckBox == null)
                return;

            suppressUpdateUiEvents = true;
            autoCheckUpdatesOnStartupCheckBox.IsChecked = ConfigManager.GetAutoCheckForReleasesOnStartup();
            suppressUpdateUiEvents = false;
            UpdateIgnoreButtonLabel();
        }

        private void UpdateIgnoreButtonLabel()
        {
            if (ignoreVersionButton == null)
                return;

            string ver = latestAvailableVersion ?? ConfigManager.GetIgnoredUpdateVersion();
            if (string.IsNullOrWhiteSpace(ver))
            {
                ignoreVersionButton.Content = "Ignore update";
                ignoreVersionButton.IsEnabled = false;
            }
            else
            {
                ignoreVersionButton.Content = $"Ignore Build {ver}";
                ignoreVersionButton.IsEnabled = IsUpdateAvailable;
            }
        }

        private void AutoCheckUpdatesOnStartupChanged(object? sender, RoutedEventArgs e)
        {
            if (suppressUpdateUiEvents || autoCheckUpdatesOnStartupCheckBox == null)
                return;

            bool autoCheck = autoCheckUpdatesOnStartupCheckBox.IsChecked == true;
            ConfigManager.SetAutoCheckForReleasesOnStartup(autoCheck);
        }

        private void IgnoreVersionClicked(object? sender, RoutedEventArgs e)
        {
            string ver = latestAvailableVersion ?? ConfigManager.GetIgnoredUpdateVersion();
            if (DevMode.IsEnabled)
            {
                Console.WriteLine($"[UpdateCheck] IgnoreVersionClicked: latestAvailableVersion='{latestAvailableVersion}', configIgnored='{ConfigManager.GetIgnoredUpdateVersion()}', resolvedVer='{ver}'");
            }

            if (!string.IsNullOrWhiteSpace(ver))
            {
                ConfigManager.SetIgnoredUpdateVersion(ver);
                IsUpdateAvailable = false;
                if (DevMode.IsEnabled)
                {
                    Console.WriteLine($"[UpdateCheck] Ignored version set to '{ver}', IsUpdateAvailable set to false.");
                }
            }
            updateOptionsFlyout?.Hide();
        }
    }
}
