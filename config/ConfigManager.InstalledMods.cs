using ModHearth.Utils.IO;

/// <summary>
/// Actually handles the logic from models\ModHearthConfig
/// </summary>
namespace ModHearth.Config
{
    public static partial class ConfigManager
    {
        public static string? TryFindInstalledModsPath()
        {
            if (!string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                string portableCandidate = Path.Combine(Config.DFFolderPath, "data", installedMods);
                if (IsDwarfFortressPortable() || Directory.Exists(portableCandidate))
                {
                    string? resolvedPortable = ResolveExistingDirectoryPath(portableCandidate);
                    if (!string.IsNullOrWhiteSpace(resolvedPortable))
                        return resolvedPortable;
                }
            }

            // fallback to standard AppData
            foreach (string candidate in GetInstalledModsPathCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string? resolved = ResolveExistingDirectoryPath(candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }

            if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                string nativeLinuxCandidate = Path.Combine(Config.DFFolderPath, "data", installedMods);
                string? resolvedNativeLinux = ResolveExistingDirectoryPath(nativeLinuxCandidate);
                if (!string.IsNullOrWhiteSpace(resolvedNativeLinux))
                    return resolvedNativeLinux;
            }

            foreach (string candidate in GetLinuxProtonInstalledModsPathCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string? resolved = ResolveExistingDirectoryPath(candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }

            return null;
        }

        public static bool IsDwarfFortressPortable()
        {
            if (string.IsNullOrWhiteSpace(Config.DFFolderPath) || !Directory.Exists(Config.DFFolderPath))
                return false;

            string prefsPath = Path.Combine(Config.DFFolderPath, "prefs");

            return Directory.Exists(Path.Combine(prefsPath, "portable")) ||
                   File.Exists(Path.Combine(prefsPath, "portable")) ||
                   File.Exists(Path.Combine(prefsPath, "portable.txt"));
        }

        private static IEnumerable<string> GetInstalledModsPathCandidates()
        {
            foreach (string basePath in GetAppDataBasePaths())
            {
                yield return Path.Combine(basePath, dfString, "data", installedMods);
                yield return Path.Combine(basePath, "Bay 12 Games", dfString, "data", installedMods);
            }
        }

        private static IEnumerable<string> GetLinuxProtonInstalledModsPathCandidates()
        {
            if (!OperatingSystem.IsLinux())
                yield break;

            foreach (string libraryRoot in EnumerateSteamLibraryRoots())
            {
                if (string.IsNullOrWhiteSpace(libraryRoot))
                    continue;

                string compatRoot = Path.Combine(libraryRoot, steamApps, "compatdata", DwarfFortressSteamAppId, "pfx",
                    "drive_c", "users", "steamuser", "AppData");

                yield return Path.Combine(compatRoot, "Local", dfString, "data", installedMods);
                yield return Path.Combine(compatRoot, "Local", "Bay 12 Games", dfString, "data", installedMods);
                yield return Path.Combine(compatRoot, "Roaming", dfString, "data", installedMods);
                yield return Path.Combine(compatRoot, "Roaming", "Bay 12 Games", dfString, "data", installedMods);
            }
        }

        private static HashSet<string> GetAppDataBasePaths()
        {
            HashSet<string> bases = new(
                PathHelper.Comparer);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
                _ = bases.Add(appData);

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                _ = bases.Add(localAppData);

            return bases;
        }

        public static string GetDefaultInstalledModsPath()
        {
            string? resolved = TryFindInstalledModsPath();
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            // If auto-discovery didn't find an existing folder, determine the structural fallback
            if (IsDwarfFortressPortable() && !string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                return Path.Combine(Config.DFFolderPath, "data", installedMods);
            }

            foreach (string candidate in GetInstalledModsPathCandidates().Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return string.Empty;
        }
    }
}
