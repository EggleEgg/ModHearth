using ModHearth.Models;
using ModHearth.Utils.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Pins the static info.txt checks to the error-log categories Dwarf Fortress emits. Keeping
    /// the categories identical lets <see cref="ModIssueClassifier"/> assign the same severity.
    /// </summary>
    public class ModInfoIssueAnalyzerTests
    {
        [Fact]
        public void RecognizedTagsProduceNoIssues()
        {
            const string info = """
                [ID:test_mod]
                [NUMERIC_VERSION:2]
                [DISPLAYED_VERSION:2.1]
                [EARLIEST_COMPATIBLE_NUMERIC_VERSION:1]
                [EARLIEST_COMPATIBLE_DISPLAYED_VERSION:1.0]
                [AUTHOR:someone]
                [NAME:Test mod]
                [DESCRIPTION:A description without brackets.]
                [REQUIRES_ID_BEFORE_ME:first]
                [REQUIRES_ID_AFTER_ME:third]
                [REQUIRES_ID:fourth]
                [CONFLICTS_WITH_ID:fifth]
                [STEAM_TITLE:Title]
                [STEAM_DESCRIPTION:Steam description]
                [STEAM_TAG:mod]
                [STEAM_FILE_ID:123]
                [STEAM_CHANGELOG:Changes]
                [STEAM_METADATA:Metadata]
                [STEAM_KEY_VALUE_TAG:key:value]
                """;

            Assert.Empty(ModInfoIssueAnalyzer.Analyze(info));
        }

        [Fact]
        public void DecimalNumericVersionIsReported()
        {
            const string info = """
                [ID:test_mod]
                [NUMERIC_VERSION:1.5]
                """;

            ModInfoIssue issue = Assert.Single(ModInfoIssueAnalyzer.Analyze(info));
            Assert.Equal(ModInfoIssueKind.InvalidVersionMetadata, issue.Kind);
            Assert.Equal("Invalid mod version metadata", issue.Category);
            Assert.Contains("1.5", issue.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public void EarliestCompatibleVersionNewerThanTheModIsReported()
        {
            const string info = """
                [ID:test_mod]
                [NUMERIC_VERSION:1]
                [EARLIEST_COMPATIBLE_NUMERIC_VERSION:5001]
                """;

            ModInfoIssue issue = Assert.Single(ModInfoIssueAnalyzer.Analyze(info));
            Assert.Equal(ModInfoIssueKind.InvalidVersionMetadata, issue.Kind);
            Assert.Contains("less than or equal", issue.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public void DecimalEarliestCompatibleVersionIsAcceptedWhenItIsNotNewer()
        {
            const string info = """
                [ID:test_mod]
                [NUMERIC_VERSION:5]
                [EARLIEST_COMPATIBLE_NUMERIC_VERSION:1.5]
                """;

            Assert.Empty(ModInfoIssueAnalyzer.Analyze(info));
        }

        [Fact]
        public void IdentifierWithWindowsInvalidCharactersIsReported()
        {
            const string info = """
                [ID:Real Book/Scroll Content!]
                [NUMERIC_VERSION:1]
                """;

            ModInfoIssue issue = Assert.Single(ModInfoIssueAnalyzer.Analyze(info));
            Assert.Equal(ModInfoIssueKind.InvalidIdentifier, issue.Kind);
            Assert.Equal("Invalid mod identifier", issue.Category);
            Assert.Contains("Real Book/Scroll Content!", issue.Detail, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("Spaces And Capitals")]
        [InlineData("SA&M_FFF")]
        [InlineData("Von's_Big_Naturals")]
        [InlineData("Real Book/Scroll Content!")]
        public void OnlyFileNameInvalidCharactersInvalidateTheIdentifier(string id)
        {
            string info = $"[ID:{id}]\n[NUMERIC_VERSION:1]";

            bool reported = ModInfoIssueAnalyzer.Analyze(info)
                .Any(issue => issue.Kind == ModInfoIssueKind.InvalidIdentifier);

            Assert.Equal(id.Contains('/'), reported);
        }

        [Fact]
        public void UnescapedBracketsInDescriptionsBecomeUnrecognizedTokens()
        {
            const string info = """
                [ID:test_mod]
                [NUMERIC_VERSION:1]
                [DESCRIPTION:Highlights [h2]bold[/h2] and [*] a list item.]
                """;

            List<ModInfoIssue> issues = ModInfoIssueAnalyzer.Analyze(info);

            Assert.Equal(3, issues.Count);
            Assert.All(issues, issue => Assert.Equal(ModInfoIssueKind.UnrecognizedToken, issue.Kind));
            Assert.Equal(
                ["*", "/h2", "h2"],
                issues.Select(issue => issue.Detail).OrderBy(value => value, StringComparer.Ordinal));
        }

        [Fact]
        public void RepeatedUnknownTokensAreReportedOnce()
        {
            const string info = """
                [ID:test_mod]
                [DESCRIPTION:[b]one[/b] [b]two[/b]]
                """;

            List<ModInfoIssue> issues = ModInfoIssueAnalyzer.Analyze(info);

            Assert.Equal(2, issues.Count);
            Assert.Contains(issues, issue => issue.Detail == "b");
            Assert.Contains(issues, issue => issue.Detail == "/b");
        }

        [Fact]
        public void MissingOrEmptyInfoHasNoIssues()
        {
            Assert.Empty(ModInfoIssueAnalyzer.Analyze(null));
            Assert.Empty(ModInfoIssueAnalyzer.Analyze("   "));
            Assert.Empty(ModInfoIssueAnalyzer.AnalyzeFile(null));
            Assert.Empty(ModInfoIssueAnalyzer.AnalyzeFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "info.txt")));
        }
    }
}
