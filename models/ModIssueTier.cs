namespace ModHearth.Models
{
    /// <summary>
    /// Severity of a guaranteed mod issue. Higher values are more severe.
    /// <see cref="None"/> means "no guaranteed issue" and is also used for potential-only warnings
    /// (for example cache-based duplicate raw definitions), which deliberately show no icon.
    /// </summary>
    public enum ModIssueTier
    {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>
    /// Single place that decides how a problem or error-log category maps onto a
    /// <see cref="ModIssueTier"/>. Add new cases here rather than in the UI so every surface
    /// (row indicator, tooltips, future issue lists) agrees on the severity.
    /// </summary>
    public static class ModIssueClassifier
    {
        public static ModIssueTier ForProblemType(ModProblem.ProblemType problemType) => problemType switch
        {
            // Structural problems: the mod list cannot be loaded as configured.
            ModProblem.ProblemType.DuplicateMod => ModIssueTier.Error,
            ModProblem.ProblemType.ConflictPresent => ModIssueTier.Error,
            ModProblem.ProblemType.MissingRequired => ModIssueTier.Error,

            // Load-order problems are real but usually recoverable by reordering.
            ModProblem.ProblemType.MissingBefore => ModIssueTier.Warning,
            ModProblem.ProblemType.MissingAfter => ModIssueTier.Warning,

            // Error-log problems are refined per category below.
            ModProblem.ProblemType.ErrorLogIssue => ModIssueTier.Warning,

            // Statically predicted raw problems: DF would report them on load, but the game is
            // not expected to fail outright.
            ModProblem.ProblemType.PossibleIssue => ModIssueTier.Warning,
            _ => ModIssueTier.Warning
        };

        /// <summary>
        /// Error-log categories are produced by <c>ModErrorLogAnalyzer</c>.
        /// </summary>
        public static ModIssueTier ForErrorLogCategory(string? category) => category switch
        {
            // Invalid identity/version metadata can prevent the mod from loading at all.
            "Invalid mod identifier" => ModIssueTier.Error,
            "Invalid mod version metadata" => ModIssueTier.Error,

            // Duplicate installations make it ambiguous which copy DF uses.
            "Duplicate mod installation" => ModIssueTier.Error,

            // Description-only metadata complaints do not prevent world creation or mod loading.
            "Unrecognized mod info token" => ModIssueTier.Info,

            // Raw/graphics/plant errors indicate broken content, but the world can still load.
            _ => ModIssueTier.Warning
        };

        /// <summary>
        /// Duplicate raw definitions reported in the error log are guaranteed conflicts (DF names
        /// the offending mods). The static raw scan proves the same conflict without launching the
        /// game, so enabling the possible-issues search promotes those to a real warning. When
        /// neither source confirms the conflict it stays a potential-only hint with no icon.
        /// </summary>
        public static ModIssueTier ForErrorLogDuplicateWarning(bool foundInErrorLog, bool staticallyConfirmed = false)
            => foundInErrorLog || staticallyConfirmed ? ModIssueTier.Warning : ModIssueTier.None;
    }

    /// <summary>
    /// Decides which problems are fatal enough that the affected mod must be left out of the
    /// active list when sorting, becoming inactive instead. This is intentionally separate from
    /// <see cref="ModIssueTier"/>: some errors are shown in red but are still worth sorting, while
    /// a duplicate installation or an unloadable error-log issue would only break the result.
    /// </summary>
    public static class ModSortExclusion
    {
        /// <summary>
        /// True when <paramref name="problem"/> means the mod cannot safely remain enabled.
        /// </summary>
        public static bool Excludes(ModProblem problem) => problem.issueType switch
        {
            // The same mod id exists in more than one place; which copy DF loads is ambiguous.
            ModProblem.ProblemType.DuplicateMod => true,

            // Only the fatal error-log tier excludes: invalid identity/version metadata and
            // duplicate installations prevent the mod from loading correctly.
            ModProblem.ProblemType.ErrorLogIssue => problem.tier == ModIssueTier.Error,

            _ => false
        };

        /// <summary>
        /// Collects the ids of all mods that have at least one fatal problem.
        /// </summary>
        public static HashSet<string> CollectExcludedModIds(IEnumerable<ModProblem> problems)
        {
            HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModProblem problem in problems)
            {
                if (string.IsNullOrWhiteSpace(problem.problemThrowerID) || !Excludes(problem))
                    continue;

                _ = excluded.Add(problem.problemThrowerID.Trim());
            }
            return excluded;
        }
    }
}
