using System.Text.RegularExpressions;
using ModHearth.Models;
using ModHearth.Utils.Logging;
using ModHearth.Utils.IO;
using ModHearth.Utils.DwarfFortress;

using static ModHearth.Manager.ModHearthManager;

/// <summary>
/// Actually handles the logic from models\ModHearthConfig
/// </summary>
namespace ModHearth.Config
{
    public static partial class ConfigManager
    {
        public static ModHearthConfig Config { get; private set; } = new();

        public static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // Guards every read/write of config.json so concurrent callers can't interleave and corrupt the file or clobber each other's writes.
        private static readonly Lock configGate = new();

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

                if (!File.Exists(ConfigPath))
                {
                    Console.WriteLine("Config file missing.");
                    Config = new ModHearthConfig();
                    return;
                }

                if (createLogs) Console.WriteLine("Config file found.");

                if (JsonFileStore.TryLoad(ConfigPath, "Config", out ModHearthConfig? loadedConfig))
                {
                    Config = loadedConfig;
                }
                else
                {
                    Console.WriteLine("Config file borked.");
                    Config = new ModHearthConfig();
                }
            }
        }

        public static void AttemptLoadConfigAndDiscover()
        {
            AttemptLoadConfig();
            AutoDiscoverConfigPaths();

            if (!Config.ShowConsole && !DevMode.IsEnabled)
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

        public static int GetTheme() => Config.Theme;

        public static void SetTheme(int theme) => Update(c => c.Theme = theme, "Theme");

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

        public static bool IsAutoOpenNewWorldSetupEnabled() => Config.IsAutoOpenNewWorldSetupEnabled;

        public static void SetAutoOpenNewWorldSetupEnabled(bool enabled) => Update(c => c.IsAutoOpenNewWorldSetupEnabled = enabled, "Autoclick create new world");

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

        public static IReadOnlyList<ConfigIssue> GetConfigIssues()
        {
            List<ConfigIssue> issues = [];
            if (string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                issues.Add(new ConfigIssue(ConfigIssueType.MissingDwarfFortressPath, "Dwarf Fortress path is not set."));
            }
            else if (!Directory.Exists(Config.DFFolderPath))
            {
                issues.Add(new ConfigIssue(ConfigIssueType.MissingDwarfFortressPath, $"Dwarf Fortress folder not found: {Config.DFFolderPath}"));
            }

            string installedModsPath = GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath) || !Directory.Exists(installedModsPath))
            {
                issues.Add(new ConfigIssue(ConfigIssueType.MissingInstalledModsPath, "installed_mods path is not set or missing."));
            }

            if (string.IsNullOrWhiteSpace(Config.DFHackFolderPath))
            {
                issues.Add(new ConfigIssue(ConfigIssueType.MissingDFHackPath, "DFHack folder path is not set."));
            }
            else if (!Directory.Exists(Config.DFHackFolderPath) || !File.Exists(Path.Combine(Config.DFHackFolderPath, OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run")))
            {
                issues.Add(new ConfigIssue(ConfigIssueType.MissingDFHackPath, "DFHack executable not found in configured folder."));
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

        public static bool GetSearchPossibleIssues() => Config.SearchPossibleIssues;

        public static void SetSearchPossibleIssues(bool enabled) => Update(c => c.SearchPossibleIssues = enabled, "Search possible issues");

        public static bool GetSearchErrorLogIssues() => Config.SearchErrorLogIssues;

        public static void SetSearchErrorLogIssues(bool enabled) => Update(c => c.SearchErrorLogIssues = enabled, "Search errorlog issues");

        public static bool? GetIsBleedingEdge() => Config.IsBleedingEdge;

        public static void SetIsBleedingEdge(bool? isBleedingEdge) => Update(c => c.IsBleedingEdge = isBleedingEdge, "Bleeding edge update channel");

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