using System.Globalization;
using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Compares the possible issues predicted from the raw cache with the reference errors Dwarf
    /// Fortress actually wrote to errorlog.txt. The comparison is intentionally token-level:
    /// a prediction and a log line match only when the mod, reference kind (permitted reaction,
    /// permitted building, reaction building) and target object all agree.
    /// </summary>
    internal static class ReferenceAuditComparer
    {
        private const int MaxEvidenceMods = 6;

        public static AuditComparison Compare(
            IReadOnlyCollection<ModPossibleIssue> predictions,
            ModErrorLogAnalysis errorLog,
            RawReferenceIndex rawReferences,
            VanillaRawBaseline vanillaBaseline,
            ICollection<string> warnings)
        {
            Dictionary<string, PredictionEntry> predictedPairs = BuildPredictionIndex(predictions, warnings);
            Dictionary<string, ReportedEntry> reportedPairs = BuildReportedIndex(errorLog.Issues, warnings);
            Dictionary<string, UnattributedEntry> unattributed = BuildUnattributedIndex(errorLog.UnattributedIssues, warnings);

            Dictionary<string, TokenInfo> tokenInfo = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<PredictionEntry>> predictionsByToken = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ReportedEntry>> reportsByToken = new(StringComparer.OrdinalIgnoreCase);

            foreach (PredictionEntry prediction in predictedPairs.Values)
            {
                // SELECT/CUT/COPY_TAGS_FROM errors have no known errorlog wording. They are listed
                // separately as "not verifiable" and must not create predicted-only mismatches.
                if (!ErrorLogReferenceMapping.IsComparable(prediction.Kind))
                    continue;

                RememberToken(tokenInfo, prediction.Kind, prediction.TargetKey);
                AddToTokenMap(predictionsByToken, prediction.Kind, prediction.TargetKey, prediction);
            }

            foreach (ReportedEntry reported in reportedPairs.Values)
            {
                RememberToken(tokenInfo, reported.Kind, reported.TargetKey);
                AddToTokenMap(reportsByToken, reported.Kind, reported.TargetKey, reported);
            }

            foreach (UnattributedEntry entry in unattributed.Values)
                RememberToken(tokenInfo, entry.Kind, entry.TargetKey);

            List<ReferenceMismatch> mismatches = [];
            List<MatchedReference> matches = [];

            foreach (TokenInfo token in tokenInfo.Values
                .OrderBy(info => info.Kind)
                .ThenBy(info => info.TargetKey, StringComparer.OrdinalIgnoreCase))
            {
                string tokenKey = TokenKey(token.Kind, token.TargetKey);
                List<PredictionEntry> tokenPredictions = predictionsByToken.GetValueOrDefault(tokenKey, []);
                List<ReportedEntry> tokenReports = reportsByToken.GetValueOrDefault(tokenKey, []);

                if (tokenPredictions.Count == 0 && tokenReports.Count == 0)
                    continue;

                List<string> predictedMods = [.. tokenPredictions
                    .Select(entry => entry.ModId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase)];
                List<string> reportedMods = [.. tokenReports
                    .Select(entry => entry.ModId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase)];

                List<string> matchedMods = [.. predictedMods.Intersect(reportedMods, StringComparer.OrdinalIgnoreCase)];
                foreach (string modId in matchedMods)
                {
                    int lineCount = tokenReports
                        .Where(entry => string.Equals(entry.ModId, modId, StringComparison.OrdinalIgnoreCase))
                        .Sum(entry => entry.LineCount);

                    matches.Add(new MatchedReference(modId, token.Kind, token.TargetKey, lineCount));
                }

                List<string> predictedOnly = [.. predictedMods.Except(reportedMods, StringComparer.OrdinalIgnoreCase)];
                List<string> reportedOnly = [.. reportedMods.Except(predictedMods, StringComparer.OrdinalIgnoreCase)];

                if (predictedOnly.Count == 0 && reportedOnly.Count == 0)
                    continue;

                AuditMismatchStatus status;
                if (predictedOnly.Count > 0 && reportedOnly.Count > 0)
                    status = AuditMismatchStatus.AttributionMismatch;
                else
                    status = predictedOnly.Count > 0 ? AuditMismatchStatus.PredictedNotReported : AuditMismatchStatus.ReportedNotPredicted;

                List<ReportedEntry> relevantReports = [.. tokenReports
                    .Where(entry => reportedOnly.Contains(entry.ModId, StringComparer.OrdinalIgnoreCase))];

                mismatches.Add(CreateMismatch(
                    status,
                    token.Kind,
                    token.TargetKey,
                    predictedOnly,
                    reportedOnly,
                    relevantReports,
                    rawReferences,
                    vanillaBaseline));
            }

            List<UnattributedReference> unattributedReferences = BuildUnattributedReferences(
                unattributed.Values,
                rawReferences,
                vanillaBaseline);

            List<UnverifiablePrediction> unverifiablePredictions = [.. predictedPairs.Values
                .Where(entry => !ErrorLogReferenceMapping.IsComparable(entry.Kind))
                .Select(entry => new UnverifiablePrediction(entry.ModId, entry.Kind, entry.TargetKey))
                .OrderBy(entry => entry.ModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Kind)
                .ThenBy(entry => entry.TargetKey, StringComparer.OrdinalIgnoreCase)];

            List<UnmappedErrorLogCategory> unmappedCategories = BuildUnmappedCategories(
                errorLog.Issues,
                errorLog.UnattributedIssues);

            mismatches.Sort(CompareMismatches);
            matches.Sort((left, right) =>
            {
                int modComparison = StringComparer.OrdinalIgnoreCase.Compare(left.ModId, right.ModId);
                if (modComparison != 0)
                    return modComparison;

                int kindComparison = left.Kind.CompareTo(right.Kind);
                return kindComparison != 0
                    ? kindComparison
                    : StringComparer.OrdinalIgnoreCase.Compare(left.TargetKey, right.TargetKey);
            });

            int predictedComparable = predictedPairs.Values.Count(entry => ErrorLogReferenceMapping.IsComparable(entry.Kind));
            int reportedLineCount = reportedPairs.Values.Sum(entry => entry.LineCount);

            AuditSummary summary = new(
                errorLog.ConsideredLineCount,
                errorLog.AttributedLineCount,
                errorLog.UnattributedLineCount,
                predictedPairs.Count,
                predictedComparable,
                predictedPairs.Count - predictedComparable,
                predictedPairs.Values
                    .Select(entry => entry.ModId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                reportedPairs.Count,
                reportedLineCount,
                reportedPairs.Values
                    .Select(entry => entry.ModId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                matches.Count,
                matches.Sum(match => match.ReportedLineCount),
                mismatches.Count(mismatch => mismatch.Status == AuditMismatchStatus.PredictedNotReported),
                mismatches.Count(mismatch => mismatch.Status == AuditMismatchStatus.ReportedNotPredicted),
                mismatches.Count(mismatch => mismatch.Status == AuditMismatchStatus.ReportedNotPredicted
                    && mismatch.ExplainedByCut),
                mismatches.Count(mismatch => mismatch.Status == AuditMismatchStatus.AttributionMismatch),
                unattributedReferences.Count,
                unattributedReferences.Sum(reference => reference.ReportedLineCount),
                unmappedCategories.Count);

            return new AuditComparison(
                summary,
                mismatches,
                matches,
                unattributedReferences,
                unverifiablePredictions,
                unmappedCategories);
        }

        private static Dictionary<string, PredictionEntry> BuildPredictionIndex(
            IEnumerable<ModPossibleIssue> predictions,
            ICollection<string> warnings)
        {
            Dictionary<string, PredictionEntry> index = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModPossibleIssue prediction in predictions)
            {
                if (prediction == null || string.IsNullOrWhiteSpace(prediction.ModId))
                    continue;

                string targetKey = NormalizeTargetKey(prediction.TargetKey, warnings);
                if (targetKey.Length == 0)
                    continue;

                string modId = prediction.ModId.Trim();
                string key = PairKey(modId, prediction.Kind, targetKey);
                _ = index.TryAdd(key, new PredictionEntry(modId, prediction.Kind, targetKey));
            }

            return index;
        }

        private static Dictionary<string, ReportedEntry> BuildReportedIndex(
            IEnumerable<ModErrorLogIssue> issues,
            ICollection<string> warnings)
        {
            Dictionary<string, ReportedEntry> index = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModErrorLogIssue issue in issues)
            {
                if (issue == null
                    || string.IsNullOrWhiteSpace(issue.ModId)
                    || !ErrorLogReferenceMapping.TryMapCategory(issue.Category, out ReferenceAuditKind referenceKind))
                {
                    continue;
                }

                string targetKey = TargetKeyFromIssue(issue, referenceKind, warnings);
                if (targetKey.Length == 0)
                    continue;

                ModPossibleIssueKind kind = ErrorLogReferenceMapping.ToIssueKind(referenceKind);
                string key = PairKey(issue.ModId, kind, targetKey);
                if (!index.TryGetValue(key, out ReportedEntry? entry))
                {
                    entry = new ReportedEntry(issue.ModId.Trim(), kind, targetKey);
                    index[key] = entry;
                }

                entry.LineCount += issue.Count;
                _ = entry.Categories.Add(issue.Category);
            }

            return index;
        }

        private static Dictionary<string, UnattributedEntry> BuildUnattributedIndex(
            IEnumerable<ModErrorLogIssue> issues,
            ICollection<string> warnings)
        {
            Dictionary<string, UnattributedEntry> index = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModErrorLogIssue issue in issues)
            {
                if (issue == null
                    || !ErrorLogReferenceMapping.TryMapCategory(issue.Category, out ReferenceAuditKind referenceKind))
                {
                    continue;
                }

                string targetKey = TargetKeyFromIssue(issue, referenceKind, warnings);
                if (targetKey.Length == 0)
                    continue;

                ModPossibleIssueKind kind = ErrorLogReferenceMapping.ToIssueKind(referenceKind);
                string key = TokenKey(kind, targetKey);
                if (!index.TryGetValue(key, out UnattributedEntry? entry))
                {
                    entry = new UnattributedEntry(kind, targetKey);
                    index[key] = entry;
                }

                entry.LineCount += issue.Count;
                _ = entry.Categories.Add(issue.Category);
            }

            return index;
        }

        private static string TargetKeyFromIssue(
            ModErrorLogIssue issue,
            ReferenceAuditKind referenceKind,
            ICollection<string> warnings)
        {
            string token = issue.Detail.Trim();
            if (token.Length == 0)
            {
                warnings.Add(
                    $"Could not compare a '{issue.Category}' line because it has no token detail (mod: " +
                    $"{(string.IsNullOrWhiteSpace(issue.ModId) ? "unattributed" : issue.ModId)}).");
                return string.Empty;
            }

            return ErrorLogReferenceMapping.ToTargetKey(referenceKind, token);
        }

        private static ReferenceMismatch CreateMismatch(
            AuditMismatchStatus status,
            ModPossibleIssueKind kind,
            string targetKey,
            IReadOnlyList<string> predictedMods,
            IReadOnlyList<string> reportedMods,
            IReadOnlyList<ReportedEntry> reportedEntries,
            RawReferenceIndex rawReferences,
            VanillaRawBaseline vanillaBaseline)
        {
            List<string> rawMods = [.. rawReferences.GetActiveMods(kind, targetKey)
                .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase)];
            bool capturedForPredicted = predictedMods.Any(modId => rawMods.Contains(modId, StringComparer.OrdinalIgnoreCase));
            bool capturedForReported = reportedMods.Any(modId => rawMods.Contains(modId, StringComparer.OrdinalIgnoreCase));

            List<string> targetDefinitions = [.. rawReferences.GetActiveDefinitionMods(targetKey)
                .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase)];
            List<string> targetCuts = [.. rawReferences.GetActiveCutMods(targetKey)
                .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase)];

            List<string> categories = [.. reportedEntries
                .SelectMany(entry => entry.Categories)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)];

            string evidence = BuildMismatchEvidence(
                status,
                kind,
                targetKey,
                predictedMods,
                reportedMods,
                rawMods,
                capturedForPredicted,
                capturedForReported,
                targetDefinitions,
                targetCuts,
                rawReferences,
                vanillaBaseline);

            return new ReferenceMismatch(
                status,
                kind,
                targetKey,
                predictedMods,
                reportedMods,
                reportedEntries.Sum(entry => entry.LineCount),
                categories,
                targetDefinitions,
                targetCuts,
                rawMods,
                capturedForPredicted,
                capturedForReported,
                evidence);
        }

        private static List<UnattributedReference> BuildUnattributedReferences(
            IEnumerable<UnattributedEntry> entries,
            RawReferenceIndex rawReferences,
            VanillaRawBaseline vanillaBaseline)
        {
            List<UnattributedReference> references = [];
            foreach (UnattributedEntry entry in entries
                .OrderBy(entry => entry.Kind)
                .ThenBy(entry => entry.TargetKey, StringComparer.OrdinalIgnoreCase))
            {
                List<string> rawMods = [.. rawReferences.GetActiveMods(entry.Kind, entry.TargetKey)
                    .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase)];
                string evidence = BuildUnattributedEvidence(
                    entry.Kind,
                    entry.TargetKey,
                    rawMods,
                    rawReferences,
                    vanillaBaseline);

                references.Add(new UnattributedReference(
                    entry.Kind,
                    entry.TargetKey,
                    entry.LineCount,
                    [.. entry.Categories.OrderBy(category => category, StringComparer.OrdinalIgnoreCase)],
                    rawMods,
                    evidence));
            }

            return references;
        }

        private static List<UnmappedErrorLogCategory> BuildUnmappedCategories(
            IEnumerable<ModErrorLogIssue> attributedIssues,
            IEnumerable<ModErrorLogIssue> unattributedIssues)
        {
            Dictionary<string, UnmappedCategoryEntry> categories = new(StringComparer.OrdinalIgnoreCase);

            void Add(ModErrorLogIssue issue)
            {
                if (issue == null
                    || ErrorLogReferenceMapping.TryMapCategory(issue.Category, out _)
                    || !ErrorLogReferenceMapping.LooksReferenceRelated(issue.Category))
                {
                    return;
                }

                if (!categories.TryGetValue(issue.Category, out UnmappedCategoryEntry? entry))
                {
                    entry = new UnmappedCategoryEntry(issue.Category);
                    categories[issue.Category] = entry;
                }

                entry.LineCount += issue.Count;
                entry.IssueTypeCount++;
                _ = entry.SampleMods.Add(string.IsNullOrWhiteSpace(issue.ModId) ? "(unattributed)" : issue.ModId);
            }

            foreach (ModErrorLogIssue issue in attributedIssues)
                Add(issue);
            foreach (ModErrorLogIssue issue in unattributedIssues)
                Add(issue);

            return [.. categories.Values
                .Select(entry => new UnmappedErrorLogCategory(
                    entry.Category,
                    entry.LineCount,
                    entry.IssueTypeCount,
                    [.. entry.SampleMods
                        .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase)
                        .Take(MaxEvidenceMods)]))
                .OrderByDescending(entry => entry.LineCount)
                .ThenBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)];
        }

        private static string BuildMismatchEvidence(
            AuditMismatchStatus status,
            ModPossibleIssueKind kind,
            string targetKey,
            IReadOnlyList<string> predictedMods,
            IReadOnlyList<string> reportedMods,
            List<string> rawMods,
            bool capturedForPredicted,
            bool capturedForReported,
            List<string> targetDefinitions,
            List<string> targetCuts,
            RawReferenceIndex rawReferences,
            VanillaRawBaseline vanillaBaseline)
        {
            bool definedByVanilla = IsDefinedByVanilla(kind, targetKey, vanillaBaseline);
            bool builtIn = IsBuiltInReactionBuilding(kind, targetKey);
            string definitionState = BuildDefinitionState(targetDefinitions, targetCuts, rawReferences, targetKey);
            string stateSuffix = definitionState.Length > 0 ? " " + definitionState : string.Empty;

            switch (status)
            {
                case AuditMismatchStatus.PredictedNotReported:
                    if (capturedForPredicted)
                    {
                        return $"raw scan captured this reference on {FormatModList(Intersect(predictedMods, rawMods))}; " +
                            "the log has no matching line (possible false positive, or the log was produced with another mod set)." +
                            stateSuffix;
                    }

                    if (definedByVanilla)
                        return "the target is defined by the vanilla raws; this prediction should not normally exist.";

                    return rawMods.Count > 0
                        ? $"raw scan captured this reference only on {FormatModList(rawMods)}, not on the predicted mod; " +
                            "the prediction entry and the raw scan disagree." + stateSuffix
                        : "raw scan did not capture this reference on any active mod; the prediction entry and the raw scan disagree." +
                            stateSuffix;

                case AuditMismatchStatus.ReportedNotPredicted:
                    // A vanilla definition (or a DF built-in workshop id) normally explains why
                    // the analyzer stayed silent. The exception is a target that an active mod
                    // defines but another active mod CUTs: that is a real false negative, and the
                    // definition state below says which mod did the cutting.
                    bool cutExplainsReference = targetDefinitions.Count > 0 && targetCuts.Count > 0;
                    if (definedByVanilla && !cutExplainsReference)
                    {
                        return "the target is defined by the vanilla raws; the analyzer intentionally stays silent, " +
                            "so this line is probably from a run without vanilla data or from a stale errorlog." +
                            stateSuffix;
                    }

                    if (builtIn && !cutExplainsReference)
                        return "the id is a DF built-in workshop/furnace type; no raw definition is expected.";

                    if (capturedForReported)
                    {
                        return $"raw scan captured this reference on reported mod(s) {FormatModList(Intersect(reportedMods, rawMods))}; " +
                            "the analyzer missed it (false negative)." + stateSuffix;
                    }

                    if (rawMods.Count > 0)
                    {
                        return $"raw scan captured this reference only on {FormatModList(rawMods)}; " +
                            "the log attributed the line to a different mod (attribution or scanner gap)." + stateSuffix;
                    }

                    return "raw scan did not capture this reference on any active mod; this is a raw-scanner gap." +
                        stateSuffix;

                default:
                    if (capturedForReported)
                    {
                        return $"raw scan captured this reference on reported mod(s) " +
                            $"{FormatModList(Intersect(reportedMods, rawMods))}, but the analyzer predicted " +
                            $"{FormatModList(predictedMods)} instead (attribution mismatch)." + stateSuffix;
                    }

                    if (capturedForPredicted)
                    {
                        return $"raw scan captured this reference on predicted mod(s) " +
                            $"{FormatModList(Intersect(predictedMods, rawMods))}, but the log attributed it to " +
                            $"{FormatModList(reportedMods)} (attribution mismatch). DF can attribute tokens added " +
                            "by a SELECT_* patch to the patched object's base mod." + stateSuffix;
                    }

                    if (definedByVanilla)
                        return "the target is defined by the vanilla raws, which explains why the analyzer stayed silent.";

                    if (builtIn)
                        return "the id is a DF built-in workshop/furnace type; no raw definition is expected.";

                    return "raw scan did not capture this reference on the reported or predicted mods; " +
                        "check the raw scanner and the errorlog attribution." + stateSuffix;
            }
        }

        private static string BuildUnattributedEvidence(
            ModPossibleIssueKind kind,
            string targetKey,
            List<string> rawMods,
            RawReferenceIndex rawReferences,
            VanillaRawBaseline vanillaBaseline)
        {
            string definitionState = BuildDefinitionState(
                rawReferences.GetActiveDefinitionMods(targetKey),
                rawReferences.GetActiveCutMods(targetKey),
                rawReferences,
                targetKey);
            string stateSuffix = definitionState.Length > 0 ? " " + definitionState : string.Empty;

            if (rawMods.Count > 0)
            {
                return $"raw scan captured this reference on {FormatModList(rawMods)}, but the log analyzer " +
                    "could not attribute the line to a mod (attribution gap)." + stateSuffix;
            }

            if (IsDefinedByVanilla(kind, targetKey, vanillaBaseline))
                return "the target is defined by the vanilla raws; the line is probably from a stale errorlog.";

            if (IsBuiltInReactionBuilding(kind, targetKey))
                return "the id is a DF built-in workshop/furnace type; no raw definition is expected.";

            return "raw scan did not capture this reference on any active mod; this is a raw-scanner gap." +
                stateSuffix;
        }

        private static string BuildDefinitionState(
            IReadOnlyCollection<string> activeDefinitions,
            IReadOnlyCollection<string> activeCuts,
            RawReferenceIndex rawReferences,
            string targetKey)
        {
            if (activeDefinitions.Count > 0)
            {
                return activeCuts.Count > 0
                    ? $"The target is directly defined by {FormatModList(activeDefinitions)} but CUT by " +
                        $"{FormatModList(activeCuts)}; ModPossibleIssueAnalyzer currently treats any direct definition " +
                        "as satisfying the reference."
                    : $"The target is directly defined by {FormatModList(activeDefinitions)}.";
            }

            IReadOnlyCollection<string> allDefinitions = rawReferences.GetAllDefinitionMods(targetKey);
            return allDefinitions.Count > 0
                ? $"The target is only directly defined by non-active cached mod(s) {FormatModList(allDefinitions)}."
                : string.Empty;
        }

        private static bool IsDefinedByVanilla(
            ModPossibleIssueKind kind,
            string targetKey,
            VanillaRawBaseline vanillaBaseline)
        {
            string objectType = ErrorLogReferenceMapping.ObjectType(kind);
            return objectType.Length > 0
                && vanillaBaseline.Contains(objectType, ErrorLogReferenceMapping.TargetId(targetKey));
        }

        private static bool IsBuiltInReactionBuilding(ModPossibleIssueKind kind, string targetKey)
            => kind == ModPossibleIssueKind.MissingReactionBuilding
                && ModPossibleIssueAnalyzer.IsBuiltInReactionBuildingId(ErrorLogReferenceMapping.TargetId(targetKey));

        private static void RememberToken(
            Dictionary<string, TokenInfo> tokenInfo,
            ModPossibleIssueKind kind,
            string targetKey)
        {
            string key = TokenKey(kind, targetKey);
            _ = tokenInfo.TryAdd(key, new TokenInfo(kind, targetKey));
        }

        private static void AddToTokenMap<T>(
            Dictionary<string, List<T>> map,
            ModPossibleIssueKind kind,
            string targetKey,
            T entry)
        {
            string key = TokenKey(kind, targetKey);
            if (!map.TryGetValue(key, out List<T>? entries))
            {
                entries = [];
                map[key] = entries;
            }

            entries.Add(entry);
        }

        private static string NormalizeTargetKey(string? targetKey, ICollection<string> warnings)
        {
            string trimmed = (targetKey ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            if (ObjectKey.TryParse(trimmed, out string objectType, out string id))
                return new ObjectKey(objectType, id).ToString();

            warnings.Add($"Prediction target '{trimmed}' is not an ObjectType:Id key; comparing it verbatim.");
            return trimmed;
        }

        private static string PairKey(string modId, ModPossibleIssueKind kind, string targetKey)
            => modId.Trim().ToUpperInvariant()
                + "\u001f"
                + ((int)kind).ToString(CultureInfo.InvariantCulture)
                + "\u001f"
                + targetKey.Trim().ToUpperInvariant();

        private static string TokenKey(ModPossibleIssueKind kind, string targetKey)
            => ((int)kind).ToString(CultureInfo.InvariantCulture)
                + "\u001f"
                + targetKey.Trim().ToUpperInvariant();

        private static int CompareMismatches(ReferenceMismatch left, ReferenceMismatch right)
        {
            int statusComparison = left.Status.CompareTo(right.Status);
            if (statusComparison != 0)
                return statusComparison;

            int kindComparison = left.Kind.CompareTo(right.Kind);
            if (kindComparison != 0)
                return kindComparison;

            int targetComparison = StringComparer.OrdinalIgnoreCase.Compare(left.TargetKey, right.TargetKey);
            if (targetComparison != 0)
                return targetComparison;

            return StringComparer.OrdinalIgnoreCase.Compare(
                string.Join(",", left.PredictedMods),
                string.Join(",", right.PredictedMods));
        }

        private static List<string> Intersect(
            IReadOnlyList<string> left,
            IReadOnlyList<string> right)
            => [.. left
                .Where(value => right.Contains(value, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];

        private static string FormatModList(IEnumerable<string> mods)
        {
            List<string> values = [.. mods
                .Where(modId => !string.IsNullOrWhiteSpace(modId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase)];

            if (values.Count == 0)
                return "(none)";

            string joined = string.Join(", ", values.Take(MaxEvidenceMods));
            return values.Count > MaxEvidenceMods
                ? $"{joined} (+{values.Count - MaxEvidenceMods} more)"
                : joined;
        }

        private sealed record PredictionEntry(string ModId, ModPossibleIssueKind Kind, string TargetKey);

        private sealed class ReportedEntry(string modId, ModPossibleIssueKind kind, string targetKey)
        {
            public string ModId { get; } = modId;
            public ModPossibleIssueKind Kind { get; } = kind;
            public string TargetKey { get; } = targetKey;
            public int LineCount { get; set; }
            public HashSet<string> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class UnattributedEntry(ModPossibleIssueKind kind, string targetKey)
        {
            public ModPossibleIssueKind Kind { get; } = kind;
            public string TargetKey { get; } = targetKey;
            public int LineCount { get; set; }
            public HashSet<string> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class UnmappedCategoryEntry(string category)
        {
            public string Category { get; } = category;
            public int LineCount { get; set; }
            public int IssueTypeCount { get; set; }
            public HashSet<string> SampleMods { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed record TokenInfo(ModPossibleIssueKind Kind, string TargetKey);
    }
}
