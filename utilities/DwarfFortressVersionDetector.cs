using System.Text.RegularExpressions;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities
{
    /// <summary>
    /// Versions read from an on-disk Dwarf Fortress / DFHack installation. Either value is null
    /// when that installation could not be found or does not expose a recognizable version.
    /// </summary>
    internal readonly record struct DwarfFortressVersionInfo(string? DwarfFortressVersion, string? DFHackVersion)
    {
        public static DwarfFortressVersionInfo Empty => default;
    }

    /// <summary>
    /// Reads DF and DFHack versions from the files shipped with their installations. Neither
    /// executable embeds a usable <see cref="System.Diagnostics.FileVersionInfo"/>, so this walks the
    /// release metadata instead (and works whether or not the game is currently running).
    ///
    /// DF version sources, in priority order:
    ///   - release notes.txt ("Release notes for 53.16 (...)")
    ///   - file changes.txt ("Auxiliary file changes for 53.16:")
    ///   - the DF version prefix of a detected DFHack release ("53.16-r3" -> "53.16")
    ///   - data/vanilla/*/info.txt ("[DISPLAYED_VERSION:53.16]")
    ///
    /// DFHack version sources, in priority order:
    ///   - hack/news.rst ("DFHack 53.16-r3" heading; "DFHack future" is skipped)
    ///   - hack/docs/docs/NEWS.txt / NEWS.html
    /// </summary>
    internal static partial class DwarfFortressVersionDetector
    {
        private const string VanillaInfoRelativePath = "data/vanilla";

        public static DwarfFortressVersionInfo Detect(string? dfFolderPath, string? dfHackFolderPath)
        {
            try
            {
                string? dfHackVersion = DetectDFHackVersion(dfFolderPath, dfHackFolderPath);
                string? dfVersion = DetectDwarfFortressVersionFromDocs(dfFolderPath)
                    ?? DeriveDwarfFortressVersionFromDFHackVersion(dfHackVersion)
                    ?? DetectDwarfFortressVersionFromVanillaInfo(dfFolderPath);

                Loggers.Diag.Dev(
                    $"Version detection: DF='{dfVersion ?? "unknown"}', DFHack='{dfHackVersion ?? "unknown"}' " +
                    $"(DF folder: '{dfFolderPath ?? "unset"}', DFHack folder: '{dfHackFolderPath ?? "unset"}')");

                return new DwarfFortressVersionInfo(dfVersion, dfHackVersion);
            }
            catch (Exception ex)
            {
                Loggers.Diag.LogWarning($"Version detection failed: {ex.Message}");
                return DwarfFortressVersionInfo.Empty;
            }
        }

        private static string? DetectDwarfFortressVersionFromDocs(string? dfFolderPath)
        {
            if (string.IsNullOrWhiteSpace(dfFolderPath))
                return null;

            string releaseNotesPath = Path.Combine(dfFolderPath, "release notes.txt");
            if (TryReadText(releaseNotesPath) is { } releaseNotes &&
                ParseDwarfFortressVersionFromReleaseNotes(releaseNotes) is { } fromReleaseNotes)
            {
                return fromReleaseNotes;
            }

            string fileChangesPath = Path.Combine(dfFolderPath, "file changes.txt");
            if (TryReadText(fileChangesPath) is { } fileChanges &&
                ParseDwarfFortressVersionFromFileChanges(fileChanges) is { } fromFileChanges)
            {
                return fromFileChanges;
            }

            return null;
        }

        private static string? DetectDwarfFortressVersionFromVanillaInfo(string? dfFolderPath)
        {
            if (string.IsNullOrWhiteSpace(dfFolderPath))
                return null;

            // Premium ships the game data raws in data/vanilla and stamps each core mod with the
            // version it was built for. These three track the game release itself; other vanilla
            // folders can intentionally lag behind, so they are not used.
            foreach (string vanillaMod in new[] { "vanilla_creatures", "vanilla_creatures_extinct", "vanilla_procedural" })
            {
                string infoPath = Path.Combine(dfFolderPath, VanillaInfoRelativePath, vanillaMod, "info.txt");
                if (TryReadText(infoPath) is { } info &&
                    ParseDwarfFortressVersionFromVanillaInfo(info) is { } fromVanillaInfo)
                {
                    return fromVanillaInfo;
                }
            }

            return null;
        }

        private static string? DetectDFHackVersion(string? dfFolderPath, string? dfHackFolderPath)
        {
            foreach (string hackFolder in EnumerateDFHackFolders(dfFolderPath, dfHackFolderPath))
            {
                string newsPath = Path.Combine(hackFolder, "news.rst");
                if (TryReadText(newsPath) is { } news &&
                    ParseDFHackVersionFromNews(news) is { } fromNews)
                {
                    return fromNews;
                }

                foreach (string docsFile in new[] { "NEWS.txt", "NEWS.html" })
                {
                    string docsPath = Path.Combine(hackFolder, "docs", "docs", docsFile);
                    if (TryReadText(docsPath) is { } docs &&
                        ParseDFHackVersionFromDocs(docs) is { } fromDocs)
                    {
                        return fromDocs;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Yields every plausible "hack" directory. The configured DFHack folder can point at the
        /// DFHack root, its hack subfolder, or (for legacy installs) the DF root itself.
        /// </summary>
        private static IEnumerable<string> EnumerateDFHackFolders(string? dfFolderPath, string? dfHackFolderPath)
        {
            HashSet<string> seen = new(PathHelper.Comparer);

            foreach (string? root in new[]
            {
                dfHackFolderPath,
                dfFolderPath,
                ParentOrNull(dfHackFolderPath),
                ParentOrNull(dfFolderPath),
            })
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                foreach (string candidate in new[] { root, Path.Combine(root, "hack") })
                {
                    if (Directory.Exists(candidate) && seen.Add(candidate))
                        yield return candidate;
                }
            }
        }

        private static string? ParentOrNull(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
            }
            catch (ArgumentException ex)
            {
                Loggers.Diag.Dev($"Ignoring invalid path '{path}': {ex.Message}");
                return null;
            }
        }

        private static string? TryReadText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                Loggers.Diag.LogWarning($"Could not read '{path}': {ex.Message}");
                return null;
            }
        }

        internal static string? ParseDwarfFortressVersionFromReleaseNotes(string text)
            => MatchVersion(ReleaseNotesVersionRegex(), text);

        internal static string? ParseDwarfFortressVersionFromFileChanges(string text)
            => MatchVersion(FileChangesVersionRegex(), text);

        internal static string? ParseDwarfFortressVersionFromVanillaInfo(string text)
            => MatchVersion(VanillaInfoVersionRegex(), text);

        internal static string? ParseDFHackVersionFromNews(string text)
            => MatchVersion(DFHackNewsVersionRegex(), text);

        internal static string? ParseDFHackVersionFromDocs(string text)
            => MatchVersion(DFHackDocsVersionRegex(), text);

        internal static string? DeriveDwarfFortressVersionFromDFHackVersion(string? dfHackVersion)
        {
            if (string.IsNullOrWhiteSpace(dfHackVersion))
                return null;

            Match match = DFHackVersionPrefixRegex().Match(dfHackVersion.Trim());
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string? MatchVersion(Regex regex, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            Match match = regex.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }

        [GeneratedRegex(@"Release notes for\s+v?(\d{1,3}(?:\.\d{1,3}){1,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex ReleaseNotesVersionRegex();

        [GeneratedRegex(@"Auxiliary file changes for\s+v?(\d{1,3}(?:\.\d{1,3}){1,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex FileChangesVersionRegex();

        [GeneratedRegex(@"\[DISPLAYED_VERSION:\s*(\d{1,3}(?:\.\d{1,3}){1,2})\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex VanillaInfoVersionRegex();

        // news.rst headings are exactly "DFHack <release>"; "DFHack future" does not match.
        [GeneratedRegex(@"^[ \t]*DFHack[ \t]+(\d{1,3}(?:\.\d{1,3}){1,2}-r\d{1,3})[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.Compiled)]
        private static partial Regex DFHackNewsVersionRegex();

        [GeneratedRegex(@"DFHack\s+(\d{1,3}(?:\.\d{1,3}){1,2}-r\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex DFHackDocsVersionRegex();

        [GeneratedRegex(@"^(\d{1,3}(?:\.\d{1,3}){1,2})-r\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex DFHackVersionPrefixRegex();
    }
}
