using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void InitializeDockingButtons()
    {
        updateLogButton.AddHandler(InputElement.PointerPressedEvent, ModUpdateLogButtonPointerPressed, RoutingStrategies.Tunnel, true);
        workshopDownloaderButton.AddHandler(InputElement.PointerPressedEvent, WorkshopDownloaderButtonPointerPressed, RoutingStrategies.Tunnel, true);
        sortRulesButton.AddHandler(InputElement.PointerPressedEvent, SortRulesButtonPointerPressed, RoutingStrategies.Tunnel, true);

        UpdateDockingButtonModeImages();
    }

    private void UpdateDockingButtonModeImages()
    {
        UpdateDockingButtonImage(workshopModeIndicator, ConfigManager.GetIsWorkshopDownloaderDocked());
        UpdateDockingButtonImage(updateLogModeIndicator, ConfigManager.GetIsModUpdateLogDocked());
        UpdateDockingButtonImage(sortRulesModeIndicator, ConfigManager.GetIsSortRulesDocked());
    }

    private void UpdateDockingButtonImage(Image? indicator, bool docked)
    {
        if (indicator == null)
            return;

        string iconName = docked ? "windowIcon.svg" : "windowDoubleIcon.svg";
        indicator.Source = ImageSourceLoader.LoadFromAssetUri(iconName) ?? indicator.Source;
    }

    private void WorkshopDownloaderButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(workshopDownloaderButton).Properties.IsRightButtonPressed)
            return;

        e.Handled = true;
        ToggleDockingMode(
            _workshopDockManager,
            ConfigManager.GetIsWorkshopDownloaderDocked(),
            docked => ConfigManager.SetIsWorkshopDownloaderDocked(docked),
            workshopModeIndicator,
            "Downloader");
    }

    private void ModUpdateLogButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(updateLogButton).Properties.IsRightButtonPressed)
            return;

        e.Handled = true;
        ToggleDockingMode(
            _updateLogDockManager,
            ConfigManager.GetIsModUpdateLogDocked(),
            docked => ConfigManager.SetIsModUpdateLogDocked(docked),
            updateLogModeIndicator,
            "Update Log");
    }

    private void SortRulesButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sortRulesButton).Properties.IsRightButtonPressed)
            return;

        e.Handled = true;
        ToggleDockingMode(
            _sortRulesDockManager,
            ConfigManager.GetIsSortRulesDocked(),
            docked => ConfigManager.SetIsSortRulesDocked(docked),
            sortRulesModeIndicator,
            "Sort Rules");
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
