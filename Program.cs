using Avalonia;
using ModHearth.Config;
using ModHearth.Models;
using ModHearth.UI;
using ModHearth.UI.Theme;
using ModHearth.Utils.Logging;

namespace ModHearth
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // For debugging features with extra logs, and using avalonia dev tools.
            // Resolved once, up front, so the rest of the app never re-reads the environment.
            bool isDevMode = HasArg(args, "--devmode") || HasArg(args, "--dev")
                || IsEnabled(Environment.GetEnvironmentVariable("MODHEARTH_DEVMODE"));
            DevMode.Set(isDevMode);
            if (isDevMode)
                Environment.SetEnvironmentVariable("MODHEARTH_DEVMODE", "1");

            RuntimeBootstrap.Initialize();
            ConfigManager.AttemptLoadConfig(false);
            try
            {
                _ = StyleLoader.LoadStyle(false);
            }
            catch
            {
                // Ignore early style load failures
            }

            if (OperatingSystem.IsLinux() && !ConfigManager.Config.ShowConsole)
            {
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
            }

            try
            {
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

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();

        private static bool HasArg(string[] args, string value) => args.Any(arg => string.Equals(arg, value, StringComparison.OrdinalIgnoreCase));

        private static bool IsEnabled(string? value) => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        private static string[] StripArgs(string[] args, params string[] toRemove) => [.. args.Where(arg => !toRemove.Any(remove => string.Equals(arg, remove, StringComparison.OrdinalIgnoreCase)))];

    }
}
