using ModHearth.Manager;
using ModHearth.Models;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Pins the severity mapping that drives the mod-list icon. The UI must not grow its own
    /// per-category switches: every new problem source belongs here.
    /// </summary>
    public class ModIssueClassifierTests
    {
        [Theory]
        [InlineData("Invalid mod identifier", ModIssueTier.Error)]
        [InlineData("Invalid mod version metadata", ModIssueTier.Error)]
        [InlineData("Duplicate mod installation", ModIssueTier.Error)]
        [InlineData("Unrecognized mod info token", ModIssueTier.Info)]
        [InlineData("Unrecognized creature token", ModIssueTier.Warning)]
        [InlineData("Missing creature caste graphics", ModIssueTier.Warning)]
        [InlineData("Something new", ModIssueTier.Warning)]
        public void ErrorLogCategoriesMapToExpectedTier(string category, ModIssueTier expected)
            => Assert.Equal(expected, ModIssueClassifier.ForErrorLogCategory(category));

        [Theory]
        [InlineData(ModProblem.ProblemType.DuplicateMod, ModIssueTier.Error)]
        [InlineData(ModProblem.ProblemType.ConflictPresent, ModIssueTier.Error)]
        [InlineData(ModProblem.ProblemType.MissingRequired, ModIssueTier.Error)]
        [InlineData(ModProblem.ProblemType.MissingBefore, ModIssueTier.Warning)]
        [InlineData(ModProblem.ProblemType.MissingAfter, ModIssueTier.Warning)]
        [InlineData(ModProblem.ProblemType.ErrorLogIssue, ModIssueTier.Warning)]
        [InlineData(ModProblem.ProblemType.PossibleIssue, ModIssueTier.Warning)]
        public void ProblemTypesMapToExpectedTier(ModProblem.ProblemType problemType, ModIssueTier expected)
            => Assert.Equal(expected, ModIssueClassifier.ForProblemType(problemType));

        [Fact]
        public void CacheOnlyDuplicateWarningsHaveNoTier()
        {
            Assert.Equal(ModIssueTier.None, ModIssueClassifier.ForErrorLogDuplicateWarning(foundInErrorLog: false));
            Assert.Equal(ModIssueTier.Warning, ModIssueClassifier.ForErrorLogDuplicateWarning(foundInErrorLog: true));
        }

        [Theory]
        [InlineData(false, false, ModIssueTier.None)]
        [InlineData(true, false, ModIssueTier.Warning)]
        [InlineData(false, true, ModIssueTier.Warning)]
        [InlineData(true, true, ModIssueTier.Warning)]
        public void DuplicateWarningsEscalateWhenEitherSearchConfirmsThem(
            bool foundInErrorLog,
            bool staticallyConfirmed,
            ModIssueTier expected)
            => Assert.Equal(expected, ModIssueClassifier.ForErrorLogDuplicateWarning(foundInErrorLog, staticallyConfirmed));

        [Theory]
        [InlineData(ModProblem.ProblemType.DuplicateMod, ModIssueTier.None, true)]
        [InlineData(ModProblem.ProblemType.DuplicateMod, ModIssueTier.Error, true)]
        [InlineData(ModProblem.ProblemType.ErrorLogIssue, ModIssueTier.Error, true)]
        [InlineData(ModProblem.ProblemType.ErrorLogIssue, ModIssueTier.Warning, false)]
        [InlineData(ModProblem.ProblemType.ErrorLogIssue, ModIssueTier.Info, false)]
        [InlineData(ModProblem.ProblemType.ErrorLogIssue, ModIssueTier.None, false)]
        [InlineData(ModProblem.ProblemType.ConflictPresent, ModIssueTier.Error, false)]
        [InlineData(ModProblem.ProblemType.MissingRequired, ModIssueTier.Error, false)]
        [InlineData(ModProblem.ProblemType.MissingBefore, ModIssueTier.Warning, false)]
        [InlineData(ModProblem.ProblemType.MissingAfter, ModIssueTier.Warning, false)]
        [InlineData(ModProblem.ProblemType.PossibleIssue, ModIssueTier.Warning, false)]
        public void SortExclusionMatchesFatalProblemRule(
            ModProblem.ProblemType problemType,
            ModIssueTier tier,
            bool expected)
        {
            ModProblem problem = new("some.mod", "detail", problemType, tier);

            Assert.Equal(expected, problem.IsSortExcluded);
            Assert.Equal(expected, ModSortExclusion.Excludes(problem));
        }

        [Fact]
        public void CollectExcludedModIdsTrimsAndDeduplicates()
        {
            ModProblem[] problems =
            [
                new("fatal.mod", "detail", ModProblem.ProblemType.DuplicateMod, ModIssueTier.Error),
                new("  fatal.mod  ", "detail", ModProblem.ProblemType.ErrorLogIssue, ModIssueTier.Error),
                new("warning.mod", "detail", ModProblem.ProblemType.ErrorLogIssue, ModIssueTier.Warning),
                new("info.mod", "detail", ModProblem.ProblemType.ErrorLogIssue, ModIssueTier.Info),
                new("conflict.mod", "detail", ModProblem.ProblemType.ConflictPresent, ModIssueTier.Error),
                new(" ", "detail", ModProblem.ProblemType.DuplicateMod, ModIssueTier.Error),
            ];

            HashSet<string> excluded = ModSortExclusion.CollectExcludedModIds(problems);

            _ = Assert.Single(excluded);
            Assert.Contains("fatal.mod", excluded);
        }
    }
}
