using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.Workshop
{
    public static class WorkshopUrlResolver
    {
        // Improved regex to handle various Steam Workshop URL formats:
        // - https://steamcommunity.com/sharedfiles/filedetails/?id=3445635304
        // - https://steamcommunity.com/workshop/filedetails/?id=3445635304
        // - steam://url/CommunityFilePage/3445635304
        // - https://steamcommunity.com/sharedfiles/filedetails/changelog/3445635304
        private static readonly Regex WorkshopIdRegex = new Regex(@"(?:id=|filepage/|sharedfiles/|filedetails/|changelog/)(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PlainIdRegex = new Regex(@"^\d+$", RegexOptions.Compiled);

        private static IEnumerable<(ulong Id, string Token)> ExtractEntries(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                yield break;

            var lines = input.Split(new[] { '\r', '\n', ' ', '\t', ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Try plain ID
                if (PlainIdRegex.IsMatch(trimmed))
                {
                    if (ulong.TryParse(trimmed, out ulong id))
                    {
                        yield return (id, trimmed);
                    }
                    continue;
                }

                // Try URL matching
                var match = WorkshopIdRegex.Match(trimmed);
                if (match.Success)
                {
                    if (ulong.TryParse(match.Groups[1].Value, out ulong idout))
                    {
                        yield return (idout, trimmed);
                    }
                }
            }
        }

        public static List<ulong> ParseUrls(string input)
        {
            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopUrlResolver: Parsing input: {input}");
            var results = new HashSet<ulong>();

            foreach (var (id, token) in ExtractEntries(input))
            {
                if (results.Add(id) && DevMode.IsEnabled)
                {
                    if (PlainIdRegex.IsMatch(token))
                        InfoLogger.LogRunDf($"WorkshopUrlResolver: Found plain ID: {id}");
                    else
                        InfoLogger.LogRunDf($"WorkshopUrlResolver: Found ID from URL: {id} (Source: {token})");
                }
            }

            if (DevMode.IsEnabled) InfoLogger.LogRunDf($"WorkshopUrlResolver: Total unique IDs found: {results.Count}");
            return new List<ulong>(results);
        }

        public static string FilterUrls(string input)
        {
            var results = new List<string>();
            var seen = new HashSet<ulong>();

            foreach (var (id, token) in ExtractEntries(input))
            {
                if (seen.Add(id))
                {
                    results.Add(token);
                }
            }

            return string.Join(Environment.NewLine, results);
        }
    }
}
