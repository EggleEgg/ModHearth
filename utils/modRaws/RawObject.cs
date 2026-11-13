namespace ModHearth.Utils.ModRaws
{
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

        /// <summary>
        /// True when this token is an entity's [PERMITTED_REACTION:ID] reference.
        /// </summary>
        public bool IsPermittedReaction { get; init; }

        /// <summary>
        /// True when this token is an entity's [PERMITTED_BUILDING:ID] reference.
        /// </summary>
        public bool IsPermittedBuilding { get; init; }

        /// <summary>
        /// True when this token is a reaction's [BUILDING:ID] reference.
        /// </summary>
        public bool IsReactionBuildingReference { get; init; }

        /// <summary>
        /// A [TAG:ID] declaration of the active object type.
        /// </summary>
        public static RawObject Definition(string objectType, string id, string sourceMod)
            => new(objectType, id, sourceMod, true, false, false);

        /// <summary>
        /// A SELECT_&lt;TYPE&gt;:ID patch target.
        /// </summary>
        public static RawObject Selection(string objectType, string id, string sourceMod)
            => new(objectType, id, sourceMod, false, true, false);

        /// <summary>
        /// A CUT_&lt;TYPE&gt;:ID token, or the target of a plain [CUT].
        /// </summary>
        public static RawObject Cut(string objectType, string id, string sourceMod)
            => new(objectType, id, sourceMod, false, false, true);

        /// <summary>
        /// A bare reference (no definition / selection / cut flag) to another object.
        /// </summary>
        public static RawObject Reference(string objectType, string id, string sourceMod)
            => new(objectType, id, sourceMod, false, false, false);

        public static RawObject CopyTagsFrom(string objectType, string id, string sourceMod)
            => Reference(objectType, id, sourceMod) with { IsCopyTagsFrom = true };

        public static RawObject PermittedReaction(string id, string sourceMod)
            => Reference("REACTION", id, sourceMod) with { IsPermittedReaction = true };

        public static RawObject PermittedBuilding(string id, string sourceMod)
            => Reference("BUILDING", id, sourceMod) with { IsPermittedBuilding = true };

        public static RawObject ReactionBuilding(string id, string sourceMod)
            => Reference("BUILDING", id, sourceMod) with { IsReactionBuildingReference = true };
    }
}
