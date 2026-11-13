using System.Text.RegularExpressions;
using ModHearth.Models;
using ModHearth.Utils.IO;
using ModHearth.Utils.Logging;
using ModHearth.Utils.ModRaws;

using static ModHearth.Config.ConfigManager;

namespace ModHearth.Manager
{
    public partial class ModHearthManager
    {
        private HashSet<string> GetActiveModIds() => new(enabledMods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

        private HashSet<string> GetLiveConflictedIds(HashSet<string> activeModIds)
        {
            HashSet<string> liveConflictedIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (HashSet<string>? group in duplicateWarningGroups.Where(group => group.Count(activeModIds.Contains) > 1))
            {
                liveConflictedIds.UnionWith(group.Where(activeModIds.Contains));
            }
            return liveConflictedIds;
        }

        public IReadOnlyDictionary<string, List<string>> GetDuplicateWarningMap()
        {
            EnsureDuplicateWarningCache(logFound: true);

            HashSet<string> activeModIds = GetActiveModIds();
            HashSet<string> liveConflictedIds = GetLiveConflictedIds(activeModIds);

            Dictionary<string, List<string>> combinedMap = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> kvp in duplicateWarningMap.Where(kvp => liveConflictedIds.Contains(kvp.Key)))
            {
                combinedMap[kvp.Key] = [.. kvp.Value];
            }

            foreach (KeyValuePair<string, List<string>> kvp in cacheDuplicateMap)
            {
                if (combinedMap.TryGetValue(kvp.Key, out List<string>? list))
                    list.AddRange(kvp.Value);
                else
                    combinedMap[kvp.Key] = [.. kvp.Value];
            }
            return combinedMap;
        }

        public bool HasErrorLogDuplicateWarning(string modId)
        {
            EnsureDuplicateWarningCache(logFound: false);
            if (string.IsNullOrWhiteSpace(modId))
                return false;

            HashSet<string> activeModIds = GetActiveModIds();
            HashSet<string> liveConflictedIds = GetLiveConflictedIds(activeModIds);

            return duplicateWarningMap.ContainsKey(modId) && liveConflictedIds.Contains(modId);
        }

        public IReadOnlyList<HashSet<string>> GetDuplicateWarningGroups()
        {
            EnsureDuplicateWarningCache(logFound: true);

            HashSet<string> activeModIds = GetActiveModIds();
            List<HashSet<string>> combinedGroups = [];
            foreach (HashSet<string> group in duplicateWarningGroups)
            {
                HashSet<string> activeMembers = new(group.Where(activeModIds.Contains), StringComparer.OrdinalIgnoreCase);
                if (activeMembers.Count > 1)
                    combinedGroups.Add(activeMembers);
            }
            combinedGroups.AddRange(cacheDuplicateGroups);
            return combinedGroups;
        }

        private void EnsureDuplicateWarningCache(bool logFound)
        {
            RefreshCacheDuplicateMap();

            bool errorLogSearchEnabled = GetSearchErrorLogIssues();
            string errorLogPath = GetErrorLogPath();
            bool exists = errorLogSearchEnabled && File.Exists(errorLogPath);
            if (logFound && exists &&
                (!PathHelper.PathsEqual(lastLoggedErrorLogPath, errorLogPath) || !lastLoggedErrorLogExists))
            {
                Console.WriteLine($"Dwarf Fortress error log found: {errorLogPath}");
            }

            lastLoggedErrorLogPath = errorLogPath;
            lastLoggedErrorLogExists = exists;

            if (!exists)
            {
                lock (stateGate)
                {
                    duplicateWarningLastWriteUtc = null;
                    duplicateWarningMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    duplicateWarningGroups = [];
                }
                return;
            }

            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(errorLogPath);
            bool upToDate;
            lock (stateGate)
                upToDate = duplicateWarningLastWriteUtc.HasValue && duplicateWarningLastWriteUtc.Value == lastWriteUtc;

            if (upToDate)
                return;

            Dictionary<string, List<string>> newMap;
            List<HashSet<string>> newGroups;
            try
            {
                ParseDuplicateWarnings(errorLogPath, out newMap, out newGroups);
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Failed to parse duplicate-warning error log '{errorLogPath}': {ex.Message}");
                newMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                newGroups = [];
            }

            lock (stateGate)
            {
                duplicateWarningLastWriteUtc = lastWriteUtc;
                duplicateWarningMap = newMap;
                duplicateWarningGroups = newGroups;
            }
        }

        private void ParseDuplicateWarnings(
            string errorLogPath,
            out Dictionary<string, List<string>> warningMap,
            out List<HashSet<string>> groups)
        {
            Dictionary<string, string> aliasMap = BuildDuplicateWarningAliasMap();
            Dictionary<string, HashSet<string>> map = new(StringComparer.OrdinalIgnoreCase);
            List<HashSet<string>> groupList = [];
            HashSet<string> groupKeys = new(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadLines(errorLogPath))
            {
                Match match = DuplicateWarningRegex().Match(line);
                if (!match.Success)
                    continue;

                string objectName = match.Groups["object"].Value.Trim();
                string offenders = match.Groups["mods"].Value.Trim();
                if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(offenders))
                    continue;

                HashSet<string> groupIds = new(StringComparer.OrdinalIgnoreCase);
                foreach (string entry in offenders.Split(','))
                {
                    string token = DuplicateWarningCountRegex().Replace(entry, string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(token))
                        continue;

                    if (!aliasMap.TryGetValue(token, out string? modId) || string.IsNullOrWhiteSpace(modId))
                        continue;

                    _ = groupIds.Add(modId);

                    if (!map.TryGetValue(modId, out HashSet<string>? objects))
                    {
                        objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        map[modId] = objects;
                    }

                    _ = objects.Add(objectName);
                }

                if (groupIds.Count >= 2)
                {
                    string key = string.Join("|", groupIds.OrderBy(value => value));
                    if (groupKeys.Add(key))
                        groupList.Add(groupIds);
                }
            }

            Dictionary<string, List<string>> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, HashSet<string>> entry in map)
                result[entry.Key] = [.. entry.Value.OrderBy(value => value)];

            warningMap = result;
            groups = groupList;
        }

        private void RefreshCacheDuplicateMap()
        {
            if (!GetSearchPossibleIssues())
            {
                lock (stateGate)
                {
                    cacheDuplicateMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    cacheDuplicateGroups = [];
                    cacheDuplicateLastWriteUtc = null;
                    cacheDuplicateActiveModIds = null;
                }

                return;
            }

            string cachePath = ModRawDependencyCacheStore.CachePath;
            bool exists = File.Exists(cachePath);
            DateTime lastWriteUtc = exists ? File.GetLastWriteTimeUtc(cachePath) : DateTime.MinValue;

            // Snapshot the volatile list once so the id set and the vanilla classification below
            // always describe the same enabled mods.
            List<DfhMod> enabledSnapshot = [.. enabledMods];
            HashSet<string> activeModIds = new(enabledSnapshot.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

            // data/vanilla folders are both ordinary enabled mods and the source of the vanilla
            // baseline, so they must not be compared against that baseline as if they were addons.
            ModSourceContext sourceContext = ModSourceContext.FromConfig();
            HashSet<string> vanillaSourceModIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (DfhMod mod in enabledSnapshot)
            {
                ModReference? modref = FindModRef(mod);
                if (modref != null && ModSourceClassifier.Classify(modref, sourceContext).IsVanilla)
                    _ = vanillaSourceModIds.Add(mod.Id);
            }

            bool upToDate;
            lock (stateGate)
            {
                upToDate = cacheDuplicateLastWriteUtc.HasValue &&
                           cacheDuplicateLastWriteUtc.Value == lastWriteUtc &&
                           cacheDuplicateActiveModIds != null &&
                           cacheDuplicateActiveModIds.SetEquals(activeModIds);
            }

            if (upToDate)
                return;

            Dictionary<string, ModRawDependencyInfo> cache = ModRawDependencyCacheStore.Load();
            Dictionary<string, List<string>> newMap = new(StringComparer.OrdinalIgnoreCase);
            List<HashSet<string>> newGroups = [];

            // DF treats the official raw folders as ordinary mods, so a direct definition that
            // collides with vanilla produces the same "Duplicate Object" line as a mod-vs-mod
            // collision. The vanilla source mods themselves are excluded from that comparison:
            // they define the baseline, they don't duplicate it.
            List<ModDuplicateDefinitionGroup> groups = ModDuplicateDefinitionAnalyzer.Analyze(
                cache.Values,
                activeModIds,
                GetVanillaBaseline(),
                vanillaSourceModIds);

            foreach (ModDuplicateDefinitionGroup duplicate in groups)
            {
                // The baseline was loaded from the active vanilla source mods, so when one of them
                // defines this key the synthetic "vanilla raws" label would just repeat a real mod.
                bool vanillaRepresented = duplicate.IncludesVanilla
                    && duplicate.ModIds.Any(vanillaSourceModIds.Contains);

                HashSet<string> group = new(duplicate.ModIds, StringComparer.OrdinalIgnoreCase);
                if (duplicate.IncludesVanilla && !vanillaRepresented)
                    _ = group.Add(ModDuplicateDefinitionAnalyzer.VanillaOffender);
                newGroups.Add(group);

                string displayLabel = ObjectKey.FormatForDisplay(duplicate.DefinitionKey);
                foreach (string modId in duplicate.ModIds)
                {
                    // Vanilla source mods are the baseline, not an addon that duplicates it. Warning
                    // only the regular mod keeps the indicator on the mod the user can actually fix.
                    if (vanillaSourceModIds.Contains(modId))
                        continue;

                    if (!newMap.TryGetValue(modId, out List<string>? objects))
                    {
                        objects = [];
                        newMap[modId] = objects;
                    }

                    List<string> otherMods = [.. duplicate.ModIds
                        .Where(id => !string.Equals(id, modId, StringComparison.OrdinalIgnoreCase))];

                    if (duplicate.IncludesVanilla && !vanillaRepresented)
                        otherMods.Add(ModDuplicateDefinitionAnalyzer.VanillaOffender);

                    objects.Add($"[Cache] Duplicate raw definition: {displayLabel} (also in: {string.Join(", ", otherMods)})");
                }
            }

            lock (stateGate)
            {
                cacheDuplicateMap = newMap;
                cacheDuplicateGroups = newGroups;
                cacheDuplicateLastWriteUtc = lastWriteUtc;
                cacheDuplicateActiveModIds = activeModIds;
            }
        }

        private Dictionary<string, string> BuildDuplicateWarningAliasMap()
        {
            Dictionary<string, string> aliasMap = new(StringComparer.OrdinalIgnoreCase);

            foreach (ModReference modref in modrefMap.Values)
            {
                AddAlias(aliasMap, modref.ID, modref.ID);
                AddAlias(aliasMap, modref.Name, modref.ID);
                AddAlias(aliasMap, Path.GetFileName(modref.Path), modref.ID);
            }

            return aliasMap;
        }

        private static void AddAlias(Dictionary<string, string> aliasMap, string? key, string modId)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(modId))
                return;
            if (!aliasMap.ContainsKey(key))
                aliasMap[key] = modId;
        }
    }
}
