using ModHearth.Models;

namespace ModHearth.Utils.ModRaws
{
    /// <summary>
    /// Centralized container for the raw objects parsed from a single mod (or a merged
    /// set of mods). It exposes the data as a structural object-type graph and provides
    /// generic conflict-resolution queries over that graph.
    /// </summary>
    public sealed class RawDatabase
    {
        public string SourceMod { get; }
        public IReadOnlyList<RawObject> Objects { get; }
        public bool IsCutter { get; }
        public bool HasGraphics { get; }

        /// <summary>
        /// ObjectType -> set of Ids. Built only from objects that are direct definitions.
        /// </summary>
        public IReadOnlyDictionary<string, HashSet<string>> DefinedObjects { get; }

        public RawDatabase(string sourceMod, IEnumerable<RawObject> objects, bool isCutter, bool hasGraphics)
        {
            SourceMod = sourceMod;
            Objects = objects.ToList().AsReadOnly();
            IsCutter = isCutter;
            HasGraphics = hasGraphics;
            DefinedObjects = BuildDefinedObjects(Objects);
        }

        private static Dictionary<string, HashSet<string>> BuildDefinedObjects(IEnumerable<RawObject> objects)
        {
            Dictionary<string, HashSet<string>> result = new(StringComparer.OrdinalIgnoreCase);

            foreach (RawObject raw in objects)
            {
                if (!raw.IsDefinition)
                    continue;

                if (string.IsNullOrWhiteSpace(raw.ObjectType) || string.IsNullOrWhiteSpace(raw.Id))
                    continue;

                if (!result.TryGetValue(raw.ObjectType, out HashSet<string>? ids))
                {
                    ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[raw.ObjectType] = ids;
                }

                _ = ids.Add(raw.Id);
            }

            return result;
        }

        /// <summary>
        /// Projects this raw database into the legacy <see cref="ModRawDependencyInfo"/>
        /// DTO used by ModSort and the persistent cache.
        /// </summary>
        public ModRawDependencyInfo ToDependencyInfo(
            string modId,
            string numericVersion,
            long objectsFolderStampTicks,
            VanillaRawBaseline? vanillaBaseline)
        {
            StringComparer comparer = StringComparer.OrdinalIgnoreCase;

            HashSet<string> cutIds = new(comparer);
            HashSet<string> selectIds = new(comparer);
            HashSet<string> copyIds = new(comparer);
            HashSet<string> directIds = new(comparer);
            HashSet<string> permittedReactionIds = new(comparer);
            HashSet<string> permittedBuildingIds = new(comparer);
            HashSet<string> reactionBuildingIds = new(comparer);

            foreach (RawObject raw in Objects)
            {
                if (raw.IsCut && !string.IsNullOrWhiteSpace(raw.Id))
                    _ = cutIds.Add(new ObjectKey(raw.ObjectType, raw.Id).ToString());

                if (raw.IsSelection && !string.IsNullOrWhiteSpace(raw.Id))
                    _ = selectIds.Add(new ObjectKey(raw.ObjectType, raw.Id).ToString());

                if (raw.IsCopyTagsFrom && !string.IsNullOrWhiteSpace(raw.Id))
                    _ = copyIds.Add(new ObjectKey(raw.ObjectType, raw.Id).ToString());

                if (raw.IsDefinition && !string.IsNullOrWhiteSpace(raw.Id))
                    _ = directIds.Add(new ObjectKey(raw.ObjectType, raw.Id).ToString());

                if (raw.IsPermittedReaction && !string.IsNullOrWhiteSpace(raw.Id))
                    _ = permittedReactionIds.Add(raw.Id);

                if (raw.IsPermittedBuilding && !string.IsNullOrWhiteSpace(raw.Id))
                    _ = permittedBuildingIds.Add(raw.Id);

                if (raw.IsReactionBuildingReference && !string.IsNullOrWhiteSpace(raw.Id))
                    _ = reactionBuildingIds.Add(raw.Id);
            }

            bool hasVanillaEntity = false;
            bool hasNewEntity = false;
            bool hasReaction = false;
            bool hasCreature = false;
            bool hasNewStuff = false;

            foreach (RawObject raw in Objects)
            {
                if (!raw.IsDefinition || string.IsNullOrWhiteSpace(raw.ObjectType))
                    continue;

                if (string.Equals(raw.ObjectType, "ENTITY", StringComparison.OrdinalIgnoreCase))
                {
                    bool isVanilla = vanillaBaseline != null && vanillaBaseline.Contains(raw.ObjectType, raw.Id);
                    if (isVanilla)
                        hasVanillaEntity = true;
                    else
                        hasNewEntity = true;
                }
                else if (string.Equals(raw.ObjectType, "REACTION", StringComparison.OrdinalIgnoreCase))
                {
                    hasReaction = true;
                }
                else if (string.Equals(raw.ObjectType, "CREATURE", StringComparison.OrdinalIgnoreCase))
                {
                    hasCreature = true;
                }
                else if (string.Equals(raw.ObjectType, "INORGANIC", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(raw.ObjectType, "PLANT", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(raw.ObjectType, "ITEM", StringComparison.OrdinalIgnoreCase))
                {
                    hasNewStuff = true;
                }
            }

            if (hasCreature)
                hasNewStuff = true;

            return new ModRawDependencyInfo
            {
                ModId = modId,
                NumericVersion = numericVersion,
                ObjectsFolderStampTicks = objectsFolderStampTicks,
                IsCutter = IsCutter,
                CutTargetIds = [.. cutIds],
                SelectTargetIds = [.. selectIds],
                CopyTagsFromSourceIds = [.. copyIds],
                DirectDefinitionIds = [.. directIds],
                PermittedReactionIds = [.. permittedReactionIds],
                PermittedBuildingIds = [.. permittedBuildingIds],
                ReactionBuildingIds = [.. reactionBuildingIds],
                HasVanillaEntity = hasVanillaEntity,
                HasNewEntity = hasNewEntity,
                HasReaction = hasReaction,
                HasCreature = hasCreature,
                HasNewStuff = hasNewStuff,
                HasGraphics = HasGraphics
            };
        }
    }
}
