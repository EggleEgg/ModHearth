using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ModHearth.Utilities;

namespace ModHearth
{
    // Uses a raw-file dependency scan (CUT/SELECT/COPY_TAGS_FROM) plus mod-author-declared hints (info.txt before/after/requires, sort rules) to build a dependency graph, then
    // topologically sorts it. Edges are added in strict priority order: sort rules, then info.txt declarations, then vanilla-conflict structural edges, then raw-scan-derived edges. 
    // And every edge is checked against the graph as it currently stands before being added, so a lower-priority source can never override or contradict a
    // higher-priority one; it can only fill in relationships nothing else addressed. Coarse trait-based grouping (GetModSortGroup) only breaks ties among mods with no
    // actual graph relationship to anything else.
    public partial class ModHearthManager
    {
        private static bool IsVanillaBaseMod(ModReference modref)
        {
            switch (modref)
            {
                case null:
                    return false;
                default:
                    return ModSourceClassifier.Classify(modref, GetModsPath(), GetVanillaModsPath()).IsVanilla;
            }
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

        // True if adding fromId -> toId would create a cycle given the edges already in the graph (i.e. toId can already reach fromId). This is
        // what makes tiered priority work: since tiers are added in priority order, a lower-priority edge that contradicts a higher-priority one
        // always fails this check and gets dropped, never the reverse.
        internal static bool WouldCreateCycle(Dictionary<string, List<string>> edges, string fromId, string toId)
        {
            if (string.Equals(fromId, toId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!edges.ContainsKey(toId))
                return false;

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stack<string> stack = new Stack<string>();
            stack.Push(toId);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                if (!visited.Add(current))
                    continue;

                if (string.Equals(current, fromId, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (edges.TryGetValue(current, out List<string>? destinations))
                {
                    foreach (string dest in destinations)
                        stack.Push(dest);
                }
            }

            return false;
        }

        internal static bool WouldCreateCycle(Dictionary<string, HashSet<string>> edges, string fromId, string toId)
        {
            if (string.Equals(fromId, toId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!edges.ContainsKey(toId))
                return false;

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stack<string> stack = new Stack<string>();
            stack.Push(toId);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                if (!visited.Add(current))
                    continue;

                if (string.Equals(current, fromId, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (edges.TryGetValue(current, out HashSet<string>? destinations))
                {
                    foreach (string dest in destinations)
                        stack.Push(dest);
                }
            }

            return false;
        }

        // Adds a "fromId must come before toId" edge, unless it's a self-edge, already present, or would create a cycle with edges already in the
        // graph, in which case it's silently dropped rather than corrupting the sort. Every edge added anywhere in ModSortEnabledMods goes
        // through this.
        private static bool TryAddEdge(Dictionary<string, List<string>> edges, Dictionary<string, int> indegree, string fromId, string toId, object? syncRoot = null)
        {
            if (syncRoot != null)
            {
                lock (syncRoot)
                {
                    return TryAddEdgeInternal(edges, indegree, fromId, toId);
                }
            }
            return TryAddEdgeInternal(edges, indegree, fromId, toId);
        }

        private static bool TryAddEdgeInternal(Dictionary<string, List<string>> edges, Dictionary<string, int> indegree, string fromId, string toId)
        {
            if (string.Equals(fromId, toId, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!edges.TryGetValue(fromId, out List<string>? destinations) || !indegree.ContainsKey(toId))
                return false;
            if (destinations.Contains(toId, StringComparer.OrdinalIgnoreCase))
                return false;
            if (WouldCreateCycle(edges, fromId, toId))
                return false;

            destinations.Add(toId);
            indegree[toId]++;
            return true;
        }

        // Same three signals baseOrder itself is sorted by (coarse trait group, original list position, then name). Used to give a
        // deterministic best-effort order between mods with no principled dependency relationship at all (e.g. two mods that both cut the
        // same raw target, or two mods that both directly define the same ID with no CUT involved).
        private int CompareForBestEffortOrder(ModReference a, ModReference b, Dictionary<string, int> originalIndex)
        {
            int groupCompare = GetModSortGroup(a).CompareTo(GetModSortGroup(b));
            if (groupCompare != 0)
                return groupCompare;

            int indexA = originalIndex.TryGetValue(a.ID, out int ia) ? ia : int.MaxValue;
            int indexB = originalIndex.TryGetValue(b.ID, out int ib) ? ib : int.MaxValue;
            int indexCompare = indexA.CompareTo(indexB);
            if (indexCompare != 0)
                return indexCompare;

            return string.Compare(a.name ?? a.ID, b.name ?? b.ID, StringComparison.OrdinalIgnoreCase);
        }

        private string? PickBestEffortOrder(ModReference a, ModReference b, Dictionary<string, int> originalIndex)
        {
            int comparison = CompareForBestEffortOrder(a, b, originalIndex);
            switch (comparison)
            {
                case 0:
                    return null;
                default:
                    return comparison < 0 ? a.ID : b.ID;
            }
        }

        public bool ModSortEnabledMods()
        {
            Dictionary<string, ModReference> idMap;
            Dictionary<string, int> originalIndex = new(StringComparer.OrdinalIgnoreCase);
            List<ModReference> enabledRefs = [];
            List<ModSortRule> sortRulesSnapshot;
            List<ModSortRule> communitySortRulesSnapshot;
            Dictionary<string, ModRelationshipRule> relationshipRulesSnapshot;
            List<DFHMod> enabledModsSnapshot;

            lock (stateGate)
            {
                idMap = new Dictionary<string, ModReference>(modrefMap.Count, StringComparer.OrdinalIgnoreCase);
                foreach (ModReference modref in modrefMap.Values)
                    if (!idMap.ContainsKey(modref.ID))
                        idMap.Add(modref.ID, modref);

                enabledModsSnapshot = [.. enabledMods];
                sortRulesSnapshot = [.. sortRules];
                communitySortRulesSnapshot = [.. communitySortRules];
                relationshipRulesSnapshot = new Dictionary<string, ModRelationshipRule>(relationshipRules, StringComparer.OrdinalIgnoreCase);
            }

            for (int i = 0; i < enabledModsSnapshot.Count; i++)
            {
                if (idMap.TryGetValue(enabledModsSnapshot[i].id, out ModReference? modref) && modref != null)
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
                    _ = enabledIds.Add(modref.ID);
                }
            }

            foreach (ModSortRule rule in sortRulesSnapshot)
            {
                if (rule == null)
                    continue;

                string requiredId = rule.RequiresId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(requiredId))
                    continue;
                if (!idMap.TryGetValue(requiredId, out ModReference? requiredRef) || requiredRef == null)
                    continue;

                _ = enabledIds.Add(requiredRef.ID);
            }

            foreach (ModSortRule rule in communitySortRulesSnapshot)
            {
                if (rule == null)
                    continue;

                string requiredId = rule.RequiresId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(requiredId))
                    continue;
                if (!idMap.TryGetValue(requiredId, out ModReference? requiredRef) || requiredRef == null)
                    continue;

                _ = enabledIds.Add(requiredRef.ID);
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
                IEnumerable<string> declaredDependencies = current.require_before_me
                    .Concat(current.require_after_me)
                    .Concat(current.require_ids);

                IEnumerable<string> customRequiredIds = relationshipRulesSnapshot.TryGetValue(current.ID, out ModRelationshipRule? customRule)
                    ? customRule.RequiredIds
                    : Enumerable.Empty<string>();

                foreach (string dep in declaredDependencies.Concat(customRequiredIds))
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId))
                        continue;
                    if (enabledIds.Contains(depId))
                        continue;
                    if (idMap.TryGetValue(depId, out ModReference? depRef) && depRef != null)
                    {
                        _ = enabledIds.Add(depRef.ID);
                        queue.Enqueue(depRef);
                    }
                }
            }

            List<ModReference> allEnabled = [];
            foreach (string id in enabledIds)
                if (idMap.TryGetValue(id, out ModReference? modref) && modref != null)
                    allEnabled.Add(modref);

            // Snapshot raw info for all enabled mods to avoid repeated locking
            Dictionary<string, ModRawDependencyInfo> rawInfoSnapshot = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in allEnabled)
            {
                ModRawDependencyInfo? info = GetRawDependencyInfo(modref);
                if (info != null)
                    rawInfoSnapshot[modref.ID] = info;
            }

            // Used as: the final "everything failed" fallback, the tie-break for Kahn's algorithm's frontier selection, and the signal
            // best-effort edges are derived from.
            List<ModReference> baseOrder = allEnabled
                .OrderBy(GetModSortGroup)
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
                edges[modref.ID] = [];
                indegree[modref.ID] = 0;
            }

            object syncRoot = new object();

            // --- Tier 1: per-mod relationship rules ---
            foreach (KeyValuePair<string, ModRelationshipRule> kvp in relationshipRulesSnapshot)
            {
                string ownerId = kvp.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ownerId) || !enabledIds.Contains(ownerId))
                    continue;

                foreach (string target in kvp.Value.BeforeIds)
                {
                    string targetId = target?.Trim() ?? string.Empty;
                    if (!enabledIds.Contains(targetId))
                        continue;
                    _ = TryAddEdge(edges, indegree, ownerId, targetId);
                }

                foreach (string target in kvp.Value.AfterIds)
                {
                    string targetId = target?.Trim() ?? string.Empty;
                    if (!enabledIds.Contains(targetId))
                        continue;
                    _ = TryAddEdge(edges, indegree, targetId, ownerId);
                }

                foreach (string target in kvp.Value.RequiredIds)
                {
                    string targetId = target?.Trim() ?? string.Empty;
                    if (!enabledIds.Contains(targetId))
                        continue;
                    _ = TryAddEdge(edges, indegree, targetId, ownerId);
                }
            }

            // --- Tier 1.1: legacy user-defined sort rules ---
            foreach (ModSortRule rule in sortRulesSnapshot)
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

                _ = TryAddEdge(edges, indegree, beforeId, afterId);
            }

            // --- Tier 1.5: community sort rules ---
            foreach (ModSortRule rule in communitySortRulesSnapshot)
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

                _ = TryAddEdge(edges, indegree, beforeId, afterId);
            }

            // --- Tier 2: declared dependencies (info.txt require_before_me / require_after_me / require_ids) ---
            foreach (ModReference modref in allEnabled)
            {
                foreach (string dep in modref.require_before_me)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    _ = TryAddEdge(edges, indegree, depId, modref.ID);
                }
                foreach (string dep in modref.require_after_me)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    _ = TryAddEdge(edges, indegree, modref.ID, depId);
                }
                foreach (string dep in modref.require_ids)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    _ = TryAddEdge(edges, indegree, depId, modref.ID);
                }
            }

            // --- Tier 3: vanilla-base structural edges ---
            var duplicateWarningGroupsSnapshot = GetDuplicateWarningGroups();
            foreach (HashSet<string> group in duplicateWarningGroupsSnapshot)
            {
                List<string> vanillaIds = [];
                List<string> modIds = [];

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
                    continue;

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

                        _ = TryAddEdge(edges, indegree, vanillaId, modId);
                    }
                }
            }

            // --- Tier 4: raw-scan-derived edges (optimized and parallelized) ---
            Dictionary<string, List<string>> directDefiners = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> selectorsByTarget = new(StringComparer.OrdinalIgnoreCase);

            foreach (string id in enabledIds)
            {
                if (!rawInfoSnapshot.TryGetValue(id, out ModRawDependencyInfo? info))
                    continue;

                foreach (string definedId in info.DirectDefinitionIds)
                {
                    if (!directDefiners.TryGetValue(definedId, out List<string>? definers))
                    {
                        definers = [];
                        directDefiners[definedId] = definers;
                    }
                    definers.Add(id);
                }

                foreach (string targetId in info.SelectTargetIds)
                {
                    if (!selectorsByTarget.TryGetValue(targetId, out List<string>? selectors))
                    {
                        selectors = [];
                        selectorsByTarget[targetId] = selectors;
                    }
                    selectors.Add(id);
                }
            }

            List<string> enabledIdsList = enabledIds.ToList();

            _ = Parallel.ForEach(enabledIdsList, cutterId =>
            {
                if (!idMap.TryGetValue(cutterId, out ModReference? cutterRef) || cutterRef == null)
                    return;
                if (!rawInfoSnapshot.TryGetValue(cutterId, out ModRawDependencyInfo? cutterInfo))
                    return;
                if (!cutterInfo.IsCutter || cutterInfo.CutTargetIds.Count == 0)
                    return;

                foreach (string cutTargetId in cutterInfo.CutTargetIds)
                {
                    if (selectorsByTarget.TryGetValue(cutTargetId, out List<string>? selectors))
                    {
                        foreach (string selectorId in selectors)
                        {
                            if (string.Equals(selectorId, cutterId, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!idMap.TryGetValue(selectorId, out ModReference? selectorRef) || selectorRef == null)
                                continue;
                            if (!rawInfoSnapshot.TryGetValue(selectorId, out ModRawDependencyInfo? selectorInfo))
                                continue;

                            if (HasExplicitOrder(selectorRef, cutterRef) || HasExplicitOrder(cutterRef, selectorRef))
                                continue;

                            bool selectorAlsoCutsSameTarget = selectorInfo.IsCutter &&
                                selectorInfo.CutTargetIds.Contains(cutTargetId, StringComparer.OrdinalIgnoreCase);

                            if (selectorAlsoCutsSameTarget)
                            {
                                string? winnerId = PickBestEffortOrder(cutterRef, selectorRef, originalIndex);
                                if (winnerId != null)
                                {
                                    string loserId = string.Equals(winnerId, cutterId, StringComparison.OrdinalIgnoreCase) ? selectorId : cutterId;
                                    _ = TryAddEdge(edges, indegree, winnerId, loserId, syncRoot);
                                }
                                continue;
                            }

                            _ = TryAddEdge(edges, indegree, cutterId, selectorId, syncRoot);
                        }
                    }
                }
            });

            // COPY_TAGS_FROM
            _ = Parallel.ForEach(enabledIdsList, copierId =>
            {
                if (!idMap.TryGetValue(copierId, out ModReference? copierRef) || copierRef == null)
                    return;
                if (!rawInfoSnapshot.TryGetValue(copierId, out ModRawDependencyInfo? copierInfo) || copierInfo.CopyTagsFromSourceIds.Count == 0)
                    return;

                foreach (string sourceId in copierInfo.CopyTagsFromSourceIds)
                {
                    if (directDefiners.TryGetValue(sourceId, out List<string>? definers))
                    {
                        foreach (string definerId in definers)
                        {
                            if (string.Equals(definerId, copierId, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!idMap.TryGetValue(definerId, out ModReference? definerRef) || definerRef == null)
                                continue;

                            if (HasExplicitOrder(copierRef, definerRef) || HasExplicitOrder(definerRef, copierRef))
                                continue;

                            _ = TryAddEdge(edges, indegree, definerId, copierId, syncRoot);
                        }
                    }
                }
            });

            // --- Tier 5: Mod-vs-mod duplicate warnings ---
            foreach (HashSet<string> group in duplicateWarningGroupsSnapshot)
            {
                List<string> modIds = group.Where(id =>
                    enabledIds.Contains(id) &&
                    idMap.TryGetValue(id, out ModReference? m) && m != null && !IsVanillaBaseMod(m)).ToList();

                if (modIds.Count <= 1)
                    continue;

                List<string> cutterIds = [];
                List<string> baseIds = [];
                foreach (string id in modIds)
                {
                    if (rawInfoSnapshot.TryGetValue(id, out ModRawDependencyInfo? info) && info.IsCutter)
                        cutterIds.Add(id);
                    else
                        baseIds.Add(id);
                }

                if (cutterIds.Count == 0 || baseIds.Count == 0)
                    continue;

                foreach (string baseId in baseIds)
                {
                    if (!idMap.TryGetValue(baseId, out ModReference? baseRef) || baseRef == null)
                        continue;

                    foreach (string cutterId in cutterIds)
                    {
                        if (!idMap.TryGetValue(cutterId, out ModReference? cutterRef) || cutterRef == null)
                            continue;

                        if (HasExplicitOrder(cutterRef, baseRef) || HasExplicitOrder(baseRef, cutterRef))
                            continue;

                        _ = TryAddEdge(edges, indegree, baseId, cutterId);
                    }
                }
            }

            // Direct definition conflicts
            List<string> conflictingKeys = [];
            HashSet<string> activeModIds = new HashSet<string>(enabledModsSnapshot.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> kvp in directDefiners)
            {
                if (kvp.Value.Count <= 1)
                    continue;

                bool anyCutRelationship = false;
                foreach (string candidateId in kvp.Value)
                {
                    if (rawInfoSnapshot.TryGetValue(candidateId, out ModRawDependencyInfo? info) &&
                        info.CutTargetIds.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        anyCutRelationship = true;
                        break;
                    }
                }

                if (anyCutRelationship)
                    continue;

                if (kvp.Value.Count(activeModIds.Contains) > 1)
                    conflictingKeys.Add(kvp.Key);

                List<ModReference> definers = kvp.Value
                    .Select(id => idMap.TryGetValue(id, out ModReference? m) ? m : null)
                    .Where(m => m != null)
                    .Cast<ModReference>()
                    .OrderBy(GetModSortGroup)
                    .ThenBy(m => originalIndex.TryGetValue(m.ID, out int idx) ? idx : int.MaxValue)
                    .ThenBy(m => m.name ?? m.ID, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < definers.Count - 1; i++)
                {
                    if (HasExplicitOrder(definers[i], definers[i + 1]) || HasExplicitOrder(definers[i + 1], definers[i]))
                        continue;
                    _ = TryAddEdge(edges, indegree, definers[i].ID, definers[i + 1].ID);
                }
            }

            if (conflictingKeys.Any())
            {
                Console.WriteLine($"[ModSort] Warning: '{string.Join(", ", conflictingKeys.Select(ObjectKey.FormatForDisplay))}' are directly defined by multiple enabled mods with no CUT relationship between them. This is likely to cause silent raw conflicts regardless of load order. Applying a best-effort order.");
                ShowNotification("Conflicting keys present. Applying a best-effort order", "sortCancelIcon.svg");
            }

            List<string> available = [];
            foreach (KeyValuePair<string, int> kv in indegree)
                if (kv.Value == 0)
                    available.Add(kv.Key);

            List<string> sortedIds = [];
            while (available.Count > 0)
            {
                string next = available.OrderBy(id => baseIndex.TryGetValue(id, out int idx) ? idx : int.MaxValue).First();
                _ = available.Remove(next);
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

            List<DFHMod> sortedMods = [];
            foreach (string id in sortedIds)
                if (idMap.TryGetValue(id, out ModReference? modref) && modref != null)
                    sortedMods.Add(modref.ToDFHMod());

            bool changed = sortedMods.Count != enabledModsSnapshot.Count;
            if (!changed)
            {
                for (int i = 0; i < sortedMods.Count; i++)
                {
                    if (sortedMods[i] != enabledModsSnapshot[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                SetActiveMods(sortedMods);
                FindModlistProblems();
            }

            return changed;
        }

        // Coarse tie-breaker for mods with no explicit graph relationship to anything else.
        private int GetModSortGroup(ModReference modref)
        {
            ModRawDependencyInfo? traits = GetRawDependencyInfo(modref);
            if (traits == null)
                return 3;

            if (traits.HasVanillaEntity || traits.IsCutter)
                return 0;
            if (traits.HasNewEntity)
                return 1;
            if (traits.HasReaction)
                return 4;
            if (traits.HasGraphics)
                return 2;
            if (traits.HasNewStuff)
                return 5;
            return 3;
        }
    }
}
