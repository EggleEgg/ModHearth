using System.Globalization;
using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Human-readable audit report. The text output is intentionally grouped by mismatch direction
    /// so a maintainer can answer "is ModPossibleIssueAnalyzer too loud or too quiet?" at a glance.
    /// </summary>
    internal static class AuditTextWriter
    {
        public static void Write(AuditReport report, AuditOptions options, TextWriter writer)
        {
            writer.WriteLine("ModHearth raw issue audit");
            writer.WriteLine("=========================");
            writer.WriteLine();

            WriteInputs(writer, report.Inputs);
            WriteSummary(writer, report.Summary);

            WriteMismatchSection(
                writer,
                "Predicted but not reported (possible false positives)",
                report.Mismatches,
                AuditMismatchStatus.PredictedNotReported,
                options.MaxDetails);

            WriteMismatchSection(
                writer,
                "Reported but not predicted (possible false negatives)",
                report.Mismatches,
                AuditMismatchStatus.ReportedNotPredicted,
                options.MaxDetails);

            WriteMismatchSection(
                writer,
                "Attribution mismatches",
                report.Mismatches,
                AuditMismatchStatus.AttributionMismatch,
                options.MaxDetails);

            WriteUnattributedSection(writer, report.UnattributedReferences, options.MaxDetails);
            WriteUnmappedCategorySection(writer, report.UnmappedErrorLogCategories, options.MaxDetails);
            WriteUnverifiableSection(writer, report.UnverifiablePredictions, options.MaxDetails);

            if (options.ShowMatches)
                WriteMatchesSection(writer, report.Matches, options.MaxDetails);

            WriteWarnings(writer, report.Warnings);

            writer.WriteLine();
            writer.WriteLine("Notes");
            if (options.AutomateWorldgen)
            {
                writer.WriteLine("  This run regenerated errorlog.txt through worldgen automation. Confirm the log's");
                writer.WriteLine("  timestamp and the active mod set still match the cache before trusting a mismatch.");
            }
            else
            {
                writer.WriteLine("  errorlog.txt can contain lines from several DF launches. For a clean audit, delete it,");
                writer.WriteLine("  launch Dwarf Fortress once with the active modlist, then run this tool again.");
            }

            writer.WriteLine("  'scanner gap' means the raw scan never captured the reference; 'analyzer miss' means");
            writer.WriteLine("  the scan captured it but ModPossibleIssueAnalyzer did not predict it.");
        }

        private static void WriteInputs(TextWriter writer, AuditInputSummary inputs)
        {
            writer.WriteLine("Inputs");
            WriteInput(
                writer,
                "Error log",
                inputs.ErrorLogPath,
                $"last written {FormatUtc(inputs.ErrorLogLastWriteUtc)}");
            WriteInput(
                writer,
                "Raw cache",
                inputs.CachePath,
                $"schema {inputs.CacheSchemaVersion}; {FormatNumber(inputs.CacheEntryCount)} entr" +
                $"{(inputs.CacheEntryCount == 1 ? "y" : "ies")}; {FormatNumber(inputs.UniqueModCount)} unique mod(s); " +
                $"last written {FormatUtc(inputs.CacheLastWriteUtc)}");
            WriteInput(
                writer,
                "Vanilla",
                inputs.VanillaPath,
                $"{FormatNumber(inputs.VanillaKeyCount)} object definition(s)");
            WriteInput(
                writer,
                "Mod list",
                inputs.ModListPath ?? "(not provided)",
                $"{inputs.ActiveModSource}; {FormatNumber(inputs.ActiveModCount)} active cached row(s)" +
                (string.IsNullOrWhiteSpace(inputs.ModListName) ? string.Empty : $"; pack '{inputs.ModListName}'"));
            WriteInput(writer, "Config", inputs.ConfigPath ?? "(auto-detected or not provided)", null);
            WriteInput(writer, "DF folder", inputs.DwarfFortressPath ?? "(not resolved)", null);
            writer.WriteLine();
        }

        private static void WriteInput(TextWriter writer, string label, string value, string? detail)
        {
            writer.WriteLine($"  {label,-10}: {value}");
            if (!string.IsNullOrWhiteSpace(detail))
                writer.WriteLine($"  {"",-10}  {detail}");
        }

        private static void WriteSummary(TextWriter writer, AuditSummary summary)
        {
            writer.WriteLine("Summary");
            writer.WriteLine(
                $"  Predictions : {FormatNumber(summary.PredictedTotal)} token pair(s) " +
                $"({FormatNumber(summary.PredictedComparable)} verifiable from errorlog, " +
                $"{FormatNumber(summary.PredictedUnverifiable)} not verifiable) across " +
                $"{FormatNumber(summary.PredictedModCount)} mod(s)");
            writer.WriteLine(
                $"  Reported    : {FormatNumber(summary.ReportedPairCount)} token pair(s), " +
                $"{FormatNumber(summary.ReportedLineCount)} line(s) across " +
                $"{FormatNumber(summary.ReportedModCount)} mod(s)");
            writer.WriteLine(
                $"  Matched     : {FormatNumber(summary.MatchedPairCount)} token pair(s), " +
                $"{FormatNumber(summary.MatchedLineCount)} line(s)");
            writer.WriteLine(
                $"  Errorlog    : {FormatNumber(summary.ConsideredLineCount)} considered, " +
                $"{FormatNumber(summary.AttributedLineCount)} attributed, " +
                $"{FormatNumber(summary.UnattributedLineCount)} unattributed line(s)");
            writer.WriteLine(
                $"  Mismatches  : {FormatNumber(summary.MismatchCount)}");
            writer.WriteLine(
                $"    predicted only               : {FormatNumber(summary.PredictedOnlyCount)}");
            writer.WriteLine(
                $"    reported only                : {FormatNumber(summary.ReportedOnlyCount)}" +
                (summary.ReportedOnlyExplainedByCutCount > 0
                    ? $" ({FormatNumber(summary.ReportedOnlyExplainedByCutCount)} explained by CUT)"
                    : string.Empty));
            writer.WriteLine(
                $"    attribution mismatch         : {FormatNumber(summary.AttributionMismatchCount)}");
            writer.WriteLine(
                $"    unattributed reference lines : {FormatNumber(summary.UnattributedReferenceCount)} " +
                $"({FormatNumber(summary.UnattributedReferenceLineCount)} line(s))");
            writer.WriteLine(
                $"    unmapped log categories      : {FormatNumber(summary.UnmappedCategoryCount)}");
            writer.WriteLine(
                summary.MismatchCount == 0
                    ? "  Result      : OK - the analyzer and errorlog agree for every mapped reference category."
                    : "  Result      : mismatches found - see the sections below.");

            writer.WriteLine();
        }

        private static void WriteMismatchSection(
            TextWriter writer,
            string title,
            IReadOnlyList<ReferenceMismatch> mismatches,
            AuditMismatchStatus status,
            int maxDetails)
        {
            List<ReferenceMismatch> filtered = [.. mismatches.Where(mismatch => mismatch.Status == status)];
            WriteSectionTitle(writer, title, filtered.Count);

            for (int i = 0; i < filtered.Count && i < maxDetails; i++)
            {
                ReferenceMismatch mismatch = filtered[i];
                writer.WriteLine(
                    $"  {i + 1}. {ErrorLogReferenceMapping.DescribeIssueKind(mismatch.Kind)}: " +
                    $"{ObjectKey.FormatForDisplay(mismatch.TargetKey)}");
                writer.WriteLine($"     predicted : {FormatMods(mismatch.PredictedMods)}");
                writer.WriteLine(
                    $"     reported  : {FormatMods(mismatch.ReportedMods)}" +
                    (mismatch.ReportedLineCount > 0
                        ? $" ({FormatNumber(mismatch.ReportedLineCount)} log line(s))"
                        : string.Empty));

                if (mismatch.ErrorLogCategories.Count > 0)
                    writer.WriteLine($"     log       : {string.Join(", ", mismatch.ErrorLogCategories)}");

                if (mismatch.TargetDefinitionMods.Count > 0)
                    writer.WriteLine($"     defined   : {FormatMods(mismatch.TargetDefinitionMods)}");

                if (mismatch.TargetCutMods.Count > 0)
                    writer.WriteLine($"     cut       : {FormatMods(mismatch.TargetCutMods)}");

                writer.WriteLine($"     evidence  : {mismatch.RawEvidence}");
            }

            WriteTruncationNote(writer, filtered.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteUnattributedSection(
            TextWriter writer,
            IReadOnlyList<UnattributedReference> references,
            int maxDetails)
        {
            WriteSectionTitle(writer, "Reported reference errors without mod attribution", references.Count);

            for (int i = 0; i < references.Count && i < maxDetails; i++)
            {
                UnattributedReference reference = references[i];
                writer.WriteLine(
                    $"  {i + 1}. {ErrorLogReferenceMapping.DescribeIssueKind(reference.Kind)}: " +
                    $"{ObjectKey.FormatForDisplay(reference.TargetKey)} " +
                    $"({FormatNumber(reference.ReportedLineCount)} log line(s))");
                writer.WriteLine($"     log       : {string.Join(", ", reference.ErrorLogCategories)}");
                writer.WriteLine($"     evidence  : {reference.RawEvidence}");
            }

            WriteTruncationNote(writer, references.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteUnmappedCategorySection(
            TextWriter writer,
            IReadOnlyList<UnmappedErrorLogCategory> categories,
            int maxDetails)
        {
            WriteSectionTitle(
                writer,
                "Reference-like errorlog categories not mapped by the audit",
                categories.Count);

            for (int i = 0; i < categories.Count && i < maxDetails; i++)
            {
                UnmappedErrorLogCategory category = categories[i];
                writer.WriteLine(
                    $"  {i + 1}. \"{category.Category}\" - {FormatNumber(category.LineCount)} line(s), " +
                    $"{FormatNumber(category.IssueTypeCount)} issue type(s)");
                writer.WriteLine($"     mods      : {FormatMods(category.SampleMods)}");
                writer.WriteLine(
                    "     action    : check whether this is a raw-reference diagnostic and add it to " +
                    "ErrorLogReferenceMapping.");
            }

            WriteTruncationNote(writer, categories.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteUnverifiableSection(
            TextWriter writer,
            IReadOnlyList<UnverifiablePrediction> predictions,
            int maxDetails)
        {
            WriteSectionTitle(
                writer,
                "Predictions not verifiable from errorlog",
                predictions.Count);

            for (int i = 0; i < predictions.Count && i < maxDetails; i++)
            {
                UnverifiablePrediction prediction = predictions[i];
                writer.WriteLine(
                    $"  {i + 1}. {prediction.ModId}: " +
                    $"{ErrorLogReferenceMapping.DescribeIssueKind(prediction.Kind)}: " +
                    $"{ObjectKey.FormatForDisplay(prediction.TargetKey)}");
            }

            WriteTruncationNote(writer, predictions.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteMatchesSection(
            TextWriter writer,
            IReadOnlyList<MatchedReference> matches,
            int maxDetails)
        {
            WriteSectionTitle(writer, "Matched predictions", matches.Count);

            for (int i = 0; i < matches.Count && i < maxDetails; i++)
            {
                MatchedReference match = matches[i];
                writer.WriteLine(
                    $"  {i + 1}. {match.ModId}: " +
                    $"{ErrorLogReferenceMapping.DescribeIssueKind(match.Kind)}: " +
                    $"{ObjectKey.FormatForDisplay(match.TargetKey)} " +
                    $"({FormatNumber(match.ReportedLineCount)} log line(s))");
            }

            WriteTruncationNote(writer, matches.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteWarnings(TextWriter writer, IReadOnlyList<string> warnings)
        {
            WriteSectionTitle(writer, "Warnings", warnings.Count);
            for (int i = 0; i < warnings.Count; i++)
                writer.WriteLine($"  {i + 1}. {warnings[i]}");

            writer.WriteLine();
        }

        private static void WriteSectionTitle(TextWriter writer, string title, int count)
        {
            writer.WriteLine(title);
            writer.WriteLine(new string('-', title.Length));
            if (count == 0)
                writer.WriteLine("  (none)");
        }

        private static void WriteTruncationNote(TextWriter writer, int total, int maxDetails)
        {
            if (total > maxDetails)
            {
                writer.WriteLine(
                    $"  ...and {FormatNumber(total - maxDetails)} more (raise --max-details or use --json).");
            }
        }

        private static string FormatMods(IReadOnlyList<string> mods)
            => mods.Count == 0 ? "(none)" : string.Join(", ", mods);

        private static string FormatNumber(int value)
            => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string FormatUtc(DateTime value)
            => value == DateTime.MinValue
                ? "(unknown)"
                : value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture);
    }
}
