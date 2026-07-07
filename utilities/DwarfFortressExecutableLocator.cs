namespace ModHearth.Utilities;

internal static class DwarfFortressExecutableLocator
{
    public static bool TryResolvePath(string? dfFolderPath, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(dfFolderPath))
            return false;

        if (OperatingSystem.IsWindows())
        {
            foreach (string exe in new[] { "df.exe", "Dwarf Fortress.exe" })
            {
                string candidate = Path.Combine(dfFolderPath, exe);
                if (File.Exists(candidate))
                {
                    executablePath = candidate;
                    return true;
                }
            }
            return false;
        }

        if (OperatingSystem.IsLinux())
        {
            // 'df' is a wrapper script (sets LD_LIBRARY_PATH, execs the real binary)
            // on classic Bay12 tarball installs; some Steam native builds ship only
            // the raw 'dwarfort' binary with no wrapper.
            foreach (string exe in new[] { "df", "dwarfort" })
            {
                string candidate = Path.Combine(dfFolderPath, exe);
                if (File.Exists(candidate))
                {
                    executablePath = candidate;
                    return true;
                }
            }
            return false;
        }

        if (OperatingSystem.IsMacOS())
        {
            string appBundle = Path.Combine(dfFolderPath, "Dwarf Fortress.app", "Contents", "MacOS", "Dwarf Fortress");
            if (File.Exists(appBundle))
            {
                executablePath = appBundle;
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