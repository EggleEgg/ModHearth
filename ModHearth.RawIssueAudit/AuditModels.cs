using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    internal enum AuditMismatchStatus
    {
        PredictedNotReported,
        ReportedNotPredicted,
        AttributionMismatch
    }

    internal sealed record AuditInputSummary(
        string ErrorLogPath,
        DateTime ErrorLogLastWriteUtc,
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
        string? DwarfFortressPath);

    internal sealed record ReferenceMismatch(
        AuditMismatchStatus Status,
        ModPossibleIssueKind Kind,
        string TargetKey,
        IReadOnlyList<string> PredictedMods,
        IReadOnlyList<string> ReportedMods,
        int ReportedLineCount,
        IReadOnlyList<string> ErrorLogCategories,
        IReadOnlyList<string> TargetDefinitionMods,
        IReadOnlyList<string> TargetCutMods,
        IReadOnlyList<string> RawReferenceMods,
        bool RawReferenceCapturedForPredictedMod,
        bool RawReferenceCapturedForReportedMod,
        string RawEvidence)
    {
        /// <summary>
        /// True when an active mod directly defines the target but another active mod CUTs it.
        /// The current analyzer treats the definition as satisfying the reference, which explains
        /// the common "reported but not predicted" false negative.
        /// </summary>
        public bool ExplainedByCut => TargetDefinitionMods.Count > 0 && TargetCutMods.Count > 0;
    }

    internal sealed record MatchedReference(
        string ModId,
        ModPossibleIssueKind Kind,
        string TargetKey,
        int ReportedLineCount);

    internal sealed record UnattributedReference(
        ModPossibleIssueKind Kind,
        string TargetKey,
        int ReportedLineCount,
        IReadOnlyList<string> ErrorLogCategories,
        IReadOnlyList<string> RawReferenceMods,
        string RawEvidence);

    internal sealed record UnverifiablePrediction(
        string ModId,
        ModPossibleIssueKind Kind,
        string TargetKey);

    internal sealed record UnmappedErrorLogCategory(
        string Category,
        int LineCount,
        int IssueTypeCount,
        IReadOnlyList<string> SampleMods);

    internal sealed record AuditSummary(
        int ConsideredLineCount,
        int AttributedLineCount,
        int UnattributedLineCount,
        int PredictedTotal,
        int PredictedComparable,
        int PredictedUnverifiable,
        int PredictedModCount,
        int ReportedPairCount,
        int ReportedLineCount,
        int ReportedModCount,
        int MatchedPairCount,
        int MatchedLineCount,
        int PredictedOnlyCount,
        int ReportedOnlyCount,
        int ReportedOnlyExplainedByCutCount,
        int AttributionMismatchCount,
        int UnattributedReferenceCount,
        int UnattributedReferenceLineCount,
        int UnmappedCategoryCount)
    {
        public int MismatchCount => PredictedOnlyCount
            + ReportedOnlyCount
            + AttributionMismatchCount
            + UnattributedReferenceCount
            + UnmappedCategoryCount;
    }

    internal sealed record AuditComparison(
        AuditSummary Summary,
        IReadOnlyList<ReferenceMismatch> Mismatches,
        IReadOnlyList<MatchedReference> Matches,
        IReadOnlyList<UnattributedReference> UnattributedReferences,
        IReadOnlyList<UnverifiablePrediction> UnverifiablePredictions,
        IReadOnlyList<UnmappedErrorLogCategory> UnmappedErrorLogCategories);

    internal sealed record AuditReport(
        AuditInputSummary Inputs,
        AuditSummary Summary,
        IReadOnlyList<ReferenceMismatch> Mismatches,
        IReadOnlyList<MatchedReference> Matches,
        IReadOnlyList<UnattributedReference> UnattributedReferences,
        IReadOnlyList<UnverifiablePrediction> UnverifiablePredictions,
        IReadOnlyList<UnmappedErrorLogCategory> UnmappedErrorLogCategories,
        IReadOnlyList<string> Warnings);
}
