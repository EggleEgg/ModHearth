using System.Globalization;
using System.Text.RegularExpressions;

namespace ModHearth.Utils.Steam
{
    /// <summary>
    /// Parses progress information shared by SteamCMD downloads and self-updates.
    /// </summary>
    internal static partial class SteamCmdOutputParser
    {
        /// <summary>
        /// Reads the stage-local percentage from a SteamCMD progress line such as
        /// <c>Update state (0x61) downloading, progress: 85.86 (120 / 140)</c>.
        /// </summary>
        public static bool TryParseProgressPercent(string? line, out double percent)
        {
            percent = 0;
            if (string.IsNullOrEmpty(line) || !line.Contains("progress:", StringComparison.OrdinalIgnoreCase))
                return false;

            Match match = ProgressRegex().Match(line);
            return match.Success
                && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
        }

        /// <summary>
        /// Splits a line from SteamCMD's bootstrap updater, which prefixes stages with a percentage
        /// or an indeterminate marker: <c>[ 42%] Downloading update (x of y KB)...</c> or
        /// <c>[----] Extracting package...</c>. <paramref name="percent"/> is null for markers.
        /// </summary>
        public static bool TryParseBracketLine(string? line, out double? percent, out string message)
        {
            percent = null;
            message = string.Empty;
            if (string.IsNullOrEmpty(line))
                return false;

            Match match = BracketLineRegex().Match(line);
            if (!match.Success)
                return false;

            if (match.Groups[1].Success
                && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                percent = parsed;
            }

            message = match.Groups[2].Value.Trim();
            return true;
        }

        [GeneratedRegex(@"progress:\s+([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex ProgressRegex();

        [GeneratedRegex(@"^\s*\[\s*(?:([0-9]{1,3}(?:\.[0-9]+)?)%|----)\]\s*(.*)$", RegexOptions.Compiled)]
        private static partial Regex BracketLineRegex();
    }
}
