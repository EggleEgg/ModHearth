using System.Text.Json;
using System.Text.RegularExpressions;
using ModHearth.Utilities;
using ModHearth.Models;
using ModHearth.Utilities.Logging;

/// <summary>
/// Actually handles the logic from models\ModHearthConfig
/// </summary>
namespace ModHearth
{
    public static partial class ConfigManager
    {
        public static ModHearthConfig Config { get; private set; } = new();

        public static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // Guards every read/write of config.json so concurrent callers can't interleave and corrupt the file or clobber each other's writes.
        private static readonly object configGate = new();

        public const string DwarfFortressSteamAppId = "975370";
        private const string Message = "Panel data";
        private static List<string>? _cachedSteamLibraryRoots;
        private static readonly object _steamLibraryRootsLock = new();
        private static List<string>? _cachedSteamAppsRoots;
        private static readonly object _steamAppsRootsLock = new();
        private static List<string>? _cachedWorkshopContentPaths;
        private static readonly object _workshopContentPathsLock = new();
        private static List<string>? _cachedEffectiveModRoots;
        private static readonly object _effectiveModRootsLock = new();
        private static void LogInfo(string message) => Loggers.Steam.Log(message);

        /// <summary>
        /// Returns the cached path list, building it once under <paramref name="gate"/> on first use.
        /// All Steam/mod-root discovery lists share this so the lock + cache shell cannot drift.
        /// </summary>
        private static List<string> GetOrCreateCached(ref List<string>? cache, object gate, Func<List<string>> factory)
        {
            lock (gate)
            {
                cache ??= factory();
                return cache;
            }
        }

        private static string DFHackExeName => OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run";
        private static readonly string installedMods = "installed_mods", dfString = "Dwarf Fortress", steamString = "Steam", steamApps = "steamapps";

        public static void AttemptLoadConfig() => AttemptLoadConfig(true);

        public static void AttemptLoadConfig(bool createLogs)
        {
            lock (configGate)
            {
                if (createLogs) Console.WriteLine("Attempting config file load.");
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        if (createLogs) Console.WriteLine("Config file found.");
                        string jsonContent = File.ReadAllText(ConfigPath);
                        ModHearthConfig? loadedConfig = JsonSerializer.Deserialize<ModHearthConfig>(jsonContent, JsonFileStore.CaseInsensitive);
                        Config = loadedConfig ?? new ModHearthConfig();

                        if (loadedConfig == null)
                            Console.WriteLine("Config file borked.");
                    }
                    else
                    {
                        Console.WriteLine("Config file missing.");
                        Config = new ModHearthConfig();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    Config = new ModHearthConfig();
                }
            }
        }

        public static void AttemptLoadConfigAndDiscover()
        {
            AttemptLoadConfig();
            AutoDiscoverConfigPaths();

            if (!Config.showConsole && !DevMode.IsEnabled)
            {
                RuntimeBootstrap.HideConsole();
            }
        }

        public static void SaveConfigFile(string? message = null)
        {
            lock (configGate)
            {
                if (!string.IsNullOrEmpty(message))
                    Console.WriteLine($"{message} config saved.");

                _ = JsonFileStore.TrySave(ConfigPath, Config, "Config");
            }
        }

        public static void Update(Action<ModHearthConfig> change, string? label = null)
        {
            lock (configGate)
            {
                change(Config);
                SaveConfigFile(label);
            }
        }

        public static void DestroyConfig()
        {
            lock (configGate)
            {
                if (File.Exists(ConfigPath))
                {
                    File.Delete(ConfigPath);
                }
            }
        }

        public static int GetTheme() => Config.theme;

        public static void SetTheme(int theme) => Update(c => c.theme = theme, "Theme");

        public static int GetAutoReloadIntervalSeconds() => Config.AutoReloadIntervalSeconds;

        public static void SetAutoReloadIntervalSeconds(int seconds) => Update(c => c.AutoReloadIntervalSeconds = seconds, "Autoreload");

        public static double GetModDataPanelProportion() => Config.ModDataPanelProportion;

        public static int GetModDataPanelOrientation() => Config.ModDataPanelOrientation;

        public static bool GetModDataPanelFirst() => Config.ModDataPanelFirst;

        public static double GetModDescriptionPanelProportion() => Config.ModDescriptionPanelProportion;

        public static double GetModInfoPanelProportion() => Config.ModInfoPanelProportion;

        public static double GetModPreviewPanelProportion() => Config.ModPreviewPanelProportion;

        public static int GetModPreviewPanelOrientation() => Config.ModPreviewPanelOrientation;

        public static bool GetModPreviewPanelFirst() => Config.ModPreviewPanelFirst;

        public static void SetModInfoDockLayout(
            double modDataProportion,
            int modDataOrientation,
            bool modDataFirst,
            double descriptionProportion,
            double infoPanelProportion,
            double previewProportion,
            int previewOrientation,
            bool previewFirst) => Update(c =>
            {
                c.ModDataPanelProportion = modDataProportion;
                c.ModDataPanelOrientation = modDataOrientation;
                c.ModDataPanelFirst = modDataFirst;
                c.ModDescriptionPanelProportion = descriptionProportion;
                c.ModInfoPanelProportion = infoPanelProportion;
                c.ModPreviewPanelProportion = previewProportion;
                c.ModPreviewPanelOrientation = previewOrientation;
                c.ModPreviewPanelFirst = previewFirst;
            }, Message);

        public static bool IsAutoSaveEnabled() => Config.IsAutoSaveEnabled;

        public static void SetAutoSaveEnabled(bool enabled) => Update(c => c.IsAutoSaveEnabled = enabled, "Autosave");

        public static bool IsAutoSortEnabled() => Config.IsAutoSortEnabled;

        public static void SetAutoSortEnabled(bool enabled) => Update(c => c.IsAutoSortEnabled = enabled, "Autosort");

        public static bool IsAutoResolveAndQueueEnabled() => Config.IsAutoResolveAndQueueEnabled;

        public static void SetAutoResolveAndQueueEnabled(bool enabled) => Update(c => c.IsAutoResolveAndQueueEnabled = enabled, "Auto-resolve and queue");

        public static bool IsAutoRetryAllEnabled() => Config.IsAutoRetryAllEnabled;

        public static void SetAutoRetryAllEnabled(bool enabled) => Update(c => c.IsAutoRetryAllEnabled = enabled, "Auto-retry all");

        public static string GetLeftSearchBarState() => Config.LeftSearchBarState;

        public static void SetLeftSearchBarState(string state) => Update(c => c.LeftSearchBarState = state);

        public static string GetRightSearchBarState() => Config.RightSearchBarState;

        public static void SetRightSearchBarState(string state) => Update(c => c.RightSearchBarState = state);

        public static bool GetOpenSteamInClient() => Config.OpenSteamInClient;

        public static void SetOpenSteamInClient(bool openInClient) => Update(c => c.OpenSteamInClient = openInClient, "Context menu");

        public static bool GetOpenSteamFolder() => Config.OpenSteamFolder;

        public static void SetOpenSteamFolder(bool openSteamFolder) => Update(c => c.OpenSteamFolder = openSteamFolder, "Context menu");

        public static bool GetCopySteamFileId() => Config.CopySteamFileId;

        public static void SetCopySteamFileId(bool copySteamFileId) => Update(c => c.CopySteamFileId = copySteamFileId, "Context menu");

        public static string GetModsPath()
        {
            if (string.IsNullOrWhiteSpace(Config.ModsPath))
                return string.Empty;

            string configuredPath = NormalizeFileSystemPath(Config.ModsPath);
            string? resolved = ResolveExistingDirectoryPath(configuredPath);
            if (string.IsNullOrWhiteSpace(resolved) &&
                !string.IsNullOrWhiteSpace(Config.ModsPathOverride) &&
                !string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                string fallback = Path.Combine(Config.DFFolderPath, "Mods");
                resolved = ResolveExistingDirectoryPath(fallback);
            }
            if (string.IsNullOrWhiteSpace(resolved))
                return configuredPath;

            return PersistResolvedPath(resolved, Config.ModsPathOverride, value => Config.ModsPathOverride = value);
        }

        public static string GetInstalledModsPath()
        {
            if (string.IsNullOrWhiteSpace(Config.InstalledModsPath))
                return GetDefaultInstalledModsPath();

            string normalizedConfigured = NormalizeFileSystemPath(Config.InstalledModsPath);
            string? resolved = ResolveExistingDirectoryPath(normalizedConfigured);
            if (string.IsNullOrWhiteSpace(resolved))
                return normalizedConfigured;

            return PersistResolvedPath(resolved, Config.InstalledModsPath, value => Config.InstalledModsPath = value);
        }

        /// <summary>
        /// Persists a path that discovery resolved to a canonical form, but only when it differs from
        /// what is already configured, so ordinary lookups don't rewrite config.json.
        /// </summary>
        private static string PersistResolvedPath(string resolvedPath, string currentValue, Action<string> assign)
        {
            if (!string.Equals(currentValue, resolvedPath, PathHelper.Comparison))
            {
                assign(resolvedPath);
                SaveConfigFile($"Path resolved: {resolvedPath},");
            }

            return resolvedPath;
        }

        /// <summary>
        /// True when <paramref name="path"/> lives inside Dwarf Fortress's disposable installed_mods
        /// cache. Pure path inspection (no config read/write, no disk access), so it is safe to call
        /// from parallel scan loops.
        /// </summary>
        public static bool IsInstalledModsPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path
#pragma warning disable IDE0300 //simplified collection fails wsl builds (for some reason)
                .Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, installedMods, StringComparison.OrdinalIgnoreCase));
#pragma warning restore IDE0300

        }

        public static string GetVanillaModsPath()
        {
            if (string.IsNullOrWhiteSpace(Config.DFFolderPath))
                return string.Empty;

            return Path.Combine(Config.DFFolderPath, "data", "vanilla");
        }

        public static string GetErrorLogPath()
        {
            if (string.IsNullOrWhiteSpace(Config.DFFolderPath))
                return Path.Combine(AppContext.BaseDirectory, "errorlog.txt");

            return Path.Combine(Config.DFFolderPath, "errorlog.txt");
        }

        public static string GetModManagerConfigPath()
        {
            if (string.IsNullOrWhiteSpace(Config.DFFolderPath))
                return string.Empty;
            return Path.Combine(Config.DFFolderPath, "dfhack-config", "mod-manager.json");
        }

        public static void SetDwarfFortressExecutablePath(string path) => Update(c =>
        {
            c.DFEXEPath = path;
            c.DFFolderPathOverride = string.Empty;
            c.ModsPathOverride = string.Empty;
        }, $"Path resolved: {path},");

        public static void SetDwarfFortressFolderPath(string path) => Update(c =>
        {
            c.DFFolderPathOverride = path;
            if (!string.IsNullOrWhiteSpace(path))
                c.DFEXEPath = string.Empty;
            c.ModsPathOverride = string.Empty;
        }, $"Path resolved: {path},");

        public static void SetInstalledModsPath(string path) => Update(c => c.InstalledModsPath = ResolveExistingDirectoryPath(path) ?? path, $"Path resolved: {path},");

        public static void SetDFHackFolderPath(string path) => Update(c => c.DFHackFolderPath = ResolveExistingDirectoryPath(path) ?? path, $"Path resolved: {path},");

        public static void SetSteamCmdPath(string path) => Update(c => c.SteamCmdPath = path, "SteamCmdPath updated");

        public static void SetCommunitySortRulesUrl(string url) => Update(c => c.CommunitySortRulesUrl = url?.Trim() ?? string.Empty, "Url");

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
            catch
            {
                // Ignore registry lookup failures.
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

        /// <summary>
        /// Use this over NormalizeFileSystemPath when symlinks are present.
        /// Rule of thumb on usage: when the code is about to put a path into a HashSet/dictionary key, compare two paths for equality, or make a security-relevant containment decision
        /// </summary>
        public static string ResolveCanonicalPath(string path)
        {
            string normalized = NormalizeFileSystemPath(path);
            if (string.IsNullOrWhiteSpace(normalized))
                return normalized;

            try
            {
                FileSystemInfo? target = null;

                if (Directory.Exists(normalized))
                {
                    target = Directory.ResolveLinkTarget(normalized, returnFinalTarget: true);
                }
                else if (File.Exists(normalized))
                {
                    target = File.ResolveLinkTarget(normalized, returnFinalTarget: true);
                }

                if (target != null)
                    return NormalizeFileSystemPath(target.FullName);

            }
            catch
            {
                // Ignore resolution failures; fall back to the normalized (possibly symlinked) path.
            }

            return normalized;
        }

        public static string NormalizeFileSystemPath(string path)
        {
            string normalized = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            if (OperatingSystem.IsWindows())
                normalized = normalized.Replace('/', '\\');
            else
                normalized = normalized.Replace('\\', '/');

            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch
            {
                // Ignore normalization failures.
            }

            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string? ResolveExistingDirectoryPath(string path) => ResolveExistingPath(path, expectDirectory: true);

        public static string? ResolveExistingPath(string path, bool expectDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }

            if (expectDirectory)
            {
                if (Directory.Exists(fullPath))
                    return NormalizeFileSystemPath(fullPath);
            }
            else
            {
                if (File.Exists(fullPath))
                    return NormalizeFileSystemPath(fullPath);
            }

            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return null;

            string remainder = fullPath[root.Length..];
#pragma warning disable IDE0300 //simplified collection fails wsl builds (for some reason)
            string[] segments = remainder.Split(
                new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
#pragma warning restore IDE0300
            if (segments.Length == 0)
            {
                if (expectDirectory && Directory.Exists(root))
                    return NormalizeFileSystemPath(root);
                return null;
            }

            string current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                bool isLast = i == segments.Length - 1;
                bool expectDirectorySegment = !isLast || expectDirectory;
                string? next = ResolveChildPathSegment(current, segments[i], expectDirectorySegment);
                if (string.IsNullOrWhiteSpace(next))
                    return null;
                current = next;
            }

            if (expectDirectory)
                return Directory.Exists(current) ? NormalizeFileSystemPath(current) : null;
            return File.Exists(current) ? NormalizeFileSystemPath(current) : null;
        }

        private static string? ResolveChildPathSegment(string parent, string name, bool expectDirectory)
            => PathHelper.ResolveChild(parent, name, expectDirectory);

        public static string? ResolveInfoFilePath(string modDirectory)
        {
            string? resolvedDirectory = ResolveExistingDirectoryPath(modDirectory);
            if (string.IsNullOrWhiteSpace(resolvedDirectory))
                return null;

            return PathHelper.ResolveFile(resolvedDirectory, "info.txt");
        }
        public static string FormatPathListForLog(IEnumerable<string> paths, int maxItems = 24)
        {
            if (paths == null)
                return "(none)";

            List<string> list = [.. paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeFileSystemPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(PathHelper.Comparer)];

            return StringFormatter.FormatListWithMoreIndicator(list, maxItems);
        }

        public static bool TryExtractSteamWorkshopItemIdFromPath(string? path, out string steamItemId)
        {
            steamItemId = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalizedPath = path.Replace('\\', '/');
            Match pathMatch = SteamWorkshopPathRegex().Match(normalizedPath);
            if (!pathMatch.Success)
                return false;

            return TryParsePositiveSteamId(pathMatch.Groups["id"].Value, out steamItemId);
        }

        // Detects Dwarf Fortress's own Steam-integration habit of materializing a workshop mod's content directly into the Mods\ folder, named after
        // its numeric Steam Workshop file id, with a trailing " (<version>)" suffix, ex: "Mods\3445635304 (20)" or "Mods\3445635304".
        // Also detects manual copies of workshop mods placed into the Mods\ folder if they have a matching Steam file id.
        // See steam discussion https://steamcommunity.com/app/975370/discussions/0/599642674183563431/ as reference of this (likely unpatched) bug
        public static bool IsLikelySteamShadowCopy(string? folderPath, string? steamId, out string workshopId)
        {
            workshopId = string.Empty;
            if (string.IsNullOrWhiteSpace(folderPath))
                return false;

            string normalizedFolderPath = NormalizeFileSystemPath(folderPath);
            string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Try identifying by folder name (canonical shadow copy)
            string? candidateIdFromFolder = null;
            Match match = SteamShadowCopyFolderNameRegex().Match(folderName);
            if (match.Success)
                candidateIdFromFolder = match.Groups["id"].Value;

            // Try identifying by provided steamId
            string? candidateIdFromMetadata = null;
            if (TryParsePositiveSteamId(steamId, out string normalizedSteamId))
                candidateIdFromMetadata = normalizedSteamId;
            // We check both sources. If either points to an existing workshop directory, this is a shadow/local copy.
            string[] candidateIds = new[] { candidateIdFromFolder, candidateIdFromMetadata }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToArray()!;

            if (candidateIds.Length == 0)
                return false;

            foreach (string workshopContentRoot in GetSteamWorkshopContentPaths())
            {
                foreach (string candidateId in candidateIds)
                {
                    string canonicalPath = NormalizeFileSystemPath(Path.Combine(workshopContentRoot, candidateId));

                    if (string.Equals(normalizedFolderPath, canonicalPath, PathHelper.Comparison))
                        continue;

                    if (Directory.Exists(canonicalPath))
                    {
                        workshopId = candidateId;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsLikelySteamShadowCopy(string? folderPath, out string workshopId) => IsLikelySteamShadowCopy(folderPath, null, out workshopId);

        //IMPORTANT: Local mods often contain [STEAM_FILE_ID:] and related steam tags. Steam/Vanilla/Local/Etc mod sorting should be done by their root folder location!

        public static bool TryParsePositiveSteamId(string? rawSteamId, out string steamItemId)
        {
            steamItemId = string.Empty;
            if (string.IsNullOrWhiteSpace(rawSteamId))
                return false;

            if (long.TryParse(rawSteamId, out long id) && id > 0)
            {
                steamItemId = rawSteamId.Trim();
                return true;
            }

            return false;
        }

        public static bool IsLikelyDwarfFortressFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            if (Directory.Exists(Path.Combine(path, "data")))
                return true;

            if (DwarfFortressExecutableLocator.TryResolvePath(path, out _))
                return true;

            if (OperatingSystem.IsMacOS() && Directory.Exists(Path.Combine(path, "Dwarf Fortress.app")))
                return true;

            return false;
        }

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
                foreach (string path in paths.OrderBy(path => path, comparer))
                    LogInfo($"Steam workshop ACF: {path}");
            }

            LogInfo($"Workshop ACF paths resolved ({paths.Count}): {FormatPathListForLog(paths)}");
            return paths;
        }

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

        public static IReadOnlyList<ModHearthManager.ConfigIssue> GetConfigIssues()
        {
            List<ModHearthManager.ConfigIssue> issues = [];
            if (string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                issues.Add(new ModHearthManager.ConfigIssue(ModHearthManager.ConfigIssueType.MissingDwarfFortressPath, "Dwarf Fortress path is not set."));
            }
            else if (!Directory.Exists(Config.DFFolderPath))
            {
                issues.Add(new ModHearthManager.ConfigIssue(ModHearthManager.ConfigIssueType.MissingDwarfFortressPath, $"Dwarf Fortress folder not found: {Config.DFFolderPath}"));
            }

            string installedModsPath = GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath) || !Directory.Exists(installedModsPath))
            {
                issues.Add(new ModHearthManager.ConfigIssue(ModHearthManager.ConfigIssueType.MissingInstalledModsPath, "installed_mods path is not set or missing."));
            }

            if (string.IsNullOrWhiteSpace(Config.DFHackFolderPath))
            {
                issues.Add(new ModHearthManager.ConfigIssue(ModHearthManager.ConfigIssueType.MissingDFHackPath, "DFHack folder path is not set."));
            }
            else if (!Directory.Exists(Config.DFHackFolderPath) || !File.Exists(Path.Combine(Config.DFHackFolderPath, OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run")))
            {
                issues.Add(new ModHearthManager.ConfigIssue(ModHearthManager.ConfigIssueType.MissingDFHackPath, "DFHack executable not found in configured folder."));
            }

            return issues;
        }

        // Thread safe
        public static List<string> GetEffectiveModRoots()
        {
            return GetOrCreateCached(ref _cachedEffectiveModRoots, _effectiveModRootsLock, () =>
            {
                HashSet<string> seen = new(PathHelper.Comparer);
                List<string> resolvedRoots = [];

                IEnumerable<string> configuredRoots = EnumerateConfiguredModRoots();
                foreach (string root in configuredRoots)
                {
                    string normalizedRoot = NormalizeFileSystemPath(root);
                    if (string.IsNullOrWhiteSpace(normalizedRoot))
                        continue;

                    if (seen.Add(normalizedRoot))
                        resolvedRoots.Add(normalizedRoot);
                }

                LogInfo($"Effective mod roots ({resolvedRoots.Count}): {FormatPathListForLog(resolvedRoots)}");
                return resolvedRoots;
            });
        }

        // IMPORTANT: order matters here. When the same mod (same id, same numeric_version) exists both as a Steam Workshop subscription and as a shadow/manual copy under the local Mods\ folder
        // FindAllModsFromDisk's "first occurrence wins" duplicate handling means whichever root is enumerated first becomes the one visible ModReference for that mod,
        // and ModSourceClassifier.Classify's result comes from THAT copy's path. The local Mods\ copy is what Dwarf Fortress actually loads in that situation,
        // so it must win the primacy slot; enumerating it before the workshop content paths accomplishes that without needing a special-cased duplicate-resolution pass elsewhere.
        // installed_mods comes last on purpose: it is DF's disposable cache, only usable as a low-priority fallback for
        // mods whose real source folder is gone, and must never shadow a canonical Mods\/workshop copy.
        public static IEnumerable<string> EnumerateConfiguredModRoots()
        {
            string modsPath = GetModsPath();
            if (!string.IsNullOrWhiteSpace(modsPath))
                yield return modsPath;

            foreach (string workshopPath in GetSteamWorkshopContentPaths())
                yield return workshopPath;

            if (!string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                string vanillaRoot = GetVanillaModsPath();
                string? vanillaPath = ResolveExistingDirectoryPath(vanillaRoot) ?? vanillaRoot;
                if (!string.IsNullOrWhiteSpace(vanillaPath))
                    yield return vanillaPath;
            }

            string installedModsPath = GetInstalledModsPath();
            string? resolvedInstalledModsPath = ResolveExistingDirectoryPath(installedModsPath);
            if (!string.IsNullOrWhiteSpace(resolvedInstalledModsPath))
                yield return resolvedInstalledModsPath;
        }

        public static double GetMainWindowGridSplitterRatio() => Config.MainWindowGridSplitterRatio;

        public static void SetMainWindowGridSplitterRatio(double ratio) => Update(c => c.MainWindowGridSplitterRatio = ratio, "MainWindow GridSplitter ratio");

        public static double GetSortRulesWindowGridSplitterRatio() => Config.SortRulesWindowGridSplitterRatio;

        public static void SetSortRulesWindowGridSplitterRatio(double ratio) => Update(c => c.SortRulesWindowGridSplitterRatio = ratio, "SortRulesWindow GridSplitter ratio");

        public static string GetDefaultWorkshopProvider() => Config.DefaultWorkshopProvider;

        public static void SetDefaultWorkshopProvider(string providerName) => Update(c => c.DefaultWorkshopProvider = providerName, "Default Workshop Provider");

        public static bool GetIsWorkshopDownloaderDocked() => Config.IsWorkshopDownloaderDocked;

        public static void SetIsWorkshopDownloaderDocked(bool docked) => Update(c => c.IsWorkshopDownloaderDocked = docked, "Workshop downloader docked");

        public static bool GetIsModUpdateLogDocked() => Config.IsModUpdateLogDocked;

        public static void SetIsModUpdateLogDocked(bool docked) => Update(c => c.IsModUpdateLogDocked = docked, "Mod update log docked");

        public static bool GetIsSortRulesDocked() => Config.IsSortRulesDocked;

        public static void SetIsSortRulesDocked(bool docked) => Update(c => c.IsSortRulesDocked = docked, "Sort rules docked");

        public static double GetDockSplitterProportion(string key, double defaultProportion)
        {
            if (Config.DockSplitterProportions != null && Config.DockSplitterProportions.TryGetValue(key, out double val))
            {
                return val;
            }
            return defaultProportion;
        }

        public static void SetDockSplitterProportion(string key, double proportion) => Update(c =>
        {
            c.DockSplitterProportions ??= [];
            c.DockSplitterProportions[key] = proportion;
        }, $"Dock splitter proportion for {key}");

        public static int GetDockSide(string key, int defaultSide)
        {
            if (Config.DockSides != null && Config.DockSides.TryGetValue(key, out int val))
            {
                return val;
            }
            return defaultSide;
        }

        public static void SetDockSide(string key, int side) => Update(c =>
        {
            c.DockSides ??= [];
            c.DockSides[key] = side;
        }, $"Dock side for {key}");

        public static string GetIgnoredUpdateVersion() => Config.IgnoredUpdateVersion;

        public static void SetIgnoredUpdateVersion(string version) => Update(c => c.IgnoredUpdateVersion = version ?? string.Empty, "Ignored update version");

        public static bool GetAutoCheckForReleasesOnStartup() => Config.AutoCheckForReleasesOnStartup;

        public static void SetAutoCheckForReleasesOnStartup(bool autoCheck) => Update(c => c.AutoCheckForReleasesOnStartup = autoCheck, "Auto check for releases on startup");

        public static bool GetBackupDataBeforeUpdate() => Config.BackupDataBeforeUpdate;

        public static void SetBackupDataBeforeUpdate(bool backup) => Update(c => c.BackupDataBeforeUpdate = backup, "Backup data before update");

        [GeneratedRegex("\"path\"\\s+\"(?<path>.*?)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex SteamLibraryPathRegex();
        [GeneratedRegex("^\\s*\"\\d+\"\\s+\"(?<path>.*?)\"", RegexOptions.Compiled)]
        private static partial Regex SteamLibraryLegacyPathRegex();
        [GeneratedRegex("/workshop/content/975370/(?<id>\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex SteamWorkshopPathRegex();
        [GeneratedRegex(@"^(?<id>\d+)(?:\s*\(\d+\))?$", RegexOptions.Compiled)]
        private static partial Regex SteamShadowCopyFolderNameRegex();
    }
}