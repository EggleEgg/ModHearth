using ModHearth.UI.Update;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Main builds use plain integer versions ("51") and bleeding-edge builds use dotted versions
    /// ("51.10"). These tests keep the update checker ranking builds numerically instead of trusting
    /// GitHub's API order, and keep the two channels from cross-comparing.
    /// </summary>
    public class UpdateHelpersTests
    {
        [Theory]
        [InlineData("51", UpdateChannel.Main)]
        [InlineData("build-51", UpdateChannel.Main)]
        [InlineData("v51", UpdateChannel.Main)]
        [InlineData("51.1", UpdateChannel.BleedingEdge)]
        [InlineData("build-51.10", UpdateChannel.BleedingEdge)]
        [InlineData("51.10.1", UpdateChannel.BleedingEdge)]
        [InlineData("none", UpdateChannel.BleedingEdge)]
        [InlineData("local-dev", UpdateChannel.BleedingEdge)]
        [InlineData("", UpdateChannel.BleedingEdge)]
        public void GetBuildChannelUsesVersionShape(string version, UpdateChannel expected)
            => Assert.Equal(expected, UpdateHelpers.GetBuildChannel(version));

        [Fact]
        public void SelectLatestReleaseFiltersToRequestedChannel()
        {
            List<GitHubRelease> releases =
            [
                Release("build-51"),
                Release("build-51.9"),
                Release("build-51.10")
            ];

            Assert.Equal("build-51", UpdateHelpers.SelectLatestRelease(releases, UpdateChannel.Main)!.TagName);
            Assert.Equal("build-51.10", UpdateHelpers.SelectLatestRelease(releases, UpdateChannel.BleedingEdge)!.TagName);
            Assert.Null(UpdateHelpers.SelectLatestRelease([Release("build-51")], UpdateChannel.BleedingEdge));
        }

        [Theory]
        [InlineData("51", "build-51", false)]
        [InlineData("51", "build-52", true)]
        [InlineData("51", "build-52.1", false)]
        [InlineData("51.1", "build-51.2", true)]
        [InlineData("51.2", "build-51.2", false)]
        [InlineData("51.2", "build-52", false)]
        public void EvaluateUpdateAvailabilityOnlyComparesTheCurrentChannel(
            string current,
            string tagName,
            bool expected)
        {
            UpdateCheckResult result = UpdateHelpers.EvaluateUpdateAvailability([Release(tagName)], current, ignoredVersion: null);

            Assert.Equal(expected, result.IsAvailable);
        }

        [Fact]
        public void EvaluateUpdateAvailabilityHonorsIgnoredVersion()
        {
            UpdateCheckResult result = UpdateHelpers.EvaluateUpdateAvailability([Release("build-52")], "51", "52");

            Assert.False(result.IsAvailable);
            Assert.Equal("52", result.LatestVersion);
        }

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
