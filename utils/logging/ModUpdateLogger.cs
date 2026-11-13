using ModHearth.Config;
using ModHearth.Manager;
using ModHearth.Models;
using ModHearth.Utils.IO;
using ModHearth.Utils.Thread;

namespace ModHearth.Utils.Logging
{
    public enum ModUpdateChangeType
    {
        Added,
        Deleted,
        Updated
    }

    public sealed class ModUpdateLogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string ModId { get; set; } = string.Empty;
        public string ModName { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public bool Active { get; set; }
        public string Path { get; set; } = string.Empty;
        public string SteamId { get; set; } = string.Empty;
        public ModUpdateChangeType ChangeType { get; set; }
    }

    internal sealed class ModUpdateSnapshotEntry
    {
        public string ModId { get; set; } = string.Empty;
        public string ModName { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string SteamId { get; set; } = string.Empty;
        public string QuickStamp { get; set; } = string.Empty;
        public string DeepStamp { get; set; } = string.Empty;
        public bool IsIgnored { get; set; }
    }
    /// <summary>
    /// Ensures that mod update logs and folder snapshots are recorded and mantained
    /// </summary>
    public static partial class ModUpdateLogger
    {
        private static readonly string OldLogDirForMigration = Path.Combine(AppContext.BaseDirectory, "logs");
        private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "logs");
        private static readonly string MetadataDir = Path.Combine(AppContext.BaseDirectory, "metadata");

        private static readonly string LogPath = Path.Combine(MetadataDir, "mod_update_log.json");
        private static readonly string SnapshotPath = Path.Combine(MetadataDir, "mod_folder_snapshot.json");
        private static readonly string WorkshopSnapshotPath = Path.Combine(MetadataDir, "steam_workshop_snapshot.json");
        private static string ResolveCanonicalPath(string path) => ConfigManager.ResolveCanonicalPath(path);

        private const int MaxLogLines = 5000;

        // Guards all three files as a unit. RecordChanges does a read-modify-write across all of them, and
        // LoadEntries (used by the UI to display the log) must not read the log file mid-append.
        private static readonly Lock fileGate = new();

        static ModUpdateLogger()
        {
            EnsureMetadataDirectoryAndMigrateOldFiles();

            // Ensure the general logs directory exists.
            if (!Directory.Exists(LogDir))
            {
                _ = Directory.CreateDirectory(LogDir);
            }
        }

        private static void EnsureMetadataDirectoryAndMigrateOldFiles()
        {
            // Ensure metadata directory exists
            if (!Directory.Exists(MetadataDir))
            {
                _ = Directory.CreateDirectory(MetadataDir);
            }

            // List of files to migrate
            string[] filesToMigrate =
            [
                "mod_update_log.json",
                "mod_folder_snapshot.json",
                "steam_workshop_snapshot.json"
            ];

            foreach (string? fileName in filesToMigrate)
            {
                string oldPath = Path.Combine(OldLogDirForMigration, fileName);
                string newPath = Path.Combine(MetadataDir, fileName);

                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    try
                    {
                        FileSystemHelper.MoveFile(oldPath, newPath);
                        Loggers.Diag.Log($"Migrated {fileName} from logs/ to metadata/.");
                    }
                    catch (Exception ex)
                    {
                        AppLogging.LogException($"Failed to migrate {fileName}", ex);
                    }
                }
            }
        }

        public static IReadOnlyList<ModUpdateLogEntry> LoadEntries()
        {
            lock (fileGate)
            {
                if (!File.Exists(LogPath))
                {
                    Loggers.Diag.Log($"ModUpdateLogger: Log file does not exist at path: {LogPath}");
                    return [];
                }

                if (!JsonFileStore.TryLoad(LogPath, "ModUpdateLog", out List<ModUpdateLogEntry>? entries))
                    return [];

                Loggers.Diag.Log($"ModUpdateLogger: Successfully deserialized {entries.Count} log entries.");
                return entries;
            }
        }

        public static void ClearEntries()
        {
            lock (fileGate)
            {
                _ = JsonFileStore.TrySave(LogPath, new List<ModUpdateLogEntry>(), "ModUpdateLog");
            }
        }

        public static void LogUnsubscribe(IEnumerable<ModReference> mods, HashSet<string> activeIds)
            => LogModChanges(mods, activeIds, ModUpdateChangeType.Deleted, refreshSnapshot: false);

        public static void LogRedownload(IEnumerable<ModReference> mods, HashSet<string> activeIds)
            => LogModChanges(mods, activeIds, ModUpdateChangeType.Updated, refreshSnapshot: true);

        /// <summary>
        /// Appends one entry per mod and updates the folder snapshot in the same locked transaction.
        /// Unsubscribe removes the mod's snapshot entry; redownload refreshes it (and its stamps) so
        /// the next scan does not report the same change again.
        /// </summary>
        private static void LogModChanges(
            IEnumerable<ModReference> mods,
            HashSet<string> activeIds,
            ModUpdateChangeType changeType,
            bool refreshSnapshot)
        {
            lock (fileGate)
            {
                try
                {
                    List<ModUpdateLogEntry> entries = [];
                    Dictionary<string, ModUpdateSnapshotEntry> previous = LoadSnapshot();
                    bool snapshotChanged = false;

                    foreach (ModReference modref in mods)
                    {
                        if (modref == null)
                            continue;

                        string id = modref.ID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        bool active = activeIds.Contains(id);
                        entries.Add(BuildEntryFromModReference(modref, active, changeType, DateTime.UtcNow));

                        if (refreshSnapshot)
                        {
                            // Update the snapshot entry for this mod.
                            string path = ResolveCanonicalPath(modref.Path ?? string.Empty);
                            ModUpdateSnapshotEntry entry = BuildSnapshotEntry(modref, id, path);
                            entry.QuickStamp = BuildLocalQuickStamp(path);
                            EnsureLocalDeepStamp(entry);

                            previous[id] = entry;
                            snapshotChanged = true;
                        }
                        else if (previous.Remove(id))
                        {
                            snapshotChanged = true;
                        }
                    }

                    if (entries.Count > 0)
                    {
                        AppendEntries(entries);
                    }

                    if (snapshotChanged)
                    {
                        SaveSnapshot(previous);
                    }
                }
                catch (Exception ex)
                {
                    AppLogging.LogException(
                        refreshSnapshot ? "Failed to log redownload action" : "Failed to log unsubscribe action",
                        ex);
                }
            }
        }

        public static void RecordChanges(
            IEnumerable<ModReference> mods,
            IEnumerable<DfhMod> activeMods,
            IEnumerable<string>? workshopAcfPaths = null)
        {
            lock (fileGate)
            {
                try
                {
                    Dictionary<string, ModUpdateSnapshotEntry> current = BuildSnapshot(mods);
                    if (!File.Exists(SnapshotPath))
                    {
                        InitializeLocalFingerprints(current.Values);
                        SaveSnapshot(current);
                        return;
                    }

                    Dictionary<string, ModUpdateSnapshotEntry> previous = LoadSnapshot();
                    HashSet<string> activeIds = new(
                        activeMods.Select(m => m.Id),
                        StringComparer.OrdinalIgnoreCase);

                    List<ModUpdateSnapshotEntry> currentList = [.. current.Values];

                    // Parallelize TryDetectLocalUpdate and EnsureLocalDeepStamp passes per mod.
                    // Computed into an index-aligned array to preserve stable entry ordering.
                    List<ModUpdateLogEntry>?[] perEntryResults = new List<ModUpdateLogEntry>?[currentList.Count];
                    _ = Parallel.For(0, currentList.Count, Parallelism.CpuBound, i =>
                    {
                        ModUpdateSnapshotEntry currentEntry = currentList[i];
                        List<ModUpdateLogEntry> localEntries = [];

                        if (!previous.TryGetValue(currentEntry.ModId, out ModUpdateSnapshotEntry? oldEntry))
                        {
                            EnsureLocalDeepStamp(currentEntry);
                            localEntries.Add(BuildEntry(currentEntry, activeIds.Contains(currentEntry.ModId), ModUpdateChangeType.Added));
                        }
                        else if (!PathsMatch(oldEntry.Path, currentEntry.Path))
                        {
                            EnsureLocalDeepStamp(currentEntry);
                            localEntries.Add(BuildEntry(oldEntry, activeIds.Contains(oldEntry.ModId), ModUpdateChangeType.Deleted));
                            localEntries.Add(BuildEntry(currentEntry, activeIds.Contains(currentEntry.ModId), ModUpdateChangeType.Added));
                        }
                        else if (TryDetectLocalUpdate(oldEntry, currentEntry))
                        {
                            localEntries.Add(BuildEntry(currentEntry, activeIds.Contains(currentEntry.ModId), ModUpdateChangeType.Updated));
                        }

                        perEntryResults[i] = localEntries.Count > 0 ? localEntries : null;
                    });

                    List<ModUpdateLogEntry> entries = [];
                    foreach (List<ModUpdateLogEntry>? localEntries in perEntryResults)
                    {
                        if (localEntries != null)
                            entries.AddRange(localEntries);
                    }

                    foreach (ModUpdateSnapshotEntry previousEntry in previous.Values.Where(previousEntry => !current.ContainsKey(previousEntry.ModId)))
                    {
                        if (previousEntry.IsIgnored)
                            continue;

                        entries.Add(BuildEntry(previousEntry, activeIds.Contains(previousEntry.ModId), ModUpdateChangeType.Deleted));
                    }

                    entries.AddRange(BuildWorkshopUpdateEntries(mods, activeIds, workshopAcfPaths));

                    if (entries.Count > 0)
                        AppendEntries(entries);

                    SaveSnapshot(current);
                }
                catch (Exception ex)
                {
                    AppLogging.LogException("Failed to record mod update changes", ex);
                }
            }
        }

        private static Dictionary<string, ModUpdateSnapshotEntry> BuildSnapshot(IEnumerable<ModReference> mods)
        {
            List<ModReference> modList = mods as List<ModReference> ?? [.. mods];

            ModUpdateSnapshotEntry?[] results = new ModUpdateSnapshotEntry?[modList.Count];
            _ = Parallel.For(0, modList.Count, Parallelism.CpuBound, i =>
            {
                ModReference? modref = modList[i];
                if (modref == null)
                    return;

                string id = modref.ID?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    return;

                string path = ResolveCanonicalPath(modref.Path ?? string.Empty);
                if (modref.IsIgnored)
                    return;

                ModUpdateSnapshotEntry entry = BuildSnapshotEntry(modref, id, path);
                entry.QuickStamp = BuildLocalQuickStamp(path);
                results[i] = entry;
            });

            Dictionary<string, ModUpdateSnapshotEntry> snapshot = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModUpdateSnapshotEntry? entry in results)
            {
                if (entry == null || snapshot.ContainsKey(entry.ModId))
                    continue;
                snapshot[entry.ModId] = entry;
            }

            return snapshot;
        }

        private static ModUpdateSnapshotEntry BuildSnapshotEntry(ModReference modref, string id, string path) => new()
        {
            ModId = id,
            ModName = string.IsNullOrWhiteSpace(modref.Name) ? id : modref.Name,
            SourceType = GetSourceType(modref),
            Path = path,
            SteamId = ResolveSteamId(modref),
            IsIgnored = modref.IsIgnored
        };

        private static ModUpdateLogEntry BuildEntry(ModUpdateSnapshotEntry entry, bool active, ModUpdateChangeType changeType) => BuildEntry(entry, active, changeType, null);

        private static ModUpdateLogEntry BuildEntry(
            ModUpdateSnapshotEntry entry,
            bool active,
            ModUpdateChangeType changeType,
            DateTime? timestampUtc) => new()
            {
                TimestampUtc = timestampUtc ?? DateTime.UtcNow,
                ModId = entry.ModId,
                ModName = entry.ModName,
                SourceType = entry.SourceType,
                Active = active,
                Path = entry.Path,
                SteamId = entry.SteamId,
                ChangeType = changeType
            };

        private static ModUpdateLogEntry BuildEntryFromModReference(
            ModReference modref,
            bool active,
            ModUpdateChangeType changeType,
            DateTime? timestampUtc)
        {
            ModUpdateSnapshotEntry entry = BuildSnapshotEntry(
                modref,
                modref.ID?.Trim() ?? string.Empty,
                ResolveCanonicalPath(modref.Path ?? string.Empty));

            return BuildEntry(entry, active, changeType, timestampUtc);
        }

        private static string GetSourceType(ModReference modref)
        {
            if (ModHearthManager.TryGetSteamWorkshopItemId(modref, out _))
                return "Steam";

            string path = modref.Path ?? string.Empty;
            if (path.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("workshop", StringComparison.OrdinalIgnoreCase))
            {
                return "Steam";
            }

            return "Local";
        }

        private static string ResolveSteamId(ModReference? modref)
        {
            if (modref == null)
                return string.Empty;

            if (ModHearthManager.TryGetSteamWorkshopItemId(modref, out string steamId))
                return steamId;

            return modref.SteamID?.Trim() ?? string.Empty;
        }

        private static bool PathsMatch(string? left, string? right)
            => PathHelper.PathsEqualNormalized(left, right);

        private static bool TryDetectLocalUpdate(ModUpdateSnapshotEntry previousEntry, ModUpdateSnapshotEntry currentEntry)
        {
            if (!IsLocalSource(currentEntry.SourceType))
                return false;

            if (string.IsNullOrWhiteSpace(currentEntry.Path) || !Directory.Exists(currentEntry.Path))
                return false;

            if (string.IsNullOrWhiteSpace(currentEntry.QuickStamp))
                currentEntry.QuickStamp = BuildLocalQuickStamp(currentEntry.Path);

            if (!string.IsNullOrWhiteSpace(previousEntry.QuickStamp) &&
                string.Equals(previousEntry.QuickStamp, currentEntry.QuickStamp, StringComparison.Ordinal))
            {
                currentEntry.DeepStamp = previousEntry.DeepStamp ?? string.Empty;
                return false;
            }

            string previousDeepStamp = previousEntry.DeepStamp ?? string.Empty;
            if (string.IsNullOrWhiteSpace(previousDeepStamp))
            {
                // One-time baseline for older snapshots to avoid false "Updated" duplicates.
                currentEntry.DeepStamp = BuildLocalDeepStamp(currentEntry.Path);
                return false;
            }

            string currentDeepStamp = BuildLocalDeepStamp(currentEntry.Path);
            if (string.IsNullOrWhiteSpace(currentDeepStamp))
            {
                currentEntry.DeepStamp = previousDeepStamp;
                return false;
            }

            currentEntry.DeepStamp = currentDeepStamp;
            return !string.Equals(previousDeepStamp, currentDeepStamp, StringComparison.Ordinal);
        }

        private static void EnsureLocalDeepStamp(ModUpdateSnapshotEntry entry)
        {
            if (!IsLocalSource(entry.SourceType))
                return;
            if (string.IsNullOrWhiteSpace(entry.Path) || !Directory.Exists(entry.Path))
                return;

            if (string.IsNullOrWhiteSpace(entry.QuickStamp))
                entry.QuickStamp = BuildLocalQuickStamp(entry.Path);
            if (string.IsNullOrWhiteSpace(entry.DeepStamp))
                entry.DeepStamp = BuildLocalDeepStamp(entry.Path);
        }

        private static void InitializeLocalFingerprints(IEnumerable<ModUpdateSnapshotEntry> entries)
        {
            if (entries == null)
                return;

            // Every mod's deep stamp is independent of every other mod's, so this is safe to parallelize, no shared state between iterations.
            _ = Parallel.ForEach(
                entries,
                Parallelism.CpuBound,
                EnsureLocalDeepStamp);
        }

        private static Dictionary<string, ModUpdateSnapshotEntry> LoadSnapshot()
            => LoadSnapshotMap<ModUpdateSnapshotEntry, ModUpdateSnapshotEntry>(
                SnapshotPath, entry => entry.ModId, entry => entry);

        private static void SaveSnapshot(Dictionary<string, ModUpdateSnapshotEntry> snapshot)
            => SaveSnapshotFile(SnapshotPath, snapshot.Values);

        /// <summary>
        /// Shared loader for the two mod-update snapshot files: JSON list in, case-insensitive map out.
        /// </summary>
        private static Dictionary<string, TValue> LoadSnapshotMap<TEntry, TValue>(
            string path,
            Func<TEntry, string> keySelector,
            Func<TEntry, TValue> valueSelector)
        {
            Dictionary<string, TValue> map = new(StringComparer.OrdinalIgnoreCase);
            if (!JsonFileStore.TryLoad(path, "ModUpdateLog", out List<TEntry>? entries))
                return map;

            foreach (TEntry entry in entries)
            {
                if (entry is null)
                    continue;

                string key = keySelector(entry);
                if (!string.IsNullOrWhiteSpace(key))
                    _ = map.TryAdd(key, valueSelector(entry));
            }

            return map;
        }

        private static void SaveSnapshotFile<TEntry>(string path, IEnumerable<TEntry> entries)
            => _ = JsonFileStore.TrySave(path, entries.ToList(), "ModUpdateLog");

        private static void AppendEntries(IEnumerable<ModUpdateLogEntry> newEntries)
        {
            List<ModUpdateLogEntry> existing = [.. LoadEntries()];
            existing.AddRange(newEntries);

            int removedCount = TrimEntriesToLineLimit(existing, MaxLogLines);
            if (removedCount > 0)
            {
                Console.WriteLine(
                    $"[ModUpdateLog] Deleted {removedCount} old log entr{(removedCount == 1 ? "y" : "ies")} to keep max {MaxLogLines} lines.");
            }

            _ = JsonFileStore.TrySave(LogPath, existing, "ModUpdateLog");
        }

        private static int TrimEntriesToLineLimit(List<ModUpdateLogEntry> entries, int maxLines)
        {
            if (maxLines <= 0)
                return 0;

            string json = SerializeEntries(entries);
            int currentLines = CountLines(json);
            if (currentLines <= maxLines)
                return 0;

            int removedCount = 0;
            while (entries.Count > 0 && currentLines > maxLines)
            {
                int estimatedLinesPerEntry = Math.Max(1, currentLines / Math.Max(entries.Count, 1));
                int overflow = currentLines - maxLines;
                int removeNow = Math.Max(1, overflow / estimatedLinesPerEntry);
                removeNow = Math.Min(removeNow, entries.Count);

                entries.RemoveRange(0, removeNow);
                removedCount += removeNow;

                json = SerializeEntries(entries);
                currentLines = CountLines(json);
            }

            return removedCount;
        }

        private static string SerializeEntries(List<ModUpdateLogEntry> entries) => JsonFileStore.Serialize(entries);

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int count = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    count++;
            }

            return count;
        }

        private static DateTime? TryConvertUnixTime(long seconds)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class SteamWorkshopSnapshotEntry
    {
        public string SteamId { get; set; } = string.Empty;
        public long TimeUpdated { get; set; }
    }
}
