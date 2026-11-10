using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(ModHearth.App.Tests.TestAppBuilder))]

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Avalonia entry point used by the headless xUnit session. The app's own builder detects the
    /// desktop platform and needs a display, so tests build their own headless builder instead.
    /// </summary>
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<UI.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .LogToTrace();
    }
}
