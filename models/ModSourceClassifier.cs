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

            // installed_mods is DF's disposable cache of enabled mods, not a real source folder.
            // Keep the ref usable (cache-only mods must stay visible in the UI) but flag it so
            // update tracking and delete/unsubscribe actions ignore it.
            modref.IsIgnored = ConfigManager.IsInstalledModsPath(path);
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
    }
}
