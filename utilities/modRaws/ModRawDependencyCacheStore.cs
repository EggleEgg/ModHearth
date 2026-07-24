using System.Text.Json;

namespace ModHearth.Utilities
{
    // Persists ModRawDependencyInfo entries to a single JSON file so mods whose objects/ folder 
    // hasn't changed since the last scan (same modId + numericVersion + folder LastWriteTimeUtc) skip re-scanning.
    internal static class ModRawDependencyCacheStore
    {
        public static readonly string CachePath = Path.Combine(AppContext.BaseDirectory, "metadata", "mod_raw_dependency_cache.json");
        private static readonly object gate = new();

        public static Dictionary<string, ModRawDependencyInfo> Load()
        {
            lock (gate)
            {
                try
                {
                    if (!File.Exists(CachePath))
                        return new Dictionary<string, ModRawDependencyInfo>(StringComparer.OrdinalIgnoreCase);

                    string json = File.ReadAllText(CachePath);
                    List<ModRawDependencyInfo>? entries = JsonSerializer.Deserialize<List<ModRawDependencyInfo>>(json);
                    if (entries == null)
                        return new Dictionary<string, ModRawDependencyInfo>(StringComparer.OrdinalIgnoreCase);

                    Dictionary<string, ModRawDependencyInfo> map = new(StringComparer.OrdinalIgnoreCase);
                    foreach (ModRawDependencyInfo entry in entries)
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
                        Directory.CreateDirectory(directory);

                    string json = JsonSerializer.Serialize(entries.ToList(), new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(CachePath, json);
                }
                catch
                {
                    Console.WriteLine("[ModRawCache] ERROR: Failed to write entries to file");
                }
            }
        }

        public static string BuildKey(string modId, string numericVersion)
            => $"{modId}|{numericVersion}";
    }
}