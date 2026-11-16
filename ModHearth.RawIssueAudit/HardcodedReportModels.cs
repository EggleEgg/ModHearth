using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    internal sealed record HardcodedReportInputs(
        string CachePath,
        DateTime CacheLastWriteUtc,
        int CacheSchemaVersion,
        int CacheEntryCount,
        int UniqueModCount,
        string VanillaPath,
        int VanillaKeyCount,
        string? ModListPath,
        string? ModListName,
        string ActiveModSource,
        int ActiveModCount,
        string? ConfigPath,
        string? DwarfFortressPath,
        string? DwarfFortressExecutablePath,
        int ScannedModCount,
        int ScannedFileCount);

    /// <summary>
    /// One catalog group as shown in the report. Tokens are included so the report is a complete
    /// inventory rather than only a set of counts.
    /// </summary>
    internal sealed record HardcodedCatalogGroupSummary(
        HardcodedTokenGroupId Id,
        HardcodedTokenDomain Domain,
        HardcodedTokenContext Context,
        HardcodedTokenBehavior Behavior,
        HardcodedTokenVerification Verification,
        string Description,
        string Source,
        string VerifiedAgainst,
        IReadOnlyList<string> Tokens);

    /// <summary>
    /// How often the active mod set uses a catalog token in a context where DF resolves it.
    /// </summary>
    internal sealed record HardcodedTokenUsage(
        HardcodedTokenGroupId GroupId,
        string Token,
        int OccurrenceCount,
        IReadOnlyList<string> Mods);

    internal enum HardcodedMaterialReferenceStatus
    {
        MissingInorganic,
        MissingPlant,
        MissingCreature
    }

    /// <summary>
    /// A material token that names a raw object no active mod or vanilla raw defines. Aggregated by
    /// mod, token, and status so a single typo repeated in many reactions stays readable.
    /// </summary>
    internal sealed record HardcodedMaterialReference(
        string ModId,
        string Token,
        string Head,
        string? ObjectId,
        HardcodedMaterialReferenceStatus Status,
        int OccurrenceCount,
        IReadOnlyList<string> SampleLocations);

    /// <summary>
    /// A PERMITTED_BUILDING id that is actually a hardcoded workshop/furnace type. DF accepts those
    /// ids in a reaction's BUILDING token but not as an entity's permitted building.
    /// </summary>
    internal sealed record HardcodedInvalidContext(
        string ModId,
        string Token,
        string Detail);

    internal enum HardcodedCatalogCheckStatus
    {
        /// <summary>A catalog entry that should have no vanilla definition now does.</summary>
        UnexpectedVanillaDefinition,

        /// <summary>Verification found no NUL-terminated copy of the token in the executable.</summary>
        CatalogTokenMissingFromExecutable,

        /// <summary>Verification found a vanilla reaction id in the executable that is not in the derived dye set.</summary>
        UnexpectedReactionIdInExecutable,

        /// <summary>A dye reaction derived from vanilla raws is no longer a standalone executable string.</summary>
        VanillaReactionMissingFromExecutable,

        /// <summary>
        /// A vanilla reaction uses a BUILDING id that no vanilla raw defines and that is missing from
        /// the built-in reaction-building catalog. A DF update may have added a hardcoded workshop.
        /// </summary>
        CatalogMissingReactionBuildingUsedByVanilla
    }

    internal sealed record HardcodedCatalogCheck(
        HardcodedTokenGroupId? GroupId,
        string Token,
        HardcodedCatalogCheckStatus Status,
        string Detail);

    /// <summary>
    /// A material token the parser could not classify. These are samples for catalog maintenance,
    /// not necessarily errors; DF may accept forms the parser does not model yet.
    /// </summary>
    internal sealed record HardcodedMaterialSample(
        string Head,
        int OccurrenceCount,
        IReadOnlyList<string> Mods,
        IReadOnlyList<string> Examples);

    internal sealed record HardcodedVerificationResult(
        string ExecutablePath,
        int VanillaReactionCount,
        int DerivedCutIgnoredReactionCount,
        int ExecutableVanillaReactionCount,
        IReadOnlyList<HardcodedCatalogCheck> Checks)
    {
        public bool IsClean => Checks.Count == 0;
    }

    internal sealed record HardcodedReport(
        HardcodedReportInputs Inputs,
        IReadOnlyList<HardcodedCatalogGroupSummary> Catalog,
        IReadOnlyList<HardcodedTokenUsage> Usage,
        IReadOnlyList<HardcodedMaterialReference> UnresolvedMaterials,
        IReadOnlyList<HardcodedInvalidContext> InvalidContexts,
        IReadOnlyList<HardcodedCatalogCheck> VanillaChecks,
        IReadOnlyList<HardcodedMaterialSample> UnclassifiedMaterials,
        IReadOnlyList<string> Warnings,
        HardcodedVerificationResult? Verification)
    {
        public int DriftCount => Verification?.Checks.Count ?? 0;
    }
}
