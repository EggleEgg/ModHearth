using ModHearth.Utilities;

namespace ModHearth.Models
{
    public static class ModSourceClassifier
    {
        public static (bool IsVanilla, bool IsLocal, bool IsSteam, bool IsSteamLocal) Classify(
            ModReference modref,
            string? modsFolderPath,
            string? vanillaFolderPath)
        {
            if (modref == null)
                return (false, false, false, false);

            string path = modref.Path?.Trim() ?? string.Empty;

            modref.IsIgnored = IsPathIgnored(path);
            if (modref.IsIgnored)
            {
                return (false, false, false, false);
            }

            bool isVanilla = PathHelper.IsUnderRoot(path, vanillaFolderPath, includeRoot: true);
            if (isVanilla)
                return (true, false, false, false);

            bool isLocalPath = PathHelper.IsUnderRoot(path, modsFolderPath);
            bool isSteamShadowCopy = isLocalPath && ConfigManager.IsLikelySteamShadowCopy(path, modref.SteamID, out _);

            bool hasSteamId = ConfigManager.TryParsePositiveSteamId(modref.SteamID, out _);

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

        public static (bool IsVanilla, bool IsLocal, bool IsSteam, bool IsSteamLocal) Classify(
            ModReference modref,
            ModSourceContext sourceContext)
            => Classify(modref, sourceContext.ModsFolderPath, sourceContext.VanillaFolderPath);

        public static (bool IsVanilla, bool IsLocal, bool IsSteam, bool IsSteamLocal) Classify(ModReference modref, string? modsFolderPath) => Classify(modref, modsFolderPath, null);

        private static bool IsPathIgnored(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "installed_mods", StringComparison.OrdinalIgnoreCase));
        }
    }
}
