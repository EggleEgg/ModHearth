namespace ModHearth.Utils.ModRaws
{
    /// <summary>
    /// The token form of a material reference found in a <c>REAGENT</c>/<c>PRODUCT</c> material slot.
    /// </summary>
    internal enum DwarfFortressMaterialTokenKind
    {
        /// <summary>No material token was present.</summary>
        Empty,

        /// <summary>The <c>NONE</c> sentinel: no material.</summary>
        None,

        /// <summary>A concrete material DF constructs itself.</summary>
        BuiltIn,

        /// <summary><c>INORGANIC:ID</c>, including the <c>INORGANIC:NONE</c> wildcard.</summary>
        Inorganic,

        /// <summary><c>METAL:ID</c>, which names an inorganic that has the metal flag.</summary>
        Metal,

        /// <summary><c>PLANT_MAT:PLANT:MATERIAL</c> or <c>PLANT:PLANT:MATERIAL</c>.</summary>
        Plant,

        /// <summary><c>CREATURE_MAT:CREATURE:MATERIAL</c> or <c>CREATURE:CREATURE:MATERIAL</c>.</summary>
        Creature,

        /// <summary><c>LOCAL_PLANT_MAT:MATERIAL</c>: a material defined by the enclosing plant.</summary>
        LocalPlant,

        /// <summary><c>LOCAL_CREATURE_MAT:MATERIAL</c>: a material defined by the enclosing creature.</summary>
        LocalCreature,

        /// <summary><c>GET_MATERIAL_FROM_REAGENT:REAGENT:MATERIAL</c>.</summary>
        FromReagent,

        /// <summary>An unrecognized or malformed token; the catalog report samples these.</summary>
        Unknown
    }

    /// <summary>
    /// Parses the first token of a DF material slot without resolving it against the raw database.
    /// Resolution needs the active mods and the vanilla baseline; this type only classifies the
    /// grammar so reporting and future analyzer work share one parser instead of ad-hoc string splits.
    /// </summary>
    internal sealed record DwarfFortressMaterialToken(
        string Raw,
        DwarfFortressMaterialTokenKind Kind,
        string Head,
        string? ObjectId,
        string? MaterialId,
        string? Gloss,
        IReadOnlyList<string> Parts)
    {
        /// <summary>
        /// True when the token itself is enough to resolve the material and no raw object lookup is
        /// needed. Local materials and from-reagent references are resolved in their enclosing raw
        /// context, which a standalone token cannot verify.
        /// </summary>
        public bool IsResolvedWithoutRawLookup => Kind is
            DwarfFortressMaterialTokenKind.Empty or
            DwarfFortressMaterialTokenKind.None or
            DwarfFortressMaterialTokenKind.BuiltIn or
            DwarfFortressMaterialTokenKind.LocalPlant or
            DwarfFortressMaterialTokenKind.LocalCreature or
            DwarfFortressMaterialTokenKind.FromReagent;

        /// <summary>
        /// The raw object type the token references, or null when the token does not name an object.
        /// </summary>
        public string? ReferencedObjectType => Kind switch
        {
            DwarfFortressMaterialTokenKind.Inorganic or DwarfFortressMaterialTokenKind.Metal => "INORGANIC",
            DwarfFortressMaterialTokenKind.Plant => "PLANT",
            DwarfFortressMaterialTokenKind.Creature => "CREATURE",
            _ => null
        };

        /// <summary>
        /// The id of the referenced raw object, or null when the token does not name one. The
        /// <c>INORGANIC:NONE</c>/<c>NO_MATGLOSS</c> wildcards match any inorganic (or, in a product,
        /// select magma) and therefore have no object id.
        /// </summary>
        public string? ReferencedObjectId
            => ReferencedObjectType == null || IsInorganicWildcard ? null : ObjectId;

        /// <summary>
        /// True for the "any inorganic" wildcard forms. <c>METAL</c> is a backward-compatible alias
        /// for <c>INORGANIC</c>, and both accept <c>NONE</c> and <c>NO_MATGLOSS</c>; in a product slot
        /// the same token selects magma instead of a raw-defined inorganic.
        /// </summary>
        public bool IsInorganicWildcard => Kind is
                DwarfFortressMaterialTokenKind.Inorganic or
                DwarfFortressMaterialTokenKind.Metal
            && (string.Equals(ObjectId, "NONE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ObjectId, "NO_MATGLOSS", StringComparison.OrdinalIgnoreCase));

        public static DwarfFortressMaterialToken Parse(string? raw)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return new DwarfFortressMaterialToken(
                    string.Empty,
                    DwarfFortressMaterialTokenKind.Empty,
                    string.Empty,
                    null,
                    null,
                    null,
                    []);
            }

            // Do not remove empty entries: the non-empty parts carry the grammar positions, and a
            // malformed slot should stay visible instead of silently shifting into a valid shape.
            string[] parts = [.. trimmed
                .Split(':', StringSplitOptions.None)
                .Select(part => part.Trim())];
            string head = parts[0].ToUpperInvariant();
            if (head.Length == 0)
                return Unknown(trimmed, head, parts);

            if (string.Equals(head, "NONE", StringComparison.OrdinalIgnoreCase))
                return new DwarfFortressMaterialToken(
                    trimmed,
                    DwarfFortressMaterialTokenKind.None,
                    head,
                    null,
                    null,
                    null,
                    parts);

            if (DwarfFortressHardcodedTokens.IsConcreteBuiltInMaterial(head))
            {
                return new DwarfFortressMaterialToken(
                    trimmed,
                    DwarfFortressMaterialTokenKind.BuiltIn,
                    head,
                    null,
                    null,
                    OptionalPart(parts, 1),
                    parts);
            }

            switch (head)
            {
                case "INORGANIC":
                case "METAL":
                    {
                        string? objectId = OptionalPart(parts, 1);
                        if (objectId == null)
                            return Unknown(trimmed, head, parts);

                        return new DwarfFortressMaterialToken(
                            trimmed,
                            head == "INORGANIC"
                                ? DwarfFortressMaterialTokenKind.Inorganic
                                : DwarfFortressMaterialTokenKind.Metal,
                            head,
                            objectId.ToUpperInvariant(),
                            null,
                            null,
                            parts);
                    }

                case "PLANT":
                case "PLANT_MAT":
                    {
                        string? plantId = OptionalPart(parts, 1);
                        if (plantId == null)
                            return Unknown(trimmed, head, parts);

                        return new DwarfFortressMaterialToken(
                            trimmed,
                            DwarfFortressMaterialTokenKind.Plant,
                            "PLANT_MAT",
                            plantId.ToUpperInvariant(),
                            OptionalPart(parts, 2)?.ToUpperInvariant(),
                            null,
                            parts);
                    }

                case "CREATURE":
                case "CREATURE_MAT":
                    {
                        string? creatureId = OptionalPart(parts, 1);
                        if (creatureId == null)
                            return Unknown(trimmed, head, parts);

                        return new DwarfFortressMaterialToken(
                            trimmed,
                            DwarfFortressMaterialTokenKind.Creature,
                            "CREATURE_MAT",
                            creatureId.ToUpperInvariant(),
                            OptionalPart(parts, 2)?.ToUpperInvariant(),
                            null,
                            parts);
                    }

                case "LOCAL_PLANT_MAT":
                    return new DwarfFortressMaterialToken(
                        trimmed,
                        DwarfFortressMaterialTokenKind.LocalPlant,
                        head,
                        null,
                        OptionalPart(parts, 1)?.ToUpperInvariant(),
                        null,
                        parts);

                case "LOCAL_CREATURE_MAT":
                    return new DwarfFortressMaterialToken(
                        trimmed,
                        DwarfFortressMaterialTokenKind.LocalCreature,
                        head,
                        null,
                        OptionalPart(parts, 1)?.ToUpperInvariant(),
                        null,
                        parts);

                case "GET_MATERIAL_FROM_REAGENT":
                    return new DwarfFortressMaterialToken(
                        trimmed,
                        DwarfFortressMaterialTokenKind.FromReagent,
                        head,
                        OptionalPart(parts, 1)?.ToUpperInvariant(),
                        OptionalPart(parts, 2)?.ToUpperInvariant(),
                        null,
                        parts);

                default:
                    return Unknown(trimmed, head, parts);
            }
        }

        private static DwarfFortressMaterialToken Unknown(
            string raw,
            string head,
            IReadOnlyList<string> parts)
            => new(
                raw,
                DwarfFortressMaterialTokenKind.Unknown,
                head,
                null,
                null,
                null,
                parts);

        private static string? OptionalPart(string[] parts, int index)
            => index < parts.Length && parts[index].Length > 0 ? parts[index] : null;
    }
}
