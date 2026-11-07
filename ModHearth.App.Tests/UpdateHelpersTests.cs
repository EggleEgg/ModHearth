using ModHearth.UI.Update;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Build versions are dotted numbers ("51.10"), not decimal numbers or plain text. These tests
    /// keep the update checker ranking them numerically instead of trusting GitHub's API order.
    /// </summary>
    public class UpdateHelpersTests
    {
        [Theory]
        [InlineData("51.10", "51.9", true)]
        [InlineData("51.9", "51.10", false)]
        [InlineData("51.10", "51.10", false)]
        [InlineData("52", "51.10", true)]
        [InlineData("50.9", "51.1", false)]
        [InlineData("51.10.1", "51.10", true)]
        [InlineData("51.10", "51.10.1", false)]
        public void IsNewerVersionComparesBuildNumbersNumerically(
            string fetched,
            string current,
            bool expected)
            => Assert.Equal(expected, UpdateHelpers.IsNewerVersion(fetched, current));

        [Fact]
        public void CompareVersionsOrdersDottedBuildsNumerically()
        {
            Assert.True(UpdateHelpers.CompareVersions("51.10", "51.9") > 0);
            Assert.True(UpdateHelpers.CompareVersions("51.9", "51.10") < 0);
            Assert.Equal(0, UpdateHelpers.CompareVersions("51.9", "build-51.9"));
        }

        [Fact]
        public void SelectLatestReleaseIgnoresApiOrder()
        {
            List<GitHubRelease> releases =
            [
                Release("build-51.9"),
                Release("build-51.8"),
                Release("build-51.10"),
                Release("build-51.1")
            ];

            GitHubRelease? latest = UpdateHelpers.SelectLatestRelease(releases);

            Assert.NotNull(latest);
            Assert.Equal("build-51.10", latest.TagName);
        }

        [Fact]
        public void SelectLatestReleaseHandlesEmptyOrUnversionedInput()
        {
            Assert.Null(UpdateHelpers.SelectLatestRelease(null));
            Assert.Null(UpdateHelpers.SelectLatestRelease([]));

            GitHubRelease unversioned = Release(string.Empty);
            Assert.Same(unversioned, UpdateHelpers.SelectLatestRelease([unversioned]));

            // A versioned release always wins over an unversioned one, even if the unversioned
            // release happens to be returned first by the API.
            Assert.Equal("build-51.10", UpdateHelpers.SelectLatestRelease([unversioned, Release("build-51.10")])!.TagName);
        }

        [Fact]
        public void OrderByNewestRanksBuildsAndKeepsAllEntries()
        {
            List<GitHubRelease> releases =
            [
                Release("build-51.9"),
                Release("build-51.2"),
                Release("build-51.10"),
                Release("build-50.9"),
                Release("build-51.1")
            ];

            List<string?> ordered = [.. UpdateHelpers.OrderByNewest(releases).Select(r => r.TagName)];

            Assert.Equal(
                ["build-51.10", "build-51.9", "build-51.2", "build-51.1", "build-50.9"],
                ordered);
        }

        private static GitHubRelease Release(string tagName) => new() { TagName = tagName };
    }
}
