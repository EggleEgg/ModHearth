namespace ModHearth.Utilities;

/// <summary>
/// A single parsed token from a Dwarf Fortress raw file. The token is interpreted
/// structurally: the object type is derived from the active [OBJECT:TYPE] context or
/// from a SELECT_/CUT_ prefix, and the operation is classified as a definition,
/// selection, cut, or COPY_TAGS_FROM reference.
/// </summary>
public record RawObject(
    string ObjectType,
    string Id,
    string SourceMod,
    bool IsDefinition,
    bool IsSelection,
    bool IsCut)
{
    /// <summary>
    /// True when this token is a [COPY_TAGS_FROM:ID] reference inside the active
    /// object type context.
    /// </summary>
    public bool IsCopyTagsFrom { get; init; }
}
