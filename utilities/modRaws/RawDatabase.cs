using ModHearth;

namespace ModHearth.Utilities;

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

    private static IReadOnlyDictionary<string, HashSet<string>> BuildDefinedObjects(IEnumerable<RawObject> objects)
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

            ids.Add(raw.Id);
        }

        return result;
    }

    public bool IsDefined(ObjectKey? key)
    {
        if (key is null)
            return false;

        return DefinedObjects.TryGetValue(key.ObjectType, out HashSet<string>? ids)
            && ids.Contains(key.Id);
    }

    public bool IsDefined(string objectType, string id)
    {
        if (string.IsNullOrWhiteSpace(objectType) || string.IsNullOrWhiteSpace(id))
            return false;

        return DefinedObjects.TryGetValue(objectType, out HashSet<string>? ids)
            && ids.Contains(id);
    }

    /// <summary>
    /// Returns definition objects that share the same object type and ID.
    /// </summary>
    public IEnumerable<IGrouping<ObjectKey, RawObject>> GetDuplicateDefinitions()
        => Objects
            .Where(o => o.IsDefinition)
            .GroupBy(o => new ObjectKey(o.ObjectType, o.Id))
            .Where(g => g.Count() > 1);

    /// <summary>
    /// Returns selection, cut, or copy-tags-from references whose target object
    /// is not defined in this database.
    /// </summary>
    public IEnumerable<RawObject> GetUnresolvedDependencies()
        => Objects
            .Where(o => (o.IsSelection || o.IsCut || o.IsCopyTagsFrom)
                        && !IsDefined(o.ObjectType, o.Id));

    /// <summary>
    /// Returns cut operations that override an object defined in this database.
    /// </summary>
    public IEnumerable<RawObject> GetOverrideRelationships()
        => Objects
            .Where(o => o.IsCut && IsDefined(o.ObjectType, o.Id));

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

        foreach (RawObject raw in Objects)
        {
            if (raw.IsCut && !string.IsNullOrWhiteSpace(raw.Id))
                cutIds.Add(raw.Id);

            if (raw.IsSelection && !string.IsNullOrWhiteSpace(raw.Id))
                selectIds.Add(raw.Id);

            if (raw.IsCopyTagsFrom && !string.IsNullOrWhiteSpace(raw.Id))
                copyIds.Add(raw.Id);

            if (raw.IsDefinition && !string.IsNullOrWhiteSpace(raw.Id))
                directIds.Add(raw.Id);
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
                  || raw.ObjectType.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase))
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
            CutTargetIds = cutIds.ToList(),
            SelectTargetIds = selectIds.ToList(),
            CopyTagsFromSourceIds = copyIds.ToList(),
            DirectDefinitionIds = directIds.ToList(),
            HasVanillaEntity = hasVanillaEntity,
            HasNewEntity = hasNewEntity,
            HasReaction = hasReaction,
            HasCreature = hasCreature,
            HasNewStuff = hasNewStuff,
            HasGraphics = HasGraphics
        };
    }
}
