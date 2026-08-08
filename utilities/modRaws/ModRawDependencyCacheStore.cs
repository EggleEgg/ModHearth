using System.Text.Json;

namespace ModHearth.Utilities
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
                try
                {
                    if (!File.Exists(CachePath))
                        return new Dictionary<string, ModRawDependencyInfo>(StringComparer.OrdinalIgnoreCase);

                    string json = File.ReadAllText(CachePath);
                    CacheFile? file = JsonSerializer.Deserialize<CacheFile>(json);
                    if (file == null || file.SchemaVersion != CurrentSchemaVersion || file.Entries == null)
                        return new Dictionary<string, ModRawDependencyInfo>(StringComparer.OrdinalIgnoreCase);

                    Dictionary<string, ModRawDependencyInfo> map = new(StringComparer.OrdinalIgnoreCase);
                    foreach (ModRawDependencyInfo entry in file.Entries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.ModId))
                            continue;
                        map[BuildKey(entry.ModId, entry.NumericVersion)] = entry;
                    }
                    return map;
                }
                catch
                {
                    return new Dictionary<string, ModRawDependencyInfo>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public static void Save(IEnumerable<ModRawDependencyInfo> entries)
        {
            lock (gate)
            {
                try
                {
                    string? directory = Path.GetDirectoryName(CachePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        _ = Directory.CreateDirectory(directory);

                    CacheFile file = new() { SchemaVersion = CurrentSchemaVersion, Entries = entries.ToList() };
                    string json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(CachePath, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ModRawCache] ERROR: Failed to write '{CachePath}': {ex.Message}");
                }
            }
        }

        public static string BuildKey(string modId, string numericVersion)
            => $"{modId}|{numericVersion}";

        private sealed class CacheFile
        {
            public int SchemaVersion { get; set; }
            public List<ModRawDependencyInfo>? Entries { get; set; }
        }
    }
}