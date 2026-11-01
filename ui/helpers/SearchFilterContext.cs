using System.Text.RegularExpressions;
using ModHearth.Models;

namespace ModHearth.UI.Helpers
{
    /// <summary>
    /// Immutable snapshot of a search filter, built once per filter pass.
    /// </summary>
    /// <remarks>
    /// Previously every item re-compiled the regex and re-split the color list. Hoisting that work into a
    /// single context per search keeps filtering allocation-free for everything except the matches themselves.
    /// </remarks>
    public sealed class SearchFilterContext
    {
        private readonly Regex? regex;
        private readonly HashSet<string>? colorNames;

        public SearchFilterContext(string? filter, SearchFilterMode mode)
        {
            Filter = filter?.Trim() ?? string.Empty;
            Mode = mode;
            HasFilter = !string.IsNullOrWhiteSpace(Filter);

            if (!HasFilter)
                return;

            switch (mode)
            {
                case SearchFilterMode.Regex:
                    regex = TryCompileRegex(Filter);
                    break;

                case SearchFilterMode.Color:
                    colorNames = new HashSet<string>(
                        Filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        StringComparer.OrdinalIgnoreCase);
                    break;
            }
        }

        public string Filter { get; }

        public SearchFilterMode Mode { get; }

        public bool HasFilter { get; }

        /// <summary>
        /// Matches <paramref name="target"/> against the regex. Invalid patterns match nothing, mirroring
        /// the previous per-item behavior.
        /// </summary>
        public bool MatchesRegex(string? target)
        {
            if (!HasFilter)
                return true;

            return regex != null && target != null && regex.IsMatch(target);
        }

        /// <summary>
        /// Matches a mod color against the comma-separated color names in the filter.
        /// </summary>
        public bool MatchesColor(ModColor color)
        {
            if (!HasFilter)
                return true;

            return colorNames == null || colorNames.Count == 0 || colorNames.Contains(color.ToString());
        }

        /// <summary>
        /// Case-insensitive substring match used by the text search modes.
        /// </summary>
        public bool Contains(string? candidate)
        {
            if (!HasFilter)
                return true;

            return !string.IsNullOrWhiteSpace(candidate)
                && candidate.Contains(Filter, StringComparison.OrdinalIgnoreCase);
        }

        private static Regex? TryCompileRegex(string pattern)
        {
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
