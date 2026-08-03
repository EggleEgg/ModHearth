using Avalonia;
using ModHearth.Utilities;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModHearth;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RuntimeBootstrap.Initialize();
        ConfigManager.AttemptLoadConfig(false);
        try
        {
            _ = ConfigManager.LoadStyle(false);
        }
        catch
        {
            // Ignore early style load failures
        }

        if (OperatingSystem.IsLinux() && !ModHearthManager.Config.showConsole)
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }

        try
        {
            // For debugging features with extra logs, and using avalonia dev tools
            bool isDevMode = HasArg(args, "--devmode") || HasArg(args, "--dev")
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
                _ = BuildAvaloniaApp().StartWithClassicDesktopLifetime(filteredArgs);
                return;
            }

            if (isSmokeTest)
            {
                _ = BuildAvaloniaApp().SetupWithoutStarting();
                return;
            }

            string[] normalArgs = StripArgs(args, "--devmode", "--dev");
            _ = BuildAvaloniaApp().StartWithClassicDesktopLifetime(normalArgs);
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
    {
        return AppBuilder.Configure<UI.App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static bool HasArg(string[] args, string value)
        => args.Any(arg => string.Equals(arg, value, StringComparison.OrdinalIgnoreCase));

    private static bool IsEnabled(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string[] StripArgs(string[] args, params string[] toRemove)
        => args.Where(arg => !toRemove.Any(remove => string.Equals(arg, remove, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
}
