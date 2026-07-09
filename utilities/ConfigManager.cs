using System.Text.Json;
using System.Text.RegularExpressions;
using ModHearth.UI;
using ModHearth.Utilities;

/// <summary>
/// Actually handles the logic from models\ModHearthConfig
/// </summary>
namespace ModHearth
{
    public static class ConfigManager
    {
        public static ModHearthConfig Config { get; private set; } = new();

        public static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        private static readonly string styleLightPath = Path.Combine(AppContext.BaseDirectory, "styles", "style.light.json");
        private static readonly string styleDarkPath = Path.Combine(AppContext.BaseDirectory, "styles", "style.dark.json");

        // Guards every read/write of config.json so concurrent callers can't interleave and corrupt the file or clobber each other's writes.
        private static readonly object configGate = new();

        private static readonly Regex SteamLibraryPathRegex = new("\"path\"\\s+\"(?<path>.*?)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SteamLibraryLegacyPathRegex = new("^\\s*\"\\d+\"\\s+\"(?<path>.*?)\"", RegexOptions.Compiled);
        private static readonly Regex SteamWorkshopPathRegex = new("/workshop/content/975370/(?<id>\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public const string DwarfFortressSteamAppId = "975370";

        private static List<string>? _cachedSteamLibraryRoots;
        private static readonly object _steamLibraryRootsLock = new object();
        private static void LogAdvancedSteam(string message) => SteamConnectionLogger.LogInfo(message);
        private static string DFHackExeName =>
            OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run";
        private static string installedMods = "installed_mods", dfString = "Dwarf Fortress", steamString = "Steam", steamApps = "steamapps";

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
                        ModHearthConfig? loadedConfig = JsonSerializer.Deserialize<ModHearthConfig>(jsonContent);
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

        public static void SaveConfigFile()
        {
            lock (configGate)
            {
                Console.WriteLine("Config saved.");
                try
                {
                    JsonSerializerOptions options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    string jsonContent = JsonSerializer.Serialize(Config, options);
                    File.WriteAllText(ConfigPath, jsonContent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred while saving config: {ex.Message}");
                }
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

        public static Style LoadStyle() => LoadStyle(true);
        public static Style LoadStyle(bool createLogs)
        {
            Style style;
            int theme = GetTheme();
            string stylePath = GetStylePathForTheme(theme);

            try
            {
                if (!File.Exists(stylePath))
                    throw new FileNotFoundException("Style file missing.", stylePath);

                if (createLogs) Console.WriteLine("Style file found.");
                if (!TryLoadStyleFromPath(stylePath, out style))
                    throw new InvalidOperationException($"Style file invalid: {stylePath}");
            }
            catch (Exception ex)
            {
                string message = $"Style load failed: {ex.Message}\nMissing or invalid style file: {stylePath}";
                Console.WriteLine(message);
                throw new InvalidOperationException(message, ex);
            }

            Style.instance = style;
            return style;
        }

        public static string GetStylePathForTheme(int theme)
        {
            return theme == 0 ? styleLightPath : styleDarkPath;
        }

        private static bool TryLoadStyleFromPath(string stylePath, out Style style)
        {
            style = null!;
            if (!File.Exists(stylePath))
                return false;

            try
            {
                string jsonContent = File.ReadAllText(stylePath);
                Style? foundStyle = JsonSerializer.Deserialize<Style>(jsonContent);
                if (foundStyle == null)
                    return false;
                if (!foundStyle.IsComplete())
                    return false;
                style = foundStyle;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int GetTheme() => Config.theme;

        public static void SetTheme(int theme)
        {
            Config.theme = theme;
            SaveConfigFile();
        }

        public static int GetAutoReloadIntervalSeconds() => Config.AutoReloadIntervalSeconds;

        public static void SetAutoReloadIntervalSeconds(int seconds)
        {
            Config.AutoReloadIntervalSeconds = seconds;
            SaveConfigFile();
        }

        public static double GetModDataPanelProportion() => Config.ModDataPanelProportion;

        public static int GetModDataPanelOrientation() => Config.ModDataPanelOrientation;

        public static bool GetModDataPanelFirst() => Config.ModDataPanelFirst;

        public static void SetModDataPanelLayout(double proportion, int orientation, bool first)
        {
            Config.ModDataPanelProportion = proportion;
            Config.ModDataPanelOrientation = orientation;
            Config.ModDataPanelFirst = first;
            SaveConfigFile();
        }

        public static double GetModPreviewPanelProportion() => Config.ModPreviewPanelProportion;

        public static int GetModPreviewPanelOrientation() => Config.ModPreviewPanelOrientation;

        public static bool GetModPreviewPanelFirst() => Config.ModPreviewPanelFirst;

        public static void SetModPreviewPanelLayout(double proportion, int orientation, bool first)
        {
            Config.ModPreviewPanelProportion = proportion;
            Config.ModPreviewPanelOrientation = orientation;
            Config.ModPreviewPanelFirst = first;
            SaveConfigFile();
        }

        public static bool IsAutoSaveEnabled() => Config.IsAutoSaveEnabled;

        public static void SetAutoSaveEnabled(bool enabled)
        {
            Config.IsAutoSaveEnabled = enabled;
            SaveConfigFile();
        }

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

            if (!string.Equals(Config.ModsPathOverride, resolved, GetFileSystemPathComparison()))
            {
                Config.ModsPathOverride = resolved;
                SaveConfigFile();
            }

            return resolved;
        }

        public static string GetInstalledModsPath()
        {
            if (string.IsNullOrWhiteSpace(Config.InstalledModsPath))
                return GetDefaultInstalledModsPath();

            if (OperatingSystem.IsWindows() && IsInstalledModsUnderGameFolder(Config.InstalledModsPath, Config.DFFolderPath))
                return GetDefaultInstalledModsPath();

            string normalizedConfigured = NormalizeFileSystemPath(Config.InstalledModsPath);
            string? resolved = ResolveExistingDirectoryPath(normalizedConfigured);
            if (string.IsNullOrWhiteSpace(resolved))
                return normalizedConfigured;

            if (!string.Equals(Config.InstalledModsPath, resolved, GetFileSystemPathComparison()))
            {
                Config.InstalledModsPath = resolved;
                SaveConfigFile();
            }

            return resolved;
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

        public static void SetDwarfFortressExecutablePath(string path)
        {
            Config.DFEXEPath = path;
            Config.DFFolderPathOverride = string.Empty;
            Config.ModsPathOverride = string.Empty;
            SaveConfigFile();
        }

        public static void SetDwarfFortressFolderPath(string path)
        {
            Config.DFFolderPathOverride = path;
            if (!string.IsNullOrWhiteSpace(path))
                Config.DFEXEPath = string.Empty;
            Config.ModsPathOverride = string.Empty;
            SaveConfigFile();
        }

        public static void SetInstalledModsPath(string path)
        {
            Config.InstalledModsPath = ResolveExistingDirectoryPath(path) ?? path;
            SaveConfigFile();
        }

        public static void SetDFHackFolderPath(string path)
        {
            Config.DFHackFolderPath = ResolveExistingDirectoryPath(path) ?? path;
            SaveConfigFile();
        }

        public static void AutoDiscoverConfigPaths()
        {
            bool updated = false;
            StringComparison pathComparison = GetFileSystemPathComparison();

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
                SaveConfigFile();
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

            return string.Empty;
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
                {
            Path.Combine(libraryRoot, steamApps, "common", "DFHack", "hack"),
            Path.Combine(libraryRoot, steamApps, "common", "DFHack"),
        };

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
                return fromDfHackSub;

            // 4. Steam library auto-detection.
            if (TryFindExeInDirectory(TryFindSteamDFHackFolder()) is { } fromSteam)
                return fromSteam;

            return string.Empty;
        }

        private static string? ResolveDwarfFortressFolderCandidate(string candidate)
        {
            string? resolvedCandidate = ResolveExistingDirectoryPath(candidate);
            if (string.IsNullOrWhiteSpace(resolvedCandidate))
                return string.Empty;

            if (IsLikelyDwarfFortressFolder(resolvedCandidate))
                return resolvedCandidate;

            if (OperatingSystem.IsMacOS())
            {
                string appResources = Path.Combine(resolvedCandidate, "Dwarf Fortress.app", "Contents", "Resources");
                if (IsLikelyDwarfFortressFolder(appResources))
                    return appResources;
            }

            return string.Empty;
        }

        public static IEnumerable<string> EnumerateSteamLibraryRoots()
        {
            lock (_steamLibraryRootsLock)
            {
                if (_cachedSteamLibraryRoots != null)
                {
                    return _cachedSteamLibraryRoots;
                }

                HashSet<string> libraries = new HashSet<string>(
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                List<string> candidateRoots = GetSteamRootCandidates()
                    .Where(root => !string.IsNullOrWhiteSpace(root))
                    .ToList();

                LogAdvancedSteam($"Steam root candidates ({candidateRoots.Count}): {FormatPathListForLog(candidateRoots)}");

                foreach (string root in candidateRoots)
                {
                    if (string.IsNullOrWhiteSpace(root))
                        continue;

                    string normalizedRoot = ResolveCanonicalPath(root);
                    if (string.IsNullOrWhiteSpace(normalizedRoot))
                        continue;

                    if (!Directory.Exists(normalizedRoot))
                        continue;

                    if (Directory.Exists(Path.Combine(normalizedRoot, steamApps)))
                        libraries.Add(normalizedRoot);

                    foreach (string library in ReadSteamLibraryFolders(normalizedRoot))
                    {
                        if (string.IsNullOrWhiteSpace(library))
                            continue;

                        string normalizedLibrary = ResolveCanonicalPath(library);
                        if (string.IsNullOrWhiteSpace(normalizedLibrary))
                            continue;

                        if (Directory.Exists(Path.Combine(normalizedLibrary, steamApps)))
                            libraries.Add(normalizedLibrary);
                    }
                }

                LogAdvancedSteam($"Steam library roots discovered ({libraries.Count}): {FormatPathListForLog(libraries)}");
                _cachedSteamLibraryRoots = libraries.ToList(); // Cache the result
                return _cachedSteamLibraryRoots;
            }
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

        private static IEnumerable<string> GetWindowsSteamRootCandidates()
        {
            HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? registryPath = TryGetWindowsSteamPathFromRegistry();
            if (!string.IsNullOrWhiteSpace(registryPath))
                candidates.Add(registryPath);

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                candidates.Add(Path.Combine(programFilesX86, steamString));

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                candidates.Add(Path.Combine(programFiles, steamString));

            string? steamPathEnv = Environment.GetEnvironmentVariable("STEAM_PATH");
            if (!string.IsNullOrWhiteSpace(steamPathEnv))
                candidates.Add(steamPathEnv);

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

        private static IEnumerable<string> ReadSteamLibraryFolders(string steamRoot)
        {
            HashSet<string> libraries = new HashSet<string>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            string vdfPath = Path.Combine(steamRoot, steamApps, "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                LogAdvancedSteam($"Steam library file missing: {vdfPath}");
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
                        libraries.Add(normalized);
                }
            }
            catch (Exception ex)
            {
                LogAdvancedSteam($"Failed reading Steam library file '{vdfPath}': {ex.Message}");
            }

            LogAdvancedSteam($"Parsed Steam libraries from '{vdfPath}' ({libraries.Count}): {FormatPathListForLog(libraries)}");
            return libraries;
        }

        private static string? TryParseSteamLibraryPath(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            Match match = SteamLibraryPathRegex.Match(line);
            if (match.Success)
                return match.Groups["path"].Value;

            match = SteamLibraryLegacyPathRegex.Match(line);
            if (!match.Success)
                return null;

            string candidate = match.Groups["path"].Value;
            if (candidate.Contains("\\") || candidate.Contains("/") || candidate.Contains(":"))
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

        public static StringComparison GetFileSystemPathComparison()
            => OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public static string? ResolveExistingDirectoryPath(string path)
            => ResolveExistingPath(path, expectDirectory: true);

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

            string remainder = fullPath.Substring(root.Length);
            string[] segments = remainder.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
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
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name) || !Directory.Exists(parent))
                return null;

            string candidate = Path.Combine(parent, name);
            if (expectDirectory)
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }
            else
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            try
            {
                IEnumerable<string> entries = expectDirectory
                    ? Directory.EnumerateDirectories(parent)
                    : Directory.EnumerateFiles(parent);
                return entries.FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        public static string? ResolveInfoFilePath(string modDirectory)
        {
            string? resolvedDirectory = ResolveExistingDirectoryPath(modDirectory);
            if (string.IsNullOrWhiteSpace(resolvedDirectory))
                return null;

            string exactInfoPath = Path.Combine(resolvedDirectory, "info.txt");
            if (File.Exists(exactInfoPath))
                return exactInfoPath;

            try
            {
                return Directory.EnumerateFiles(resolvedDirectory)
                    .FirstOrDefault(file =>
                        string.Equals(Path.GetFileName(file), "info.txt", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }
        public static string FormatPathListForLog(IEnumerable<string> paths, int maxItems = 24)
        {
            if (paths == null)
                return "(none)";

            List<string> list = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizeFileSystemPath(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToList();

            return StringFormatter.FormatListWithMoreIndicator(list, maxItems);
        }

        public static bool TryExtractSteamWorkshopItemIdFromPath(string? path, out string steamItemId)
        {
            steamItemId = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalizedPath = path.Replace('\\', '/');
            Match pathMatch = SteamWorkshopPathRegex.Match(normalizedPath);
            if (!pathMatch.Success)
                return false;

            return TryParsePositiveSteamId(pathMatch.Groups["id"].Value, out steamItemId);
        }

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
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            HashSet<string> steamAppsRoots = new HashSet<string>(comparer);

            foreach (string libraryRoot in EnumerateSteamLibraryRoots())
            {
                if (string.IsNullOrWhiteSpace(libraryRoot))
                    continue;

                string steamAppsRoot = NormalizeFileSystemPath(Path.Combine(libraryRoot, steamApps));
                if (string.IsNullOrWhiteSpace(steamAppsRoot))
                    continue;

                if (!Directory.Exists(steamAppsRoot))
                    continue;

                steamAppsRoots.Add(steamAppsRoot);
            }

            LogAdvancedSteam($"SteamApps roots ({steamAppsRoots.Count}): {FormatPathListForLog(steamAppsRoots)}");
            return steamAppsRoots;
        }

        public static IEnumerable<string> GetSteamWorkshopContentPaths()
        {
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            HashSet<string> paths = new HashSet<string>(comparer);
            List<string> steamAppsRoots = EnumerateSteamAppsRoots().ToList();
            LogAdvancedSteam($"Workshop content scan starting. SteamApps roots input ({steamAppsRoots.Count}).");
            foreach (string steamAppsRoot in steamAppsRoots)
            {
                string candidate = NormalizeFileSystemPath(
                    Path.Combine(steamAppsRoot, "workshop", "content", DwarfFortressSteamAppId));
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (Directory.Exists(candidate))
                {
                    paths.Add(candidate);
                    LogAdvancedSteam($"Workshop content path found: {candidate}");
                }
                else
                {
                    LogAdvancedSteam($"Workshop content path missing: {candidate}");
                }
            }

            LogAdvancedSteam($"Workshop content paths discovered ({paths.Count}): {FormatPathListForLog(paths)}");
            return paths;
        }

        public static IEnumerable<string> GetSteamWorkshopAcfPaths()
        {
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            HashSet<string> paths = new HashSet<string>(comparer);
            List<string> steamAppsRoots = EnumerateSteamAppsRoots().ToList();

            SteamConnectionLogger.Log(
                $"Steam workshop scan started for app {DwarfFortressSteamAppId}. Library roots discovered: {steamAppsRoots.Count}.");

            foreach (string steamAppsRoot in steamAppsRoots)
            {
                if (string.IsNullOrWhiteSpace(steamAppsRoot))
                    continue;

                string primaryCandidate = Path.Combine(steamAppsRoot, $"appworkshop_{DwarfFortressSteamAppId}.acf");
                if (File.Exists(primaryCandidate))
                    paths.Add(NormalizeFileSystemPath(primaryCandidate));

                string workshopCandidate = Path.Combine(steamAppsRoot, "workshop", $"appworkshop_{DwarfFortressSteamAppId}.acf");
                if (File.Exists(workshopCandidate))
                    paths.Add(NormalizeFileSystemPath(workshopCandidate));
            }

            if (paths.Count == 0)
            {
                SteamConnectionLogger.Log("Steam workshop scan completed: no workshop ACF files found.");
            }
            else
            {
                SteamConnectionLogger.Log($"Steam workshop scan completed: found {paths.Count} workshop ACF file(s).");
                foreach (string path in paths.OrderBy(path => path, comparer))
                    SteamConnectionLogger.Log($"Steam workshop ACF: {path}");
            }

            LogAdvancedSteam($"Workshop ACF paths resolved ({paths.Count}): {FormatPathListForLog(paths)}");
            return paths;
        }

        public static string? TryFindInstalledModsPath()
        {
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

            return string.Empty;
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

        public static bool IsInstalledModsUnderGameFolder(string installedModsPath, string? dfFolderPath)
        {
            if (string.IsNullOrWhiteSpace(installedModsPath) || string.IsNullOrWhiteSpace(dfFolderPath))
                return false;

            try
            {
                string installedFull = Path.GetFullPath(installedModsPath);
                string dfFull = Path.GetFullPath(dfFolderPath);
                return IsPathUnderRoot(installedFull, dfFull);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            string normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            normalizedRoot += Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return normalizedPath.StartsWith(normalizedRoot, comparison);
        }

        private static IEnumerable<string> GetAppDataBasePaths()
        {
            HashSet<string> bases = new HashSet<string>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
                bases.Add(appData);

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                bases.Add(localAppData);

            return bases;
        }

        public static string GetDefaultInstalledModsPath()
        {
            string? resolved = TryFindInstalledModsPath();
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            foreach (string candidate in GetInstalledModsPathCandidates().Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        public static IReadOnlyList<ModHearthManager.ConfigIssue> GetConfigIssues()
        {
            List<ModHearthManager.ConfigIssue> issues = new List<ModHearthManager.ConfigIssue>();
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
                issues.Add(new ModHearthManager.ConfigIssue(ModHearthManager.ConfigIssueType.MissingInstalledModsPath, "Installed mods path is not set or missing."));
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

        public static IEnumerable<string> EnumerateModRoots()
        {
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            HashSet<string> seen = new HashSet<string>(comparer);
            List<string> resolvedRoots = new List<string>();

            IEnumerable<string> configuredRoots = EnumerateConfiguredModRoots();
            foreach (string root in configuredRoots)
            {
                string normalizedRoot = NormalizeFileSystemPath(root);
                if (string.IsNullOrWhiteSpace(normalizedRoot))
                    continue;

                if (seen.Add(normalizedRoot))
                    resolvedRoots.Add(normalizedRoot);
            }

            LogAdvancedSteam($"Effective mod roots ({resolvedRoots.Count}): {FormatPathListForLog(resolvedRoots)}");
            foreach (string root in resolvedRoots)
                yield return root;
        }

        public static IEnumerable<string> EnumerateConfiguredModRoots()
        {
            string modsPath = GetModsPath();
            if (!string.IsNullOrWhiteSpace(modsPath))
                yield return modsPath;

            foreach (string workshopPath in GetSteamWorkshopContentPaths())
                yield return workshopPath;

            string installedModsPath = GetInstalledModsPath();
            if (!string.IsNullOrWhiteSpace(installedModsPath))
                yield return installedModsPath;

            if (!string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                string vanillaRoot = GetVanillaModsPath();
                string? vanillaPath = ResolveExistingDirectoryPath(vanillaRoot) ?? vanillaRoot;
                if (!string.IsNullOrWhiteSpace(vanillaPath))
                    yield return vanillaPath;
            }
        }
    }
}