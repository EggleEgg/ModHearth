using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModHearth.Utilities;

namespace ModHearth;

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
}

public static class ModUpdateLogger
{
    private static readonly string OldLogDirForMigration = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string MetadataDir = Path.Combine(AppContext.BaseDirectory, "metadata");

    private static readonly string LogPath = Path.Combine(MetadataDir, "mod_update_log.json");
    private static readonly string SnapshotPath = Path.Combine(MetadataDir, "mod_folder_snapshot.json");
    private static readonly string WorkshopSnapshotPath = Path.Combine(MetadataDir, "steam_workshop_snapshot.json");
    private static string ResolveCanonicalPath(string path) => ConfigManager.ResolveCanonicalPath(path);

    static ModUpdateLogger()
    {
        EnsureMetadataDirectoryAndMigrateOldFiles();

        // Ensure the general logs directory (for things like updatelog.txt) exists.
        if (!Directory.Exists(LogDir))
        {
            Directory.CreateDirectory(LogDir);
        }
    }

    private static void EnsureMetadataDirectoryAndMigrateOldFiles()
    {
        // Ensure metadata directory exists
        if (!Directory.Exists(MetadataDir))
        {
            Directory.CreateDirectory(MetadataDir);
        }

        // List of files to migrate
        var filesToMigrate = new[]
        {
            "mod_update_log.json",
            "mod_folder_snapshot.json",
            "steam_workshop_snapshot.json"
        };

        foreach (var fileName in filesToMigrate)
        {
            string oldPath = Path.Combine(OldLogDirForMigration, fileName);
            string newPath = Path.Combine(MetadataDir, fileName);

            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                try
                {
                    File.Move(oldPath, newPath);
                    InfoLogger.Log($"Migrated {fileName} from logs/ to metadata/.");
                }
                catch (Exception ex)
                {
                    AppLogging.LogException($"Failed to migrate {fileName}", ex);
                }
            }
        }
    }
    private const int MaxLogLines = 5000;
    // Guards all three files as a unit. RecordChanges does a read-modify-write across all of them, and
    // LoadEntries (used by the UI to display the log) must not read the log file mid-append.
    private static readonly object fileGate = new();

    public static IReadOnlyList<ModUpdateLogEntry> LoadEntries()
    {
        lock (fileGate)
        {
            try
            {
                if (!File.Exists(LogPath))
                    return Array.Empty<ModUpdateLogEntry>();

                string json = File.ReadAllText(LogPath);
                List<ModUpdateLogEntry>? entries = JsonSerializer.Deserialize<List<ModUpdateLogEntry>>(json);
                return entries ?? new List<ModUpdateLogEntry>();
            }
            catch
            {
                return Array.Empty<ModUpdateLogEntry>();
            }
        }
    }

    public static void RecordChanges(
        IEnumerable<ModReference> mods,
        IEnumerable<DFHMod> activeMods,
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
                HashSet<string> activeIds = new HashSet<string>(
                    activeMods.Select(m => m.id),
                    StringComparer.OrdinalIgnoreCase);

                List<ModUpdateLogEntry> entries = new List<ModUpdateLogEntry>();

                foreach (ModUpdateSnapshotEntry currentEntry in current.Values)
                {
                    if (!previous.TryGetValue(currentEntry.ModId, out ModUpdateSnapshotEntry? oldEntry))
                    {
                        EnsureLocalDeepStamp(currentEntry);
                        entries.Add(BuildEntry(currentEntry, activeIds.Contains(currentEntry.ModId), ModUpdateChangeType.Added));
                        continue;
                    }

                    if (!PathsMatch(oldEntry.Path, currentEntry.Path))
                    {
                        EnsureLocalDeepStamp(currentEntry);
                        entries.Add(BuildEntry(oldEntry, activeIds.Contains(oldEntry.ModId), ModUpdateChangeType.Deleted));
                        entries.Add(BuildEntry(currentEntry, activeIds.Contains(currentEntry.ModId), ModUpdateChangeType.Added));
                        continue;
                    }

                    if (TryDetectLocalUpdate(oldEntry, currentEntry))
                        entries.Add(BuildEntry(currentEntry, activeIds.Contains(currentEntry.ModId), ModUpdateChangeType.Updated));
                }

                foreach (ModUpdateSnapshotEntry previousEntry in previous.Values.Where(previousEntry => !current.ContainsKey(previousEntry.ModId)))
                    entries.Add(BuildEntry(previousEntry, activeIds.Contains(previousEntry.ModId), ModUpdateChangeType.Deleted));

                entries.AddRange(BuildWorkshopUpdateEntries(mods, activeIds, workshopAcfPaths));

                if (entries.Count > 0)
                    AppendEntries(entries);

                SaveSnapshot(current);
            }
            catch
            {
                // Ignore logging failures.
            }
        }
    }

    private static Dictionary<string, ModUpdateSnapshotEntry> BuildSnapshot(IEnumerable<ModReference> mods)
    {
        Dictionary<string, ModUpdateSnapshotEntry> snapshot = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModReference modref in mods)
        {
            if (modref == null)
                continue;
            string id = modref.ID?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) || snapshot.ContainsKey(id))
                continue;

            snapshot[id] = new ModUpdateSnapshotEntry
            {
                ModId = id,
                ModName = string.IsNullOrWhiteSpace(modref.name) ? id : modref.name,
                SourceType = GetSourceType(modref),
                Path = ResolveCanonicalPath(modref.path ?? string.Empty),
                SteamId = ResolveSteamId(modref),
                QuickStamp = BuildLocalQuickStamp(modref.path ?? string.Empty),
                DeepStamp = string.Empty
            };
        }

        return snapshot;
    }

    private static ModUpdateLogEntry BuildEntry(ModUpdateSnapshotEntry entry, bool active, ModUpdateChangeType changeType)
    {
        return BuildEntry(entry, active, changeType, null);
    }

    private static ModUpdateLogEntry BuildEntry(
        ModUpdateSnapshotEntry entry,
        bool active,
        ModUpdateChangeType changeType,
        DateTime? timestampUtc)
    {
        return new ModUpdateLogEntry
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
    }

    private static ModUpdateLogEntry BuildEntryFromModReference(
        ModReference modref,
        bool active,
        ModUpdateChangeType changeType,
        DateTime? timestampUtc)
    {
        ModUpdateSnapshotEntry entry = new ModUpdateSnapshotEntry
        {
            ModId = modref.ID?.Trim() ?? string.Empty,
            ModName = string.IsNullOrWhiteSpace(modref.name) ? (modref.ID?.Trim() ?? string.Empty) : modref.name,
            SourceType = GetSourceType(modref),
            Path = ResolveCanonicalPath(modref.path ?? string.Empty),
            SteamId = ResolveSteamId(modref)
        };

        return BuildEntry(entry, active, changeType, timestampUtc);
    }

    private static string GetSourceType(ModReference modref)
    {
        if (ModHearthManager.TryGetSteamWorkshopItemId(modref, out _))
            return "Steam";

        string path = modref.path ?? string.Empty;
        if (path.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("workshop", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Steam";

        return "Local";
    }

    private static string ResolveSteamId(ModReference? modref)
    {
        if (modref == null)
            return string.Empty;

        if (ModHearthManager.TryGetSteamWorkshopItemId(modref, out string steamId))
            return steamId;

        return modref.steamID?.Trim() ?? string.Empty;
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
        Parallel.ForEach(
            entries,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            EnsureLocalDeepStamp);
    }

    private static bool IsLocalSource(string? sourceType)
    {
        return string.Equals(sourceType?.Trim(), "Local", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildLocalQuickStamp(string? modPath)
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
                FileInfo info = new FileInfo(infoPath);
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
            List<string> metadata = new List<string>();
            foreach (string filePath in Directory.EnumerateFiles(normalizedPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(filePath);
                    string relativePath = Path.GetRelativePath(normalizedPath, filePath)
                        .Replace('\\', '/')
                        .Trim();
                    metadata.Add($"{relativePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}");
                }
                catch
                {
                    // Ignore inaccessible files and continue fingerprinting.
                }
            }

            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            metadata.Sort(comparer);

            using SHA256 sha256 = SHA256.Create();
            foreach (string line in metadata)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, ModUpdateSnapshotEntry> LoadSnapshot()
    {
        try
        {
            if (!File.Exists(SnapshotPath))
                return new Dictionary<string, ModUpdateSnapshotEntry>(StringComparer.OrdinalIgnoreCase);

            string json = File.ReadAllText(SnapshotPath);
            List<ModUpdateSnapshotEntry>? entries = JsonSerializer.Deserialize<List<ModUpdateSnapshotEntry>>(json);
            if (entries == null)
                return new Dictionary<string, ModUpdateSnapshotEntry>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, ModUpdateSnapshotEntry> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModUpdateSnapshotEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ModId))
                    continue;
                if (!map.ContainsKey(entry.ModId))
                    map[entry.ModId] = entry;
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, ModUpdateSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveSnapshot(Dictionary<string, ModUpdateSnapshotEntry> snapshot)
    {
        try
        {
            List<ModUpdateSnapshotEntry> entries = snapshot.Values.ToList();
            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SnapshotPath, json);
        }
        catch
        {
            // Ignore snapshot failures.
        }
    }

    private static void AppendEntries(IEnumerable<ModUpdateLogEntry> newEntries)
    {
        List<ModUpdateLogEntry> existing = LoadEntries().ToList();
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

    private static string SerializeEntries(List<ModUpdateLogEntry> entries)
    {
        return JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
    }

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
        List<ModUpdateLogEntry> entries = new List<ModUpdateLogEntry>();
        if (!SteamProcessHelper.TryDetectSteamProcess(out List<string> runningProcesses))
        {
            SteamConnectionLogger.Log("Steam workshop update scan skipped: no active Steam process detected.");
            return entries;
        }

        SteamConnectionLogger.Log(
            $"Steam workshop update scan started. Active Steam process(es): {string.Join(", ", runningProcesses)}.");

        StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        List<string> acfPaths = workshopAcfPaths?
            .Select(ResolveCanonicalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(pathComparer)
            .ToList() ?? new List<string>();

        SteamConnectionLogger.Log($"Steam workshop update scan: {acfPaths.Count} workshop ACF path(s) received.");
        if (acfPaths.Count == 0)
            return entries;

        Dictionary<string, long> currentUpdates = LoadWorkshopUpdateTimes(acfPaths);
        SteamConnectionLogger.Log($"Steam workshop update scan: parsed {currentUpdates.Count} workshop item timestamps.");
        if (currentUpdates.Count == 0)
            return entries;

        bool snapshotExists = File.Exists(WorkshopSnapshotPath);
        Dictionary<string, long> previousUpdates = LoadWorkshopSnapshot();
        if (!snapshotExists)
        {
            SteamConnectionLogger.Log("Steam workshop snapshot missing. Creating baseline snapshot.");
            SaveWorkshopSnapshot(currentUpdates);
            return entries;
        }

        Dictionary<string, ModReference> steamIdMap = new Dictionary<string, ModReference>(StringComparer.OrdinalIgnoreCase);
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

            DateTime? timestamp = TryConvertUnixTime(kvp.Value);
            bool isActive = activeIds.Contains(modref.ID ?? string.Empty);
            entries.Add(BuildEntryFromModReference(modref, isActive, ModUpdateChangeType.Updated, timestamp));
        }

        SaveWorkshopSnapshot(currentUpdates);
        SteamConnectionLogger.Log(
            $"Steam workshop update scan completed. Logged {entries.Count} updated mod(s), {unchangedCount} unchanged item(s), {unmappedCount} unmapped item(s).");
        return entries;
    }

    private static Dictionary<string, long> LoadWorkshopUpdateTimes(IEnumerable<string> acfPaths)
    {
        Dictionary<string, long> updates = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in acfPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            try
            {
                string content = File.ReadAllText(path);
                Dictionary<string, object> root = SteamAcfKeyValueParser.Parse(content);
                if (!root.TryGetValue("AppWorkshop", out object? appObj) || appObj is not Dictionary<string, object> appDict)
                    continue;

                MergeWorkshopSection(appDict, "WorkshopItemsInstalled", updates);
                MergeWorkshopSection(appDict, "WorkshopItemDetails", updates);
            }
            catch (Exception ex)
            {
                SteamConnectionLogger.LogError($"Failed to parse workshop ACF '{path}': {ex.Message}");
            }
        }

        return updates;
    }

    private static void MergeWorkshopSection(
        Dictionary<string, object> appDict,
        string sectionName,
        Dictionary<string, long> updates)
    {
        if (!appDict.TryGetValue(sectionName, out object? sectionObj) ||
            sectionObj is not Dictionary<string, object> sectionDict)
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
        if (timeObj is long longValue)
        {
            timeUpdated = longValue;
            return true;
        }

        if (timeObj is int intValue)
        {
            timeUpdated = intValue;
            return true;
        }

        if (timeObj is string text && long.TryParse(text, out long parsed))
        {
            timeUpdated = parsed;
            return true;
        }

        return false;
    }

    private static Dictionary<string, long> LoadWorkshopSnapshot()
    {
        try
        {
            if (!File.Exists(WorkshopSnapshotPath))
                return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            string json = File.ReadAllText(WorkshopSnapshotPath);
            List<SteamWorkshopSnapshotEntry>? entries = JsonSerializer.Deserialize<List<SteamWorkshopSnapshotEntry>>(json);
            if (entries == null)
                return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, long> map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (SteamWorkshopSnapshotEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.SteamId))
                    continue;
                if (!map.ContainsKey(entry.SteamId))
                    map[entry.SteamId] = entry.TimeUpdated;
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveWorkshopSnapshot(Dictionary<string, long> snapshot)
    {
        try
        {
            List<SteamWorkshopSnapshotEntry> entries = snapshot
                .Select(kv => new SteamWorkshopSnapshotEntry { SteamId = kv.Key, TimeUpdated = kv.Value })
                .ToList();
            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(WorkshopSnapshotPath, json);
        }
        catch
        {
            // Ignore snapshot failures.
        }
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
