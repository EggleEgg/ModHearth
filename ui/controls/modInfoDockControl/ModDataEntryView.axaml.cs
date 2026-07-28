using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;

namespace ModHearth.UI;

public partial class ModDataEntryView : UserControl
{
    public ModDataEntryView()
    {
        InitializeComponent();
    }

    private static IBrush HoverBrush => Style.instance != null
        ? BrushCache.GetBrush(Style.instance.modRefPanelColor.ToAvaloniaColor())
        : BrushCache.GetBrush(Color.Parse("#22888888"));

    private void RowPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is ModDataEntryViewModel vm)
            vm.Background = HoverBrush;
    }

    private void RowPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is ModDataEntryViewModel vm)
            vm.Background = Brushes.Transparent;
    }

    private async void RowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (DataContext is not ModDataEntryViewModel vm)
            return;

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        await clipboard.SetTextAsync(vm.Value);

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow mainWindow)
        {
            mainWindow.ShowNotification($"Copied {vm.Label}", "copyIcon.svg");
        }
    }
}