namespace ModHearth.Utils.DwarfFortress
{
    internal static class DwarfFortressExecutableLocator
    {
        /// <summary>
        /// Executable paths to probe for the current platform, in order of preference. Exposed so
        /// tests and diagnostics can create or inspect the same layout the locator expects.
        /// </summary>
        public static IReadOnlyList<string> GetCandidatePaths(string? dfFolderPath)
        {
            if (string.IsNullOrWhiteSpace(dfFolderPath))
                return [];

            if (OperatingSystem.IsWindows())
            {
                // Classic Bay12 installs use 'Dwarf Fortress.exe'; some packagings ship 'df.exe'.
                return
                [
                    Path.Combine(dfFolderPath, "df.exe"),
                    Path.Combine(dfFolderPath, "Dwarf Fortress.exe")
                ];
            }

            if (OperatingSystem.IsLinux())
            {
                // 'df' is a wrapper script (sets LD_LIBRARY_PATH, execs the real binary)
                // on classic Bay12 tarball installs; some Steam native builds ship only
                // the raw 'dwarfort' binary with no wrapper.
                return
                [
                    Path.Combine(dfFolderPath, "df"),
                    Path.Combine(dfFolderPath, "dwarfort")
                ];
            }

            if (OperatingSystem.IsMacOS())
            {
                return
                [
                    Path.Combine(dfFolderPath, "Dwarf Fortress.app", "Contents", "MacOS", "Dwarf Fortress")
                ];
            }

            return [];
        }

        public static bool TryResolvePath(string? dfFolderPath, out string executablePath)
        {
            executablePath = string.Empty;
            if (string.IsNullOrWhiteSpace(dfFolderPath))
                return false;

            foreach (string candidate in GetCandidatePaths(dfFolderPath))
            {
                if (File.Exists(candidate))
                {
                    executablePath = candidate;
                    return true;
                }
            }

            return false;
        }

        public static string? TryResolveBundledLibraryPath(string? dfFolderPath)
        {
            if (string.IsNullOrWhiteSpace(dfFolderPath) || !OperatingSystem.IsLinux())
                return null;

            foreach (string candidate in new[] { "libs", "lib", "lib64" })
            {
                string path = Path.Combine(dfFolderPath, candidate);
                if (Directory.Exists(path))
                    return path;
            }

            // Some Steam Linux packagings (no 'df' wrapper) ship their bundled .so files
            // directly in the DF root rather than a dedicated lib folder.
            if (Directory.EnumerateFiles(dfFolderPath, "*.so*").Any())
                return dfFolderPath;

            return null;
        }
    }
}
