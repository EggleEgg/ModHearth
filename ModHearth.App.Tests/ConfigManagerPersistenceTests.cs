using ModHearth.Config;
using ModHearth.Models;
using ModHearth.Utils.IO;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Covers the config file contract that <see cref="ConfigManager"/> owns: values survive a
    /// save/reload through disk, and a malformed file degrades to defaults instead of throwing.
    /// These mutate process-wide config state, so they share the serialized UI collection.
    /// </summary>
    [Collection(AvaloniaUiTestCollection.Name)]
    public class ConfigManagerPersistenceTests
    {
        [Fact]
        public void SaveReloadRoundTripsValuesThroughDisk()
        {
            string? originalJson = SnapshotConfigFile();
            try
            {
                int originalTheme = ConfigManager.GetTheme();
                bool originalAutoSave = ConfigManager.IsAutoSaveEnabled();
                int theme = originalTheme == 1 ? 0 : 1;

                ConfigManager.SetTheme(theme);
                ConfigManager.SetAutoSaveEnabled(!originalAutoSave);
                ConfigManager.SetLeftSearchBarState("round-trip-state");
                ConfigManager.SaveConfigFile();

                Assert.True(File.Exists(ConfigManager.ConfigPath));

                // Dirty the in-memory copy so a successful assert proves values came back from disk.
                ConfigManager.Config.Theme = originalTheme;
                ConfigManager.Config.IsAutoSaveEnabled = originalAutoSave;
                ConfigManager.Config.LeftSearchBarState = "dirty-in-memory";

                ConfigManager.AttemptLoadConfig(createLogs: false);

                Assert.Equal(theme, ConfigManager.GetTheme());
                Assert.Equal(!originalAutoSave, ConfigManager.IsAutoSaveEnabled());
                Assert.Equal("round-trip-state", ConfigManager.GetLeftSearchBarState());
            }
            finally
            {
                RestoreConfigFile(originalJson);
            }
        }

        [Fact]
        public void MalformedConfigFileFallsBackToDefaultsWithoutThrowing()
        {
            string? originalJson = SnapshotConfigFile();
            try
            {
                File.WriteAllText(ConfigManager.ConfigPath, "{ this is not valid json ");

                ConfigManager.AttemptLoadConfig(createLogs: false);

                Assert.NotNull(ConfigManager.Config);
                ModHearthConfig defaults = new();
                Assert.Equal(defaults.Theme, ConfigManager.Config.Theme);
                Assert.Equal(defaults.IsAutoSaveEnabled, ConfigManager.Config.IsAutoSaveEnabled);
            }
            finally
            {
                RestoreConfigFile(originalJson);
            }
        }

        [Fact]
        public void JsonFileStoreRoundTripsConfigModelAndRejectsMissingFiles()
        {
            using TempFolder temp = new("modhearth-config");
            string path = temp.Combine("config.json");
            ModHearthConfig expected = new()
            {
                Theme = 1,
                IsAutoSaveEnabled = false,
                LeftSearchBarState = "state"
            };

            Assert.True(JsonFileStore.TrySave(path, expected, "TestConfig"));

            Assert.True(JsonFileStore.TryLoad(path, "TestConfig", out ModHearthConfig? loaded));
            Assert.NotNull(loaded);
            Assert.Equal(expected.Theme, loaded.Theme);
            Assert.Equal(expected.IsAutoSaveEnabled, loaded.IsAutoSaveEnabled);
            Assert.Equal(expected.LeftSearchBarState, loaded.LeftSearchBarState);

            Assert.False(JsonFileStore.TryLoad(temp.Combine("missing.json"), "TestConfig", out ModHearthConfig? missing));
            Assert.Null(missing);
        }

        private static string? SnapshotConfigFile()
            => File.Exists(ConfigManager.ConfigPath) ? File.ReadAllText(ConfigManager.ConfigPath) : null;

        private static void RestoreConfigFile(string? originalJson)
        {
            if (originalJson == null)
            {
                if (File.Exists(ConfigManager.ConfigPath))
                    File.Delete(ConfigManager.ConfigPath);

                ConfigManager.AttemptLoadConfig(createLogs: false);
                return;
            }

            File.WriteAllText(ConfigManager.ConfigPath, originalJson);
            ConfigManager.AttemptLoadConfig(createLogs: false);
        }
    }
}
