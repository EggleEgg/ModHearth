using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.Workshop
{
    public static partial class WorkshopUrlResolver
    {

        [SuppressMessage("Style", "IDE0300:Use collection expression for array")] //simplified collection fails wsl builds (for some reason)
        private static readonly char[] separator = new char[] { '\r', '\n', ' ', '\t', ',', '|', ';' };

        // Improved regex to handle various Steam Workshop URL formats:
        // - https://steamcommunity.com/sharedfiles/filedetails/?id=3445635304
        // - https://steamcommunity.com/workshop/filedetails/?id=3445635304
        // - steam://url/CommunityFilePage/3445635304
        // - https://steamcommunity.com/sharedfiles/filedetails/changelog/3445635304

        private static IEnumerable<(ulong Id, string Token)> ExtractEntries(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                yield break;

            string[] lines = input.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Try plain ID
                if (PlainIdRegex().IsMatch(trimmed))
                {
                    if (ulong.TryParse(trimmed, out ulong id))
                    {
                        yield return (id, trimmed);
                    }
                    continue;
                }

                // Try URL matching
                Match match = WorkshopIdRegex().Match(trimmed);
                if (match.Success && ulong.TryParse(match.Groups[1].Value, out ulong idout))
                    yield return (idout, trimmed);
            }
        }

        public static List<ulong> ParseUrls(string input)
        {
            Loggers.Workshop.Log($"WorkshopUrlResolver: Parsing input: {input}");
            HashSet<ulong> results = [];

            foreach ((ulong id, string? token) in ExtractEntries(input))
            {
                if (results.Add(id))
                {
                    if (PlainIdRegex().IsMatch(token))
                        Loggers.Workshop.Log($"WorkshopUrlResolver: Found plain ID: {id}");
                    else
                        Loggers.Workshop.Log($"WorkshopUrlResolver: Found ID from URL: {id} (Source: {token})");
                }
            }

            Loggers.Workshop.Log($"WorkshopUrlResolver: Total unique IDs found: {results.Count}");
            return [.. results];
        }

        public static string FilterUrls(string input)
        {
            List<string> results = [];
            HashSet<ulong> seen = [];

            foreach ((ulong id, string? token) in ExtractEntries(input))
            {
                if (seen.Add(id))
                {
                    results.Add(token);
                }
            }

            return string.Join(Environment.NewLine, results);
        }

        [GeneratedRegex(@"^(?:https?://)?(?:www\.)?(?:steamcommunity\.com/(?:sharedfiles|workshop)/filedetails/(?:changelog/)?(?:\?(?:[^&\s]+&)*id=|/)?|steam://url/CommunityFilePage/)(\d{7,20})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex WorkshopIdRegex();
        [GeneratedRegex(@"^\d{7,20}$", RegexOptions.Compiled)]
        private static partial Regex PlainIdRegex();
    }
}
