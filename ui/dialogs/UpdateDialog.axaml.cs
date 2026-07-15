using Avalonia.Controls;
using Avalonia.Interactivity;
using ModHearth.UI.ViewModels;

namespace ModHearth.UI;

public partial class UpdateDialog : Window
{
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
        UpdateDialog dialog = new UpdateDialog
        {
            Title = "Update ModHearth",
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        dialog.HeaderText.Text = "Select a build to install:";
        dialog.HeaderText.FontSize = 16;
        dialog.ReleaseList.ItemsSource = releases
            .Select((release, index) => ReleaseEntry.FromRelease(release, index, currentBuild))
            .ToList();

        return await dialog.ShowDialog<GitHubRelease?>(owner);
    }

    private void InstallClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ReleaseEntry entry })
        {
            Close(entry.Release);
        }
    }

}
