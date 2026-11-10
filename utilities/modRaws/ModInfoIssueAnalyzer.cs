using System.Globalization;
using System.Text.RegularExpressions;
using ModHearth.Models;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.ModRaws
{
    /// <summary>
    /// Statically validates a mod's <c>info.txt</c> the way Dwarf Fortress does while loading it.
    ///
    /// DF's info parser treats every <c>[</c> as the start of a tag name, so an unescaped bracket in
    /// a description turns the following text into an "unrecognized mod info token". The same parser
    /// requires <c>NUMERIC_VERSION</c> to be an integer, rejects an <c>EARLIEST_COMPATIBLE_NUMERIC_VERSION</c>
    /// newer than the mod's own version, and strips characters that cannot appear in a file name from
    /// the mod identifier. These are the same three error-log categories the runtime analyzer
    /// attributes to mods, so the severity mapping stays in one place.
    /// </summary>
    internal static partial class ModInfoIssueAnalyzer
    {
        /// <summary>
        /// Tags DF recognizes in <c>info.txt</c>. The list comes from the official format plus the
        /// fallback aliases ModHearth accepts when a mod omits the canonical tag. Anything else is
        /// reported as an unrecognized token instead of being guessed at.
        /// </summary>
        private static readonly HashSet<string> RecognizedInfoTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "ID",
            "NUMERIC_VERSION",
            "DISPLAYED_VERSION",
            "EARLIEST_COMPATIBLE_NUMERIC_VERSION",
            "EARLIEST_COMPATIBLE_DISPLAYED_VERSION",
            "NAME",
            "DESCRIPTION",
            "AUTHOR",
            "REQUIRES_ID_BEFORE_ME",
            "REQUIRES_ID_AFTER_ME",
            "REQUIRES_ID",
            "CONFLICTS_WITH_ID",
            "STEAM_TITLE",
            "STEAM_DESCRIPTION",
            "STEAM_TAG",
            "STEAM_FILE_ID",
            "STEAM_CHANGELOG",
            "STEAM_METADATA",
            "STEAM_KEY_VALUE_TAG",

            // Fallback aliases ModHearth reads when the canonical tags are absent. DF accepts these
            // in practice and no installed mod is reported for using them.
            "VERSION",
            "EARLIEST_COMPATIBLE_VERSION"
        };

        // Dwarf Fortress runs on Windows and removes these characters from the identifier before
        // using it as a folder name. Keeping the list explicit makes the check deterministic on
        // Linux too, where Path.GetInvalidFileNameChars() only returns '/' and '\0'.
        private static readonly char[] InvalidIdentifierCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

        /// <summary>
        /// Reads and analyzes <paramref name="infoPath"/>. Missing or unreadable files produce no
        /// issues: DF's own errors for those cases are not metadata-token problems.
        /// </summary>
        public static List<ModInfoIssue> AnalyzeFile(string? infoPath)
        {
            if (string.IsNullOrWhiteSpace(infoPath))
                return [];

            try
            {
                return Analyze(File.ReadAllText(infoPath));
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Failed to read mod info file '{infoPath}' for static issue detection: {ex.Message}");
                return [];
            }
        }

        public static List<ModInfoIssue> Analyze(string? infoText)
        {
            List<ModInfoIssue> issues = [];
            if (string.IsNullOrWhiteSpace(infoText))
                return issues;

            HashSet<string> reportedTokens = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in InfoTokenRegex().Matches(infoText))
            {
                string token = match.Groups["token"].Value.Trim();
                if (token.Length == 0 || RecognizedInfoTags.Contains(token))
                    continue;

                string normalized = token.ToLowerInvariant();
                if (!reportedTokens.Add(normalized))
                    continue;

                issues.Add(new ModInfoIssue(
                    ModInfoIssueKind.UnrecognizedToken,
                    normalized));
            }

            string? numericVersion = TryGetTagValue(infoText, "NUMERIC_VERSION");
            if (!string.IsNullOrEmpty(numericVersion) && !IntegerVersionRegex().IsMatch(numericVersion))
            {
                issues.Add(new ModInfoIssue(
                    ModInfoIssueKind.InvalidVersionMetadata,
                    $"NUMERIC_VERSION is '{numericVersion}'; it must be a whole number (it should not have a decimal point)."));
            }

            string? earliestCompatibleVersion = TryGetTagValue(infoText, "EARLIEST_COMPATIBLE_NUMERIC_VERSION");
            if (!string.IsNullOrEmpty(numericVersion)
                && !string.IsNullOrEmpty(earliestCompatibleVersion)
                && decimal.TryParse(numericVersion, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedVersion)
                && decimal.TryParse(earliestCompatibleVersion, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedEarliest)
                && parsedEarliest > parsedVersion)
            {
                issues.Add(new ModInfoIssue(
                    ModInfoIssueKind.InvalidVersionMetadata,
                    $"EARLIEST_COMPATIBLE_NUMERIC_VERSION '{earliestCompatibleVersion}' must be less than or equal to NUMERIC_VERSION '{numericVersion}'."));
            }

            string? id = TryGetTagValue(infoText, "ID");
            if (!string.IsNullOrEmpty(id) && id.IndexOfAny(InvalidIdentifierCharacters) >= 0)
            {
                issues.Add(new ModInfoIssue(
                    ModInfoIssueKind.InvalidIdentifier,
                    $"Mod identifier '{id}' has characters that can't be used in a file name; DF loads it without those characters."));
            }

            return issues;
        }

        /// <summary>
        /// Returns the trimmed value of the first <c>[TAG:value]</c> occurrence, or null when the
        /// tag is absent. Values used by the checks above never contain nested brackets.
        /// </summary>
        private static string? TryGetTagValue(string infoText, string tag)
        {
            foreach (Match match in TagValueRegex().Matches(infoText))
            {
                if (!string.Equals(match.Groups["tag"].Value, tag, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = match.Groups["value"].Value.Trim();
                return value.Length == 0 ? null : value;
            }

            return null;
        }

        // DF reads a token from '[' up to the first ':' or ']'. Consuming the delimiter is important:
        // an unescaped '[' inside a description must be picked up as its own token even though the
        // surrounding tag's value continues afterwards.
        [GeneratedRegex(@"\[(?<token>[^\]\r\n:]*)[:\]]", RegexOptions.Compiled)]
        private static partial Regex InfoTokenRegex();

        [GeneratedRegex(@"\[(?<tag>[A-Za-z0-9_]+):(?<value>[^\]\r\n]*)\]", RegexOptions.Compiled)]
        private static partial Regex TagValueRegex();

        [GeneratedRegex(@"^\d+$", RegexOptions.Compiled)]
        private static partial Regex IntegerVersionRegex();
    }
}
