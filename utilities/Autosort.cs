using System.Text.RegularExpressions;

namespace ModHearth
{
    /** <summary> 
    Uses a raw-file dependency scan (CUT/SELECT/COPY_TAGS_FROM) plus mod-author-declared
    hints (info.txt before/after/requires, sort rules) to build a dependency graph, then
    topologically sorts it. Edges are added in strict priority order — sort rules, then
    info.txt declarations, then vanilla-conflict structural edges, then raw-scan-derived
    edges — and every edge is checked against the graph as it currently stands before
    being added, so a lower-priority source can never override or contradict a
    higher-priority one; it can only fill in relationships nothing else addressed.
    Coarse trait-based grouping (GetAutoSortGroup) only breaks ties among mods with no
    actual graph relationship to anything else.
    </summary>*/
    public partial class ModHearthManager
    {
        private IReadOnlyList<HashSet<string>> GetDuplicateWarningGroups()
        {
            EnsureDuplicateWarningCache(logFound: false);
            return duplicateWarningGroups;
        }

        private static bool IsVanillaBaseMod(ModReference modref)
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

        // True if adding fromId -> toId would create a cycle given the edges
        // already in the graph (i.e. toId can already reach fromId). This is
        // what makes tiered priority work: since tiers are added in priority
        // order, a lower-priority edge that contradicts a higher-priority one
        // always fails this check and gets dropped — never the reverse.
        private static bool WouldCreateCycle(Dictionary<string, List<string>> edges, string fromId, string toId)
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

        // Adds a "fromId must come before toId" edge, unless it's a self-edge,
        // already present, or would create a cycle with edges already in the
        // graph — in which case it's silently dropped rather than corrupting
        // the sort. Every edge added anywhere in AutoSortEnabledMods goes
        // through this.
        private static bool TryAddEdge(Dictionary<string, List<string>> edges, Dictionary<string, int> indegree, string fromId, string toId)
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

        // Same three signals baseOrder itself is sorted by (coarse trait
        // group, original list position, then name) — used to give a
        // deterministic best-effort order between mods with no principled
        // dependency relationship at all (e.g. two mods that both cut the
        // same raw target, or two mods that both directly define the same ID
        // with no CUT involved).
        private int CompareForBestEffortOrder(ModReference a, ModReference b, Dictionary<string, int> originalIndex)
        {
            int groupCompare = GetAutoSortGroup(a).CompareTo(GetAutoSortGroup(b));
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
            if (comparison == 0)
                return null;
            return comparison < 0 ? a.ID : b.ID;
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

            // Used as: the final "everything failed" fallback, the tie-break
            // for Kahn's algorithm's frontier selection, and the signal
            // best-effort edges are derived from.
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

            // --- Tier 1: user-defined sort rules ---
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

                TryAddEdge(edges, indegree, beforeId, afterId);
            }

            // --- Tier 2: declared dependencies (info.txt require_before_me / require_after_me / require_ids) ---
            foreach (ModReference modref in allEnabled)
            {
                foreach (string dep in modref.require_before_me)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    TryAddEdge(edges, indegree, depId, modref.ID);
                }
                foreach (string dep in modref.require_after_me)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    TryAddEdge(edges, indegree, modref.ID, depId);
                }
                foreach (string dep in modref.require_ids)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    TryAddEdge(edges, indegree, depId, modref.ID);
                }
            }

            // --- Tier 3: vanilla-base structural edges. A mod sharing a raw
            // definition with a vanilla object (per DF's own errorlog.txt) is
            // almost always intended to patch/extend it, not replace it
            // silently — so vanilla goes first. ---
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

                        TryAddEdge(edges, indegree, vanillaId, modId);
                    }
                }
            }

            // --- Tier 4: raw-scan-derived edges (lowest priority — mechanically
            // inferred, not declared by anyone) ---
            Dictionary<string, List<string>> directDefiners = new(StringComparer.OrdinalIgnoreCase);
            foreach (string id in enabledIds)
            {
                if (!idMap.TryGetValue(id, out ModReference? modref) || modref == null)
                    continue;
                ModRawDependencyInfo? info = GetRawDependencyInfo(modref);
                if (info == null)
                    continue;

                foreach (string definedId in info.DirectDefinitionIds)
                {
                    if (!directDefiners.TryGetValue(definedId, out List<string>? definers))
                    {
                        definers = new List<string>();
                        directDefiners[definedId] = definers;
                    }
                    definers.Add(id);
                }
            }

            // CUT-before-SELECT: if mod A cuts target T and mod B separately
            // SELECTs (patches) T without also cutting it, A must load before
            // B — otherwise A's CUT silently erases B's additions. If both A
            // and B cut the same target, there's no principled correct order
            // (whichever cuts last is the one whose CUT sticks) — rather than
            // leave that undetermined, a deterministic best-effort order is
            // assigned using the same signal baseOrder itself is built from.
            foreach (string cutterId in enabledIds)
            {
                if (!idMap.TryGetValue(cutterId, out ModReference? cutterRef) || cutterRef == null)
                    continue;
                ModRawDependencyInfo? cutterInfo = GetRawDependencyInfo(cutterRef);
                if (cutterInfo == null || !cutterInfo.IsCutter || cutterInfo.CutTargetIds.Count == 0)
                    continue;

                HashSet<string> cutTargets = new HashSet<string>(cutterInfo.CutTargetIds, StringComparer.OrdinalIgnoreCase);

                foreach (string selectorId in enabledIds)
                {
                    if (string.Equals(selectorId, cutterId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!idMap.TryGetValue(selectorId, out ModReference? selectorRef) || selectorRef == null)
                        continue;
                    ModRawDependencyInfo? selectorInfo = GetRawDependencyInfo(selectorRef);
                    if (selectorInfo == null)
                        continue;

                    bool selectsCutTarget = selectorInfo.SelectTargetIds.Any(t => cutTargets.Contains(t));
                    if (!selectsCutTarget)
                        continue;

                    if (HasExplicitOrder(selectorRef, cutterRef) || HasExplicitOrder(cutterRef, selectorRef))
                        continue;

                    bool selectorAlsoCutsSameTarget = selectorInfo.IsCutter &&
                        selectorInfo.CutTargetIds.Any(t => cutTargets.Contains(t));

                    if (selectorAlsoCutsSameTarget)
                    {
                        string? winnerId = PickBestEffortOrder(cutterRef, selectorRef, originalIndex);
                        if (winnerId != null)
                        {
                            string loserId = string.Equals(winnerId, cutterId, StringComparison.OrdinalIgnoreCase) ? selectorId : cutterId;
                            TryAddEdge(edges, indegree, winnerId, loserId);
                        }
                        continue;
                    }

                    TryAddEdge(edges, indegree, cutterId, selectorId);
                }
            }

            // COPY_TAGS_FROM: a hard dependency — whoever directly defines the
            // source ID must load before anything that copies its tags.
            foreach (string copierId in enabledIds)
            {
                if (!idMap.TryGetValue(copierId, out ModReference? copierRef) || copierRef == null)
                    continue;
                ModRawDependencyInfo? copierInfo = GetRawDependencyInfo(copierRef);
                if (copierInfo == null || copierInfo.CopyTagsFromSourceIds.Count == 0)
                    continue;

                foreach (string sourceId in copierInfo.CopyTagsFromSourceIds)
                {
                    if (!directDefiners.TryGetValue(sourceId, out List<string>? definers))
                        continue;

                    foreach (string definerId in definers)
                    {
                        if (string.Equals(definerId, copierId, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!idMap.TryGetValue(definerId, out ModReference? definerRef) || definerRef == null)
                            continue;

                        if (HasExplicitOrder(copierRef, definerRef) || HasExplicitOrder(definerRef, copierRef))
                            continue;

                        TryAddEdge(edges, indegree, definerId, copierId);
                    }
                }
            }

            // Mod-vs-mod duplicate warnings (from DF's own errorlog.txt) where
            // none of the conflicting mods are vanilla — if some are cutters
            // and some aren't, put non-cutters first. Complementary to the
            // CUT-before-SELECT pass above: this comes from DF's actual
            // reported conflicts rather than requiring the scan to match a
            // specific SELECT target ID.
            foreach (HashSet<string> group in GetDuplicateWarningGroups())
            {
                List<string> modIds = group.Where(id =>
                    enabledIds.Contains(id) &&
                    idMap.TryGetValue(id, out ModReference? m) && m != null && !IsVanillaBaseMod(m)).ToList();

                if (modIds.Count <= 1)
                    continue;

                List<string> cutterIds = new List<string>();
                List<string> baseIds = new List<string>();
                foreach (string id in modIds)
                {
                    if (!idMap.TryGetValue(id, out ModReference? modref) || modref == null)
                        continue;
                    if (GetRawDependencyInfo(modref)?.IsCutter == true)
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

                        TryAddEdge(edges, indegree, baseId, cutterId);
                    }
                }
            }

            // Two or more enabled mods directly defining the same raw ID with
            // no CUT relationship between them is a genuine conflict — no
            // order makes it correct, DF will silently misbehave regardless.
            // Still assign a deterministic best-effort order (chained through
            // the whole group) rather than leave it to incidental
            // Kahn's-algorithm frontier timing, and surface it so it's at
            // least traceable.
            foreach (KeyValuePair<string, List<string>> kvp in directDefiners)
            {
                if (kvp.Value.Count <= 1)
                    continue;

                bool anyCutRelationship = false;
                foreach (string candidateId in kvp.Value)
                {
                    if (idMap.TryGetValue(candidateId, out ModReference? candidate) && candidate != null &&
                        GetRawDependencyInfo(candidate)?.CutTargetIds.Contains(kvp.Key) == true)
                    {
                        anyCutRelationship = true;
                        break;
                    }
                }

                if (anyCutRelationship)
                    continue; // already handled by the CUT-before-SELECT pass above

                Console.WriteLine($"[AutoSort] Warning: '{kvp.Key}' is directly defined by multiple enabled mods with no CUT relationship between them ({string.Join(", ", kvp.Value)}). This is likely to cause silent raw conflicts regardless of load order — applying a best-effort order.");

                List<ModReference> definers = kvp.Value
                    .Select(id => idMap.TryGetValue(id, out ModReference? m) ? m : null)
                    .Where(m => m != null)
                    .Cast<ModReference>()
                    .OrderBy(m => GetAutoSortGroup(m))
                    .ThenBy(m => originalIndex.TryGetValue(m.ID, out int idx) ? idx : int.MaxValue)
                    .ThenBy(m => m.name ?? m.ID, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < definers.Count - 1; i++)
                {
                    if (HasExplicitOrder(definers[i], definers[i + 1]) || HasExplicitOrder(definers[i + 1], definers[i]))
                        continue;
                    TryAddEdge(edges, indegree, definers[i].ID, definers[i + 1].ID);
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

            // With every edge added through TryAddEdge's cycle check, the
            // graph should always be acyclic by construction — this remains
            // only as a defensive fallback.
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

        // Coarse tie-breaker for mods with no explicit graph relationship to
        // anything else.
        private int GetAutoSortGroup(ModReference modref)
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