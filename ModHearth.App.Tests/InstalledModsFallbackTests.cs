using ModHearth.Models;
using ModHearth.Utilities;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Regression tests for mods that Dwarf Fortress only reports through its disposable
    /// data/installed_mods cache. They must stay visible, while a real source folder (Mods/ or a
    /// Steam Workshop item) must always win over the cached copy.
    /// </summary>
    public class InstalledModsFallbackTests
    {
        [Fact]
        public void ReferenceKeyMatchesDfhModKey()
        {
            const string id = "my_mod";

            // DfhMod compacts versions by removing dots: "1.0" -> 10, matching DFHack's on-disk key.
            Assert.Equal(new DfhMod(id, 10).ToString(), ModHearthManager.TryBuildModReferenceKey(id, "1.0"));
            Assert.Equal(new DfhMod(id, 3).ToString(), ModHearthManager.TryBuildModReferenceKey(id, "3"));
            Assert.Equal(new DfhMod(id, 42).ToString(), ModHearthManager.TryBuildModReferenceKey(id, " 4.2 "));

            Assert.Null(ModHearthManager.TryBuildModReferenceKey(string.Empty, "1"));
            Assert.Null(ModHearthManager.TryBuildModReferenceKey(id, string.Empty));
            Assert.Null(ModHearthManager.TryBuildModReferenceKey(id, "not-a-version"));
        }

        [Fact]
        public void RemapsCachePathToCanonicalSourceWhenIdAndVersionMatch()
        {
            string tempRoot = Directory.CreateTempSubdirectory("modhearth-remap-").FullName;
            try
            {
                string canonicalDir = Path.Combine(tempRoot, "Mods", "my_mod");
                string cacheDir = Path.Combine(tempRoot, "data", "installed_mods", "123 (10)");
                _ = Directory.CreateDirectory(canonicalDir);
                _ = Directory.CreateDirectory(cacheDir);

                Dictionary<string, string> modData = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = "my_mod",
                    ["numeric_version"] = "1.0",
                };
                Dictionary<string, string> canonicalByReferenceKey = new(StringComparer.OrdinalIgnoreCase)
                {
                    [new DfhMod("my_mod", 10).ToString()] = canonicalDir,
                };

                string remapped = ModHearthManager.TryRemapInstalledModsPath(cacheDir, modData, canonicalByReferenceKey);

                Assert.Equal(Path.GetFullPath(canonicalDir), Path.GetFullPath(remapped));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public void KeepsCachePathWhenNoCanonicalSourceMatches()
        {
            string tempRoot = Directory.CreateTempSubdirectory("modhearth-remap-").FullName;
            try
            {
                string canonicalDir = Path.Combine(tempRoot, "Mods", "other_mod");
                string cacheDir = Path.Combine(tempRoot, "data", "installed_mods", "123 (10)");
                _ = Directory.CreateDirectory(canonicalDir);
                _ = Directory.CreateDirectory(cacheDir);

                Dictionary<string, string> modData = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = "my_mod",
                    ["numeric_version"] = "1.0",
                };
                Dictionary<string, string> canonicalByReferenceKey = new(StringComparer.OrdinalIgnoreCase)
                {
                    // Same id, different version: must not be treated as the same mod folder.
                    [new DfhMod("my_mod", 11).ToString()] = canonicalDir,
                };

                Assert.Equal(cacheDir, ModHearthManager.TryRemapInstalledModsPath(cacheDir, modData, canonicalByReferenceKey));
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public void LeavesCanonicalPathsUntouched()
        {
            string canonicalDir = Path.Combine(Path.GetTempPath(), "Mods", "my_mod");
            Dictionary<string, string> modData = new(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "my_mod",
                ["numeric_version"] = "1.0",
            };
            Dictionary<string, string> canonicalByReferenceKey = new(StringComparer.OrdinalIgnoreCase)
            {
                [new DfhMod("my_mod", 10).ToString()] = Path.Combine(Path.GetTempPath(), "Mods", "elsewhere"),
            };

            Assert.Equal(canonicalDir, ModHearthManager.TryRemapInstalledModsPath(canonicalDir, modData, canonicalByReferenceKey));
        }

        [Fact]
        public void CacheOnlyReferenceFallsBackButNeverShadowsAnExistingRegistration()
        {
            Dictionary<string, ModReference> modrefMap = new(StringComparer.OrdinalIgnoreCase);
            HashSet<DfhMod> modPool = [];
            ModReference cacheRef = CreateReference("my_mod", "1.0", Path.Combine(Path.GetTempPath(), "installed_mods", "123 (10)"));

            // No real source folder was registered: the cached copy is the only way to show the mod.
            Assert.True(ModHearthManager.TryAddCacheModReference(cacheRef, modrefMap, modPool));
            Assert.Same(cacheRef, modrefMap[new DfhMod("my_mod", 10).ToString()]);
            _ = Assert.Single(modPool);

            // Once a canonical ref for the same id/version exists, the cache copy must not replace it.
            ModReference canonicalRef = CreateReference("my_mod", "1.0", Path.Combine(Path.GetTempPath(), "Mods", "my_mod"));
            modrefMap[new DfhMod("my_mod", 10).ToString()] = canonicalRef;

            Assert.False(ModHearthManager.TryAddCacheModReference(cacheRef, modrefMap, modPool));
            Assert.Same(canonicalRef, modrefMap[new DfhMod("my_mod", 10).ToString()]);
            _ = Assert.Single(modPool);
        }

        private static ModReference CreateReference(string id, string numericVersion, string path) => new(new Dictionary<string, string>
        {
            ["id"] = id,
            ["numeric_version"] = numericVersion,
            ["displayed_version"] = numericVersion,
            ["earliest_compatible_numeric_version"] = numericVersion,
            ["earliest_compatible_displayed_version"] = numericVersion,
            ["author"] = "test",
            ["name"] = id,
            ["description"] = string.Empty,
            ["steam_file_id"] = string.Empty,
            ["steam_title"] = string.Empty,
            ["steam_description"] = string.Empty,
            ["src_dir"] = path,
        });
    }
}
