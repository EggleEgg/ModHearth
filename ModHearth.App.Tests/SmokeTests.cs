using Avalonia.Headless.XUnit;
using Xunit;

namespace ModHearth.App.Tests;

public class SmokeTests
{
    [AvaloniaFact]
    public void MainWindow_CanInstantiate()
    {
        var window = new ModHearth.UI.MainWindow();
        Assert.NotNull(window);
    }
}
