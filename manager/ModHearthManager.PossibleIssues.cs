using System.Text;
using ModHearth.Models;
using ModHearth.Utils.Logging;
using ModHearth.Utils.ModRaws;

using static ModHearth.Config.ConfigManager;

namespace ModHearth.Manager
{
    public partial class ModHearthManager
    {
        // Keep the per-mod tooltip readable even when a mod references dozens of missing objects.
        private const int MaxPossibleIssuesPerMod = 8;

        private readonly Lock possibleIssueGate = new();
        private volatile Dictionary<string, List<ModProblem>> possibleIssueMap = new(StringComparer.OrdinalIgnoreCase);
        private DateTime? possibleIssueCacheWriteUtc;
        private HashSet<string>? possibleIssueActiveModIds;

        /// <summary>
        /// Asks for a fresh raw dependency scan (also required by ModSort) and then rebuilds the
        /// problem list so the warning indicators see the new raw data.
        /// </summary>
        public async Task RefreshRawAnalysisAsync()
        {
            await EnsureModRawDependencyCacheAsync();
            FindModlistProblems();
        }

        /// <summary>
        /// Rebuilds the problem list after a search setting changed without rescanning raw files.
        /// The raw cache is keyed by each mod's objects/ stamp, so toggling a setting never needs a
        /// full rescan.
        /// </summary>
        public void RefreshIssueAnalysis()
        {
            ClearErrorLogProblemCache();
            FindModlistProblems();
        }

        private void AppendPossibleIssueProblems(IEnumerable<DfhMod> mods, List<ModProblem> problems)
        {
            EnsurePossibleIssueCache();

            Dictionary<string, List<ModProblem>> map = possibleIssueMap;
            if (map.Count == 0)
                return;

            foreach (DfhMod mod in mods)
            {
                if (map.TryGetValue(mod.Id, out List<ModProblem>? modProblems))
                    problems.AddRange(modProblems);
            }
        }

        private void EnsurePossibleIssueCache()
        {
            if (!GetSearchPossibleIssues())
            {
                ClearPossibleIssueCache();
                return;
            }

            DateTime cacheWriteUtc = GetRawDependencyCacheWriteUtc();
            HashSet<string> activeModIds = GetActiveModIds();

            lock (possibleIssueGate)
            {
                bool upToDate =
                    possibleIssueCacheWriteUtc.HasValue
                    && possibleIssueCacheWriteUtc.Value == cacheWriteUtc
                    && possibleIssueActiveModIds != null
                    && possibleIssueActiveModIds.SetEquals(activeModIds);

                if (upToDate)
                    return;

                // Without a persisted raw scan there is nothing to analyze yet. Skipping here also
                // avoids loading the vanilla baseline before the first scan has ever run.
                if (cacheWriteUtc == DateTime.MinValue)
                {
                    possibleIssueMap = new Dictionary<string, List<ModProblem>>(StringComparer.OrdinalIgnoreCase);
                    possibleIssueCacheWriteUtc = cacheWriteUtc;
                    possibleIssueActiveModIds = activeModIds;
                    return;
                }

                Dictionary<string, List<ModProblem>> newMap = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    IReadOnlyCollection<ModRawDependencyInfo> rawInfos = GetRawDependencyInfoSnapshot();

                    // info.txt problems are independent of the vanilla baseline, so they are added
                    // even when the DF install path is unavailable.
                    foreach (KeyValuePair<string, List<ModProblem>> entry in BuildInfoIssueProblems(rawInfos, activeModIds))
                        newMap[entry.Key] = entry.Value;

                    VanillaRawBaseline vanillaBaseline = GetVanillaBaseline();
                    if (vanillaBaseline.IsEmpty)
                    {
                        Loggers.Diag.Dev(
                            $"Possible raw issue scan skipped: the vanilla raw baseline at " +
                            $"'{GetVanillaModsPath()}' is unavailable.");
                    }
                    else
                    {
                        List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                            rawInfos,
                            activeModIds,
                            vanillaBaseline);

                        foreach (IGrouping<string, ModPossibleIssue> grouping in issues.GroupBy(issue => issue.ModId, StringComparer.OrdinalIgnoreCase))
                        {
                            List<ModPossibleIssue> modIssues = [.. grouping
                                .OrderBy(issue => issue.Kind)
                                .ThenBy(issue => issue.TargetKey, StringComparer.OrdinalIgnoreCase)];

                            if (!newMap.TryGetValue(grouping.Key, out List<ModProblem>? modProblems))
                            {
                                modProblems = [];
                                newMap[grouping.Key] = modProblems;
                            }

                            modProblems.Add(new ModProblem(
                                grouping.Key,
                                BuildPossibleIssueText(modIssues),
                                ModProblem.ProblemType.PossibleIssue,
                                ModIssueClassifier.ForProblemType(ModProblem.ProblemType.PossibleIssue)));
                        }

                        int affectedModCount = issues
                            .Select(issue => issue.ModId)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();

                        Loggers.Diag.Dev(
                            $"Possible raw issue scan: {issues.Count} unresolved reference(s) across " +
                            $"{affectedModCount} mod(s) from {activeModIds.Count} active mod(s).");
                    }
                }
                catch (Exception ex)
                {
                    AppLogging.LogException("Failed to analyze mod raws for possible issues", ex);
                }

                possibleIssueMap = newMap;
                possibleIssueCacheWriteUtc = cacheWriteUtc;
                possibleIssueActiveModIds = activeModIds;
            }
        }

        private void ClearPossibleIssueCache()
        {
            lock (possibleIssueGate)
            {
                if (!possibleIssueCacheWriteUtc.HasValue && possibleIssueMap.Count == 0)
                    return;

                possibleIssueMap = new Dictionary<string, List<ModProblem>>(StringComparer.OrdinalIgnoreCase);
                possibleIssueCacheWriteUtc = null;
                possibleIssueActiveModIds = null;
            }
        }

        private static string BuildPossibleIssueText(List<ModPossibleIssue> issues)
        {
            StringBuilder builder = new(
                "Dwarf Fortress would report these raw problems (detected without launching the game):" + Environment.NewLine);

            foreach (ModPossibleIssue issue in issues.Take(MaxPossibleIssuesPerMod))
                _ = builder.AppendLine().Append("- ").Append(DescribePossibleIssue(issue));

            if (issues.Count > MaxPossibleIssuesPerMod)
                _ = builder.AppendLine().Append("- ...and ").Append(issues.Count - MaxPossibleIssuesPerMod).Append(" more missing reference(s)");

            return builder.ToString();
        }

        private static Dictionary<string, List<ModProblem>> BuildInfoIssueProblems(
            IEnumerable<ModRawDependencyInfo> rawInfos,
            HashSet<string> activeModIds)
        {
            // A cache may still contain two versions of the same active id until the next scan.
            // Group by mod and category so identical tokens are reported once.
            Dictionary<string, Dictionary<string, HashSet<string>>> aggregated = new(StringComparer.OrdinalIgnoreCase);

            foreach (ModRawDependencyInfo info in rawInfos)
            {
                if (string.IsNullOrWhiteSpace(info.ModId)
                    || !activeModIds.Contains(info.ModId)
                    || info.InfoIssues.Count == 0)
                {
                    continue;
                }

                if (!aggregated.TryGetValue(info.ModId, out Dictionary<string, HashSet<string>>? byCategory))
                {
                    byCategory = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    aggregated[info.ModId] = byCategory;
                }

                foreach (ModInfoIssue issue in info.InfoIssues)
                {
                    if (!byCategory.TryGetValue(issue.Category, out HashSet<string>? details))
                    {
                        details = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        byCategory[issue.Category] = details;
                    }

                    _ = details.Add(issue.Detail);
                }
            }

            Dictionary<string, List<ModProblem>> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Dictionary<string, HashSet<string>>> modEntry in aggregated)
            {
                foreach (KeyValuePair<string, HashSet<string>> categoryEntry in modEntry.Value
                    .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
                {
                    List<string> details = [.. categoryEntry.Value
                        .OrderBy(detail => detail, StringComparer.OrdinalIgnoreCase)];

                    if (!map.TryGetValue(modEntry.Key, out List<ModProblem>? problems))
                    {
                        problems = [];
                        map[modEntry.Key] = problems;
                    }

                    problems.Add(new ModProblem(
                        modEntry.Key,
                        BuildInfoIssueText(categoryEntry.Key, details),
                        ModProblem.ProblemType.PossibleIssue,
                        ModIssueClassifier.ForErrorLogCategory(categoryEntry.Key)));
                }
            }

            return map;
        }

        private static string BuildInfoIssueText(string category, List<string> details)
        {
            StringBuilder builder = new(
                "Dwarf Fortress would report these info.txt problems (detected without launching the game):");

            foreach (string detail in details.Take(MaxPossibleIssuesPerMod))
                _ = builder.AppendLine().Append("- ").Append(category).Append(": ").Append(detail);

            if (details.Count > MaxPossibleIssuesPerMod)
            {
                _ = builder.AppendLine()
                    .Append("- ...and ")
                    .Append(details.Count - MaxPossibleIssuesPerMod)
                    .Append(" more info.txt problem(s)");
            }

            return builder.ToString();
        }

        private static string DescribePossibleIssue(ModPossibleIssue issue)
        {
            string reference = issue.Kind switch
            {
                ModPossibleIssueKind.MissingSelectTarget => "SELECT target",
                ModPossibleIssueKind.MissingCutTarget => "CUT target",
                ModPossibleIssueKind.MissingPermittedReaction => "PERMITTED_REACTION target",
                ModPossibleIssueKind.MissingPermittedBuilding => "PERMITTED_BUILDING target",
                ModPossibleIssueKind.MissingReactionBuilding => "reaction BUILDING target",
                _ => "COPY_TAGS_FROM source"
            };

            return $"{reference} '{ObjectKey.FormatForDisplay(issue.TargetKey)}' is not defined by any active mod or vanilla raw.";
        }
    }
}
