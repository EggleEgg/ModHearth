using ModHearth.Models;

namespace ModHearth.Utils.ModRaws
{
    /// <summary>
    /// One raw object that more than one loaded source defines directly. <see cref="IncludesVanilla"/>
    /// is true when a non-vanilla active mod collides with the vanilla baseline, so the official raws
    /// are an additional offender beyond the mods in <see cref="ModIds"/>.
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
    ///
    /// Vanilla source mods are the folders the baseline was loaded from, so their own definitions
    /// are not collisions with vanilla. Only a non-vanilla mod redefining a vanilla id is reported;
    /// otherwise every enabled vanilla mod would warn that it duplicates itself.
    /// </summary>
    internal static class ModDuplicateDefinitionAnalyzer
    {
        /// <summary>Synthetic offender id used for the official raw folders.</summary>
        public const string VanillaOffender = "vanilla raws";

        /// <param name="vanillaSourceModIds">
        /// Active mod ids whose folders are part of the vanilla baseline (the <c>data/vanilla</c>
        /// mods). Their definitions count as vanilla, not as duplicate definitions.
        /// </param>
        public static List<ModDuplicateDefinitionGroup> Analyze(
            IEnumerable<ModRawDependencyInfo> rawInfos,
            IEnumerable<string> activeModIds,
            VanillaRawBaseline vanillaBaseline,
            IEnumerable<string>? vanillaSourceModIds = null)
        {
            HashSet<string> activeFilter = new(
                activeModIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            HashSet<string> vanillaSources = new(
                (vanillaSourceModIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            Dictionary<string, HashSet<string>> definitionToMods = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> nonVanillaDefinitions = new(StringComparer.OrdinalIgnoreCase);
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
                    if (!vanillaSources.Contains(info.ModId))
                        _ = nonVanillaDefinitions.Add(definitionKey);
                }
            }

            bool hasVanillaBaseline = !vanillaBaseline.IsEmpty;
            List<ModDuplicateDefinitionGroup> groups = [];

            foreach (KeyValuePair<string, HashSet<string>> kvp in definitionToMods
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                // A vanilla source mod defining its own vanilla id is the baseline describing itself,
                // not a duplicate. The collision is only real when a non-vanilla mod defines the key.
                bool collidesWithVanilla = hasVanillaBaseline
                    && nonVanillaDefinitions.Contains(kvp.Key)
                    && vanillaBaseline.Contains(kvp.Key);
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
