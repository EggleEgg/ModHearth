namespace ModHearth;

public static class ModSourceClassifier
{
    public static (bool IsVanilla, bool IsLocal, bool IsSteam, bool IsSteamLocal) Classify(
        ModReference modref,
        string? modsFolderPath,
        string? vanillaFolderPath)
    {
        if (modref == null)
            return (false, false, false, false);

        string path = modref.path?.Trim() ?? string.Empty;

        modref.IsIgnored = IsPathIgnored(path);
        if (modref.IsIgnored)
        {
            return (false, false, false, false);
        }

        bool isVanilla = IsPathUnderRootOrEqual(path, vanillaFolderPath);
        if (isVanilla)
            return (true, false, false, false);

        bool isLocalPath = IsPathUnderRoot(path, modsFolderPath);
        bool isSteamShadowCopy = isLocalPath && ConfigManager.IsLikelySteamShadowCopy(path, modref.steamID, out _);

        bool hasSteamId = ConfigManager.TryParsePositiveSteamId(modref.steamID, out _);

        bool isSteamLocal = isLocalPath && isSteamShadowCopy;

        if (isSteamLocal)
            return (false, false, false, true);

        if (isLocalPath)
            return (false, true, false, false);

        bool steamPathHint = path.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             path.IndexOf("workshop", StringComparison.OrdinalIgnoreCase) >= 0;

        bool isSteam = hasSteamId || steamPathHint;
        if (isSteam)
            return (false, false, true, false);

        return (false, false, false, false);
    }

    public static (bool IsVanilla, bool IsLocal, bool IsSteam, bool IsSteamLocal) Classify(ModReference modref, string? modsFolderPath)
        => Classify(modref, modsFolderPath, null);

    private static bool IsPathUnderRoot(string path, string? root)
        => IsPathUnderRootCore(path, root, includeRoot: false);

    private static bool IsPathUnderRootOrEqual(string path, string? root)
        => IsPathUnderRootCore(path, root, includeRoot: true);

    private static bool IsPathUnderRootCore(string path, string? root, bool includeRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;

        try
        {
            string fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (includeRoot && string.Equals(fullPath, fullRoot, comparison))
                return true;

            string prefix = fullRoot + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, comparison);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPathIgnored(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "installed_mods", StringComparison.OrdinalIgnoreCase));
    }
}
