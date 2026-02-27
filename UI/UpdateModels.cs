using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModHearth.UI;

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
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
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

        return tag.Substring(prefix.Length);
    }

    public static string GetReleaseTitle(GitHubRelease release, int index)
    {
        if (!string.IsNullOrWhiteSpace(release.Name))
            return release.Name!;

        if (!string.IsNullOrWhiteSpace(release.TagName))
            return release.TagName!;

        return $"Build {index + 1}";
    }

    public static string GetReleaseSubtitle(GitHubRelease release, string currentBuild)
    {
        string date = release.PublishedAt?.LocalDateTime.ToString("yyyy-MM-dd") ?? "unknown date";
        string? buildNumber = TryGetBuildNumber(release);
        string buildLabel = string.IsNullOrWhiteSpace(buildNumber) ? "unknown build" : $"build-{buildNumber}";

        bool isCurrent = !string.IsNullOrWhiteSpace(buildNumber) &&
                         string.Equals(buildNumber, currentBuild, StringComparison.OrdinalIgnoreCase);

        return isCurrent
            ? $"{buildLabel} · {date} (current)"
            : $"{buildLabel} · {date}";
    }
}
