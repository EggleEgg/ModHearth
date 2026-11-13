using ModHearth.Models;
using ModHearth.RawIssueAudit;
using ModHearth.Utils.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Pins the comparison rules behind the development-only raw issue audit. The audit exists to
    /// catch drift between ModPossibleIssueAnalyzer and the errors Dwarf Fortress actually writes,
    /// so these tests focus on the failure modes that must stay visible: false positives, false
    /// negatives, attribution gaps, and the SELECT/CUT predictions that errorlog cannot verify.
    /// </summary>
    public class RawIssueAuditComparerTests
    {
        [Fact]
        public void MatchedPredictionProducesNoMismatch()
        {
            ModRawDependencyInfo entity = Info(
                "entity_mod",
                definitions: ["ENTITY:TEST_ENTITY"],
                permittedReactions: ["GOOD_REACTION"]);

            AuditComparison comparison = Compare(
                [Prediction("entity_mod", ModPossibleIssueKind.MissingPermittedReaction, "REACTION", "GOOD_REACTION")],
                ErrorLog([Issue("entity_mod", "Unrecognized permitted reaction token", "GOOD_REACTION")], considered: 1, attributed: 1),
                Index(entity));

            Assert.Empty(comparison.Mismatches);
            Assert.Equal(0, comparison.Summary.MismatchCount);
            Assert.Equal(1, comparison.Summary.MatchedPairCount);
            Assert.Equal(1, comparison.Summary.MatchedLineCount);
            MatchedReference match = Assert.Single(comparison.Matches);
            Assert.Equal("entity_mod", match.ModId);
            Assert.Equal("REACTION:GOOD_REACTION", match.TargetKey);
        }

        [Fact]
        public void PredictedButNotReportedIsReportedAsPossibleFalsePositive()
        {
            ModRawDependencyInfo entity = Info(
                "entity_mod",
                definitions: ["ENTITY:TEST_ENTITY"],
                permittedReactions: ["NEVER_REPORTED"]);

            AuditComparison comparison = Compare(
                [Prediction("entity_mod", ModPossibleIssueKind.MissingPermittedReaction, "REACTION", "NEVER_REPORTED")],
                ErrorLog(),
                Index(entity));

            ReferenceMismatch mismatch = Assert.Single(comparison.Mismatches);
            Assert.Equal(AuditMismatchStatus.PredictedNotReported, mismatch.Status);
            Assert.Equal("REACTION:NEVER_REPORTED", mismatch.TargetKey);
            Assert.Equal(["entity_mod"], mismatch.PredictedMods);
            Assert.Empty(mismatch.ReportedMods);
            Assert.True(mismatch.RawReferenceCapturedForPredictedMod);
            Assert.Contains("possible false positive", mismatch.RawEvidence, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, comparison.Summary.PredictedOnlyCount);
        }

        [Fact]
        public void ReportedButNotPredictedIsReportedAsFalseNegative()
        {
            ModRawDependencyInfo entity = Info(
                "entity_mod",
                definitions: ["ENTITY:TEST_ENTITY"],
                permittedReactions: ["MISSED_REACTION"]);

            AuditComparison comparison = Compare(
                [],
                ErrorLog([Issue("entity_mod", "Unrecognized permitted reaction token", "MISSED_REACTION", count: 3)], considered: 3, attributed: 3),
                Index(entity));

            ReferenceMismatch mismatch = Assert.Single(comparison.Mismatches);
            Assert.Equal(AuditMismatchStatus.ReportedNotPredicted, mismatch.Status);
            Assert.Equal(3, mismatch.ReportedLineCount);
            Assert.True(mismatch.RawReferenceCapturedForReportedMod);
            Assert.Contains("analyzer missed it", mismatch.RawEvidence, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, comparison.Summary.ReportedOnlyCount);
            Assert.Equal(3, comparison.Summary.ReportedLineCount);
        }

        [Fact]
        public void DifferentModsForTheSameTokenAreAnAttributionMismatch()
        {
            ModRawDependencyInfo predicted = Info(
                "predicted_mod",
                permittedReactions: ["SHARED_REACTION"]);
            ModRawDependencyInfo reported = Info(
                "reported_mod",
                permittedReactions: ["SHARED_REACTION"]);

            AuditComparison comparison = Compare(
                [Prediction("predicted_mod", ModPossibleIssueKind.MissingPermittedReaction, "REACTION", "SHARED_REACTION")],
                ErrorLog([Issue("reported_mod", "Unrecognized permitted reaction token", "SHARED_REACTION")], considered: 1, attributed: 1),
                Index(predicted, reported));

            ReferenceMismatch mismatch = Assert.Single(comparison.Mismatches);
            Assert.Equal(AuditMismatchStatus.AttributionMismatch, mismatch.Status);
            Assert.Equal(["predicted_mod"], mismatch.PredictedMods);
            Assert.Equal(["reported_mod"], mismatch.ReportedMods);
            Assert.Contains("attribution mismatch", mismatch.RawEvidence, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, comparison.Summary.AttributionMismatchCount);
        }

        [Fact]
        public void SelectCutAndCopyPredictionsAreUnverifiableInsteadOfMismatches()
        {
            ModRawDependencyInfo mod = Info(
                "patcher",
                selects: ["CREATURE:MISSING_SELECT"],
                cuts: ["CREATURE:MISSING_CUT"],
                copyTagsFrom: ["CREATURE:MISSING_SOURCE"]);

            AuditComparison comparison = Compare(
                [
                    Prediction("patcher", ModPossibleIssueKind.MissingSelectTarget, "CREATURE", "MISSING_SELECT"),
                    Prediction("patcher", ModPossibleIssueKind.MissingCutTarget, "CREATURE", "MISSING_CUT"),
                    Prediction("patcher", ModPossibleIssueKind.MissingCopyTagsFromSource, "CREATURE", "MISSING_SOURCE")
                ],
                ErrorLog(),
                Index(mod));

            Assert.Empty(comparison.Mismatches);
            Assert.Equal(0, comparison.Summary.MismatchCount);
            Assert.Equal(3, comparison.Summary.PredictedTotal);
            Assert.Equal(0, comparison.Summary.PredictedComparable);
            Assert.Equal(3, comparison.Summary.PredictedUnverifiable);
            Assert.Equal(3, comparison.UnverifiablePredictions.Count);
        }

        [Fact]
        public void ReportedOnlyReferenceExplainedByCutCarriesDefinitionEvidence()
        {
            ModRawDependencyInfo vanillaReactions = Info(
                "vanilla_reactions",
                definitions: ["REACTION:ACACIA_BARK_DYE"]);
            ModRawDependencyInfo cutter = Info(
                "advanced_agriculture",
                cuts: ["REACTION:ACACIA_BARK_DYE"]);
            ModRawDependencyInfo entity = Info(
                "entity_mod",
                permittedReactions: ["ACACIA_BARK_DYE"]);

            AuditComparison comparison = Compare(
                [],
                ErrorLog([Issue("entity_mod", "Unrecognized permitted reaction token", "ACACIA_BARK_DYE")], considered: 1, attributed: 1),
                Index(vanillaReactions, cutter, entity));

            ReferenceMismatch mismatch = Assert.Single(comparison.Mismatches);
            Assert.Equal(AuditMismatchStatus.ReportedNotPredicted, mismatch.Status);
            Assert.True(mismatch.ExplainedByCut);
            Assert.Equal(["vanilla_reactions"], mismatch.TargetDefinitionMods);
            Assert.Equal(["advanced_agriculture"], mismatch.TargetCutMods);
            Assert.Contains("CUT by advanced_agriculture", mismatch.RawEvidence, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, comparison.Summary.ReportedOnlyExplainedByCutCount);
        }

        [Fact]
        public void UnattributedReferenceIsReportedSeparatelyFromModIssues()
        {
            ModRawDependencyInfo entity = Info(
                "entity_mod",
                permittedReactions: ["UNATTRIBUTED_REACTION"]);

            AuditComparison comparison = Compare(
                [],
                ErrorLog(
                    [],
                    [Issue(string.Empty, "Unrecognized permitted reaction token", "UNATTRIBUTED_REACTION", count: 2)],
                    considered: 2,
                    attributed: 0,
                    unattributedLines: 2),
                Index(entity));

            Assert.Empty(comparison.Mismatches);
            UnattributedReference reference = Assert.Single(comparison.UnattributedReferences);
            Assert.Equal("REACTION:UNATTRIBUTED_REACTION", reference.TargetKey);
            Assert.Equal(2, reference.ReportedLineCount);
            Assert.Equal(["entity_mod"], reference.RawReferenceMods);
            Assert.Contains("could not attribute", reference.RawEvidence, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, comparison.Summary.UnattributedReferenceCount);
            Assert.Equal(2, comparison.Summary.UnattributedReferenceLineCount);
            Assert.Equal(1, comparison.Summary.MismatchCount);
        }

        [Fact]
        public void BuiltInWorkshopIdExplainsAReportedReactionBuilding()
        {
            ModRawDependencyInfo reactions = Info(
                "reaction_mod",
                reactionBuildings: ["CARPENTER"]);

            AuditComparison comparison = Compare(
                [],
                ErrorLog([Issue("reaction_mod", "Unrecognized building token", "CARPENTER")], considered: 1, attributed: 1),
                Index(reactions));

            ReferenceMismatch mismatch = Assert.Single(comparison.Mismatches);
            Assert.Equal(AuditMismatchStatus.ReportedNotPredicted, mismatch.Status);
            Assert.Equal(["reaction_mod"], mismatch.RawReferenceMods);
            Assert.True(mismatch.RawReferenceCapturedForReportedMod);
            Assert.Contains("built-in workshop/furnace", mismatch.RawEvidence, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VanillaDefinitionExplainsWhyTheAnalyzerStayedSilent()
        {
            ModRawDependencyInfo reactions = Info(
                "reaction_mod",
                reactionBuildings: ["VANILLA_WORKSHOP"]);
            VanillaRawBaseline baseline = new([new ObjectKey("BUILDING", "VANILLA_WORKSHOP")]);

            AuditComparison comparison = Compare(
                [],
                ErrorLog([Issue("reaction_mod", "Unrecognized building token", "VANILLA_WORKSHOP")], considered: 1, attributed: 1),
                Index(reactions),
                baseline);

            ReferenceMismatch mismatch = Assert.Single(comparison.Mismatches);
            Assert.Contains("defined by the vanilla raws", mismatch.RawEvidence, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ReferenceLikeCategoryWithoutMappingIsSurfacedForMaintenance()
        {
            ModErrorLogIssue issue = Issue(
                "entity_mod",
                "Unrecognized permitted material token",
                "MISSING_MATERIAL");

            AuditComparison comparison = Compare(
                [],
                ErrorLog([issue], considered: 1, attributed: 1),
                Index(Info("entity_mod")));

            Assert.Empty(comparison.Mismatches);
            UnmappedErrorLogCategory category = Assert.Single(comparison.UnmappedErrorLogCategories);
            Assert.Equal("Unrecognized permitted material token", category.Category);
            Assert.Equal(1, category.LineCount);
            Assert.Equal(["entity_mod"], category.SampleMods);
            Assert.Equal(1, comparison.Summary.UnmappedCategoryCount);
            Assert.Equal(1, comparison.Summary.MismatchCount);
        }

        [Fact]
        public void UnrecognizedReactionTokenIsNotTreatedAsAReferenceCategory()
        {
            // "Unrecognized reaction token" names a token inside a reaction definition; it is a
            // different diagnostic from "Unrecognized permitted reaction token" and must not be
            // flagged as an unmapped missing-object reference.
            ModErrorLogIssue issue = Issue(
                "reaction_mod",
                "Unrecognized reaction token",
                "TOOL_USE");

            AuditComparison comparison = Compare(
                [],
                ErrorLog([issue], considered: 1, attributed: 1),
                Index(Info("reaction_mod")));

            Assert.Empty(comparison.UnmappedErrorLogCategories);
            Assert.Equal(0, comparison.Summary.MismatchCount);
        }

        [Fact]
        public void ErrorLogCategorySuffixStillMapsToTheReferenceKind()
        {
            AuditComparison comparison = Compare(
                [Prediction("entity_mod", ModPossibleIssueKind.MissingPermittedReaction, "REACTION", "SUFFIXED")],
                ErrorLog(
                    [Issue("entity_mod", "Unrecognized permitted reaction token in entity test_entity", "SUFFIXED")],
                    considered: 1,
                    attributed: 1),
                Index(Info("entity_mod", permittedReactions: ["SUFFIXED"])));

            Assert.Empty(comparison.Mismatches);
            Assert.Equal(1, comparison.Summary.MatchedPairCount);
        }

        [Fact]
        public void PredictionWithoutCompositeKeyIsComparedVerbatimWithAWarning()
        {
            List<string> warnings = [];

            AuditComparison comparison = Compare(
                [new ModPossibleIssue("mod_a", "NOT_A_COMPOSITE_KEY", ModPossibleIssueKind.MissingPermittedReaction)],
                ErrorLog([Issue("mod_a", "Unrecognized permitted reaction token", "NOT_A_COMPOSITE_KEY")], considered: 1, attributed: 1),
                Index(Info("mod_a")),
                warnings: warnings);

            // A malformed prediction cannot match a log line, because the log target is always a
            // composite key. Keep both sides visible and point at the bad prediction instead of
            // silently dropping it.
            Assert.Equal(1, comparison.Summary.PredictedOnlyCount);
            Assert.Equal(1, comparison.Summary.ReportedOnlyCount);
            Assert.Contains(warnings, warning => warning.Contains("not an ObjectType:Id key", StringComparison.OrdinalIgnoreCase));
        }

        private static AuditComparison Compare(
            IReadOnlyCollection<ModPossibleIssue> predictions,
            ModErrorLogAnalysis errorLog,
            RawReferenceIndex rawReferences,
            VanillaRawBaseline? vanillaBaseline = null,
            List<string>? warnings = null)
            => ReferenceAuditComparer.Compare(
                predictions,
                errorLog,
                rawReferences,
                vanillaBaseline ?? VanillaRawBaseline.Empty,
                warnings ?? []);

        private static ModErrorLogAnalysis ErrorLog(
            IReadOnlyList<ModErrorLogIssue>? issues = null,
            IReadOnlyList<ModErrorLogIssue>? unattributedIssues = null,
            int considered = 0,
            int attributed = 0,
            int unattributedLines = 0)
            => new(
                issues ?? [],
                unattributedIssues ?? [],
                considered,
                attributed,
                unattributedLines,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        private static ModErrorLogIssue Issue(string modId, string category, string detail, int count = 1)
            => new(modId, category, detail, count, [], []);

        private static ModPossibleIssue Prediction(
            string modId,
            ModPossibleIssueKind kind,
            string objectType,
            string id)
            => new(modId, $"{objectType}:{id}", kind);

        private static RawReferenceIndex Index(params ModRawDependencyInfo[] infos)
            => RawReferenceIndex.Build(
                infos,
                new HashSet<string>(infos.Select(info => info.ModId), StringComparer.OrdinalIgnoreCase));

        private static ModRawDependencyInfo Info(
            string modId,
            string[]? definitions = null,
            string[]? selects = null,
            string[]? cuts = null,
            string[]? copyTagsFrom = null,
            string[]? permittedReactions = null,
            string[]? permittedBuildings = null,
            string[]? reactionBuildings = null) => new()
            {
                ModId = modId,
                DirectDefinitionIds = [.. definitions ?? []],
                SelectTargetIds = [.. selects ?? []],
                CutTargetIds = [.. cuts ?? []],
                CopyTagsFromSourceIds = [.. copyTagsFrom ?? []],
                PermittedReactionIds = [.. permittedReactions ?? []],
                PermittedBuildingIds = [.. permittedBuildings ?? []],
                ReactionBuildingIds = [.. reactionBuildings ?? []]
            };
    }
}
