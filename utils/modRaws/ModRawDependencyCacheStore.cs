using System.Diagnostics.CodeAnalysis;
using ModHearth.Models;
using ModHearth.Utils.IO;

namespace ModHearth.Utils.ModRaws
{
    internal static class ModRawDependencyCacheStore
    {
        public static readonly string CachePath = Path.Combine(AppContext.BaseDirectory, "metadata", "mod_raw_dependency_cache.json");
        public static readonly string trimmedCachePath = Path.Combine("metadata", "mod_raw_dependency_cache.json");
        private static readonly Lock gate = new();

        // Bump whenever ToDependencyInfo()'s serialized format or the scanner's interpretation of
        // the raw files changes in a way that makes older cached rows unsafe to reuse as-is (for
        // example the ObjectType:Id key format, or reaction definitions under a wrong OBJECT
        // header). Load() discards a cache file written under a different version wholesale,
        // forcing a one-time full rescan rather than silently mixing old and new scan results.
        internal const int CurrentSchemaVersion = 5;

        public static Dictionary<string, ModRawDependencyInfo> Load()
        {
            lock (gate)
            {
                Dictionary<string, ModRawDependencyInfo> map = new(StringComparer.OrdinalIgnoreCase);
                if (!TryLoadEntriesCore(CachePath, out List<ModRawDependencyInfo>? entries, out _))
                    return map;

                foreach (ModRawDependencyInfo entry in entries)
                    map[BuildKey(entry.ModId, entry.NumericVersion)] = entry;

                return map;
            }
        }

        /// <summary>
        /// Loads the raw cache from an explicit path. Unlike <see cref="Load"/> this keeps every
        /// version row, so maintenance tooling can compare the file with the active modlist instead
        /// of silently collapsing entries with the same id. <paramref name="schemaVersion"/> is
        /// reported even when it does not match <see cref="CurrentSchemaVersion"/>.
        /// </summary>
        public static bool TryLoadEntries(
            string path,
            [NotNullWhen(true)] out List<ModRawDependencyInfo>? entries,
            out int schemaVersion)
        {
            lock (gate)
                return TryLoadEntriesCore(path, out entries, out schemaVersion);
        }

        private static bool TryLoadEntriesCore(
            string path,
            [NotNullWhen(true)] out List<ModRawDependencyInfo>? entries,
            out int schemaVersion)
        {
            entries = null;
            schemaVersion = 0;

            if (!JsonFileStore.TryLoad(path, "ModRawCache", out CacheFile? file))
                return false;

            schemaVersion = file.SchemaVersion;
            if (file.SchemaVersion != CurrentSchemaVersion || file.Entries == null)
                return false;

            entries = [.. file.Entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.ModId))];
            return true;
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