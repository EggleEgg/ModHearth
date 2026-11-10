using ModHearth.UI.Theme;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// One-time config and on-disk fixtures the UI tests need before they build views.
    /// Avalonia itself is started by <see cref="TestAppBuilder"/> for the test session.
    /// </summary>
    internal static class AvaloniaTestEnvironment
    {
        private static readonly object SyncRoot = new();
        private static bool _initialized;

        public static void EnsureInitialized(Action<string> log)
        {
            lock (SyncRoot)
            {
                if (_initialized)
                    return;

                ConfigManager.AttemptLoadConfig(false);

                try
                {
                    _ = StyleLoader.LoadStyle(false);
                }
                catch (Exception ex)
                {
                    // Missing style files are expected in some test environments. Views are only
                    // themed when a style actually loaded, so the test can continue.
                    log($"AvaloniaTestEnvironment: Style load skipped: {ex.Message}");
                }

                string testEnvDir = Path.Combine(AppContext.BaseDirectory, "test_env");
                _ = Directory.CreateDirectory(testEnvDir);
                _ = Directory.CreateDirectory(Path.Combine(testEnvDir, "data", "installed_mods"));
                _ = Directory.CreateDirectory(Path.Combine(testEnvDir, "hack"));
                string dfhackRunPath = Path.Combine(testEnvDir, "hack", OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run");
                if (!File.Exists(dfhackRunPath))
                    File.WriteAllText(dfhackRunPath, string.Empty);

                ConfigManager.SetDwarfFortressFolderPath(testEnvDir);
                ConfigManager.SetDFHackFolderPath(Path.Combine(testEnvDir, "hack"));
                ConfigManager.SetInstalledModsPath(Path.Combine(testEnvDir, "data", "installed_mods"));

                _initialized = true;
            }
        }
    }
}
