using System.Text.RegularExpressions;

namespace ModHearth
{
    /** <summary> 
    Uses partial mod string search to find dependency mods order (contains patch, addon, etc).
    <para>
    I dont like this approach at all and a full understanding how df handles mod content priority + modders always filling their info.txt properly would be miles better
    </para></summary>*/

    //TODO consider implementing a community rules database if this ever gets a userbase
    public partial class ModHearthManager
    {
        private IReadOnlyList<HashSet<string>> GetDuplicateWarningGroups()
        {
            EnsureDuplicateWarningCache(logFound: false);
            return duplicateWarningGroups;
        }

        private bool IsVanillaBaseMod(ModReference modref)
        {
            if (modref == null)
                return false;
            return ModSourceClassifier.Classify(modref, GetModsPath(), GetVanillaModsPath()).IsVanilla;
        }

        private static bool ContainsModId(IEnumerable<string> values, string id)
        {
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                if (string.Equals(value.Trim(), id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasExplicitOrder(ModReference modref, ModReference other)
        {
            if (modref == null || other == null)
                return false;
            return ContainsModId(modref.require_before_me, other.ID) ||
                   ContainsModId(modref.require_after_me, other.ID) ||
                   ContainsModId(modref.require_ids, other.ID);
        }

        private bool IsPatchLike(ModReference modref)
        {
            if (modref == null)
                return false;
            if (_patchCache.TryGetValue(modref.ID, out bool cached))
                return cached;

            string label = $"{modref.name} {modref.ID}".ToLowerInvariant();
            bool patchLike = label.Contains("patch") ||
                             label.Contains("compat") ||
                             label.Contains("compatibility") ||
                             label.Contains("fix") ||
                             label.Contains("hotfix") ||
                             label.Contains("addon") ||
                             label.Contains("add-on") ||
                             label.Contains("graphics patch") ||
                             label.Contains("graphicspatch") ||
                             label.Contains("civ patch");

            if (!patchLike && !string.IsNullOrWhiteSpace(modref.path))
            {
                string infoPath = Path.Combine(modref.path, "info.txt");
                if (File.Exists(infoPath))
                {
                    try
                    {
                        string info = File.ReadAllText(infoPath);
                        if (info.IndexOf("STEAM_TAG:compatibility", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            info.IndexOf("STEAM_TAG:compatibility fix", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            info.IndexOf("STEAM_TAG:fix", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            info.IndexOf("STEAM_TAG:patch", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            patchLike = true;
                        }
                    }
                    catch
                    {
                        // Ignore unreadable info files.
                    }
                }
            }

            _patchCache[modref.ID] = patchLike;
            return patchLike;
        }

        public bool AutoSortEnabledMods()
        {
            Dictionary<string, ModReference> idMap = new Dictionary<string, ModReference>(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in modrefMap.Values)
                if (!idMap.ContainsKey(modref.ID))
                    idMap.Add(modref.ID, modref);

            Dictionary<string, int> originalIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<ModReference> enabledRefs = new List<ModReference>();
            for (int i = 0; i < enabledMods.Count; i++)
            {
                if (modrefMap.TryGetValue(enabledMods[i].ToString(), out ModReference? modref) && modref != null)
                {
                    enabledRefs.Add(modref);
                    if (!originalIndex.ContainsKey(modref.ID))
                        originalIndex[modref.ID] = i;
                }
            }

            HashSet<string> enabledIds = new HashSet<string>(enabledRefs.Select(m => m.ID), StringComparer.OrdinalIgnoreCase);

            foreach (ModReference modref in idMap.Values)
            {
                if (IsVanillaBaseMod(modref))
                {
                    enabledIds.Add(modref.ID);
                }
            }

            foreach (ModSortRule rule in sortRules)
            {
                if (rule == null)
                    continue;

                string requiredId = rule.RequiresId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(requiredId))
                    continue;
                if (!idMap.TryGetValue(requiredId, out ModReference? requiredRef) || requiredRef == null)
                    continue;

                enabledIds.Add(requiredRef.ID);
            }

            Queue<ModReference> queue = new Queue<ModReference>();
            foreach (string enabledId in enabledIds)
            {
                if (!idMap.TryGetValue(enabledId, out ModReference? enabledRef) || enabledRef == null)
                    continue;
                queue.Enqueue(enabledRef);
            }

            while (queue.Count > 0)
            {
                ModReference current = queue.Dequeue();
                foreach (string dep in current.require_before_me.Concat(current.require_after_me).Concat(current.require_ids))
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId))
                        continue;
                    if (enabledIds.Contains(depId))
                        continue;
                    if (idMap.TryGetValue(depId, out ModReference? depRef) && depRef != null)
                    {
                        enabledIds.Add(depRef.ID);
                        queue.Enqueue(depRef);
                    }
                }
            }

            List<ModReference> allEnabled = new List<ModReference>();
            foreach (string id in enabledIds)
                if (idMap.TryGetValue(id, out ModReference? modref) && modref != null)
                    allEnabled.Add(modref);

            List<ModReference> baseOrder = allEnabled
                .OrderBy(m => GetAutoSortGroup(m))
                .ThenBy(m => originalIndex.TryGetValue(m.ID, out int idx) ? idx : int.MaxValue)
                .ThenBy(m => m.name ?? m.ID)
                .ToList();

            Dictionary<string, int> baseIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < baseOrder.Count; i++)
                baseIndex[baseOrder[i].ID] = i;

            Dictionary<string, List<string>> edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in allEnabled)
            {
                edges[modref.ID] = new List<string>();
                indegree[modref.ID] = 0;
            }

            foreach (ModReference modref in allEnabled)
            {
                foreach (string dep in modref.require_before_me)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    edges[depId].Add(modref.ID);
                    indegree[modref.ID]++;
                }
                foreach (string dep in modref.require_after_me)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    edges[modref.ID].Add(depId);
                    indegree[depId]++;
                }
                foreach (string dep in modref.require_ids)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    edges[depId].Add(modref.ID);
                    indegree[modref.ID]++;
                }
            }

            foreach (ModSortRule rule in sortRules)
            {
                if (rule == null)
                    continue;

                string beforeId = rule.BeforeId?.Trim() ?? string.Empty;
                string afterId = rule.AfterId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(beforeId) || string.IsNullOrWhiteSpace(afterId))
                    continue;
                if (string.Equals(beforeId, afterId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!enabledIds.Contains(beforeId) || !enabledIds.Contains(afterId))
                    continue;

                if (!edges[beforeId].Contains(afterId))
                {
                    edges[beforeId].Add(afterId);
                    indegree[afterId]++;
                }
            }

            foreach (HashSet<string> group in GetDuplicateWarningGroups())
            {
                List<string> vanillaIds = new List<string>();
                List<string> modIds = new List<string>();

                foreach (string id in group)
                {
                    if (!enabledIds.Contains(id))
                        continue;
                    if (!idMap.TryGetValue(id, out ModReference? modref) || modref == null)
                        continue;

                    if (IsVanillaBaseMod(modref))
                        vanillaIds.Add(id);
                    else
                        modIds.Add(id);
                }

                if (vanillaIds.Count == 0 || modIds.Count == 0)
                {
                    if (modIds.Count <= 1)
                        continue;

                    List<string> patchIds = new List<string>();
                    List<string> baseIds = new List<string>();
                    foreach (string id in modIds)
                    {
                        if (!idMap.TryGetValue(id, out ModReference? modref) || modref == null)
                            continue;
                        if (IsPatchLike(modref))
                            patchIds.Add(id);
                        else
                            baseIds.Add(id);
                    }

                    if (patchIds.Count == 0 || baseIds.Count == 0)
                        continue;

                    foreach (string baseId in baseIds)
                    {
                        if (!idMap.TryGetValue(baseId, out ModReference? baseRef) || baseRef == null)
                            continue;

                        foreach (string patchId in patchIds)
                        {
                            if (!idMap.TryGetValue(patchId, out ModReference? patchRef) || patchRef == null)
                                continue;

                            if (HasExplicitOrder(patchRef, baseRef) || HasExplicitOrder(baseRef, patchRef))
                                continue;

                            if (!edges[baseId].Contains(patchId))
                            {
                                edges[baseId].Add(patchId);
                                indegree[patchId]++;
                            }
                        }
                    }

                    continue;
                }

                foreach (string vanillaId in vanillaIds)
                {
                    if (!idMap.TryGetValue(vanillaId, out ModReference? vanillaRef) || vanillaRef == null)
                        continue;

                    foreach (string modId in modIds)
                    {
                        if (!idMap.TryGetValue(modId, out ModReference? modRef) || modRef == null)
                            continue;

                        if (HasExplicitOrder(modRef, vanillaRef) || HasExplicitOrder(vanillaRef, modRef))
                            continue;

                        if (!edges[vanillaId].Contains(modId))
                        {
                            edges[vanillaId].Add(modId);
                            indegree[modId]++;
                        }
                    }
                }
            }

            List<string> available = new List<string>();
            foreach (KeyValuePair<string, int> kv in indegree)
                if (kv.Value == 0)
                    available.Add(kv.Key);

            List<string> sortedIds = new List<string>();
            while (available.Count > 0)
            {
                string next = available.OrderBy(id => baseIndex.TryGetValue(id, out int idx) ? idx : int.MaxValue).First();
                available.Remove(next);
                sortedIds.Add(next);
                foreach (string dest in edges[next])
                {
                    indegree[dest]--;
                    if (indegree[dest] == 0)
                        available.Add(dest);
                }
            }

            if (sortedIds.Count != enabledIds.Count)
                sortedIds = baseOrder.Select(m => m.ID).ToList();

            List<DFHMod> sortedMods = new List<DFHMod>();
            foreach (string id in sortedIds)
                if (idMap.TryGetValue(id, out ModReference? modref) && modref != null)
                    sortedMods.Add(modref.ToDFHMod());

            bool changed = sortedMods.Count != enabledMods.Count;
            if (!changed)
                for (int i = 0; i < sortedMods.Count; i++)
                    if (sortedMods[i] != enabledMods[i])
                    {
                        changed = true;
                        break;
                    }

            if (changed)
            {
                SetActiveMods(sortedMods);
                FindModlistProblems();
            }

            return changed;
        }

        private int GetAutoSortGroup(ModReference modref)
        {
            var traits = GetModTraits(modref);
            if (traits.beforeVanilla)
                return 0;
            if (traits.vanillaEntity)
                return 0;
            if (traits.newEntity)
                return 1;
            if (traits.reaction)
                return 4;
            if (traits.graphics)
                return 2;
            if (traits.newStuff)
                return 5;
            return 3;
        }

        private (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla) GetModTraits(
            ModReference modref)
        {
            if (_modTraitCache.TryGetValue(modref.ID, out var cached))
                return cached;

            bool vanillaEntity = false;
            bool newEntity = false;
            bool reaction = false;
            bool creature = false;
            bool newStuff = false;
            bool graphics = false;
            bool beforeVanilla = false;

            string infoPath = Path.Combine(modref.path, "info.txt");
            if (File.Exists(infoPath))
            {
                // Hardcoded bullshit from this post comments https://www.reddit.com/r/dwarffortress/comments/13nhfjr/mod_load_order/
                // it works well enough, but dont do this
                string info = File.ReadAllText(infoPath);
                if (info.IndexOf("before vanilla", StringComparison.OrdinalIgnoreCase) >= 0)
                    beforeVanilla = true;
                if (info.IndexOf("graphics", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("tileset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("tile set", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("portrait", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("landscape", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("stone variation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("rounded hills", StringComparison.OrdinalIgnoreCase) >= 0)
                    graphics = true;
            }

            if (Directory.Exists(Path.Combine(modref.path, "graphics")) ||
                Directory.Exists(Path.Combine(modref.path, "raw", "graphics")))
                graphics = true;

            if (Directory.Exists(modref.path))
            {
                foreach (string file in Directory.EnumerateFiles(modref.path, "*.txt", SearchOption.AllDirectories))
                {
                    string lowerPath = file.ToLowerInvariant();
                    if (lowerPath.Contains("\\graphics\\") || lowerPath.Contains("/graphics/"))
                        graphics = true;
                    if (!lowerPath.Contains("\\raw\\") && !lowerPath.Contains("/raw/"))
                        continue;

                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!reaction && text.IndexOf("[REACTION:", StringComparison.OrdinalIgnoreCase) >= 0)
                        reaction = true;
                    if (!creature && text.IndexOf("[CREATURE:", StringComparison.OrdinalIgnoreCase) >= 0)
                        creature = true;
                    if (!newStuff &&
                        (text.IndexOf("[INORGANIC:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("[PLANT:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("[ITEM_", StringComparison.OrdinalIgnoreCase) >= 0))
                        newStuff = true;

                    if (!vanillaEntity || !newEntity)
                    {
                        MatchCollection entityMatches = Regex.Matches(text, @"\[ENTITY:([^\]]+)\]", RegexOptions.IgnoreCase);
                        foreach (Match match in entityMatches)
                        {
                            string ent = match.Groups[1].Value.Trim();
                            if (IsVanillaEntity(ent))
                                vanillaEntity = true;
                            else if (!string.IsNullOrEmpty(ent))
                                newEntity = true;
                        }
                    }

                    if (reaction && creature && newStuff && graphics && (vanillaEntity || newEntity))
                        break;
                }
            }

            if (creature)
                newStuff = true;

            var result = (vanillaEntity, newEntity, reaction, creature, newStuff, graphics, beforeVanilla);
            _modTraitCache[modref.ID] = result;
            return result;
        }

        private bool IsVanillaEntity(string id)
        {
            switch (id.ToUpperInvariant())
            {
                case "DWARF":
                case "ELF":
                case "HUMAN":
                case "GOBLIN":
                case "KOBOLD":
                    return true;
            }
            return false;
        }
    }
}
