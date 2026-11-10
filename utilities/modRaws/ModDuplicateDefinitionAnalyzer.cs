using ModHearth.Models;

namespace ModHearth.Utilities.ModRaws
{
    /// <summary>
    /// One raw object that more than one loaded source defines directly. <see cref="IncludesVanilla"/>
    /// is true when the official raw folders defining the object are part of the collision.
    /// </summary>
    public sealed record ModDuplicateDefinitionGroup(
        string DefinitionKey,
        IReadOnlyList<string> ModIds,
        bool IncludesVanilla);

    /// <summary>
    /// Finds direct-definition collisions that Dwarf Fortress reports as "Duplicate Object" in
    /// errorlog.txt. DF treats the official raw folders as ordinary mods, so a mod that redefines a
    /// vanilla object (for example a plant) collides exactly like two mods defining the same id.
    /// A definition removed by an active CUT is not a collision.
    /// </summary>
    internal static class ModDuplicateDefinitionAnalyzer
    {
        /// <summary>Synthetic offender id used for the official raw folders.</summary>
        public const string VanillaOffender = "vanilla raws";

        public static List<ModDuplicateDefinitionGroup> Analyze(
            IEnumerable<ModRawDependencyInfo> rawInfos,
            IEnumerable<string> activeModIds,
            VanillaRawBaseline vanillaBaseline)
        {
            HashSet<string> activeFilter = new(
                activeModIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            Dictionary<string, HashSet<string>> definitionToMods = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> activeCutIds = new(StringComparer.OrdinalIgnoreCase);

            foreach (ModRawDependencyInfo info in rawInfos)
            {
                if (info == null
                    || string.IsNullOrWhiteSpace(info.ModId)
                    || !activeFilter.Contains(info.ModId))
                {
                    continue;
                }

                foreach (string cutId in info.CutTargetIds)
                {
                    if (!string.IsNullOrWhiteSpace(cutId))
                        _ = activeCutIds.Add(cutId);
                }

                foreach (string definitionKey in info.DirectDefinitionIds)
                {
                    if (string.IsNullOrWhiteSpace(definitionKey))
                        continue;

                    if (!definitionToMods.TryGetValue(definitionKey, out HashSet<string>? mods))
                    {
                        mods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        definitionToMods[definitionKey] = mods;
                    }

                    _ = mods.Add(info.ModId);
                }
            }

            bool hasVanillaBaseline = !vanillaBaseline.IsEmpty;
            List<ModDuplicateDefinitionGroup> groups = [];

            foreach (KeyValuePair<string, HashSet<string>> kvp in definitionToMods
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                bool collidesWithVanilla = hasVanillaBaseline && vanillaBaseline.Contains(kvp.Key);
                if (kvp.Value.Count <= 1 && !collidesWithVanilla)
                    continue;

                if (activeCutIds.Contains(kvp.Key))
                    continue;

                List<string> modIds = [.. kvp.Value.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)];
                groups.Add(new ModDuplicateDefinitionGroup(kvp.Key, modIds, collidesWithVanilla));
            }

            return groups;
        }
    }
}
