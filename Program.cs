using Avalonia;
using System;

namespace ModHearth;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RuntimeBootstrap.Initialize();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLogging.LogException("Unhandled exception in Main", ex);
            throw;
        }
        finally
        {
            AppLogging.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
