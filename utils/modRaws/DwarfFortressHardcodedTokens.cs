namespace ModHearth.Utils.ModRaws
{
    /// <summary>
    /// Raw-object domain a hardcoded token belongs to.
    /// </summary>
    internal enum HardcodedTokenDomain
    {
        Reaction,
        Building,
        Material
    }

    /// <summary>
    /// The raw context in which a hardcoded token is resolved.
    /// </summary>
    internal enum HardcodedTokenContext
    {
        /// <summary>A reaction's <c>[BUILDING:ID]</c> token.</summary>
        ReactionBuilding,

        /// <summary>A <c>CUT_REACTION</c> target.</summary>
        CutReaction,

        /// <summary>The material/matgloss slot of a <c>REAGENT</c> or <c>PRODUCT</c>.</summary>
        MaterialField,

        /// <summary>A material grammar keyword or sentinel rather than a concrete material.</summary>
        MaterialGrammar
    }

    /// <summary>
    /// How Dwarf Fortress treats a hardcoded token.
    /// </summary>
    internal enum HardcodedTokenBehavior
    {
        /// <summary>Resolved against a built-in enum/string table; no raw definition is expected.</summary>
        BuiltInReference,

        /// <summary>DF silently ignores a CUT for this id.</summary>
        CutIgnored,

        /// <summary>Constructed by DF itself instead of loaded from raws.</summary>
        BuiltInMaterial,

        /// <summary>Part of the material token grammar, not a material definition.</summary>
        Grammar
    }

    /// <summary>
    /// How a catalog group can be checked after a Dwarf Fortress update.
    /// </summary>
    internal enum HardcodedTokenVerification
    {
        /// <summary>
        /// The id must appear as a standalone NUL-terminated ASCII string in the executable. This is
        /// reliable for reaction ids and material tokens, which DF keeps in its string table.
        /// </summary>
        ExecutableString,

        /// <summary>
        /// The id is not reliably present as a standalone executable string: DF inlines short
        /// comparisons and uses prefixed graphics names (for example <c>WORKSHOP_LOOM</c>) for some
        /// workshops. Verify it against the vanilla raw reaction usage instead.
        /// </summary>
        VanillaRawUsage
    }

    /// <summary>
    /// Identifies one scoped catalog group. Group ids are stable because the audit report writes them.
    /// </summary>
    internal enum HardcodedTokenGroupId
    {
        ReactionBuildings,
        CutImmuneDyeReactions,
        ConcreteBuiltInMaterials,
        MaterialGrammarTokens
    }

    /// <summary>
    /// A scoped set of raw tokens whose resolution is hardcoded in Dwarf Fortress rather than defined
    /// by a raw file. Keeping domain, context, behavior, verification strategy, source, and
    /// verification version together prevents a list from being reused in a context where DF
    /// resolves it differently.
    /// </summary>
    internal sealed record HardcodedTokenGroup(
        HardcodedTokenGroupId Id,
        HardcodedTokenDomain Domain,
        HardcodedTokenContext Context,
        HardcodedTokenBehavior Behavior,
        HardcodedTokenVerification Verification,
        string Description,
        string Source,
        IReadOnlyList<string> Tokens);

    /// <summary>
    /// The central catalog of raw IDs and grammar tokens Dwarf Fortress hardcodes. It is deliberately
    /// more than the two lists the analyzer needs today: every entry records where DF resolves it and
    /// which DF version confirmed it, so the audit report can flag stale entries after a game update.
    ///
    /// The catalog is internal to ModHearth and the development-only RawIssueAudit tool. Refresh it
    /// when a DF update changes vanilla raws or the executable's hardcoded string table; run
    /// <c>--hardcoded-report --verify-hardcoded</c> to detect drift. The dye group records metadata
    /// only: <see cref="VanillaDyeReactionCatalog"/> derives its tokens from the installed raws.
    /// </summary>
    internal static class DwarfFortressHardcodedTokens
    {
        public const string VerifiedAgainst = "Dwarf Fortress 53.17 (2026-11-09)";

        private const string BuildingSource =
            "df-structures workshop_type and furnace_type enums. Some short ids are inlined in the " +
            "executable, so the drift check compares the catalog with vanilla reaction BUILDING usage " +
            "instead of requiring standalone executable strings.";
        private const string DyeSource =
            "data/vanilla/vanilla_reactions/objects/reaction_dyes.txt (definitions) and " +
            "data/vanilla/vanilla_entities/objects/entity_default.txt (permitted reactions), " +
            "intersected at runtime; the Dwarf Fortress.exe string table and real errorlog cut " +
            "experiments confirm the cut immunity.";
        private const string MaterialSource =
            "https://dwarffortresswiki.org/index.php/Hardcoded_material and DFHack Material-encoding docs.";
        private const string GrammarSource =
            "DFHack Material-encoding docs and vanilla/mod REAGENT/PRODUCT material slots.";

        public static IReadOnlyList<HardcodedTokenGroup> Groups { get; } =
        [
            new HardcodedTokenGroup(
                HardcodedTokenGroupId.ReactionBuildings,
                HardcodedTokenDomain.Building,
                HardcodedTokenContext.ReactionBuilding,
                HardcodedTokenBehavior.BuiltInReference,
                HardcodedTokenVerification.VanillaRawUsage,
                "Built-in workshops and furnaces accepted by a reaction's [BUILDING:ID] token. " +
                "No raw file defines them, and they are not valid PERMITTED_BUILDING ids.",
                BuildingSource,
                [
                    // workshop_type
                    "ASHERY", "BOWYER", "BUTCHER", "CARPENTER", "CLOTHES", "CRAFTSMAN", "DYER",
                    "FARMER", "FISHERY", "JEWELER", "KENNEL", "KITCHEN", "LAVAMILL", "LEATHER",
                    "LOOM", "MASON", "MECHANIC", "METALSMITH", "MILLSTONE", "QUERN", "SIEGE",
                    "STILL", "TANNER", "TOOL",

                    // furnace_type
                    "GLASS", "GLASS_LAVA", "KILN", "KILN_LAVA", "SMELTER", "SMELTER_LAVA", "WOOD"
                ]),

            new HardcodedTokenGroup(
                HardcodedTokenGroupId.CutImmuneDyeReactions,
                HardcodedTokenDomain.Reaction,
                HardcodedTokenContext.CutReaction,
                HardcodedTokenBehavior.CutIgnored,
                HardcodedTokenVerification.ExecutableString,
                "The vanilla dye reactions defined by reaction_dyes.txt and permitted by " +
                "entity_default.txt. DF carries their ids in the executable and silently ignores " +
                "CUT_REACTION for them; mod-added dye reactions are cut normally. Tokens are " +
                "discovered from the installed vanilla raws at runtime, not stored in this catalog.",
                DyeSource,
                []),

            new HardcodedTokenGroup(
                HardcodedTokenGroupId.ConcreteBuiltInMaterials,
                HardcodedTokenDomain.Material,
                HardcodedTokenContext.MaterialField,
                HardcodedTokenBehavior.BuiltInMaterial,
                HardcodedTokenVerification.ExecutableString,
                "Materials DF constructs itself. They need no raw definition and are valid as the " +
                "first material token of a REAGENT/PRODUCT slot.",
                MaterialSource,
                [
                    "AMBER", "ASH", "COAL", "CORAL", "FILTH_B", "FILTH_Y", "GLASS_CLEAR",
                    "GLASS_CRYSTAL", "GLASS_GREEN", "GRIME", "LYE", "MUD", "PEARLASH", "POTASH",
                    "SALT", "UNKNOWN_SUBSTANCE", "VOMIT", "WATER"
                ]),

            new HardcodedTokenGroup(
                HardcodedTokenGroupId.MaterialGrammarTokens,
                HardcodedTokenDomain.Material,
                HardcodedTokenContext.MaterialGrammar,
                HardcodedTokenBehavior.Grammar,
                HardcodedTokenVerification.ExecutableString,
                "Material grammar keywords and sentinels. They select how the remaining material " +
                "tokens are interpreted and are not material definitions themselves.",
                GrammarSource,
                [
                    "NONE", "INORGANIC", "METAL", "PLANT", "PLANT_MAT", "LOCAL_PLANT_MAT",
                    "CREATURE", "CREATURE_MAT", "LOCAL_CREATURE_MAT", "GET_MATERIAL_FROM_REAGENT"
                ])
        ];

        private static readonly Dictionary<HardcodedTokenGroupId, HardcodedTokenGroup> GroupsById =
            Groups.ToDictionary(group => group.Id);

        private static readonly Dictionary<HardcodedTokenGroupId, HashSet<string>> TokenSets =
            Groups.ToDictionary(
                group => group.Id,
                group => new HashSet<string>(group.Tokens, StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// The 31 built-in workshop/furnace ids accepted by a reaction's <c>[BUILDING:ID]</c>.
        /// </summary>
        public static IReadOnlySet<string> ReactionBuildingTokens { get; } =
            TokenSets[HardcodedTokenGroupId.ReactionBuildings];

        /// <summary>
        /// The 18 concrete materials DF constructs itself.
        /// </summary>
        public static IReadOnlySet<string> ConcreteBuiltInMaterialTokens { get; } =
            TokenSets[HardcodedTokenGroupId.ConcreteBuiltInMaterials];

        /// <summary>
        /// The 10 material grammar keywords and sentinels.
        /// </summary>
        public static IReadOnlySet<string> MaterialGrammarTokens { get; } =
            TokenSets[HardcodedTokenGroupId.MaterialGrammarTokens];

        public static HardcodedTokenGroup GroupOf(HardcodedTokenGroupId groupId)
            => GroupsById[groupId];

        public static IReadOnlyList<string> TokensOf(HardcodedTokenGroupId groupId)
            => GroupsById[groupId].Tokens;

        public static bool Contains(HardcodedTokenGroupId groupId, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            return TokenSets[groupId].Contains(token.Trim());
        }

        /// <summary>
        /// True when DF resolves this id in a reaction's <c>[BUILDING:ID]</c> against its hardcoded
        /// workshop/furnace types. Used by maintenance tooling to explain why the analyzer stayed
        /// silent for a token the errorlog reported.
        /// </summary>
        public static bool IsReactionBuildingId(string? id)
            => Contains(HardcodedTokenGroupId.ReactionBuildings, id);

        /// <summary>
        /// True when the first material token names a concrete material DF constructs itself.
        /// </summary>
        public static bool IsConcreteBuiltInMaterial(string? token)
            => Contains(HardcodedTokenGroupId.ConcreteBuiltInMaterials, token);

        /// <summary>
        /// True when the first material token is grammar or a sentinel rather than a material name.
        /// </summary>
        public static bool IsMaterialGrammarToken(string? token)
            => Contains(HardcodedTokenGroupId.MaterialGrammarTokens, token);
    }
}
