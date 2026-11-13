using Avalonia.Controls;
using Avalonia.Interactivity;
using ModHearth.Config;
using ModHearth.UI.Theme;
using ModHearth.UI.Update;
using ModHearth.UI.ViewModels;
using ModHearth.Utils.Logging;

namespace ModHearth.UI
{
    public partial class UpdateDialog : Window
    {
        private bool _isBleedingEdge = UpdateService.IsCurrentBuildBleedingEdge();
        private string _currentBuild = string.Empty;
        private List<GitHubRelease> _releases = [];

        public UpdateDialog()
        {
            InitializeComponent();
            WindowThemeManager.Register(this);
        }

        public static async Task<GitHubRelease?> ShowAsync(
            Window owner,
            IReadOnlyList<GitHubRelease> releases,
            string currentBuild)
        {
            UpdateDialog dialog = new()
            {
                Title = "Update ModHearth",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                _currentBuild = currentBuild,
                _releases = [.. releases],
                _isBleedingEdge = UpdateService.IsCurrentBuildBleedingEdge()
            };

            dialog.InitializeDialog();
            return await dialog.ShowDialog<GitHubRelease?>(owner);
        }

        private void InitializeDialog()
        {
            HeaderText.Text = "Select a build to install:";
            HeaderText.FontSize = 16;
            UpdateChannelButtonState();
            RefreshReleaseList();
        }

        private void UpdateChannelButtonState()
        {
            ChannelSwitchButton.Content = "Switch to " + (_isBleedingEdge ? "Main" : "Bleeding Edge");
            BleedingEdgeWarningPanel.IsVisible = _isBleedingEdge;
        }

        private void RefreshReleaseList()
        {
            // Show the newest builds first even if the API returned them out of build order.
            _releases = UpdateHelpers.OrderByNewest(_releases);
            ReleaseList.ItemsSource = _releases
                .Select((release, index) => ReleaseEntry.FromRelease(release, index, _currentBuild))
                .ToList();
        }

        private async void ChannelSwitchClicked(object? sender, RoutedEventArgs e)
        {
            _isBleedingEdge = !_isBleedingEdge;
            ConfigManager.SetIsBleedingEdge(_isBleedingEdge);
            UpdateChannelButtonState();
            ChannelSwitchButton.IsEnabled = false;

            try
            {
                HeaderText.Text = _isBleedingEdge ? "Fetching bleeding edge builds..." : "Fetching standard builds...";
                _releases = await UpdateService.FetchRecentBuildsAsync(5, _isBleedingEdge);
                HeaderText.Text = "Select a build to install:";
                RefreshReleaseList();
            }
            catch (Exception ex)
            {
                HeaderText.Text = $"Failed to fetch builds: {ex.Message}";
                Loggers.Updates.LogError($"Channel switch failed: {ex.Message}");
            }
            finally
            {
                ChannelSwitchButton.IsEnabled = true;
            }
        }

        private void InstallClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ReleaseEntry entry })
            {
                Close(entry.Release);
            }
        }

    }
}
