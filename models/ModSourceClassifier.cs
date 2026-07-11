namespace ModHearth;

public static class ModSourceClassifier
{
    public static (bool IsVanilla, bool IsLocal, bool IsSteam) Classify(
    ModReference modref,
    string? modsFolderPath,
    string? vanillaFolderPath)
    {
        if (modref == null)
            return (false, false, false);

        string path = modref.path?.Trim() ?? string.Empty;
        bool isVanilla = IsPathUnderRootOrEqual(path, vanillaFolderPath);

        bool isLocal = !isVanilla && IsPathUnderRoot(path, modsFolderPath);

        bool hasSteamId = !string.IsNullOrWhiteSpace(modref.steamID) &&
                          long.TryParse(modref.steamID, out _);
        bool steamPathHint = path.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             path.IndexOf("workshop", StringComparison.OrdinalIgnoreCase) >= 0;

        bool isSteamShadowCopy = isLocal && ConfigManager.TryGetSteamShadowCopyWorkshopId(path, out _);
        if (isSteamShadowCopy)
            isLocal = false;

        bool isSteam = !isVanilla && (isSteamShadowCopy || (!isLocal && (hasSteamId || steamPathHint)));
        return (isVanilla, isLocal, isSteam);
    }

    public static (bool IsVanilla, bool IsLocal, bool IsSteam) Classify(ModReference modref, string? modsFolderPath)
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
}
