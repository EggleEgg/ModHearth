using ModHearth.Utils.Formatting;
using ModHearth.Utils.IO;
using ModHearth.Utils.Logging;

/// <summary>
/// Actually handles the logic from models\ModHearthConfig
/// </summary>
namespace ModHearth.Config
{
    public static partial class ConfigManager
    {
        /// <summary>
        /// Use this over NormalizeFileSystemPath when symlinks are present.
        /// Rule of thumb on usage: when the code is about to put a path into a HashSet/dictionary key, compare two paths for equality, or make a security-relevant containment decision
        /// </summary>
        public static string ResolveCanonicalPath(string path)
        {
            string normalized = NormalizeFileSystemPath(path);
            if (string.IsNullOrWhiteSpace(normalized))
                return normalized;

            try
            {
                FileSystemInfo? target = null;

                if (Directory.Exists(normalized))
                {
                    target = Directory.ResolveLinkTarget(normalized, returnFinalTarget: true);
                }
                else if (File.Exists(normalized))
                {
                    target = File.ResolveLinkTarget(normalized, returnFinalTarget: true);
                }

                if (target != null)
                    return NormalizeFileSystemPath(target.FullName);

            }
            catch (Exception ex)
            {
                // Fall back to the normalized (possibly symlinked) path.
                Loggers.Diag.Dev($"Failed to resolve symlink target for '{normalized}': {ex.Message}");
            }

            return normalized;
        }

        public static string NormalizeFileSystemPath(string path)
        {
            string normalized = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            if (OperatingSystem.IsWindows())
                normalized = normalized.Replace('/', '\\');
            else
                normalized = normalized.Replace('\\', '/');

            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Failed to normalize path '{normalized}': {ex.Message}");
            }

            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string? ResolveExistingDirectoryPath(string path) => ResolveExistingPath(path, expectDirectory: true);

        public static string? ResolveExistingPath(string path, bool expectDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }

            if (expectDirectory)
            {
                if (Directory.Exists(fullPath))
                    return NormalizeFileSystemPath(fullPath);
            }
            else
            {
                if (File.Exists(fullPath))
                    return NormalizeFileSystemPath(fullPath);
            }

            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return null;

            string remainder = fullPath[root.Length..];
#pragma warning disable IDE0300 //simplified collection fails wsl builds (for some reason)
            string[] segments = remainder.Split(
                new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
#pragma warning restore IDE0300
            if (segments.Length == 0)
            {
                if (expectDirectory && Directory.Exists(root))
                    return NormalizeFileSystemPath(root);
                return null;
            }

            string current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                bool isLast = i == segments.Length - 1;
                bool expectDirectorySegment = !isLast || expectDirectory;
                string? next = ResolveChildPathSegment(current, segments[i], expectDirectorySegment);
                if (string.IsNullOrWhiteSpace(next))
                    return null;
                current = next;
            }

            if (expectDirectory)
                return Directory.Exists(current) ? NormalizeFileSystemPath(current) : null;
            return File.Exists(current) ? NormalizeFileSystemPath(current) : null;
        }

        private static string? ResolveChildPathSegment(string parent, string name, bool expectDirectory)
            => PathHelper.ResolveChild(parent, name, expectDirectory);

        public static string? ResolveInfoFilePath(string modDirectory)
        {
            string? resolvedDirectory = ResolveExistingDirectoryPath(modDirectory);
            if (string.IsNullOrWhiteSpace(resolvedDirectory))
                return null;

            return PathHelper.ResolveFile(resolvedDirectory, "info.txt");
        }
        public static string FormatPathListForLog(IEnumerable<string> paths, int maxItems = 24)
        {
            if (paths == null)
                return "(none)";

            List<string> list = [.. paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeFileSystemPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(PathHelper.Comparer)];

            return StringFormatter.FormatListWithMoreIndicator(list, maxItems);
        }
    }
}
