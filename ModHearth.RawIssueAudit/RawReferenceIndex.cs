using ModHearth.Models;
using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Indexes which mods contain a captured raw reference, definition, or CUT target. Keeping an
    /// all-mod index alongside the active-mod one lets the audit distinguish "the scanner saw the
    /// reference on this mod" from "the scanner never captured this token" when DF reports an error
    /// the analyzer did not predict.
    /// </summary>
    internal sealed class RawReferenceIndex
    {
        private readonly Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> activeReferences;
        private readonly Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> allReferences;
        private readonly Dictionary<string, HashSet<string>> activeDefinitions;
        private readonly Dictionary<string, HashSet<string>> allDefinitions;
        private readonly Dictionary<string, HashSet<string>> activeCuts;
        private readonly Dictionary<string, HashSet<string>> allCuts;

        private RawReferenceIndex(
            Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> activeReferences,
            Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> allReferences,
            Dictionary<string, HashSet<string>> activeDefinitions,
            Dictionary<string, HashSet<string>> allDefinitions,
            Dictionary<string, HashSet<string>> activeCuts,
            Dictionary<string, HashSet<string>> allCuts)
        {
            this.activeReferences = activeReferences;
            this.allReferences = allReferences;
            this.activeDefinitions = activeDefinitions;
            this.allDefinitions = allDefinitions;
            this.activeCuts = activeCuts;
            this.allCuts = allCuts;
        }

        public static RawReferenceIndex Build(
            IEnumerable<ModRawDependencyInfo> modInfos,
            IReadOnlySet<string> activeModIds)
        {
            Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> activeReferences = NewReferenceMap();
            Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> allReferences = NewReferenceMap();
            Dictionary<string, HashSet<string>> activeDefinitions = NewObjectMap();
            Dictionary<string, HashSet<string>> allDefinitions = NewObjectMap();
            Dictionary<string, HashSet<string>> activeCuts = NewObjectMap();
            Dictionary<string, HashSet<string>> allCuts = NewObjectMap();

            foreach (ModRawDependencyInfo info in modInfos)
            {
                if (info == null || string.IsNullOrWhiteSpace(info.ModId))
                    continue;

                bool isActive = activeModIds.Contains(info.ModId);

                foreach (string definition in info.DirectDefinitionIds)
                {
                    string targetKey = NormalizeObjectKey(definition);
                    if (targetKey.Length == 0)
                        continue;

                    _ = AddToObjectMap(allDefinitions, targetKey, info.ModId);
                    if (isActive)
                        _ = AddToObjectMap(activeDefinitions, targetKey, info.ModId);
                }

                foreach (string cut in info.CutTargetIds)
                {
                    string targetKey = NormalizeObjectKey(cut);
                    if (targetKey.Length == 0)
                        continue;

                    _ = AddToObjectMap(allCuts, targetKey, info.ModId);
                    if (isActive)
                        _ = AddToObjectMap(activeCuts, targetKey, info.ModId);
                }

                foreach ((ModPossibleIssueKind kind, IEnumerable<string> references) in GetReferences(info))
                {
                    foreach (string reference in references)
                    {
                        if (string.IsNullOrWhiteSpace(reference))
                            continue;

                        string targetKey = NormalizeReference(kind, reference);
                        _ = AddToReferenceMap(allReferences, kind, targetKey, info.ModId);
                        if (isActive)
                            _ = AddToReferenceMap(activeReferences, kind, targetKey, info.ModId);
                    }
                }
            }

            return new RawReferenceIndex(
                activeReferences,
                allReferences,
                activeDefinitions,
                allDefinitions,
                activeCuts,
                allCuts);
        }

        public bool HasActiveReference(ModPossibleIssueKind kind, string targetKey)
            => GetActiveMods(kind, targetKey).Count > 0;

        public IReadOnlyCollection<string> GetActiveMods(ModPossibleIssueKind kind, string targetKey)
            => LookupReference(activeReferences, kind, targetKey);

        public IReadOnlyCollection<string> GetAllMods(ModPossibleIssueKind kind, string targetKey)
            => LookupReference(allReferences, kind, targetKey);

        public IReadOnlyCollection<string> GetActiveDefinitionMods(string targetKey)
            => LookupObject(activeDefinitions, targetKey);

        public IReadOnlyCollection<string> GetAllDefinitionMods(string targetKey)
            => LookupObject(allDefinitions, targetKey);

        public IReadOnlyCollection<string> GetActiveCutMods(string targetKey)
            => LookupObject(activeCuts, targetKey);

        public IReadOnlyCollection<string> GetAllCutMods(string targetKey)
            => LookupObject(allCuts, targetKey);

        private static Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> NewReferenceMap()
            => new()
            {
                [ModPossibleIssueKind.MissingSelectTarget] = new(StringComparer.OrdinalIgnoreCase),
                [ModPossibleIssueKind.MissingCutTarget] = new(StringComparer.OrdinalIgnoreCase),
                [ModPossibleIssueKind.MissingCopyTagsFromSource] = new(StringComparer.OrdinalIgnoreCase),
                [ModPossibleIssueKind.MissingPermittedReaction] = new(StringComparer.OrdinalIgnoreCase),
                [ModPossibleIssueKind.MissingPermittedBuilding] = new(StringComparer.OrdinalIgnoreCase),
                [ModPossibleIssueKind.MissingReactionBuilding] = new(StringComparer.OrdinalIgnoreCase)
            };

        private static Dictionary<string, HashSet<string>> NewObjectMap()
            => new(StringComparer.OrdinalIgnoreCase);

        private static IEnumerable<(ModPossibleIssueKind Kind, IEnumerable<string> References)> GetReferences(
            ModRawDependencyInfo info)
        {
            yield return (ModPossibleIssueKind.MissingSelectTarget, info.SelectTargetIds);
            yield return (ModPossibleIssueKind.MissingCutTarget, info.CutTargetIds);
            yield return (ModPossibleIssueKind.MissingCopyTagsFromSource, info.CopyTagsFromSourceIds);
            yield return (ModPossibleIssueKind.MissingPermittedReaction, info.PermittedReactionIds);
            yield return (ModPossibleIssueKind.MissingPermittedBuilding, info.PermittedBuildingIds);
            yield return (ModPossibleIssueKind.MissingReactionBuilding, info.ReactionBuildingIds);
        }

        private static string NormalizeReference(ModPossibleIssueKind kind, string reference)
        {
            string trimmed = reference.Trim();
            return kind switch
            {
                ModPossibleIssueKind.MissingPermittedReaction => new ObjectKey("REACTION", trimmed).ToString(),
                ModPossibleIssueKind.MissingPermittedBuilding => new ObjectKey("BUILDING", trimmed).ToString(),
                ModPossibleIssueKind.MissingReactionBuilding => new ObjectKey("BUILDING", trimmed).ToString(),
                _ => NormalizeObjectKey(trimmed)
            };
        }

        private static string NormalizeObjectKey(string? compositeKey)
        {
            string trimmed = (compositeKey ?? string.Empty).Trim();
            return ObjectKey.TryParse(trimmed, out string objectType, out string id)
                ? new ObjectKey(objectType, id).ToString()
                : string.Empty;
        }

        private static HashSet<string> LookupReference(
            Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> map,
            ModPossibleIssueKind kind,
            string targetKey)
            => map.TryGetValue(kind, out Dictionary<string, HashSet<string>>? byTarget)
                && byTarget.TryGetValue(NormalizeObjectKey(targetKey), out HashSet<string>? mods)
                    ? mods
                    : [];

        private static HashSet<string> LookupObject(
            Dictionary<string, HashSet<string>> map,
            string targetKey)
            => map.TryGetValue(NormalizeObjectKey(targetKey), out HashSet<string>? mods)
                ? mods
                : [];

        private static bool AddToReferenceMap(
            Dictionary<ModPossibleIssueKind, Dictionary<string, HashSet<string>>> map,
            ModPossibleIssueKind kind,
            string targetKey,
            string modId)
        {
            if (!map.TryGetValue(kind, out Dictionary<string, HashSet<string>>? byTarget))
            {
                byTarget = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                map[kind] = byTarget;
            }

            return AddToObjectMap(byTarget, targetKey, modId);
        }

        private static bool AddToObjectMap(
            Dictionary<string, HashSet<string>> map,
            string targetKey,
            string modId)
        {
            if (!map.TryGetValue(targetKey, out HashSet<string>? mods))
            {
                mods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[targetKey] = mods;
            }

            return mods.Add(modId);
        }
    }
}
