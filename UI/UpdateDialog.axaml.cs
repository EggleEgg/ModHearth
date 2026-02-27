using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModHearth.UI;

public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    public static async Task<GitHubRelease?> ShowAsync(
        Window owner,
        IReadOnlyList<GitHubRelease> releases,
        string currentBuild)
    {
        UpdateDialog dialog = new UpdateDialog
        {
            Title = "Update ModHearth",
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        dialog.HeaderText.Text = "Select a build to install:";
        dialog.ReleaseList.ItemsSource = releases
            .Select((release, index) => ReleaseEntry.FromRelease(release, index, currentBuild))
            .ToList();

        return await dialog.ShowDialog<GitHubRelease?>(owner);
    }

    private void InstallClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ReleaseEntry entry)
            return;

        Close(entry.Release);
    }

    private sealed class ReleaseEntry
    {
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string ButtonLabel { get; init; } = "Install";
        public bool CanInstall { get; init; } = true;
        public GitHubRelease Release { get; init; } = new GitHubRelease();

        public static ReleaseEntry FromRelease(GitHubRelease release, int index, string currentBuild)
        {
            string title = UpdateHelpers.GetReleaseTitle(release, index);
            string subtitle = UpdateHelpers.GetReleaseSubtitle(release, currentBuild);
            string? buildNumber = UpdateHelpers.TryGetBuildNumber(release);
            bool isCurrent = !string.IsNullOrWhiteSpace(buildNumber) &&
                             string.Equals(buildNumber, currentBuild, StringComparison.OrdinalIgnoreCase);

            return new ReleaseEntry
            {
                Title = title,
                Subtitle = subtitle,
                ButtonLabel = isCurrent ? "Reinstall" : "Install",
                Release = release
            };
        }
    }
}
