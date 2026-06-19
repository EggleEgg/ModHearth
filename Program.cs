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
            // For debugging features with extra logs
            bool isDevMode = HasArg(args, "--devmode")
                || IsEnabled(Environment.GetEnvironmentVariable("MODHEARTH_DEVMODE"));
            if (isDevMode)
                Environment.SetEnvironmentVariable("MODHEARTH_DEVMODE", "1");

            // For testing graphics libraries in github actions
            bool isSmokeTestWindow = HasArg(args, "--smoke-test-window")
                || IsEnabled(Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW"));
            bool isSmokeTest = HasArg(args, "--smoke-test")
                || IsEnabled(Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST"));

            if (isSmokeTestWindow)
            {
                Environment.SetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW", "1");
                string[] filteredArgs = StripArgs(args, "--smoke-test-window", "--smoke-test", "--devmode");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(filteredArgs);
                return;
            }

            if (isSmokeTest)
            {
                BuildAvaloniaApp().SetupWithoutStarting();
                return;
            }

            string[] normalArgs = StripArgs(args, "--devmode");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(normalArgs);
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

    private static bool IsEnabled(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string[] StripArgs(string[] args, params string[] toRemove)
        => args.Where(arg => !toRemove.Any(remove => string.Equals(arg, remove, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
}
