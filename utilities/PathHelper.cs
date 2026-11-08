namespace ModHearth.Utilities
{
    /// <summary>
    /// Cross-platform file-system path comparison. Windows paths are case-insensitive; Linux and
    /// macOS paths are treated as case-sensitive. Centralised here so path equality and containment
    /// checks can never drift between call sites.
    /// </summary>
    public static class PathHelper
    {
        public static StringComparison Comparison => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public static StringComparer Comparer => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        public static bool PathsEqual(string? a, string? b)
            => !string.IsNullOrWhiteSpace(a)
               && !string.IsNullOrWhiteSpace(b)
               && string.Equals(a, b, Comparison);

        /// <summary>
        /// Returns whether <paramref name="path"/> lies inside <paramref name="root"/>. When
        /// <paramref name="includeRoot"/> is true the root itself also counts as contained.
        /// Unresolvable paths are treated as not contained.
        /// </summary>
        public static bool IsUnderRoot(string? path, string? root, bool includeRoot = false)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
                return false;

            try
            {
                string fullPath = TrimTrailingSeparators(Path.GetFullPath(path));
                string fullRoot = TrimTrailingSeparators(Path.GetFullPath(root));

                if (includeRoot && string.Equals(fullPath, fullRoot, Comparison))
                    return true;

                return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, Comparison);
            }
            catch
            {
                // Invalid characters / unresolvable paths are simply not under the root.
                return false;
            }
        }

        private static string TrimTrailingSeparators(string path)
            => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>
        /// Finds <paramref name="fileName"/> inside <paramref name="directory"/>, falling back to a
        /// case-insensitive scan when the exact name is not present. Returns null when the directory
        /// or file is missing/unreadable.
        /// </summary>
        public static string? ResolveFile(string? directory, string fileName)
            => ResolveChild(directory, fileName, expectDirectory: false);

        /// <summary>
        /// Resolves a single child entry (<paramref name="name"/>) inside <paramref name="parent"/>,
        /// falling back to a case-insensitive scan when the exact name is not present. Set
        /// <paramref name="expectDirectory"/> when the child must be a folder rather than a file.
        /// Returns null when the parent is missing or nothing matches.
        /// </summary>
        public static string? ResolveChild(string? parent, string name, bool expectDirectory)
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                return null;
            if (!Directory.Exists(parent))
                return null;

            string exactPath = Path.Combine(parent, name);
            if (expectDirectory ? Directory.Exists(exactPath) : File.Exists(exactPath))
                return exactPath;

            try
            {
                IEnumerable<string> entries = expectDirectory
                    ? Directory.EnumerateDirectories(parent)
                    : Directory.EnumerateFiles(parent);
                return entries.FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }
    }
}
