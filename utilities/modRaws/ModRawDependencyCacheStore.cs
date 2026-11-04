using ModHearth.Models;

namespace ModHearth.Utilities.ModRaws
{
    internal static class ModRawDependencyCacheStore
    {
        public static readonly string CachePath = Path.Combine(AppContext.BaseDirectory, "metadata", "mod_raw_dependency_cache.json");
        public static readonly string trimmedCachePath = Path.Combine("metadata", "mod_raw_dependency_cache.json");
        private static readonly object gate = new();

        // Bump whenever ToDependencyInfo()'s serialized format changes in a way that makes older
        // cached rows unsafe to reuse as-is (e.g. the ObjectType:Id key format introduced here).
        // Load() discards a cache file written under a different version wholesale, forcing a
        // one-time full rescan rather than silently mixing old- and new-format data.
        private const int CurrentSchemaVersion = 3;

        public static Dictionary<string, ModRawDependencyInfo> Load()
        {
            lock (gate)
            {
                Dictionary<string, ModRawDependencyInfo> map = new(StringComparer.OrdinalIgnoreCase);
                if (!JsonFileStore.TryLoad(CachePath, "ModRawCache", out CacheFile? file)
                    || file.SchemaVersion != CurrentSchemaVersion || file.Entries == null)
                    return map;

                foreach (ModRawDependencyInfo entry in file.Entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.ModId))
                        continue;
                    map[BuildKey(entry.ModId, entry.NumericVersion)] = entry;
                }
                return map;
            }
        }

        public static void Save(IEnumerable<ModRawDependencyInfo> entries)
        {
            lock (gate)
                _ = JsonFileStore.TrySave(CachePath, new CacheFile { SchemaVersion = CurrentSchemaVersion, Entries = [.. entries] }, "ModRawCache");
        }

        public static string BuildKey(string modId, string numericVersion) => $"{modId}|{numericVersion}";

        private sealed class CacheFile
        {
            public int SchemaVersion { get; set; }
            public List<ModRawDependencyInfo>? Entries { get; set; }
        }
    }
}