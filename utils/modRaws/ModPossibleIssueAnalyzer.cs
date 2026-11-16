using ModHearth.Models;

namespace ModHearth.Utils.ModRaws
{
    /// <summary>
    /// Which raw token produced a predicted issue.
    /// </summary>
    public enum ModPossibleIssueKind
    {
        MissingSelectTarget,
        MissingCutTarget,
        MissingCopyTagsFromSource,
        MissingPermittedReaction,
        MissingPermittedBuilding,
        MissingReactionBuilding
    }

    /// <summary>
    /// A raw reference that Dwarf Fortress would fail to resolve at load time. The target is a
    /// "ObjectType:Id" composite key (see <see cref="ObjectKey"/>).
    /// </summary>
    public sealed record ModPossibleIssue(string ModId, string TargetKey, ModPossibleIssueKind Kind);

    /// <summary>
    /// Predicts the raw-file errors Dwarf Fortress writes to errorlog.txt without launching the
    /// game. It only needs the persisted raw scan (<see cref="ModRawDependencyInfo"/>), so it runs
    /// against exactly the same data ModSort already uses.
    ///
    /// Only references that cannot be resolved at all are reported. References satisfied by another
    /// active mod or by the vanilla raws are silent, and a target that is both SELECTed and CUT is
    /// reported once.
    ///
    /// For the reference kinds errorlog.txt can verify, CUT is modeled in load order: vanilla
    /// definitions form the initial state, each active mod removes its CUT targets, and then adds
    /// its own definitions. A later definition therefore restores a target that an earlier mod cut.
    /// DF's hardcoded vanilla dye reactions (derived from vanilla raws) are exempt because DF
    /// silently ignores <c>CUT_REACTION</c> for them; mod-added dye reactions are cut normally.
    ///
    /// SELECT/CUT/COPY_TAGS_FROM are still checked against the union of active and vanilla
    /// definitions. DF emits no stable diagnostic for them, so a load-order model there could not
    /// be validated against real logs.
    ///
    /// Besides SELECT/CUT/COPY_TAGS_FROM this covers the bare-id references DF resolves as its own
    /// loading step: an entity's [PERMITTED_REACTION]/[PERMITTED_BUILDING] tokens and a reaction's
    /// [BUILDING:ID] token. Their diagnostic wording in errorlog.txt is "Unrecognized permitted
    /// reaction/building token" and "Unrecognized Building Token".
    ///
    /// Reference types that no active mod or vanilla raw actually defines are ignored. DF raw files
    /// also use SELECT_/CUT_ for nested selectors (SELECT_CASTE, SELECT_MATERIAL,
    /// SELECT_TISSUE_LAYER, ...), which name tokens inside an object rather than an object, and
    /// treating those as missing objects would produce nothing but false positives.
    /// </summary>
    public static class ModPossibleIssueAnalyzer
    {
        /// <summary>
        /// True when DF resolves this id in a reaction's <c>[BUILDING:ID]</c> token against its
        /// hardcoded workshop/furnace types. Used by maintenance tooling to explain why the
        /// analyzer stayed silent for a token the errorlog reported.
        /// </summary>
        internal static bool IsBuiltInReactionBuildingId(string? id)
            => DwarfFortressHardcodedTokens.IsReactionBuildingId(id);

        /// <summary>
        /// Analyzes the active mod raws. <paramref name="modLoadOrder"/> is the Dwarf Fortress load
        /// order of the active mod ids; when omitted, the enumeration order of
        /// <paramref name="modInfos"/> is treated as load order. Active mods missing from the order
        /// are applied last so an incomplete order never hides their definitions.
        /// </summary>
        public static List<ModPossibleIssue> Analyze(
            IEnumerable<ModRawDependencyInfo> modInfos,
            IEnumerable<string>? activeModIds,
            VanillaRawBaseline? vanillaBaseline,
            IReadOnlyList<string>? modLoadOrder = null)
        {
            HashSet<string>? activeFilter = activeModIds == null
                ? null
                : new HashSet<string>(
                    activeModIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                    StringComparer.OrdinalIgnoreCase);

            List<ModRawDependencyInfo> infos = [.. modInfos
                .Where(info => info != null && !string.IsNullOrWhiteSpace(info.ModId))
                .Where(info => activeFilter == null || activeFilter.Contains(info.ModId))];

            List<ModRawDependencyInfo> loadOrder = OrderByLoadOrder(infos, modLoadOrder);

            // UNION for SELECT/CUT/COPY_TAGS_FROM: DF does not verify these in errorlog.txt, so
            // they keep the conservative "any active definition counts" behavior.
            HashSet<string> definedKeys = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> knownObjectTypes = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModRawDependencyInfo info in infos)
            {
                foreach (string definitionKey in info.DirectDefinitionIds)
                {
                    if (string.IsNullOrWhiteSpace(definitionKey))
                        continue;

                    _ = definedKeys.Add(definitionKey);
                    if (ObjectKey.TryParse(definitionKey, out string objectType, out _))
                        _ = knownObjectTypes.Add(objectType);
                }
            }

            if (vanillaBaseline != null)
                knownObjectTypes.UnionWith(vanillaBaseline.ObjectTypes);

            // FINAL STATE for the reference kinds the errorlog can verify. Vanilla is the initial
            // load state; each active mod removes its CUT targets and then adds its definitions.
            // Definitions are applied after cuts because the cache stores an unordered set per mod:
            // a replacer that cuts a vanilla object and defines its own replacement in the same mod
            // must keep that replacement.
            HashSet<string> resolvedReactionIds = vanillaBaseline == null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(vanillaBaseline.IdsOf("REACTION"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> resolvedBuildingIds = vanillaBaseline == null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(vanillaBaseline.IdsOf("BUILDING"), StringComparer.OrdinalIgnoreCase);

            foreach (ModRawDependencyInfo info in loadOrder)
            {
                foreach (string cutKey in info.CutTargetIds)
                {
                    if (!ObjectKey.TryParse(cutKey, out string objectType, out string id))
                        continue;

                    // DF ignores CUT_REACTION for its hardcoded vanilla dye reactions. The baseline
                    // derives those ids from reaction_dyes.txt and entity_default.txt. Mod-added
                    // dye reactions (for example DCCF_DEEP_OAK_BARK_DYE) are cut normally.
                    if (string.Equals(objectType, "REACTION", StringComparison.OrdinalIgnoreCase)
                        && vanillaBaseline?.IsCutIgnoredDyeReaction(id) == true)
                    {
                        continue;
                    }

                    if (string.Equals(objectType, "REACTION", StringComparison.OrdinalIgnoreCase))
                        _ = resolvedReactionIds.Remove(id);
                    else if (string.Equals(objectType, "BUILDING", StringComparison.OrdinalIgnoreCase))
                        _ = resolvedBuildingIds.Remove(id);
                }

                foreach (string definitionKey in info.DirectDefinitionIds)
                {
                    if (!ObjectKey.TryParse(definitionKey, out string objectType, out string id))
                        continue;

                    if (string.Equals(objectType, "REACTION", StringComparison.OrdinalIgnoreCase))
                        _ = resolvedReactionIds.Add(id);
                    else if (string.Equals(objectType, "BUILDING", StringComparison.OrdinalIgnoreCase))
                        _ = resolvedBuildingIds.Add(id);
                }
            }

            List<ModPossibleIssue> issues = [];
            foreach (ModRawDependencyInfo info in infos)
            {
                // One issue per missing target even when the mod patches it more than once. The
                // precedence order keeps the strongest semantic (a missing COPY_TAGS_FROM source
                // breaks loading, while a redundant CUT is the mildest).
                Dictionary<string, ModPossibleIssueKind> missingTargets = new(StringComparer.OrdinalIgnoreCase);

                CollectMissing(info.CopyTagsFromSourceIds, ModPossibleIssueKind.MissingCopyTagsFromSource);
                CollectMissing(info.SelectTargetIds, ModPossibleIssueKind.MissingSelectTarget);
                CollectMissing(info.CutTargetIds, ModPossibleIssueKind.MissingCutTarget);

                // Entity [PERMITTED_*] tokens and reaction [BUILDING:...] tokens name bare ids.
                // They are only checked when nothing in the loaded set defines that object type,
                // which keeps a missing or incomplete vanilla baseline from producing noise.
                if (knownObjectTypes.Contains("REACTION"))
                {
                    CollectMissingBare(
                        info.PermittedReactionIds,
                        "REACTION",
                        ModPossibleIssueKind.MissingPermittedReaction,
                        resolvedReactionIds);
                }

                if (knownObjectTypes.Contains("BUILDING"))
                {
                    // PERMITTED_BUILDING only accepts mod-defined custom buildings, so the built-in
                    // workshop/furnace ids are not valid there. A reaction's [BUILDING:ID] does
                    // accept them because DF matches the id against its hardcoded building types
                    // before looking at raw-defined buildings.
                    CollectMissingBare(
                        info.PermittedBuildingIds,
                        "BUILDING",
                        ModPossibleIssueKind.MissingPermittedBuilding,
                        resolvedBuildingIds);
                    CollectMissingBare(
                        info.ReactionBuildingIds,
                        "BUILDING",
                        ModPossibleIssueKind.MissingReactionBuilding,
                        resolvedBuildingIds,
                        DwarfFortressHardcodedTokens.ReactionBuildingTokens);
                }

                foreach (KeyValuePair<string, ModPossibleIssueKind> missing in missingTargets
                    .OrderBy(entry => entry.Value)
                    .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add(new ModPossibleIssue(info.ModId, missing.Key, missing.Value));
                }

                void CollectMissing(IEnumerable<string>? targetKeys, ModPossibleIssueKind kind)
                {
                    if (targetKeys == null)
                        return;

                    foreach (string targetKey in targetKeys)
                    {
                        if (string.IsNullOrWhiteSpace(targetKey)
                            || !ObjectKey.TryParse(targetKey, out string objectType, out _)
                            || !knownObjectTypes.Contains(objectType)
                            || definedKeys.Contains(targetKey)
                            || vanillaBaseline?.Contains(targetKey) == true)
                        {
                            continue;
                        }

                        _ = missingTargets.TryAdd(targetKey, kind);
                    }
                }

                void CollectMissingBare(
                    IEnumerable<string>? ids,
                    string objectType,
                    ModPossibleIssueKind kind,
                    HashSet<string> resolvedIds,
                    IReadOnlySet<string>? builtInIds = null)
                {
                    if (ids == null)
                        return;

                    foreach (string id in ids)
                    {
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        string trimmedId = id.Trim();
                        if (resolvedIds.Contains(trimmedId)
                            || builtInIds?.Contains(trimmedId) == true)
                        {
                            continue;
                        }

                        _ = missingTargets.TryAdd(new ObjectKey(objectType, trimmedId).ToString(), kind);
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Orders the analyzed mods by DF load order. When no order is supplied, the input order is
        /// already the caller's best available load order. Mods missing from
        /// <paramref name="modLoadOrder"/> are appended in input order so their definitions still
        /// resolve references.
        /// </summary>
        private static List<ModRawDependencyInfo> OrderByLoadOrder(
            List<ModRawDependencyInfo> infos,
            IReadOnlyList<string>? modLoadOrder)
        {
            if (modLoadOrder == null || modLoadOrder.Count == 0)
                return infos;

            Dictionary<string, ModRawDependencyInfo> byModId = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModRawDependencyInfo info in infos)
                _ = byModId.TryAdd(info.ModId, info);

            List<ModRawDependencyInfo> ordered = new(infos.Count);
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string modId in modLoadOrder)
            {
                if (string.IsNullOrWhiteSpace(modId) || !seen.Add(modId.Trim()))
                    continue;

                if (byModId.TryGetValue(modId.Trim(), out ModRawDependencyInfo? info))
                    ordered.Add(info);
            }

            foreach (ModRawDependencyInfo info in infos)
            {
                if (seen.Add(info.ModId))
                    ordered.Add(info);
            }

            return ordered;
        }
    }
}
