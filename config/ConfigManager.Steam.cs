using ModHearth.Utils.IO;

/// <summary>
/// Actually handles the logic from models\ModHearthConfig
/// </summary>
namespace ModHearth.Config
{
    public static partial class ConfigManager
    {
        public static IEnumerable<string> EnumerateSteamAppsRoots()
        {
            return GetOrCreateCached(ref _cachedSteamAppsRoots, _steamAppsRootsLock, () =>
            {
                HashSet<string> steamAppsRoots = new(PathHelper.Comparer);

                foreach (string libraryRoot in EnumerateSteamLibraryRoots())
                {
                    if (string.IsNullOrWhiteSpace(libraryRoot))
                        continue;

                    string steamAppsRoot = NormalizeFileSystemPath(Path.Combine(libraryRoot, steamApps));
                    if (string.IsNullOrWhiteSpace(steamAppsRoot))
                        continue;

                    if (!Directory.Exists(steamAppsRoot))
                        continue;

                    _ = steamAppsRoots.Add(steamAppsRoot);
                }

                LogInfo($"SteamApps roots ({steamAppsRoots.Count}): {FormatPathListForLog(steamAppsRoots)}");
                return [.. steamAppsRoots];
            });
        }

        public static IEnumerable<string> GetSteamWorkshopContentPaths()
        {
            return GetOrCreateCached(ref _cachedWorkshopContentPaths, _workshopContentPathsLock, () =>
            {
                HashSet<string> paths = new(PathHelper.Comparer);
                List<string> steamAppsRoots = [.. EnumerateSteamAppsRoots()];
                LogInfo($"Workshop content scan starting. SteamApps roots input ({steamAppsRoots.Count}).");
                foreach (string steamAppsRoot in steamAppsRoots)
                {
                    string candidate = NormalizeFileSystemPath(
                        Path.Combine(steamAppsRoot, "workshop", "content", DwarfFortressSteamAppId));
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    if (Directory.Exists(candidate))
                    {
                        _ = paths.Add(candidate);
                        LogInfo($"Workshop content path found: {candidate}");
                    }
                    else
                    {
                        LogInfo($"Workshop content path missing: {candidate}");
                    }
                }

                LogInfo($"Workshop content paths discovered ({paths.Count}): {FormatPathListForLog(paths)}");
                return [.. paths];
            });
        }

        public static IEnumerable<string> GetSteamWorkshopAcfPaths()
        {
            StringComparer comparer = PathHelper.Comparer;
            HashSet<string> paths = new(comparer);
            List<string> steamAppsRoots = [.. EnumerateSteamAppsRoots()];

            LogInfo($"Steam workshop scan started for app {DwarfFortressSteamAppId}. Library roots discovered: {steamAppsRoots.Count}.");

            foreach (string steamAppsRoot in steamAppsRoots)
            {
                if (string.IsNullOrWhiteSpace(steamAppsRoot))
                    continue;

                string primaryCandidate = Path.Combine(steamAppsRoot, $"appworkshop_{DwarfFortressSteamAppId}.acf");
                if (File.Exists(primaryCandidate))
                    _ = paths.Add(NormalizeFileSystemPath(primaryCandidate));

                string workshopCandidate = Path.Combine(steamAppsRoot, "workshop", $"appworkshop_{DwarfFortressSteamAppId}.acf");
                if (File.Exists(workshopCandidate))
                    _ = paths.Add(NormalizeFileSystemPath(workshopCandidate));
            }

            if (paths.Count == 0)
            {
                LogInfo("Steam workshop scan completed: no workshop ACF files found.");
            }
            else
            {
                LogInfo($"Steam workshop scan completed: found {paths.Count} workshop ACF file(s).");
                foreach (string path in paths.OrderBy(static path => path, comparer))
                    LogInfo($"Steam workshop ACF: {path}");
            }

            LogInfo($"Workshop ACF paths resolved ({paths.Count}): {FormatPathListForLog(paths)}");
            return paths;
        }
    }
}
