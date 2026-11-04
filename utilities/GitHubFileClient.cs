using System.Text.RegularExpressions;

namespace ModHearth.Utilities
{
    /// <summary>
    /// Lightweight GitHub raw-content helper used to fetch files such as community modsort_rules.json
    /// </summary>
    public static class GitHubFileClient
    {
        public static HttpClient Instance { get; } = CreateClient();

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
        private static readonly Regex RepoRegex = MyRegex();

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

        [GeneratedRegex(@"^https?://(?:www\.)?github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/?(?:$|(?:tree|blob)/(?<branch>[^/]+)(?:/(?<path>.*))?)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
        private static partial Regex MyRegex();
    }
}
