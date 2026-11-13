using Avalonia.Controls;
using ModHearth.Config;
using ModHearth.UI.Docking;
using ModHearth.UI.Helpers;
using ModHearth.UI.Rendering;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private void InitializeDockingButtons()
        {
            ButtonHelpers.AttachRightClickHandler(updateLogButton, () =>
                ToggleDockingMode(
                    _updateLogDockManager,
                    ConfigManager.GetIsModUpdateLogDocked(),
                    ConfigManager.SetIsModUpdateLogDocked,
                    updateLogModeIndicator,
                    "Update Log"));
            ButtonHelpers.AttachRightClickHandler(workshopDownloaderButton, () =>
                ToggleDockingMode(
                    _workshopDockManager,
                    ConfigManager.GetIsWorkshopDownloaderDocked(),
                    ConfigManager.SetIsWorkshopDownloaderDocked,
                    workshopModeIndicator,
                    "Downloader"));
            ButtonHelpers.AttachRightClickHandler(sortRulesButton, () =>
                ToggleDockingMode(
                    _sortRulesDockManager,
                    ConfigManager.GetIsSortRulesDocked(),
                    ConfigManager.SetIsSortRulesDocked,
                    sortRulesModeIndicator,
                    "Sort Rules"));

            UpdateDockingButtonModeImages();
        }

        private void UpdateDockingButtonModeImages()
        {
            UpdateDockingButtonImage(workshopModeIndicator, ConfigManager.GetIsWorkshopDownloaderDocked());
            UpdateDockingButtonImage(updateLogModeIndicator, ConfigManager.GetIsModUpdateLogDocked());
            UpdateDockingButtonImage(sortRulesModeIndicator, ConfigManager.GetIsSortRulesDocked());
        }

        private static void UpdateDockingButtonImage(Image? indicator, bool docked)
        {
            if (indicator == null)
                return;

            string iconName = docked ? "windowIcon.svg" : "windowDoubleIcon.svg";
            indicator.Source = ImageSourceLoader.LoadFromAssetUri(iconName) ?? indicator.Source;
        }

        private void ToggleDockingMode<TControl, TWindow>(
            DockingManager<TControl, TWindow>? manager,
            bool currentDocked,
            Action<bool> saveConfig,
            Image? indicator,
            string itemName)
            where TControl : UserControl
            where TWindow : Window
        {
            bool newDocked = !currentDocked;
            saveConfig(newDocked);
            manager?.SetDocked(newDocked);
            UpdateDockingButtonImage(indicator, newDocked);

            string status = newDocked ? $"{itemName} set to docked mode" : $"{itemName} set to window mode";
            string icon = newDocked ? "windowIcon.svg" : "windowDoubleIcon.svg";
            ShowNotification(status, icon);
        }

        private void SaveMainWindowGridSplitterRatio()
        {
            if (mainGrid != null && mainGrid.ColumnDefinitions.Count >= 5)
            {
                double w2 = mainGrid.ColumnDefinitions[2].ActualWidth;
                double w4 = mainGrid.ColumnDefinitions[4].ActualWidth;
                if (w2 + w4 > 0)
                {
                    double ratio = w2 / (w2 + w4);
                    ratio = Math.Clamp(ratio, 0.05, 0.95);
                    ConfigManager.SetMainWindowGridSplitterRatio(ratio);
                }
            }
        }
    }
}
