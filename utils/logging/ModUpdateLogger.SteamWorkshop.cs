using ModHearth.Models;
using ModHearth.Utils.IO;
using ModHearth.Utils.Steam;

namespace ModHearth.Utils.Logging
{
    public static partial class ModUpdateLogger
    {
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
    }
}
