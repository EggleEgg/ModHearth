using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModHearth.Models;
using ModHearth.Utilities.Steam;

namespace ModHearth.Utilities.Logging
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
    public static class ModUpdateLogger
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
        private static readonly object fileGate = new();

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
                        try
                        {
                            File.Move(oldPath, newPath);
                        }
                        catch (IOException)
                        {
                            File.Copy(oldPath, newPath, true);
                            File.Delete(oldPath);
                        }
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
                try
                {
                    if (!File.Exists(LogPath))
                    {
                        Loggers.Diag.Log($"ModUpdateLogger: Log file does not exist at path: {LogPath}");
                        return [];
                    }

                    FileInfo fileInfo = new(LogPath);
                    Loggers.Diag.Log($"ModUpdateLogger: Loading log file from '{LogPath}'. Size: {fileInfo.Length} bytes, LastWrite: {fileInfo.LastWriteTimeUtc} UTC");

                    string json = File.ReadAllText(LogPath);
                    Loggers.Diag.Log($"ModUpdateLogger: Read JSON content length: {json.Length} chars");

                    List<ModUpdateLogEntry>? entries = JsonSerializer.Deserialize<List<ModUpdateLogEntry>>(json, JsonFileStore.CaseInsensitive);
                    int count = entries?.Count ?? 0;
                    Loggers.Diag.Log($"ModUpdateLogger: Successfully deserialized {count} log entries.");

                    return entries ?? [];
                }
                catch (Exception ex)
                {
                    Loggers.Diag.Log($"ModUpdateLogger: Error loading or deserializing log file at '{LogPath}': {ex.Message}");
                    AppLogging.LogException("ModUpdateLogger failed to load entries", ex);
                    return [];
                }
            }
        }

        public static void ClearEntries()
        {
            lock (fileGate)
            {
                try
                {
                    if (File.Exists(LogPath))
                    {
                        File.WriteAllText(LogPath, "[]");
                    }
                }
                catch (Exception ex)
                {
                    AppLogging.LogException("Failed to clear update log entries", ex);
                }
            }
        }

        public static void LogUnsubscribe(IEnumerable<ModReference> mods, HashSet<string> activeIds)
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
                        entries.Add(BuildEntryFromModReference(modref, active, ModUpdateChangeType.Deleted, DateTime.UtcNow));

                        if (previous.Remove(id))
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
                    AppLogging.LogException("Failed to log unsubscribe action", ex);
                }
            }
        }

        public static void LogRedownload(IEnumerable<ModReference> mods, HashSet<string> activeIds)
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
                        entries.Add(BuildEntryFromModReference(modref, active, ModUpdateChangeType.Updated, DateTime.UtcNow));

                        // Update the snapshot entry for this mod
                        string path = ResolveCanonicalPath(modref.Path ?? string.Empty);
                        ModUpdateSnapshotEntry entry = BuildSnapshotEntry(modref, id, path);
                        entry.QuickStamp = BuildLocalQuickStamp(path);
                        EnsureLocalDeepStamp(entry);

                        previous[id] = entry;
                        snapshotChanged = true;
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
                    AppLogging.LogException("Failed to log redownload action", ex);
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
                    _ = Parallel.For(0, currentList.Count, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    }, i =>
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
            _ = Parallel.For(0, modList.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
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
            if (path.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("workshop", StringComparison.OrdinalIgnoreCase) >= 0)
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
        {
            string a = (left ?? string.Empty).Trim();
            string b = (right ?? string.Empty).Trim();
            if (a.Length == 0 || b.Length == 0)
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

            return string.Equals(
                Path.GetFullPath(a),
                Path.GetFullPath(b),
                StringComparison.OrdinalIgnoreCase);
        }

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
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                EnsureLocalDeepStamp);
        }

        private static bool IsLocalSource(string? sourceType) => string.Equals(sourceType?.Trim(), "Local", StringComparison.OrdinalIgnoreCase);

        internal static string BuildLocalQuickStamp(string? modPath)
        {
            string normalizedPath = ResolveCanonicalPath(modPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
                return string.Empty;

            try
            {
                long dirTicks = Directory.GetLastWriteTimeUtc(normalizedPath).Ticks;
                string infoPath = Path.Combine(normalizedPath, "info.txt");
                long infoTicks = 0;
                long infoSize = 0;
                if (File.Exists(infoPath))
                {
                    FileInfo info = new(infoPath);
                    infoTicks = info.LastWriteTimeUtc.Ticks;
                    infoSize = info.Length;
                }

                return $"{dirTicks}:{infoTicks}:{infoSize}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildLocalDeepStamp(string? modPath)
        {
            string normalizedPath = ResolveCanonicalPath(modPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
                return string.Empty;

            try
            {
                List<string> metadata = [];
                foreach (string filePath in Directory.EnumerateFiles(normalizedPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fileInfo = new(filePath);
                        string relativePath = Path.GetRelativePath(normalizedPath, filePath);
                        if (Path.IsPathRooted(relativePath) && !relativePath.StartsWith("..", StringComparison.Ordinal))
                        {
                            relativePath = Path.GetFileName(filePath);
                        }
                        relativePath = relativePath
                            .Replace('\\', '/')
                            .Trim();
                        metadata.Add($"{relativePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}");
                    }
                    catch
                    {
                        // Ignore inaccessible files and continue fingerprinting.
                    }
                }

                StringComparer comparer = PathHelper.Comparer;
                metadata.Sort(comparer);

                using SHA256 sha256 = SHA256.Create();
                foreach (string line in metadata)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                    _ = sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }

                _ = sha256.TransformFinalBlock([], 0, 0);
                return Convert.ToHexString(sha256.Hash ?? []);
            }
            catch
            {
                return string.Empty;
            }
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

            string json = SerializeEntries(existing);
            int removedCount = TrimEntriesToLineLimit(existing, ref json, MaxLogLines);
            if (removedCount > 0)
            {
                Console.WriteLine(
                    $"[ModUpdateLog] Deleted {removedCount} old log entr{(removedCount == 1 ? "y" : "ies")} to keep max {MaxLogLines} lines.");
            }

            File.WriteAllText(LogPath, json);
        }

        private static int TrimEntriesToLineLimit(List<ModUpdateLogEntry> entries, ref string json, int maxLines)
        {
            if (entries == null || maxLines <= 0)
                return 0;

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

        private static List<ModUpdateLogEntry> BuildWorkshopUpdateEntries(
            IEnumerable<ModReference> mods,
            HashSet<string> activeIds,
            IEnumerable<string>? workshopAcfPaths)
        {
            static void LogInfo(string message)
            {
                Loggers.Steam.Log(message);
            }

            List<ModUpdateLogEntry> entries = [];
            if (!SteamProcessHelper.TryDetectSteamProcess(out List<string> runningProcesses))
            {
                LogInfo("Steam workshop update scan skipped: no active Steam process detected.");
                return entries;
            }

            LogInfo($"Steam workshop update scan started. Active Steam process(es): {string.Join(", ", runningProcesses)}.");

            StringComparer pathComparer = PathHelper.Comparer;
            List<string> acfPaths = workshopAcfPaths?
                .Select(ResolveCanonicalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(pathComparer)
                .ToList() ?? [];

            LogInfo($"Steam workshop update scan: {acfPaths.Count} workshop ACF path(s) received.");
            if (acfPaths.Count == 0)
                return entries;

            Dictionary<string, long> currentUpdates = LoadWorkshopUpdateTimes(acfPaths);
            LogInfo($"Steam workshop update scan: parsed {currentUpdates.Count} workshop item timestamps.");
            if (currentUpdates.Count == 0)
                return entries;

            bool snapshotExists = File.Exists(WorkshopSnapshotPath);
            Dictionary<string, long> previousUpdates = LoadWorkshopSnapshot();
            if (!snapshotExists)
            {
                LogInfo("Steam workshop snapshot missing. Creating baseline snapshot.");
                SaveWorkshopSnapshot(currentUpdates);
                return entries;
            }

            Dictionary<string, ModReference> steamIdMap = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in mods)
            {
                if (modref == null)
                    continue;
                string steamId = ResolveSteamId(modref);
                if (string.IsNullOrWhiteSpace(steamId))
                    continue;
                if (!steamIdMap.ContainsKey(steamId))
                    steamIdMap[steamId] = modref;
            }

            int unchangedCount = 0;
            int unmappedCount = 0;
            foreach (KeyValuePair<string, long> kvp in currentUpdates)
            {
                if (previousUpdates.TryGetValue(kvp.Key, out long previousTime) && kvp.Value <= previousTime)
                {
                    unchangedCount++;
                    continue;
                }

                if (!steamIdMap.TryGetValue(kvp.Key, out ModReference? modref))
                {
                    unmappedCount++;
                    continue;
                }

                if (modref.IsIgnored)
                    continue;

                DateTime? timestamp = TryConvertUnixTime(kvp.Value);
                bool isActive = activeIds.Contains(modref.ID ?? string.Empty);
                entries.Add(BuildEntryFromModReference(modref, isActive, ModUpdateChangeType.Updated, timestamp));
            }

            SaveWorkshopSnapshot(currentUpdates);
            LogInfo(
                $"Steam workshop update scan completed. Logged {entries.Count} updated mod(s), {unchangedCount} unchanged item(s), {unmappedCount} unmapped item(s).");
            return entries;
        }

        private static Dictionary<string, long> LoadWorkshopUpdateTimes(IEnumerable<string> acfPaths)
        {
            Dictionary<string, long> updates = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in acfPaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                try
                {
                    string content = File.ReadAllText(path);
                    Dictionary<string, object> root = SteamAcfKeyValueParser.Parse(content);
                    if (!SteamAcfKeyValueParser.TryGetAppWorkshop(root, out Dictionary<string, object>? appDict))
                        continue;

                    MergeWorkshopSection(appDict, "WorkshopItemsInstalled", updates);
                    MergeWorkshopSection(appDict, "WorkshopItemDetails", updates);
                }
                catch (Exception ex)
                {
                    Loggers.Steam.LogError($"Failed to parse workshop ACF '{path}': {ex.Message}");
                }
            }

            return updates;
        }

        private static void MergeWorkshopSection(
            Dictionary<string, object> appDict,
            string sectionName,
            Dictionary<string, long> updates)
        {
            if (!SteamAcfKeyValueParser.TryGetSection(appDict, sectionName, out Dictionary<string, object>? sectionDict))
                return;

            foreach (KeyValuePair<string, object> kvp in sectionDict)
            {
                if (kvp.Value is not Dictionary<string, object> itemDict)
                    continue;

                if (!itemDict.TryGetValue("timeupdated", out object? timeObj))
                    continue;

                if (!TryParseTimeUpdated(timeObj, out long timeUpdated))
                    continue;

                if (updates.TryGetValue(kvp.Key, out long existing))
                    updates[kvp.Key] = Math.Max(existing, timeUpdated);
                else
                    updates[kvp.Key] = timeUpdated;
            }
        }

        private static bool TryParseTimeUpdated(object timeObj, out long timeUpdated)
        {
            timeUpdated = 0;
            switch (timeObj)
            {
                case long longValue:
                    timeUpdated = longValue;
                    return true;
                case int intValue:
                    timeUpdated = intValue;
                    return true;
                case string text when long.TryParse(text, out long parsed):
                    timeUpdated = parsed;
                    return true;
                default:
                    return false;
            }
        }

        private static Dictionary<string, long> LoadWorkshopSnapshot()
            => LoadSnapshotMap<SteamWorkshopSnapshotEntry, long>(
                WorkshopSnapshotPath, entry => entry.SteamId, entry => entry.TimeUpdated);

        private static void SaveWorkshopSnapshot(Dictionary<string, long> snapshot)
            => SaveSnapshotFile(WorkshopSnapshotPath,
                snapshot.Select(kv => new SteamWorkshopSnapshotEntry { SteamId = kv.Key, TimeUpdated = kv.Value }));

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