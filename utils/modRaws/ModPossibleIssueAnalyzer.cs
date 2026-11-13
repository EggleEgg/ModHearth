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
        /// DF hardcodes its built-in workshops and furnaces; no raw file defines them, but vanilla
        /// reactions still use their ids in <c>[BUILDING:ID]</c> tokens (for example
        /// <c>[BUILDING:CARPENTER]</c> and <c>[BUILDING:SMELTER]</c>). These are the original enum
        /// names from df-structures' <c>workshop_type</c> and <c>furnace_type</c>, excluding
        /// NONE/CUSTOM because those are not real building ids. Unknown ids are still reported.
        /// </summary>
        private static readonly HashSet<string> BuiltInReactionBuildingIds = new(StringComparer.OrdinalIgnoreCase)
        {
            // workshop_type
            "CARPENTER", "FARMER", "MASON", "CRAFTSMAN", "JEWELER", "METALSMITH", "LAVAMILL",
            "BOWYER", "MECHANIC", "SIEGE", "BUTCHER", "LEATHER", "TANNER", "CLOTHES",
            "FISHERY", "STILL", "LOOM", "QUERN", "KENNEL", "KITCHEN", "ASHERY", "DYER",
            "MILLSTONE", "TOOL",

            // furnace_type
            "WOOD", "SMELTER", "GLASS", "KILN", "SMELTER_LAVA", "GLASS_LAVA", "KILN_LAVA"
        };

        /// <summary>
        /// True when DF resolves this id in a reaction's <c>[BUILDING:ID]</c> token against its
        /// hardcoded workshop/furnace types. Used by maintenance tooling to explain why the
        /// analyzer stayed silent for a token the errorlog reported.
        /// </summary>
        internal static bool IsBuiltInReactionBuildingId(string? id)
            => !string.IsNullOrWhiteSpace(id) && BuiltInReactionBuildingIds.Contains(id.Trim());

        public static List<ModPossibleIssue> Analyze(
            IEnumerable<ModRawDependencyInfo> modInfos,
            IEnumerable<string>? activeModIds,
            VanillaRawBaseline? vanillaBaseline)
        {
            HashSet<string>? activeFilter = activeModIds == null
                ? null
                : new HashSet<string>(
                    activeModIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                    StringComparer.OrdinalIgnoreCase);

            List<ModRawDependencyInfo> infos = [.. modInfos
                .Where(info => info != null && !string.IsNullOrWhiteSpace(info.ModId))
                .Where(info => activeFilter == null || activeFilter.Contains(info.ModId))];

            // Every ID directly defined by an active mod resolves references, no matter which mod
            // defines it: DF merges the loaded raws before resolving SELECT/CUT/COPY_TAGS_FROM.
            HashSet<string> definedKeys = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> knownObjectTypes = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> definedReactionIds = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> definedBuildingIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModRawDependencyInfo info in infos)
            {
                foreach (string definitionKey in info.DirectDefinitionIds)
                {
                    if (string.IsNullOrWhiteSpace(definitionKey))
                        continue;

                    _ = definedKeys.Add(definitionKey);
                    if (!ObjectKey.TryParse(definitionKey, out string objectType, out string id))
                        continue;

                    _ = knownObjectTypes.Add(objectType);
                    if (string.Equals(objectType, "REACTION", StringComparison.OrdinalIgnoreCase))
                        _ = definedReactionIds.Add(id);
                    else if (string.Equals(objectType, "BUILDING", StringComparison.OrdinalIgnoreCase))
                        _ = definedBuildingIds.Add(id);
                }
            }

            if (vanillaBaseline != null)
                knownObjectTypes.UnionWith(vanillaBaseline.ObjectTypes);

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
                        definedReactionIds);
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
                        definedBuildingIds);
                    CollectMissingBare(
                        info.ReactionBuildingIds,
                        "BUILDING",
                        ModPossibleIssueKind.MissingReactionBuilding,
                        definedBuildingIds,
                        BuiltInReactionBuildingIds);
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
                    HashSet<string> definedIds,
                    HashSet<string>? builtInIds = null)
                {
                    if (ids == null)
                        return;

                    foreach (string id in ids)
                    {
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        string trimmedId = id.Trim();
                        if (definedIds.Contains(trimmedId)
                            || builtInIds?.Contains(trimmedId) == true
                            || vanillaBaseline?.Contains(objectType, trimmedId) == true)
                        {
                            continue;
                        }

                        _ = missingTargets.TryAdd(new ObjectKey(objectType, trimmedId).ToString(), kind);
                    }
                }
            }

            return issues;
        }
    }
}
