using ModHearth.Config;
using ModHearth.Manager;
using ModHearth.Models;
using ModHearth.UI.ModList;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Covers the context-menu action pipeline after the shared-helper refactor: target classification,
    /// batch deletion (success and per-mod failures), cache-only deletion, and the prompt/label text.
    /// Dialog-driven entry points are not tested here; the helpers they delegate to are.
    /// </summary>
    [Collection(AvaloniaUiTestCollection.Name)]
    public sealed class ModContextMenuSupportTests : IDisposable
    {
        private readonly TempFolder temp = new("modhearth-context-menu");
        private readonly string? originalConfigJson;
        private readonly ModHearthManager manager;
        private readonly string modsRoot;
        private readonly string cacheRoot;

        public ModContextMenuSupportTests()
        {
            originalConfigJson = File.Exists(ConfigManager.ConfigPath)
                ? File.ReadAllText(ConfigManager.ConfigPath)
                : null;

            // The manager constructor loads config and runs discovery, so point it at the temp tree
            // afterwards; the delete paths read ConfigManager statically.
            manager = new ModHearthManager();

            modsRoot = temp.Combine("Mods");
            cacheRoot = temp.Combine("data", "installed_mods");
            _ = Directory.CreateDirectory(modsRoot);
            _ = Directory.CreateDirectory(cacheRoot);
            _ = Directory.CreateDirectory(temp.Combine("hack"));

            ConfigManager.SetDwarfFortressFolderPath(temp.Path);
            ConfigManager.SetInstalledModsPath(cacheRoot);
            ConfigManager.SetDFHackFolderPath(temp.Combine("hack"));
        }

        public void Dispose()
        {
            if (originalConfigJson == null)
            {
                if (File.Exists(ConfigManager.ConfigPath))
                    File.Delete(ConfigManager.ConfigPath);
            }
            else
            {
                File.WriteAllText(ConfigManager.ConfigPath, originalConfigJson);
            }

            ConfigManager.AttemptLoadConfig(createLogs: false);
            temp.Dispose();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void DeleteLocalModsDeletesFolderAndReportsNoFailures()
        {
            string modDir = CreateModFolder(modsRoot, "my_mod");
            ModReference mod = CreateReference("my_mod", "1.0", modDir);

            List<string> failures = ModContextMenuSupport.DeleteLocalMods(manager, [mod], requestUIReload: false);

            Assert.Empty(failures);
            Assert.False(Directory.Exists(modDir));
        }

        [Fact]
        public void DeleteLocalModsReportsTargetsOutsideModsFolder()
        {
            string outsideDir = temp.Combine("elsewhere", "outside_mod");
            _ = Directory.CreateDirectory(outsideDir);
            ModReference mod = CreateReference("outside_mod", "1.0", outsideDir);

            List<string> failures = ModContextMenuSupport.DeleteLocalMods(manager, [mod], requestUIReload: false);

            string failure = Assert.Single(failures);
            Assert.Contains("not in the Mods folder", failure);
            Assert.True(Directory.Exists(outsideDir));
        }

        [Fact]
        public void DeleteCacheOnlyModsDeletesCacheFolder()
        {
            string cacheDir = CreateModFolder(cacheRoot, "123 (10)");
            ModReference mod = CreateReference("cached_mod", "1.0", cacheDir);

            Assert.True(ModHearthManager.CanDeleteCacheOnlyMod(mod));

            List<string> failures = ModContextMenuSupport.DeleteCacheOnlyMods(manager, [mod], requestUIReload: false);

            Assert.Empty(failures);
            Assert.False(Directory.Exists(cacheDir));
        }

        [Fact]
        public void GetCacheOnlyDeletableModsDeduplicatesByPathAndSkipsRealSources()
        {
            string cacheDir = CreateModFolder(cacheRoot, "456 (10)");
            ModReference first = CreateReference("cached_a", "1.0", cacheDir);
            ModReference second = CreateReference("cached_b", "1.0", cacheDir);
            ModReference local = CreateReference("local_mod", "1.0", CreateModFolder(modsRoot, "local_mod"));
            ModReference missing = CreateReference("missing", "1.0", Path.Combine(cacheRoot, "does-not-exist"));

            List<ModReference> targets = ModContextMenuSupport.GetCacheOnlyDeletableMods([first, second, local, missing]);

            ModReference target = Assert.Single(targets);
            Assert.Same(first, target);
        }

        [Fact]
        public void BuildStateSeparatesLocalSteamAndCacheTargets()
        {
            ModReference local = CreateReference("local_mod", "1.0", CreateModFolder(modsRoot, "local_mod"));
            string steamDir = CreateModFolder(temp.Combine("steamapps", "workshop", "content", "975370"), "987654");
            ModReference steam = CreateReference("steam_mod", "1.0", steamDir, steamId: "987654");
            ModReference cache = CreateReference("cached_mod", "1.0", CreateModFolder(cacheRoot, "321 (10)"));

            ModContextMenuState state = ModContextMenuSupport.BuildState(manager, steam, [local, steam, cache]);

            Assert.Equal(1, state.LocalCount);
            Assert.Equal(1, state.SteamCount);
            Assert.Equal(1, state.CacheOnlyCount);
            Assert.True(state.HasLocalActions);
            Assert.True(state.HasSteamActions);
            Assert.True(state.HasCacheOnlyActions);
            Assert.True(state.HasSteamPage);
        }

        [Theory]
        [InlineData(1, "Delete 'Test Mod' from the Mods folder?")]
        [InlineData(2, "Delete 2 mods from the Mods folder?")]
        public void DeletePromptNamesSingleTargetAndCountsMultiple(int count, string expected)
        {
            List<ModReference> targets = [.. Enumerable.Range(0, count)
                .Select(index => CreateReference($"mod{index}", "1.0", Path.Combine(temp.Path, $"mod{index}"), name: "Test Mod"))];

            Assert.Equal(expected, ModContextMenuSupport.BuildDeletePrompt(targets));
        }

        [Fact]
        public void SteamPromptsNameSingleTargetAndCountMultiple()
        {
            ModReference single = CreateReference("steam_mod", "1.0", temp.Path, steamId: "123", name: "Steam Mod");
            ModReference second = CreateReference("steam_mod_two", "1.0", temp.Path, steamId: "456", name: "Second");

            Assert.Equal("Unsubscribe from 'Steam Mod' on Steam Workshop?", ModContextMenuSupport.BuildUnsubscribePrompt([single]));
            Assert.Equal("Unsubscribe from 2 Steam mods?", ModContextMenuSupport.BuildUnsubscribePrompt([single, second]));
            Assert.Equal("Redownload 'Steam Mod' from Steam Workshop?", ModContextMenuSupport.BuildRedownloadPrompt([single]));
            Assert.Equal("Redownload 2 Steam mods?", ModContextMenuSupport.BuildRedownloadPrompt([single, second]));
        }

        private string CreateModFolder(params string[] parts)
        {
            string path = temp.Combine(parts);
            _ = Directory.CreateDirectory(path);
            return path;
        }

        private static ModReference CreateReference(
            string id,
            string numericVersion,
            string path,
            string steamId = "",
            string? name = null) => new(new Dictionary<string, string>
            {
                ["id"] = id,
                ["numeric_version"] = numericVersion,
                ["displayed_version"] = numericVersion,
                ["earliest_compatible_numeric_version"] = numericVersion,
                ["earliest_compatible_displayed_version"] = numericVersion,
                ["author"] = "test",
                ["name"] = name ?? id,
                ["description"] = string.Empty,
                ["steam_file_id"] = steamId,
                ["steam_title"] = string.Empty,
                ["steam_description"] = string.Empty,
                ["src_dir"] = path,
            });
    }
}
