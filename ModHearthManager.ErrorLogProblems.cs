using System.Text;
using ModHearth.Models;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;
using ModHearth.Utilities.ModRaws;
using static ModHearth.ConfigManager;

namespace ModHearth
{
    public partial class ModHearthManager
    {
        // Keep the per-mod tooltip readable even when a mod produces hundreds of distinct errors.
        private const int MaxErrorLogIssuesPerMod = 8;

        private readonly object errorLogProblemGate = new();
        private volatile Dictionary<string, List<ModProblem>> errorLogProblemMap = new(StringComparer.OrdinalIgnoreCase);
        private string? errorLogProblemPath;
        private DateTime? errorLogProblemLastWriteUtc;
        private long errorLogProblemLength = -1;
        private Dictionary<string, ModReference>? errorLogProblemModSnapshot;
        private DateTime errorLogProblemRawCacheWriteUtc = DateTime.MinValue;

        private void AppendErrorLogProblems(List<ModProblem> problems)
        {
            EnsureErrorLogProblemCache();

            Dictionary<string, List<ModProblem>> map = errorLogProblemMap;
            if (map.Count == 0)
                return;

            foreach (DfhMod mod in modPool)
            {
                if (map.TryGetValue(mod.Id, out List<ModProblem>? modProblems))
                    problems.AddRange(modProblems);
            }
        }

        private void EnsureErrorLogProblemCache()
        {
            string errorLogPath = GetErrorLogPath();
            if (!File.Exists(errorLogPath))
            {
                ClearErrorLogProblemCache();
                return;
            }

            DateTime lastWriteUtc;
            long length;
            try
            {
                lastWriteUtc = File.GetLastWriteTimeUtc(errorLogPath);
                length = new FileInfo(errorLogPath).Length;
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Could not stat Dwarf Fortress errorlog '{errorLogPath}': {ex.Message}");
                ClearErrorLogProblemCache();
                return;
            }

            Dictionary<string, ModReference> modSnapshot = modrefMap;
            DateTime rawCacheWriteUtc = GetRawDependencyCacheWriteUtc();

            lock (errorLogProblemGate)
            {
                bool upToDate =
                    string.Equals(errorLogProblemPath, errorLogPath, PathHelper.Comparison)
                    && errorLogProblemLastWriteUtc == lastWriteUtc
                    && errorLogProblemLength == length
                    && ReferenceEquals(errorLogProblemModSnapshot, modSnapshot)
                    && errorLogProblemRawCacheWriteUtc == rawCacheWriteUtc;

                if (upToDate)
                    return;

                Dictionary<string, List<ModProblem>> newMap = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                        errorLogPath,
                        [.. modSnapshot.Values],
                        GetRawDependencyInfoSnapshot());

                    foreach (IGrouping<string, ModErrorLogIssue> grouping in analysis.Issues.GroupBy(issue => issue.ModId, StringComparer.OrdinalIgnoreCase))
                    {
                        List<ModErrorLogIssue> issues = [.. grouping
                            .OrderByDescending(issue => ModIssueClassifier.ForErrorLogCategory(issue.Category))
                            .ThenByDescending(issue => issue.Count)
                            .ThenBy(issue => issue.Category, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(issue => issue.Detail, StringComparer.OrdinalIgnoreCase)];

                        // One row per mod, at the highest tier any of its error-log categories
                        // reaches. A mod with only description-metadata noise stays at Info.
                        ModIssueTier tier = issues.Max(issue => ModIssueClassifier.ForErrorLogCategory(issue.Category));

                        newMap[grouping.Key] = [new ModProblem(
                            grouping.Key,
                            BuildErrorLogProblemText(issues),
                            ModProblem.ProblemType.ErrorLogIssue,
                            tier)];
                    }

                    Loggers.Diag.Dev(
                        $"DF errorlog: {analysis.ConsideredLineCount} error line(s), {analysis.AttributedLineCount} attributed, " +
                        $"{analysis.UnattributedLineCount} unresolved, {analysis.Issues.Count} issue type(s).");
                }
                catch (Exception ex)
                {
                    AppLogging.LogException($"Failed to analyze Dwarf Fortress errorlog '{errorLogPath}'", ex);
                }

                errorLogProblemMap = newMap;
                errorLogProblemPath = errorLogPath;
                errorLogProblemLastWriteUtc = lastWriteUtc;
                errorLogProblemLength = length;
                errorLogProblemModSnapshot = modSnapshot;
                errorLogProblemRawCacheWriteUtc = rawCacheWriteUtc;
            }
        }

        private void ClearErrorLogProblemCache()
        {
            lock (errorLogProblemGate)
            {
                if (errorLogProblemPath == null && errorLogProblemMap.Count == 0)
                    return;

                errorLogProblemMap = new Dictionary<string, List<ModProblem>>(StringComparer.OrdinalIgnoreCase);
                errorLogProblemPath = null;
                errorLogProblemLastWriteUtc = null;
                errorLogProblemLength = -1;
                errorLogProblemModSnapshot = null;
                errorLogProblemRawCacheWriteUtc = DateTime.MinValue;
            }
        }

        private static string BuildErrorLogProblemText(IReadOnlyList<ModErrorLogIssue> issues)
        {
            int totalCount = issues.Sum(issue => issue.Count);
            StringBuilder builder = new("Dwarf Fortress errorlog: ");
            _ = builder.Append(totalCount).Append(" error line(s) across ").Append(issues.Count).Append(" issue type(s):");

            foreach (ModErrorLogIssue issue in issues.Take(MaxErrorLogIssuesPerMod))
                _ = builder.AppendLine().Append("- ").Append(issue.ToDisplayString());

            if (issues.Count > MaxErrorLogIssuesPerMod)
                _ = builder.AppendLine().Append("- ...and ").Append(issues.Count - MaxErrorLogIssuesPerMod).Append(" more issue type(s)");

            return builder.ToString();
        }

        private static DateTime GetRawDependencyCacheWriteUtc()
        {
            try
            {
                return File.Exists(ModRawDependencyCacheStore.CachePath)
                    ? File.GetLastWriteTimeUtc(ModRawDependencyCacheStore.CachePath)
                    : DateTime.MinValue;
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Could not stat the raw dependency cache: {ex.Message}");
                return DateTime.MinValue;
            }
        }
    }
}
