using ModHearth.Utilities;

namespace ModHearth.Models
{
    public static class ModSourceClassifier
    {
        public static (bool IsVanilla, bool IsLocal, bool IsSteam, bool IsSteamLocal, bool IsCacheOnly) Classify(
            ModReference modref,
            string? modsFolderPath,
            string? vanillaFolderPath)
        {
            if (modref == null)
                return (false, false, false, false, false);

            string path = modref.Path?.Trim() ?? string.Empty;

            // installed_mods is DF's disposable cache of enabled mods, not a real source folder.
            // Keep the ref usable (cache-only mods must stay visible in the UI) but flag it so
            // update tracking and delete/unsubscribe actions ignore it.
            modref.IsIgnored = ConfigManager.IsInstalledModsPath(path);
            if (modref.IsIgnored)
            {
                return (false, false, false, false, true);
            }

            bool isVanilla = PathHelper.IsUnderRoot(path, vanillaFolderPath, includeRoot: true);
            if (isVanilla)
                return (true, false, false, false, false);

            bool isLocalPath = PathHelper.IsUnderRoot(path, modsFolderPath);
            bool isSteamShadowCopy = isLocalPath && ConfigManager.IsLikelySteamShadowCopy(path, modref.SteamID, out _);

            bool hasSteamId = ConfigManager.TryParsePositiveSteamId(modref.SteamID, out _);

            bool isSteamLocal = isLocalPath && isSteamShadowCopy;

            if (isSteamLocal)
                return (false, false, false, true, false);

            if (isLocalPath)
                return (false, true, false, false, false);

            bool steamPathHint = path.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ||
                                 path.Contains("workshop", StringComparison.OrdinalIgnoreCase);

            bool isSteam = hasSteamId || steamPathHint;
            if (isSteam)
                return (false, false, true, false, false);

            return (false, false, false, false, false);
        }

        public static (bool IsVanilla, bool IsLocal, bool IsSteam, bool IsSteamLocal, bool IsCacheOnly) Classify(
            ModReference modref,
            ModSourceContext sourceContext)
            => Classify(modref, sourceContext.ModsFolderPath, sourceContext.VanillaFolderPath);

        public static (bool IsVanilla, bool IsLocal, bool IsSteam, bool IsSteamLocal, bool IsCacheOnly) Classify(ModReference modref, string? modsFolderPath) => Classify(modref, modsFolderPath, null);

        /// <summary>
        /// True when the reference lives in Dwarf Fortress's disposable installed_mods cache rather
        /// than in one of the real mod source folders.
        /// </summary>
        public static bool IsCacheOnly(ModReference modref) => modref != null && (modref.IsIgnored || ConfigManager.IsInstalledModsPath(modref.Path));
    }
}
