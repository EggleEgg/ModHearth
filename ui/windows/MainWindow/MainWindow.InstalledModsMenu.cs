using Avalonia;
using Avalonia.Controls;
using ModHearth.UI.Helpers;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private Flyout? installedModsOptionsFlyout;
        private Button? removeErrorLogButton, clearInstalledModsButton;

        private void EnsureInstalledModsOptionsFlyout()
        {
            string errorLogPath = ConfigManager.GetErrorLogPath();
            removeErrorLogButton ??= ButtonHelpers.CreateMenuButtonWithHelp(
                "docTextWhiteIcon.svg", "Delete DF errorlog",
                "https://dwarffortresswiki.org/index.php/Errorlog.txt", "Click here to view errorlog.txt wiki docs",
                async () => { installedModsOptionsFlyout?.Hide(); await ClearErrorLogAsync(); });
            removeErrorLogButton.IsEnabled = !string.IsNullOrWhiteSpace(errorLogPath) && File.Exists(errorLogPath);

            string installedModsPath = ConfigManager.GetInstalledModsPath();
            clearInstalledModsButton ??= ButtonHelpers.CreateMenuButton(
                "deleteFolderIcon.svg", "Clear installed_mods",
                async () => { installedModsOptionsFlyout?.Hide(); await ClearInstalledModsAsync(); });
            clearInstalledModsButton.IsEnabled = !string.IsNullOrWhiteSpace(installedModsPath) &&
                Directory.Exists(installedModsPath) &&
                Directory.EnumerateFileSystemEntries(installedModsPath).Any();

            if (installedModsOptionsFlyout != null)
                return;

            installedModsOptionsFlyout = new Flyout
            {
                FlyoutPresenterClasses = { "compact-flyout" },
                Placement = PlacementMode.Bottom,
                Content = new StackPanel
                {
                    Margin = new Thickness(6),
                    Spacing = 12,
                    Children =
                    {
                        removeErrorLogButton,
                        clearInstalledModsButton,
                        ButtonHelpers.CreateMenuButtonWithHelp(
                            "openFolderIcon.svg", "Open installed_mods",
                            "https://dwarffortresswiki.org/Modding", "Click here to view installed_mods wiki docs",
                            async () => { installedModsOptionsFlyout?.Hide(); await RevealInstalledModsFolderAsync(); })
                    }
                }
            };
        }
    }
}
