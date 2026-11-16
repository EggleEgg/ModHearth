using System.Globalization;
using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Human-readable hardcoded token report. The catalog inventory answers "what does DF resolve
    /// itself?", the usage section answers "which active mods rely on it?", and the unresolved and
    /// unclassified sections identify material forms that need catalog maintenance.
    /// </summary>
    internal static class HardcodedReportTextWriter
    {
        private const int MaxUsageMods = 8;

        public static void Write(HardcodedReport report, AuditOptions options, TextWriter writer)
        {
            writer.WriteLine("ModHearth hardcoded token report");
            writer.WriteLine("================================");
            writer.WriteLine();

            WriteInputs(writer, report.Inputs);
            WriteSummary(writer, report);
            WriteCatalog(writer, report.Catalog, options.MaxDetails);
            WriteUsage(writer, report.Usage, options.MaxDetails);
            WriteInvalidContexts(writer, report.InvalidContexts, options.MaxDetails);
            WriteUnresolvedMaterials(writer, report.UnresolvedMaterials, options.MaxDetails);
            WriteUnclassifiedMaterials(writer, report.UnclassifiedMaterials, options.MaxDetails);
            WriteVanillaChecks(writer, report.VanillaChecks, options.MaxDetails);
            WriteVerification(writer, report.Verification, options.MaxDetails);
            WriteWarnings(writer, report.Warnings);

            writer.WriteLine();
            writer.WriteLine("Notes");
            writer.WriteLine("  The report is read-only: it does not write config.json, the raw cache, or DF data.");
            writer.WriteLine("  Catalog entries record where DF resolves a token, not where a raw definition lives;");
            writer.WriteLine("  the dye reactions are discovered from reaction_dyes.txt and entity_default.txt, and");
            writer.WriteLine("  their cut immunity is confirmed against the executable, not defined by the catalog.");
            writer.WriteLine("  Executable verification covers reaction ids and material tokens; reaction building");
            writer.WriteLine("  ids are checked against the buildings vanilla reactions actually use.");
            writer.WriteLine("  Run with --hardcoded-report --verify-hardcoded to check the catalog against the");
            writer.WriteLine("  installed Dwarf Fortress executable after a game update.");
        }

        private static void WriteInputs(TextWriter writer, HardcodedReportInputs inputs)
        {
            writer.WriteLine("Inputs");
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
            if (!string.IsNullOrWhiteSpace(inputs.DwarfFortressExecutablePath))
            {
                WriteInput(writer, "Executable", inputs.DwarfFortressExecutablePath, null);
            }

            WriteInput(
                writer,
                "Material scan",
                $"{FormatNumber(inputs.ScannedModCount)} mod(s), {FormatNumber(inputs.ScannedFileCount)} raw file(s)",
                null);
            writer.WriteLine();
        }

        private static void WriteInput(TextWriter writer, string label, string value, string? detail)
        {
            writer.WriteLine($"  {label,-11}: {value}");
            if (!string.IsNullOrWhiteSpace(detail))
                writer.WriteLine($"  {"",-11}  {detail}");
        }

        private static void WriteSummary(TextWriter writer, HardcodedReport report)
        {
            int catalogTokens = report.Catalog.Sum(group => group.Tokens.Count);

            writer.WriteLine("Summary");
            writer.WriteLine(
                $"  Catalog       : {FormatNumber(report.Catalog.Count)} group(s), " +
                $"{FormatNumber(catalogTokens)} token(s)");
            writer.WriteLine(
                $"  Usage         : {FormatNumber(report.Usage.Count)} token(s) used by the active mods");
            writer.WriteLine(
                $"  Unresolved    : {FormatNumber(report.UnresolvedMaterials.Count)} aggregated material reference(s)");
            writer.WriteLine(
                $"  Invalid       : {FormatNumber(report.InvalidContexts.Count)} PERMITTED_BUILDING context(s)");
            writer.WriteLine(
                $"  Vanilla check : {FormatNumber(report.VanillaChecks.Count)} catalog/vanilla disagreement(s)");
            writer.WriteLine(
                $"  Unclassified  : {FormatNumber(report.UnclassifiedMaterials.Count)} material token sample(s)");
            writer.WriteLine(
                $"  Verification  : {FormatVerification(report.Verification)}");
            writer.WriteLine();
        }

        private static void WriteCatalog(
            TextWriter writer,
            IReadOnlyList<HardcodedCatalogGroupSummary> catalog,
            int maxDetails)
        {
            WriteSectionTitle(writer, "Catalog inventory", catalog.Count);

            foreach (HardcodedCatalogGroupSummary group in catalog)
            {
                writer.WriteLine(
                    $"  {group.Id} - {FormatNumber(group.Tokens.Count)} token(s), " +
                    $"{group.Domain}/{group.Context}/{group.Behavior}, verified by {group.Verification}");
                writer.WriteLine($"    {group.Description}");
                writer.WriteLine($"    source   : {group.Source}");
                writer.WriteLine($"    verified : {group.VerifiedAgainst}");

                for (int i = 0; i < group.Tokens.Count && i < maxDetails; i++)
                {
                    writer.WriteLine(
                        i == 0
                            ? $"    tokens   : {group.Tokens[i]}"
                            : $"               {group.Tokens[i]}");
                }

                if (group.Tokens.Count > maxDetails)
                {
                    writer.WriteLine(
                        $"               ...and {FormatNumber(group.Tokens.Count - maxDetails)} more " +
                        "(raise --max-details or use --json).");
                }

                writer.WriteLine();
            }
        }

        private static void WriteUsage(
            TextWriter writer,
            IReadOnlyList<HardcodedTokenUsage> usage,
            int maxDetails)
        {
            WriteSectionTitle(writer, "Active-mod usage", usage.Count);
            if (usage.Count == 0)
            {
                writer.WriteLine();
                return;
            }

            foreach (IGrouping<HardcodedTokenGroupId, HardcodedTokenUsage> group in usage
                .GroupBy(entry => entry.GroupId)
                .OrderBy(group => group.Key))
            {
                writer.WriteLine($"  {group.Key}");

                int shown = 0;
                foreach (HardcodedTokenUsage entry in group
                    .OrderBy(entry => entry.Token, StringComparer.OrdinalIgnoreCase))
                {
                    if (shown >= maxDetails)
                        break;

                    writer.WriteLine(
                        $"    {entry.Token}: {FormatNumber(entry.OccurrenceCount)} use(s) across " +
                        $"{FormatNumber(entry.Mods.Count)} mod(s)");
                    writer.WriteLine($"      mods: {FormatMods(entry.Mods)}");
                    shown++;
                }

                int total = group.Count();
                if (total > shown)
                {
                    writer.WriteLine(
                        $"    ...and {FormatNumber(total - shown)} more token(s) " +
                        "(raise --max-details or use --json).");
                }

                if (group.Key == HardcodedTokenGroupId.CutImmuneDyeReactions)
                {
                    writer.WriteLine(
                        "    note: these CUT_REACTION attempts are silently ignored by DF; " +
                        "mod-added dye reactions are cut normally.");
                }

                writer.WriteLine();
            }
        }

        private static void WriteInvalidContexts(
            TextWriter writer,
            IReadOnlyList<HardcodedInvalidContext> contexts,
            int maxDetails)
        {
            WriteSectionTitle(writer, "Invalid PERMITTED_BUILDING contexts", contexts.Count);

            for (int i = 0; i < contexts.Count && i < maxDetails; i++)
            {
                HardcodedInvalidContext context = contexts[i];
                writer.WriteLine($"  {i + 1}. {context.ModId}: {context.Token}");
                writer.WriteLine($"     {context.Detail}");
            }

            WriteTruncationNote(writer, contexts.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteUnresolvedMaterials(
            TextWriter writer,
            IReadOnlyList<HardcodedMaterialReference> references,
            int maxDetails)
        {
            WriteSectionTitle(writer, "Unresolved material references", references.Count);

            for (int i = 0; i < references.Count && i < maxDetails; i++)
            {
                HardcodedMaterialReference reference = references[i];
                writer.WriteLine(
                    $"  {i + 1}. {reference.ModId}: {reference.Token} " +
                    $"({FormatStatus(reference.Status)}, {FormatNumber(reference.OccurrenceCount)} occurrence(s))");
                writer.WriteLine(
                    $"     object: {reference.ObjectId ?? "(none)"} [{reference.Head}]");

                foreach (string location in reference.SampleLocations)
                    writer.WriteLine($"     at    : {location}");
            }

            WriteTruncationNote(writer, references.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteUnclassifiedMaterials(
            TextWriter writer,
            IReadOnlyList<HardcodedMaterialSample> samples,
            int maxDetails)
        {
            WriteSectionTitle(writer, "Unclassified material token samples", samples.Count);

            for (int i = 0; i < samples.Count && i < maxDetails; i++)
            {
                HardcodedMaterialSample sample = samples[i];
                writer.WriteLine(
                    $"  {i + 1}. {sample.Head}: {FormatNumber(sample.OccurrenceCount)} occurrence(s) across " +
                    $"{FormatNumber(sample.Mods.Count)} mod(s)");
                writer.WriteLine($"     mods: {FormatMods(sample.Mods)}");

                foreach (string example in sample.Examples)
                    writer.WriteLine($"     example: {example}");
            }

            WriteTruncationNote(writer, samples.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteVanillaChecks(
            TextWriter writer,
            IReadOnlyList<HardcodedCatalogCheck> checks,
            int maxDetails)
        {
            WriteSectionTitle(writer, "Catalog/vanilla consistency", checks.Count);

            for (int i = 0; i < checks.Count && i < maxDetails; i++)
            {
                HardcodedCatalogCheck check = checks[i];
                writer.WriteLine(
                    $"  {i + 1}. {check.Token} [{check.GroupId}] - {check.Status}");
                writer.WriteLine($"     {check.Detail}");
            }

            WriteTruncationNote(writer, checks.Count, maxDetails);
            writer.WriteLine();
        }

        private static void WriteVerification(
            TextWriter writer,
            HardcodedVerificationResult? verification,
            int maxDetails)
        {
            const string title = "Executable verification";
            writer.WriteLine(title);
            writer.WriteLine(new string('-', title.Length));
            if (verification == null)
            {
                writer.WriteLine("  (not requested; pass --verify-hardcoded with --hardcoded-report)");
                writer.WriteLine();
                return;
            }

            writer.WriteLine($"  executable      : {verification.ExecutablePath}");
            writer.WriteLine($"  vanilla reactions: {FormatNumber(verification.VanillaReactionCount)}");
            writer.WriteLine($"  derived dyes    : {FormatNumber(verification.DerivedCutIgnoredReactionCount)}");
            writer.WriteLine($"  found in exe    : {FormatNumber(verification.ExecutableVanillaReactionCount)}");
            writer.WriteLine(
                verification.IsClean
                    ? "  result          : clean - the derived dye set and catalog tokens match the executable."
                    : $"  result          : {FormatNumber(verification.Checks.Count)} drift item(s) - refresh the catalog.");
            writer.WriteLine(
                "  note            : reaction building ids are checked against vanilla reaction BUILDING " +
                "usage, not executable strings; DF inlines several short ids.");

            for (int i = 0; i < verification.Checks.Count && i < maxDetails; i++)
            {
                HardcodedCatalogCheck check = verification.Checks[i];
                writer.WriteLine(
                    $"  {i + 1}. {check.Token} [{check.GroupId ?? HardcodedTokenGroupId.CutImmuneDyeReactions}] " +
                    $"- {check.Status}");
                writer.WriteLine($"     {check.Detail}");
            }

            WriteTruncationNote(writer, verification.Checks.Count, maxDetails);
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

        private static string FormatVerification(HardcodedVerificationResult? verification)
            => verification == null
                ? "not requested"
                : verification.IsClean
                    ? "clean"
                    : $"{FormatNumber(verification.Checks.Count)} drift item(s)";

        private static string FormatStatus(HardcodedMaterialReferenceStatus status) => status switch
        {
            HardcodedMaterialReferenceStatus.MissingInorganic => "missing inorganic",
            HardcodedMaterialReferenceStatus.MissingPlant => "missing plant",
            HardcodedMaterialReferenceStatus.MissingCreature => "missing creature",
            _ => status.ToString()
        };

        private static string FormatMods(IReadOnlyList<string> mods)
        {
            if (mods.Count == 0)
                return "(none)";

            string joined = string.Join(", ", mods.Take(MaxUsageMods));
            return mods.Count > MaxUsageMods
                ? $"{joined} (+{mods.Count - MaxUsageMods} more)"
                : joined;
        }

        private static string FormatNumber(int value)
            => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string FormatUtc(DateTime value)
            => value == DateTime.MinValue
                ? "(unknown)"
                : value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture);
    }
}
