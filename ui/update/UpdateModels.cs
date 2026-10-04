using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ModHearth.UI;

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

        return tag.Substring(prefix.Length);
    }

    public static string CleanVersionString(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        string v = version.Trim();
        if (v.StartsWith("build-", StringComparison.OrdinalIgnoreCase))
            v = v.Substring("build-".Length);
        else if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase) && v.Length > 1 && char.IsDigit(v[1]))
            v = v.Substring(1);

        return v.Trim();
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

        string cleanFetched = CleanVersionString(fetched);
        string cleanCurrent = CleanVersionString(current);

        if (int.TryParse(cleanFetched, out int fNum) && int.TryParse(cleanCurrent, out int cNum))
        {
            return fNum > cNum;
        }

        if (Version.TryParse(cleanFetched, out Version? fVer) && Version.TryParse(cleanCurrent, out Version? cVer))
        {
            return fVer > cVer;
        }

        return string.Compare(cleanFetched, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
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

/// <summary>
/// Lightweight GitHub raw-content helper used to fetch files such as community modsort_rules.json
/// </summary>
public static class GitHubFileClient
{
    private static readonly HttpClient Client = CreateClient();

    public static HttpClient Instance => Client;

    private static HttpClient CreateClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ModHearth/1.0");
        return client;
    }
}

/// <summary>
/// Parses common GitHub repository URLs and converts them to raw file URLs.
/// </summary>
public static partial class GitHubUrlParser
{
    private static readonly Regex RepoRegex = new(
        @"^https?://(?:www\.)?github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/?(?:$|(?:tree|blob)/(?<branch>[^/]+)(?:/(?<path>.*))?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string input, out string owner, out string repo, out string branch, out string filePath)
    {
        owner = string.Empty;
        repo = string.Empty;
        branch = "main";
        filePath = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        Match match = RepoRegex.Match(input.Trim());
        if (!match.Success)
            return false;

        owner = match.Groups["owner"].Value;
        repo = match.Groups["repo"].Value;
        if (match.Groups["branch"].Success)
            branch = match.Groups["branch"].Value;
        if (match.Groups["path"].Success)
            filePath = match.Groups["path"].Value.Trim('/');

        return true;
    }

    public static string? ToRawFileUrl(string input, string fileName = "modsort_rules.json")
    {
        if (!TryParse(input, out string owner, out string repo, out string branch, out string filePath))
            return null;

        string path = string.IsNullOrWhiteSpace(filePath) ? fileName : filePath;
        return $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";
    }
}
