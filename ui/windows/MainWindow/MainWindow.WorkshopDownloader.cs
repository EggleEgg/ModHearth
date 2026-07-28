using Avalonia.Input;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void UpdateWorkshopDownloaderButtonModeImage()
    {
        if (workshopModeIndicator == null)
            return;

        bool docked = ConfigManager.GetIsWorkshopDownloaderDocked();
        string iconName = docked ? "windowIcon.svg" : "windowDoubleIcon.svg";
        workshopModeIndicator.Source = ImageSourceLoader.LoadFromAssetUri(iconName) ?? workshopModeIndicator.Source;
    }

    private void WorkshopDownloaderButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(workshopDownloaderButton).Properties.IsRightButtonPressed)
            return;

        e.Handled = true;
        bool docked = !ConfigManager.GetIsWorkshopDownloaderDocked();
        ConfigManager.SetIsWorkshopDownloaderDocked(docked);
        _workshopDockManager?.SetDocked(docked);
        UpdateWorkshopDownloaderButtonModeImage();

        ShowNotification(docked ? "Downloader set to docked mode" : "Downloader set to window mode", docked ? "windowIcon.svg" : "windowDoubleIcon.svg");
    }
}
