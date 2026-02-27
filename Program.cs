using Avalonia;
using System;
using System.Linq;

namespace ModHearth;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RuntimeBootstrap.Initialize();
        try
        {
            bool isSmokeTestWindow = HasArg(args, "--smoke-test-window")
                || string.Equals(Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW"), "1", StringComparison.OrdinalIgnoreCase);
            bool isSmokeTest = HasArg(args, "--smoke-test")
                || string.Equals(Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST"), "1", StringComparison.OrdinalIgnoreCase);

            if (isSmokeTestWindow)
            {
                Environment.SetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW", "1");
                string[] filteredArgs = StripArgs(args, "--smoke-test-window", "--smoke-test");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(filteredArgs);
                return;
            }

            if (isSmokeTest)
            {
                BuildAvaloniaApp().SetupWithoutStarting();
                return;
            }

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

    private static bool HasArg(string[] args, string value)
        => args.Any(arg => string.Equals(arg, value, StringComparison.OrdinalIgnoreCase));

    private static string[] StripArgs(string[] args, params string[] toRemove)
        => args.Where(arg => !toRemove.Any(remove => string.Equals(arg, remove, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
}
