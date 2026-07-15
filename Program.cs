using Avalonia;
using ModHearth.Utilities;
using System.Diagnostics;

namespace ModHearth;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RegisterSteamApiResolver();
        RuntimeBootstrap.Initialize();
        ConfigManager.AttemptLoadConfig(false);

        if (OperatingSystem.IsLinux() && !ModHearthManager.Config.showConsole)
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }

        try
        {
            // For debugging features with extra logs
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
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(filteredArgs);
                return;
            }

            if (isSmokeTest)
            {
                BuildAvaloniaApp().SetupWithoutStarting();
                return;
            }

            string[] normalArgs = StripArgs(args, "--devmode", "--dev");
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
            SteamManager.Shutdown();
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

    private static void RegisterSteamApiResolver()
    {
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(
            typeof(Steamworks.SteamAPI).Assembly,
            (libraryName, assembly, searchPath) =>
            {
                if (!libraryName.Contains("steam_api", StringComparison.OrdinalIgnoreCase))
                    return IntPtr.Zero;

                string? fileName;

                if (OperatingSystem.IsWindows())
                    fileName = "steam_api64.dll";
                else if (OperatingSystem.IsLinux())
                    fileName = "libsteam_api.so";
                else if (OperatingSystem.IsMacOS())
                    fileName = "libsteam_api.dylib";
                else
                    fileName = null;

                if (fileName == null)
                    return IntPtr.Zero;

                string[] candidates =
                {
                Path.Combine(AppContext.BaseDirectory, "libs", fileName),
                Path.Combine(AppContext.BaseDirectory, fileName)
                };

                foreach (string candidate in candidates)
                {
                    if (File.Exists(candidate) &&
                        System.Runtime.InteropServices.NativeLibrary.TryLoad(candidate, out IntPtr handle))
                    {
                        return handle;
                    }
                }

                return IntPtr.Zero;
            });
    }
}
