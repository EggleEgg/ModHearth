using System.Text.Json.Serialization;

namespace ModHearth.UI.Update
{
    /// <summary>
    /// Used for github release API deserialization
    /// </summary>
    public sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }

    public sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        // Used for sha verification
        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }

    internal static class UpdateHelpers
    {
        public static string? TryGetBuildNumber(GitHubRelease release)
        {
            string? tag = release.TagName;
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            const string prefix = "build-";
            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            return tag[prefix.Length..];
        }

        public static string CleanVersionString(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return string.Empty;

            string v = version.Trim();
            if (v.StartsWith("build-", StringComparison.OrdinalIgnoreCase))
                v = v["build-".Length..];
            else if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase) && v.Length > 1 && char.IsDigit(v[1]))
                v = v[1..];

            return v.Trim();
        }

        /// <summary>
        /// Build version of a release, falling back to the cleaned tag when it is not a "build-..." tag.
        /// </summary>
        public static string GetReleaseVersion(GitHubRelease release) =>
            TryGetBuildNumber(release) ?? CleanVersionString(release.TagName);

        /// <summary>
        /// Compares two build versions by their numeric parts, so "51.10" sorts after "51.9" even
        /// though ordinal text comparison would place it before. Returns a positive value when
        /// <paramref name="left"/> is newer, a negative value when it is older, and zero when equal.
        /// </summary>
        public static int CompareVersions(string? left, string? right)
        {
            string cleanLeft = CleanVersionString(left);
            string cleanRight = CleanVersionString(right);

            if (int.TryParse(cleanLeft, out int leftNumber) && int.TryParse(cleanRight, out int rightNumber))
                return leftNumber.CompareTo(rightNumber);

            if (Version.TryParse(cleanLeft, out Version? leftVersion) &&
                Version.TryParse(cleanRight, out Version? rightVersion))
            {
                return leftVersion.CompareTo(rightVersion);
            }

            return string.Compare(cleanLeft, cleanRight, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNewerVersion(string? fetched, string? current)
        {
            if (string.IsNullOrWhiteSpace(fetched))
                return false;

            if (string.IsNullOrWhiteSpace(current) ||
                string.Equals(current, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "local-dev", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "dev", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(CleanVersionString(fetched));
            }

            return CompareVersions(fetched, current) > 0;
        }

        /// <summary>
        /// Returns the release with the highest build version, regardless of the order the GitHub API
        /// returned it in (the API sorts by creation time, not by build number). Releases without a
        /// usable version are only used as a fallback when no versioned release exists.
        /// </summary>
        public static GitHubRelease? SelectLatestRelease(IEnumerable<GitHubRelease>? releases)
        {
            if (releases == null)
                return null;

            GitHubRelease? latest = null;
            string latestVersion = string.Empty;
            GitHubRelease? firstUnversioned = null;

            foreach (GitHubRelease release in releases)
            {
                string version = GetReleaseVersion(release);
                if (string.IsNullOrWhiteSpace(version))
                {
                    firstUnversioned ??= release;
                    continue;
                }

                if (latest == null || CompareVersions(version, latestVersion) > 0)
                {
                    latest = release;
                    latestVersion = version;
                }
            }

            return latest ?? firstUnversioned;
        }

        /// <summary>
        /// Orders releases newest-first by build version. LINQ's ordering is stable, so releases with
        /// equal versions keep the order the API returned them in.
        /// </summary>
        public static List<GitHubRelease> OrderByNewest(IEnumerable<GitHubRelease> releases) =>
            [.. releases.OrderByDescending(GetReleaseVersion, VersionComparer.Instance)];

        private sealed class VersionComparer : IComparer<string>
        {
            public static readonly VersionComparer Instance = new();

            public int Compare(string? x, string? y) => CompareVersions(x, y);
        }

        public static bool IsCurrentVersion(string? buildNumber, string? currentBuild)
        {
            if (string.IsNullOrWhiteSpace(buildNumber) || string.IsNullOrWhiteSpace(currentBuild))
                return false;

            return string.Equals(CleanVersionString(buildNumber), CleanVersionString(currentBuild), StringComparison.OrdinalIgnoreCase);
        }

        public static string GetReleaseTitle(GitHubRelease release, int index)
        {
            if (!string.IsNullOrWhiteSpace(release.Name))
                return release.Name;
            if (!string.IsNullOrWhiteSpace(release.TagName))
                return release.TagName;

            return $"Build {index + 1}";
        }

        public static string GetReleaseSubtitle(GitHubRelease release, string currentBuild)
        {
            string date = release.PublishedAt?.LocalDateTime.ToString("yyyy-MM-dd") ?? "unknown date";
            string? buildNumber = TryGetBuildNumber(release);
            string buildLabel = string.IsNullOrWhiteSpace(buildNumber) ? "unknown build" : $"build-{buildNumber}";

            bool isCurrent = !string.IsNullOrWhiteSpace(buildNumber) && IsCurrentVersion(buildNumber, currentBuild);

            return isCurrent ? $"{buildLabel} · {date} (current)" : $"{buildLabel} · {date}";
        }
    }
}
