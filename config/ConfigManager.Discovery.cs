using System.Text.RegularExpressions;
using ModHearth.Utils.IO;
using ModHearth.Utils.Logging;

/// <summary>
/// Actually handles the logic from models\ModHearthConfig
/// </summary>
namespace ModHearth.Config
{
    public static partial class ConfigManager
    {
        public static void AutoDiscoverConfigPaths()
        {
            bool updated = false;
            StringComparison pathComparison = PathHelper.Comparison;

            if (string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                string? dfFolder = TryFindSteamDwarfFortressFolder();
                if (!string.IsNullOrWhiteSpace(dfFolder))
                {
                    Config.DFFolderPathOverride = dfFolder;
                    Config.DFEXEPath = string.Empty;
                    updated = true;
                }
            }
            else
            {
                string? resolvedDfFolder = ResolveExistingDirectoryPath(Config.DFFolderPath);
                if (!string.IsNullOrWhiteSpace(resolvedDfFolder) &&
                    !string.Equals(Config.DFFolderPath, resolvedDfFolder, pathComparison))
                {
                    Config.DFFolderPathOverride = resolvedDfFolder;
                    updated = true;
                }
            }

            if (string.IsNullOrWhiteSpace(Config.InstalledModsPath))
            {
                string? installedModsPath = TryFindInstalledModsPath();
                if (!string.IsNullOrWhiteSpace(installedModsPath))
                {
                    Config.InstalledModsPath = installedModsPath;
                    updated = true;
                }
            }
            else
            {
                string? resolvedInstalledMods = ResolveExistingDirectoryPath(Config.InstalledModsPath);
                if (!string.IsNullOrWhiteSpace(resolvedInstalledMods) &&
                    !string.Equals(Config.InstalledModsPath, resolvedInstalledMods, pathComparison))
                {
                    Config.InstalledModsPath = resolvedInstalledMods;
                    updated = true;
                }
            }

            if (string.IsNullOrWhiteSpace(Config.DFHackFolderPath))
            {
                string? dfhackFolder = TryFindSteamDFHackFolder();
                if (!string.IsNullOrWhiteSpace(dfhackFolder))
                {
                    Config.DFHackFolderPath = dfhackFolder;
                    updated = true;
                }
            }
            else
            {
                string? resolvedDfhackFolder = ResolveExistingDirectoryPath(Config.DFHackFolderPath);
                if (!string.IsNullOrWhiteSpace(resolvedDfhackFolder) &&
                    !string.Equals(Config.DFHackFolderPath, resolvedDfhackFolder, pathComparison))
                {
                    Config.DFHackFolderPath = resolvedDfhackFolder;
                    updated = true;
                }
            }

            string? resolvedModsPath = ResolveExistingDirectoryPath(Config.ModsPath);
            if (string.IsNullOrWhiteSpace(resolvedModsPath) &&
                !string.IsNullOrWhiteSpace(Config.ModsPathOverride) &&
                !string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                resolvedModsPath = ResolveExistingDirectoryPath(Path.Combine(Config.DFFolderPath, "Mods"));
            }
            if (!string.IsNullOrWhiteSpace(resolvedModsPath) &&
                !string.Equals(Config.ModsPathOverride, resolvedModsPath, pathComparison))
            {
                Config.ModsPathOverride = resolvedModsPath;
                updated = true;
            }

            if (updated)
                SaveConfigFile($"Path autodiscover");
        }

        public static string? TryFindSteamDwarfFortressFolder()
        {
            foreach (string libraryRoot in EnumerateSteamLibraryRoots())
            {
                if (string.IsNullOrWhiteSpace(libraryRoot))
                    continue;

                string candidate = Path.Combine(libraryRoot, steamApps, "common", dfString);
                string? resolved = ResolveDwarfFortressFolderCandidate(candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }

            return null;
        }

        // Returns the full exe path if dfhack-run exists in the given directory, null otherwise.
        private static string? TryFindExeInDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return null;
            string path = Path.Combine(directory, DFHackExeName);
            return File.Exists(path) ? path : null;
        }

        // Finds the directory containing dfhack-run across all Steam library roots.
        // Checks .../DFHack/hack first, then .../DFHack root.
        public static string? TryFindSteamDFHackFolder()
        {
            foreach (string libraryRoot in EnumerateSteamLibraryRoots())
            {
                if (string.IsNullOrWhiteSpace(libraryRoot))
                    continue;

                string[] candidates =
                [
            Path.Combine(libraryRoot, steamApps, "common", "DFHack", "hack"),
            Path.Combine(libraryRoot, steamApps, "common", "DFHack"),
        ];

                foreach (string candidate in candidates)
                {
                    string? resolved = ResolveExistingDirectoryPath(candidate);
                    if (TryFindExeInDirectory(resolved) != null)
                        return resolved;
                }
            }

            return null;
        }

        // Returns the full path to dfhack-run, trying all known locations in priority order.
        public static string GetDfhackRunPath()
        {
            if (Config == null)
                return string.Empty;

            // 1. Explicit DFHack folder from config.
            if (TryFindExeInDirectory(Config.DFHackFolderPath) is { } fromDfhackFolder)
                return fromDfhackFolder;

            // 2. DF folder root (legacy install location).
            if (TryFindExeInDirectory(Config.DFFolderPath) is { } fromDfFolder)
                return fromDfFolder;

            // 3. /hack subfolder inside DF folder.
            if (!string.IsNullOrWhiteSpace(Config.DFFolderPath) &&
                TryFindExeInDirectory(Path.Combine(Config.DFFolderPath, "hack")) is { } fromDfHackSub)
            {
                return fromDfHackSub;
            }

            // 4. Steam library auto-detection.
            return TryFindExeInDirectory(TryFindSteamDFHackFolder()) ?? string.Empty;
        }

        private static string? ResolveDwarfFortressFolderCandidate(string candidate)
        {
            string? resolvedCandidate = ResolveExistingDirectoryPath(candidate);
            if (string.IsNullOrWhiteSpace(resolvedCandidate))
                return null;

            if (IsLikelyDwarfFortressFolder(resolvedCandidate))
                return resolvedCandidate;

            if (OperatingSystem.IsMacOS())
            {
                string appResources = Path.Combine(resolvedCandidate, "Dwarf Fortress.app", "Contents", "Resources");
                if (IsLikelyDwarfFortressFolder(appResources))
                    return appResources;
            }

            return null;
        }

        public static IEnumerable<string> EnumerateSteamLibraryRoots()
        {
            return GetOrCreateCached(ref _cachedSteamLibraryRoots, _steamLibraryRootsLock, () =>
            {
                HashSet<string> libraries = new(
                    PathHelper.Comparer);
                List<string> candidateRoots = [.. GetSteamRootCandidates().Where(root => !string.IsNullOrWhiteSpace(root))];

                LogInfo($"Steam root candidates ({candidateRoots.Count}): {FormatPathListForLog(candidateRoots)}");

                HashSet<string> processedRoots = new(
                    PathHelper.Comparer);

                foreach (string root in candidateRoots)
                {
                    if (string.IsNullOrWhiteSpace(root))
                        continue;

                    string normalizedRoot = ResolveCanonicalPath(root);
                    if (string.IsNullOrWhiteSpace(normalizedRoot))
                        continue;

                    if (!Directory.Exists(normalizedRoot))
                        continue;

                    if (!processedRoots.Add(normalizedRoot))
                        continue;

                    if (Directory.Exists(Path.Combine(normalizedRoot, steamApps)))
                        _ = libraries.Add(normalizedRoot);

                    foreach (string library in ReadSteamLibraryFolders(normalizedRoot))
                    {
                        if (string.IsNullOrWhiteSpace(library))
                            continue;

                        string normalizedLibrary = ResolveCanonicalPath(library);
                        if (string.IsNullOrWhiteSpace(normalizedLibrary))
                            continue;

                        if (Directory.Exists(Path.Combine(normalizedLibrary, steamApps)))
                            _ = libraries.Add(normalizedLibrary);
                    }
                }

                LogInfo($"Steam library roots discovered ({libraries.Count}): {FormatPathListForLog(libraries)}");
                return [.. libraries];
            });
        }

        private static IEnumerable<string> GetSteamRootCandidates()
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (string candidate in GetWindowsSteamRootCandidates())
                    yield return candidate;
                yield break;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                yield break;

            if (OperatingSystem.IsMacOS())
            {
                yield return Path.Combine(home, "Library", "Application Support", steamString);
                yield break;
            }

            if (OperatingSystem.IsLinux())
            {
                yield return Path.Combine(home, ".steam", steamString);
                yield return Path.Combine(home, ".steam", "root");
                yield return Path.Combine(home, ".local", "share", steamString);
                yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", steamString);
                yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", steamString);
            }
        }

        private static HashSet<string> GetWindowsSteamRootCandidates()
        {
            HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);

            string? registryPath = TryGetWindowsSteamPathFromRegistry();
            if (!string.IsNullOrWhiteSpace(registryPath))
                _ = candidates.Add(registryPath);

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                _ = candidates.Add(Path.Combine(programFilesX86, steamString));

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                _ = candidates.Add(Path.Combine(programFiles, steamString));

            string? steamPathEnv = Environment.GetEnvironmentVariable("STEAM_PATH");
            if (!string.IsNullOrWhiteSpace(steamPathEnv))
                _ = candidates.Add(steamPathEnv);

            return candidates;
        }

        private static string? TryGetWindowsSteamPathFromRegistry()
        {
            if (!OperatingSystem.IsWindows())
                return null;

            try
            {
                object? value = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                if (value is string path && !string.IsNullOrWhiteSpace(path))
                    return path;

                value = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null);
                if (value is string path64 && !string.IsNullOrWhiteSpace(path64))
                    return path64;
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Registry lookup for the Dwarf Fortress install failed: {ex.Message}");
            }

            return null;
        }

        private static HashSet<string> ReadSteamLibraryFolders(string steamRoot)
        {
            HashSet<string> libraries = new(
                PathHelper.Comparer);

            string vdfPath = Path.Combine(steamRoot, steamApps, "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                LogInfo($"Steam library file missing: {vdfPath}");
                return libraries;
            }

            try
            {
                foreach (string line in File.ReadLines(vdfPath))
                {
                    string? parsed = TryParseSteamLibraryPath(line);
                    if (string.IsNullOrWhiteSpace(parsed))
                        continue;

                    string normalized = NormalizeSteamPath(parsed);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        _ = libraries.Add(normalized);
                }
            }
            catch (Exception ex)
            {
                LogInfo($"Failed reading Steam library file '{vdfPath}': {ex.Message}");
            }

            LogInfo($"Parsed Steam libraries from '{vdfPath}' ({libraries.Count}): {FormatPathListForLog(libraries)}");
            return libraries;
        }

        private static string? TryParseSteamLibraryPath(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            Match match = SteamLibraryPathRegex().Match(line);
            if (match.Success)
                return match.Groups["path"].Value;

            match = SteamLibraryLegacyPathRegex().Match(line);
            if (!match.Success)
                return null;

            string candidate = match.Groups["path"].Value;
            if (candidate.Contains('\\') || candidate.Contains('/') || candidate.Contains(':'))
                return candidate;

            return null;
        }

        private static string NormalizeSteamPath(string path)
        {
            string normalized = path.Trim();
            if (OperatingSystem.IsWindows())
                normalized = normalized.Replace("\\\\", "\\").Replace('/', '\\');
            else
                normalized = normalized.Replace('\\', '/');

            return NormalizeFileSystemPath(normalized);
        }
    }
}
