using ModHearth.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using ModHearth.Utilities;

namespace ModHearth
{
    /// <summary>
    /// Config class, to store folder information.
    /// </summary>
    [Serializable]
    public class ModHearthConfig
    {
        // Path to DF executable (platform-specific).
        public string DFEXEPath { get; set; } = string.Empty;

        // Optional override for the DF base folder (used when a folder is selected instead of an executable).
        public string DFFolderPathOverride { get; set; } = string.Empty;

        // Optional override for the mods folder path (used when filesystem casing differs).
        public string ModsPathOverride { get; set; } = string.Empty;

        public string DFFolderPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DFFolderPathOverride))
                    return DFFolderPathOverride;
                if (string.IsNullOrWhiteSpace(DFEXEPath))
                    return string.Empty;
                return Path.GetDirectoryName(DFEXEPath) ?? string.Empty;
            }
        }

        public string ModsPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ModsPathOverride))
                    return ModsPathOverride;
                if (string.IsNullOrWhiteSpace(DFFolderPath))
                    return string.Empty;
                return Path.Combine(DFFolderPath, "Mods");
            }
        }

        // Path to installed mods cache.
        public string InstalledModsPath { get; set; } = string.Empty;

        // Should this be in lightmode?
        public int theme { get; set; }

        // Auto-reload interval for modlists in seconds. -1 means disabled by checkbox.
        public int AutoReloadIntervalSeconds { get; set; } = -1;

        // Hide console logs on startup.
        public bool showConsole { get; set; } = true;

    }

    [Serializable]
    public struct ModProblem
    {
        public string problemThrowerID;
        public string problemID;

        public enum ProblemType
        {
            MissingBefore,
            MissingAfter,
            ConflictPresent
        };

        public ProblemType problemType;

        public ModProblem(string problemThrowerID, string problemID, ProblemType problemType)
        {
            this.problemThrowerID = problemThrowerID;
            this.problemID = problemID;
            this.problemType = problemType;
        }

        public override string ToString()
        {
            switch (problemType)
            {
                case ProblemType.MissingBefore:
                    return $"Mod '{problemThrowerID}' requires mod '{problemID}' to be loaded before it.";
                case ProblemType.MissingAfter:
                    return $"Mod '{problemThrowerID}' requires mod '{problemID}' to be loaded after it.";
                case ProblemType.ConflictPresent:
                    return $"Mod '{problemThrowerID}' is incompatible with mod '{problemID}'.";
            }
            return "";
        }
    }

    public enum UserActionRequired
    {
        OpenWorldCreationScreen
    }

    public sealed class UserActionRequiredException : Exception
    {
        public UserActionRequired ActionRequired { get; }

        public UserActionRequiredException(UserActionRequired actionRequired, string message)
            : base(message)
        {
            ActionRequired = actionRequired;
        }
    }

    public partial class ModHearthManager
    {
        public enum ModpackStorageBackend
        {
            DFHackConfig,
            LocalFallback
        }

        public readonly record struct ModpackSaveResult(
            ModpackStorageBackend Backend,
            string Path,
            bool LiveReloadApplied,
            bool LiveReloadDeferred,
            string LiveReloadMessage)
        {
            public bool UsesFallbackStorage => Backend == ModpackStorageBackend.LocalFallback;
        }

        public static string GetBuildVersionString()
        {
            string? buildNumber = Environment.GetEnvironmentVariable("MODHEARTH_BUILD_NUMBER");
            if (!string.IsNullOrWhiteSpace(buildNumber))
                return buildNumber;

            string? infoVersion = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                int plusIndex = infoVersion.IndexOf('+');
                if (plusIndex > 0)
                    infoVersion = infoVersion.Substring(0, plusIndex);
                if (!string.IsNullOrWhiteSpace(infoVersion))
                    return infoVersion;
            }

            return "none";
        }

        // Maps strings to ModReferences. The keys match DFHMods.ToString() perfectly. Given a value V, V.ToDFHMod.ToString() returns it's key.
        private Dictionary<string, ModReference> modrefMap = new(StringComparer.OrdinalIgnoreCase);

        // Get a ModReference given a string key.
        public ModReference GetModRef(string key) => modrefMap[key];

        // Get a DFHMod given a string key.
        public DFHMod GetDFHackMod(string key) => modrefMap[key].ToDFHMod();

        // Get a ModReference given a DFHMod key.
        public ModReference GetRefFromDFHMod(DFHMod dfmod) => modrefMap[dfmod.ToString()];

        // The sorted list of enabled DFHmods. This list is modified by the form, and when saved it overwrites the list of a ModPack.
        public List<DFHMod> enabledMods = new();

        // The unsorted list of disabled DFHmods
        public HashSet<DFHMod> disabledMods = new();

        // The unsorted list of all available DFHmods
        public HashSet<DFHMod> modPool = new();

        // Get the currently selected modpack
        public DFHModpack SelectedModlist => modpacks[selectedModlistIndex];

        // List of all modpacks. After a modpack in this list is modified the list is saved to file.
        public List<DFHModpack> modpacks = new();

        // The index of the currently selected modpack.
        public int selectedModlistIndex;

        // The file config for this class.
        private ModHearthConfig config = new();

        // Paths.
        private static readonly string baseDir = AppContext.BaseDirectory;
        private static readonly string configPath = Path.Combine(baseDir, "config.json");
        private static readonly string styleLightPath = Path.Combine(baseDir, "styles", "style.light.json");
        private static readonly string styleDarkPath = Path.Combine(baseDir, "styles", "style.dark.json");
        private static readonly string modSortRulesPath = Path.Combine(baseDir, "modsort_rules.json");
        private static readonly string localFallbackModpacksPath = Path.Combine(baseDir, "modpacks.local.json");
        private static readonly Regex SteamLibraryPathRegex = new("\"path\"\\s+\"(?<path>.*?)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SteamLibraryLegacyPathRegex = new("^\\s*\"\\d+\"\\s+\"(?<path>.*?)\"", RegexOptions.Compiled);
        private static readonly Regex SteamWorkshopPathRegex = new("/workshop/content/975370/(?<id>\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DuplicateWarningRegex = new("^Duplicate Object:\\s*(?<object>.+?);\\s*Offending mods are\\s*(?<mods>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DuplicateWarningCountRegex = new("\\s*\\(x\\d+\\)\\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private const string DwarfFortressSteamAppId = "975370";

        // Mod problem tracker.
        public List<ModProblem> modproblems = new();
        public bool IsSavingModpacks { get; private set; }
        private HashSet<string> installedCacheModIds = new(StringComparer.OrdinalIgnoreCase);
        private List<ModSortRule> sortRules = new();
        public string LastMissingModsMessage { get; private set; } = string.Empty;
        private DateTime? duplicateWarningLastWriteUtc;
        private Dictionary<string, List<string>> duplicateWarningMap = new(StringComparer.OrdinalIgnoreCase);
        private List<HashSet<string>> duplicateWarningGroups = new();
        private string? lastLoggedErrorLogPath;
        private bool lastLoggedErrorLogExists;
        private readonly object modManagerReloadGate = new();
        private CancellationTokenSource? deferredModManagerReloadCts;
        private ModpackStorageBackend activeModpackBackend = ModpackStorageBackend.LocalFallback;
        private string activeModpackPath = localFallbackModpacksPath;
        private static readonly TimeSpan DeferredModManagerReloadInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DeferredModManagerReloadTimeout = TimeSpan.FromMinutes(5);

        public ModHearthManager()
        {
            Console.WriteLine($"Crafting Hearth v{GetBuildVersionString()}");

            // Get and load config file, fix if needed.
            AttemptLoadConfig();
            LoadSortRules();
        }

        public void Initialize(string? preferredModlistName = null)
        {
            // Find all mods and add to the lists.
            FindAllModsDFHackLua();

            // Find DFHModpacks, and fix them if needed.
            FindModpacks(preferredModlistName);

            // Write some info on found things.
            Console.WriteLine();
            Console.WriteLine($"Found {modrefMap.Count} mods and {modpacks.Count} modlists");
            Console.WriteLine();

            ModUpdateLogger.RecordChanges(modrefMap.Values, enabledMods, GetSteamWorkshopAcfPaths());
        }


        //??
        public ModHearthConfig GetConfig()
        {
            return config;
        }

        public IReadOnlyList<ModSortRule> GetSortRules()
        {
            return sortRules;
        }
        public string GetSortRulesPath() => modSortRulesPath;

        public void SetSortRules(IEnumerable<ModSortRule> rules)
        {
            sortRules = NormalizeSortRules(rules);
            SaveSortRules();
        }

        private static List<ModSortRule> NormalizeSortRules(IEnumerable<ModSortRule>? rules)
        {
            List<ModSortRule> normalized = new List<ModSortRule>();
            if (rules == null)
                return normalized;

            HashSet<string> seenEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenRequires = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModSortRule rule in rules)
            {
                if (rule == null)
                    continue;

                string before = rule.BeforeId?.Trim() ?? string.Empty;
                string after = rule.AfterId?.Trim() ?? string.Empty;
                string requires = rule.RequiresId?.Trim() ?? string.Empty;

                bool hasEdge = !string.IsNullOrWhiteSpace(before) &&
                               !string.IsNullOrWhiteSpace(after) &&
                               !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
                bool hasRequires = !string.IsNullOrWhiteSpace(requires);

                if (!hasEdge && !hasRequires)
                    continue;

                if (hasEdge)
                {
                    string edgeKey = $"{before}>>{after}";
                    if (seenEdges.Add(edgeKey))
                    {
                        normalized.Add(new ModSortRule
                        {
                            BeforeId = before,
                            AfterId = after
                        });
                    }
                }

                if (hasRequires && seenRequires.Add(requires))
                {
                    normalized.Add(new ModSortRule
                    {
                        RequiresId = requires
                    });
                }
            }

            return normalized;
        }

        private void LoadSortRules()
        {
            sortRules = new List<ModSortRule>();
            if (!File.Exists(modSortRulesPath))
                return;

            try
            {
                string jsonContent = File.ReadAllText(modSortRulesPath);
                List<ModSortRule>? loadedRules = JsonSerializer.Deserialize<List<ModSortRule>>(jsonContent);
                if (loadedRules != null)
                    sortRules = NormalizeSortRules(loadedRules);
            }
            catch
            {
                sortRules = new List<ModSortRule>();
            }
        }

        private void SaveSortRules()
        {
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string jsonContent = JsonSerializer.Serialize(sortRules, options);
                File.WriteAllText(modSortRulesPath, jsonContent);
            }
            catch
            {
                // Ignore sort rule save failures.
            }
        }

        public string GetModsPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.ModsPath))
                return string.Empty;

            string configuredPath = NormalizeFileSystemPath(config.ModsPath);
            string? resolved = ResolveExistingDirectoryPath(configuredPath);
            if (string.IsNullOrWhiteSpace(resolved) &&
                !string.IsNullOrWhiteSpace(config.ModsPathOverride) &&
                !string.IsNullOrWhiteSpace(config.DFFolderPath))
            {
                string fallback = Path.Combine(config.DFFolderPath, "Mods");
                resolved = ResolveExistingDirectoryPath(fallback);
            }
            if (string.IsNullOrWhiteSpace(resolved))
                return configuredPath;

            if (!string.Equals(config.ModsPathOverride, resolved, GetFileSystemPathComparison()))
            {
                config.ModsPathOverride = resolved;
                SaveConfigFile();
            }

            return resolved;
        }

        public string GetInstalledModsPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.InstalledModsPath))
                return GetDefaultInstalledModsPath();

            if (IsInstalledModsUnderGameFolder(config.InstalledModsPath, config.DFFolderPath))
                return GetDefaultInstalledModsPath();

            string normalizedConfigured = NormalizeFileSystemPath(config.InstalledModsPath);
            string? resolved = ResolveExistingDirectoryPath(normalizedConfigured);
            if (string.IsNullOrWhiteSpace(resolved))
                return normalizedConfigured;

            if (!string.Equals(config.InstalledModsPath, resolved, GetFileSystemPathComparison()))
            {
                config.InstalledModsPath = resolved;
                SaveConfigFile();
            }

            return resolved;
        }

        public string GetVanillaModsPath()
        {
            if (string.IsNullOrWhiteSpace(config?.DFFolderPath))
                return string.Empty;

            return Path.Combine(config.DFFolderPath, "data", "vanilla");
        }

        public string GetErrorLogPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.DFFolderPath))
                return Path.Combine(AppContext.BaseDirectory, "errorlog.txt");

            return Path.Combine(config.DFFolderPath, "errorlog.txt");
        }

        public string GetModManagerConfigPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.DFFolderPath))
                return string.Empty;
            return Path.Combine(config.DFFolderPath, "dfhack-config", "mod-manager.json");
        }

        public ModpackStorageBackend ActiveModpackBackend
        {
            get
            {
                ResolveActiveModpackStorage();
                return activeModpackBackend;
            }
        }

        public string GetActiveModpackPath() => activeModpackPath;

        public string GetLocalFallbackModpacksPath() => localFallbackModpacksPath;

        public bool HasDfhack()
        {
            string dfhackRunPath = GetDfhackRunPath();
            if (!string.IsNullOrWhiteSpace(dfhackRunPath) && File.Exists(dfhackRunPath))
                return true;

            // sometimes steam leaves critical dfhack files like dfhooks.dlls inside the df folder even after uninstalling,
            // making it near impossible to know whether dfhack works or not before running it
            if (HasDfhackFiles() && !DwarfFortressRunning())
                return true;

            return DFHackRpcClient.IsDFHackRunning(config?.DFFolderPath);
        }

        public bool HasDfhackFiles()
        {
            string dfhackRunPath = GetDfhackRunPath();
            bool hasExe = !string.IsNullOrWhiteSpace(dfhackRunPath) && File.Exists(dfhackRunPath);

            bool hasDll = false;
            if (!string.IsNullOrWhiteSpace(config?.DFFolderPath))
            {
                hasDll = File.Exists(Path.Combine(config.DFFolderPath, "dfhooks.dll"));
            }

            return hasExe || hasDll;
        }

        public bool IsDwarfFortressProcessRunning()
        {
            HashSet<string> knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Dwarf Fortress",
                "df",
                "dwarfort"
            };

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (knownNames.Contains(process.ProcessName))
                        return true;

                    if (!string.IsNullOrWhiteSpace(config?.DFFolderPath))
                    {
                        string? fileName = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(fileName) &&
                            fileName.StartsWith(config.DFFolderPath, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                catch
                {
                    // Ignore processes we cannot inspect.
                }
            }

            return false;
        }



        private void ResolveActiveModpackStorage()
        {
            string dfhackPath = GetModManagerConfigPath();
            if (HasDfhack() && !string.IsNullOrWhiteSpace(dfhackPath))
            {
                SetActiveModpackStorage(ModpackStorageBackend.DFHackConfig, dfhackPath);
                return;
            }

            SetActiveModpackStorage(ModpackStorageBackend.LocalFallback, localFallbackModpacksPath);
        }

        private void SetActiveModpackStorage(ModpackStorageBackend backend, string path)
        {
            activeModpackBackend = backend;
            activeModpackPath = string.IsNullOrWhiteSpace(path)
                ? localFallbackModpacksPath
                : path;

            if (backend == ModpackStorageBackend.LocalFallback)
                CancelDeferredModManagerReload();
        }

        private bool TryReadModpackFile(string path, out List<DFHModpack> loadedModpacks, out string error)
        {
            loadedModpacks = new List<DFHModpack>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = "Modpack file missing.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                List<DFHModpack>? parsed = JsonSerializer.Deserialize<List<DFHModpack>>(json);
                if (parsed == null)
                {
                    error = "Modpack file could not be parsed.";
                    return false;
                }

                loadedModpacks = parsed
                    .Where(modpack => modpack != null)
                    .ToList();
                foreach (DFHModpack modpack in loadedModpacks)
                {
                    modpack.name ??= "Unnamed";
                    modpack.modlist ??= new List<DFHMod>();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                loadedModpacks = new List<DFHModpack>();
                return false;
            }
        }

        private List<DFHModpack> CreateDefaultModpacks()
        {
            DFHModpack newPack = new DFHModpack(true, GenerateVanillaModlist(), "Default");
            return new List<DFHModpack> { newPack };
        }

        public bool CanDeleteModFromModsFolder(ModReference modref)
        {
            if (modref == null || string.IsNullOrWhiteSpace(modref.path) || config == null)
                return false;
            string modsPath = GetModsPath();
            if (string.IsNullOrWhiteSpace(modsPath))
                return false;
            string modPath = NormalizeFileSystemPath(modref.path);
            modsPath = NormalizeFileSystemPath(modsPath);
            if (string.IsNullOrWhiteSpace(modPath) || string.IsNullOrWhiteSpace(modsPath))
                return false;
            if (!IsPathUnderRoot(modPath, modsPath))
                return false;
            return Directory.Exists(modPath);
        }

        public bool DeleteModFromModsFolder(ModReference modref, out string message)
        {
            if (modref == null)
            {
                message = "No mod selected.";
                return false;
            }

            if (!CanDeleteModFromModsFolder(modref))
            {
                message = "Mod is not in the Mods folder or was already removed.";
                return false;
            }

            string modPath = NormalizeFileSystemPath(modref.path);
            if (string.IsNullOrWhiteSpace(modPath))
            {
                message = "Mod path is invalid.";
                return false;
            }
            try
            {
                Directory.Delete(modPath, true);
            }
            catch (Exception ex)
            {
                message = $"Failed to delete mod folder: {ex.Message}";
                return false;
            }

            DFHMod dfm = modref.ToDFHMod();
            modPool.Remove(dfm);
            enabledMods.Remove(dfm);
            disabledMods.Remove(dfm);
            modrefMap.Remove(modref.DFHackCompatibleString());
            FindModlistProblems();
            TryRequestModManagerReload(out _, out _);

            message = $"Deleted {modPath}";
            return true;
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

        private string GetDefaultInstalledModsPath()
        {
            foreach (string candidate in GetInstalledModsPathCandidates())
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private void AutoDiscoverConfigPaths()
        {
            if (config == null)
                config = new ModHearthConfig();

            bool updated = false;
            StringComparison pathComparison = GetFileSystemPathComparison();

            if (string.IsNullOrWhiteSpace(config.DFFolderPath))
            {
                string? dfFolder = TryFindSteamDwarfFortressFolder();
                if (!string.IsNullOrWhiteSpace(dfFolder))
                {
                    config.DFFolderPathOverride = dfFolder;
                    config.DFEXEPath = string.Empty;
                    updated = true;
                }
            }
            else
            {
                string? resolvedDfFolder = ResolveExistingDirectoryPath(config.DFFolderPath);
                if (!string.IsNullOrWhiteSpace(resolvedDfFolder) &&
                    !string.Equals(config.DFFolderPath, resolvedDfFolder, pathComparison))
                {
                    config.DFFolderPathOverride = resolvedDfFolder;
                    updated = true;
                }
            }

            if (string.IsNullOrWhiteSpace(config.InstalledModsPath))
            {
                string? installedMods = TryFindInstalledModsPath();
                if (!string.IsNullOrWhiteSpace(installedMods))
                {
                    config.InstalledModsPath = installedMods;
                    updated = true;
                }
            }
            else
            {
                string? resolvedInstalledMods = ResolveExistingDirectoryPath(config.InstalledModsPath);
                if (!string.IsNullOrWhiteSpace(resolvedInstalledMods) &&
                    !string.Equals(config.InstalledModsPath, resolvedInstalledMods, pathComparison))
                {
                    config.InstalledModsPath = resolvedInstalledMods;
                    updated = true;
                }
            }

            string? resolvedModsPath = ResolveExistingDirectoryPath(config.ModsPath);
            if (string.IsNullOrWhiteSpace(resolvedModsPath) &&
                !string.IsNullOrWhiteSpace(config.ModsPathOverride) &&
                !string.IsNullOrWhiteSpace(config.DFFolderPath))
            {
                resolvedModsPath = ResolveExistingDirectoryPath(Path.Combine(config.DFFolderPath, "Mods"));
            }
            if (!string.IsNullOrWhiteSpace(resolvedModsPath) &&
                !string.Equals(config.ModsPathOverride, resolvedModsPath, pathComparison))
            {
                config.ModsPathOverride = resolvedModsPath;
                updated = true;
            }

            if (updated)
                SaveConfigFile();
        }

        private string? TryFindSteamDwarfFortressFolder()
        {
            foreach (string libraryRoot in EnumerateSteamLibraryRoots())
            {
                if (string.IsNullOrWhiteSpace(libraryRoot))
                    continue;

                string candidate = Path.Combine(libraryRoot, "steamapps", "common", "Dwarf Fortress");
                string? resolved = ResolveDwarfFortressFolderCandidate(candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }

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

        private IEnumerable<string> EnumerateSteamLibraryRoots()
        {
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

                string normalizedRoot = NormalizeFileSystemPath(root);
                if (string.IsNullOrWhiteSpace(normalizedRoot))
                    continue;

                if (!Directory.Exists(normalizedRoot))
                    continue;

                if (Directory.Exists(Path.Combine(normalizedRoot, "steamapps")))
                    libraries.Add(normalizedRoot);

                foreach (string library in ReadSteamLibraryFolders(normalizedRoot))
                {
                    if (string.IsNullOrWhiteSpace(library))
                        continue;

                    string normalizedLibrary = NormalizeFileSystemPath(library);
                    if (string.IsNullOrWhiteSpace(normalizedLibrary))
                        continue;

                    if (Directory.Exists(Path.Combine(normalizedLibrary, "steamapps")))
                        libraries.Add(normalizedLibrary);
                }
            }

            LogAdvancedSteam($"Steam library roots discovered ({libraries.Count}): {FormatPathListForLog(libraries)}");
            return libraries;
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
                yield return Path.Combine(home, "Library", "Application Support", "Steam");
                yield break;
            }

            if (OperatingSystem.IsLinux())
            {
                yield return Path.Combine(home, ".steam", "steam");
                yield return Path.Combine(home, ".steam", "root");
                yield return Path.Combine(home, ".local", "share", "Steam");
                yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
                yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
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
                candidates.Add(Path.Combine(programFilesX86, "Steam"));

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                candidates.Add(Path.Combine(programFiles, "Steam"));

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

            string vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
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
                // Ignore errors when reading Steam library folders.
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

        private static string NormalizeFileSystemPath(string path)
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
                // Ignore normalization failures and keep the original path.
            }

            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static StringComparison GetFileSystemPathComparison()
            => OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static string? ResolveExistingDirectoryPath(string path)
            => ResolveExistingPath(path, expectDirectory: true);

        private static string? ResolveExistingPath(string path, bool expectDirectory)
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

        private static string? ResolveInfoFilePath(string modDirectory)
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

        private static void LogAdvancedSteam(string message)
        {
            if (!DevMode.IsEnabled)
                return;

            SteamConnectionLogger.Log($"[DIAG] {message}");
        }

        private static string FormatPathListForLog(IEnumerable<string> paths, int maxItems = 24)
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

        private static bool TryExtractSteamWorkshopItemIdFromPath(string? path, out string steamItemId)
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

        private static bool IsLikelyDwarfFortressFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            if (Directory.Exists(Path.Combine(path, "data")))
                return true;

            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(Path.Combine(path, "Dwarf Fortress.exe")) || File.Exists(Path.Combine(path, "df.exe")))
                    return true;
            }
            else if (OperatingSystem.IsLinux())
            {
                if (File.Exists(Path.Combine(path, "df")))
                    return true;
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (Directory.Exists(Path.Combine(path, "Dwarf Fortress.app")))
                    return true;
            }

            return false;
        }

        private IEnumerable<string> EnumerateSteamAppsRoots()
        {
            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            HashSet<string> steamAppsRoots = new HashSet<string>(comparer);

            foreach (string libraryRoot in EnumerateSteamLibraryRoots())
            {
                if (string.IsNullOrWhiteSpace(libraryRoot))
                    continue;

                string steamAppsRoot = NormalizeFileSystemPath(Path.Combine(libraryRoot, "steamapps"));
                if (string.IsNullOrWhiteSpace(steamAppsRoot))
                    continue;

                if (!Directory.Exists(steamAppsRoot))
                    continue;

                steamAppsRoots.Add(steamAppsRoot);
            }

            LogAdvancedSteam($"SteamApps roots ({steamAppsRoots.Count}): {FormatPathListForLog(steamAppsRoots)}");
            return steamAppsRoots;
        }

        public IEnumerable<string> GetSteamWorkshopContentPaths()
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

        public IEnumerable<string> GetSteamWorkshopAcfPaths()
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

        private string? TryFindInstalledModsPath()
        {
            foreach (string candidate in GetInstalledModsPathCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string? resolved = ResolveExistingDirectoryPath(candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
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
                yield return Path.Combine(basePath, "Dwarf Fortress", "data", "installed_mods");
                yield return Path.Combine(basePath, "Bay 12 Games", "Dwarf Fortress", "data", "installed_mods");
            }
        }

        private IEnumerable<string> GetLinuxProtonInstalledModsPathCandidates()
        {
            if (!OperatingSystem.IsLinux())
                yield break;

            foreach (string libraryRoot in EnumerateSteamLibraryRoots())
            {
                if (string.IsNullOrWhiteSpace(libraryRoot))
                    continue;

                string compatRoot = Path.Combine(libraryRoot, "steamapps", "compatdata", DwarfFortressSteamAppId, "pfx",
                    "drive_c", "users", "steamuser", "AppData");

                yield return Path.Combine(compatRoot, "Local", "Dwarf Fortress", "data", "installed_mods");
                yield return Path.Combine(compatRoot, "Local", "Bay 12 Games", "Dwarf Fortress", "data", "installed_mods");
                yield return Path.Combine(compatRoot, "Roaming", "Dwarf Fortress", "data", "installed_mods");
                yield return Path.Combine(compatRoot, "Roaming", "Bay 12 Games", "Dwarf Fortress", "data", "installed_mods");
            }
        }

        private static bool IsInstalledModsUnderGameFolder(string installedModsPath, string? dfFolderPath)
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

        public bool ClearInstalledModsFolder(out string message)
        {
            string installedModsPath = GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath))
            {
                message = "Installed mods path is not set.";
                return false;
            }

            if (!Directory.Exists(installedModsPath))
            {
                message = $"Installed mods folder not found:\n{installedModsPath}";
                return false;
            }

            int deleted = 0;
            List<string> failures = new List<string>();
            foreach (string entry in Directory.EnumerateFileSystemEntries(installedModsPath))
            {
                try
                {
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, true);
                    else if (File.Exists(entry))
                        File.Delete(entry);
                    deleted++;
                }
                catch
                {
                    failures.Add(Path.GetFileName(entry));
                }
            }

            if (failures.Count > 0)
            {
                message = "Failed to delete: " + string.Join(", ", failures);
                return false;
            }

            message = $"Cleared {deleted} item(s).";
            RefreshInstalledCacheModIds();
            return true;
        }

        public HashSet<string> GetInstalledCacheModIds()
        {
            if (installedCacheModIds == null)
                installedCacheModIds = BuildInstalledCacheModIds();
            return installedCacheModIds;
        }

        public void RefreshInstalledCacheModIds()
        {
            installedCacheModIds = BuildInstalledCacheModIds();
        }

        private HashSet<string> BuildInstalledCacheModIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> roots = new List<string>();

            string installedModsPath = GetInstalledModsPath();
            if (!string.IsNullOrWhiteSpace(installedModsPath))
                roots.Add(ResolveExistingDirectoryPath(installedModsPath) ?? installedModsPath);

            if (!string.IsNullOrWhiteSpace(config?.DFFolderPath))
            {
                string? vanillaPath = GetVanillaModsPath();
                if (!string.IsNullOrWhiteSpace(vanillaPath))
                    roots.Add(ResolveExistingDirectoryPath(vanillaPath) ?? vanillaPath);
            }

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                foreach (string dir in EnumerateModDirectoriesWithInfo(root))
                {
                    string? infoPath = ResolveInfoFilePath(dir);
                    if (string.IsNullOrWhiteSpace(infoPath))
                        continue;
                    try
                    {
                        string info = File.ReadAllText(infoPath);
                        Match idMatch = Regex.Match(info, @"\[ID:([^\]]+)\]", RegexOptions.IgnoreCase);
                        if (idMatch.Success)
                        {
                            string id = idMatch.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(id))
                                ids.Add(id);
                        }
                    }
                    catch
                    {
                        // Ignore unreadable info files.
                    }
                }
            }

            return ids;
        }

        private void FindAllModsDFHackLua()
        {
            if (!DwarfFortressRunning())
            {
                Console.WriteLine("DF not running. Falling back to filesystem scan.");
                LogAdvancedSteam("DF process not detected. Using filesystem mod scan.");
                FindAllModsFromDisk();
                return;
            }

            // Initialize relevant variables.
            modrefMap = new Dictionary<string, ModReference>(StringComparer.OrdinalIgnoreCase);
            modPool = new HashSet<DFHMod>();

            // Get all mod folders.
            Console.WriteLine("Finding all mods... ");

            HashSet<Dictionary<string, string>> modData;
            try
            {
                modData = GetModMemoryData();
            }
            catch (UserActionRequiredException)
            {
                Console.WriteLine("DF not on world creation screen. Falling back to filesystem scan.");
                LogAdvancedSteam("DFHack memory query unavailable (not on world creation screen). Using filesystem mod scan.");
                FindAllModsFromDisk();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DFHack memory query failed. Falling back to filesystem scan: {ex.Message}");
                LogAdvancedSteam($"DFHack memory query failed. Using filesystem mod scan. Error: {ex.Message}");
                FindAllModsFromDisk();
                return;
            }

            LogAdvancedSteam($"DFHack memory mod entries received: {modData.Count}.");
            Dictionary<string, string> modIdPathMap = BuildModIdPathMap();

            foreach (Dictionary<string, string> modDataEntry in modData)
            {
                // Directory correction.
                modDataEntry["src_dir"] = ResolveModPath(modDataEntry, modIdPathMap);

                // Mod setup and registry.
                ModReference modRef = new ModReference(modDataEntry);
                string key = modRef.DFHackCompatibleString();
                Console.WriteLine($"   Mod found + registered: {modRef.name}.");
                modrefMap.Add(key, modRef);
                modPool.Add(modRef.ToDFHMod());
            }
        }

        private void FindAllModsFromDisk()
        {
            modrefMap = new Dictionary<string, ModReference>(StringComparer.OrdinalIgnoreCase);
            modPool = new HashSet<DFHMod>();
            bool diagnosticsEnabled = DevMode.IsEnabled;

            Console.WriteLine("Finding all mods (filesystem)...");

            foreach (string root in EnumerateModRoots())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                int candidateCount = 0;
                int addedCount = 0;
                int duplicateCount = 0;
                foreach (string dir in EnumerateModDirectoriesWithInfo(root))
                {
                    candidateCount++;
                    string? infoPath = ResolveInfoFilePath(dir);
                    if (string.IsNullOrWhiteSpace(infoPath))
                        continue;
                    Dictionary<string, string> modData = BuildModMemoryDataFromInfo(infoPath, dir, out bool missingVersion);
                    if (!modData.TryGetValue("id", out string? id) || string.IsNullOrWhiteSpace(id))
                        continue;

                    ModReference modRef = new ModReference(modData)
                    {
                        MissingVersion = missingVersion
                    };

                    string key = modRef.DFHackCompatibleString();
                    if (modrefMap.ContainsKey(key))
                    {
                        duplicateCount++;
                        continue;
                    }

                    Console.WriteLine($"   Mod found + registered: {modRef.name}.");
                    modrefMap.Add(key, modRef);
                    modPool.Add(modRef.ToDFHMod());
                    addedCount++;
                }

                if (diagnosticsEnabled)
                {
                    LogAdvancedSteam(
                        $"Disk mod scan root='{NormalizeFileSystemPath(root)}' candidates={candidateCount}, added={addedCount}, duplicates={duplicateCount}, total_registered={modrefMap.Count}.");
                }
            }

            if (diagnosticsEnabled)
                LogAdvancedSteam($"Disk mod scan completed. Total registered mods={modrefMap.Count}.");
        }

        private IEnumerable<string> EnumerateModRoots()
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

        private IEnumerable<string> EnumerateConfiguredModRoots()
        {
            string modsPath = GetModsPath();
            if (!string.IsNullOrWhiteSpace(modsPath))
                yield return modsPath;

            foreach (string workshopPath in GetSteamWorkshopContentPaths())
                yield return workshopPath;

            string installedModsPath = GetInstalledModsPath();
            if (!string.IsNullOrWhiteSpace(installedModsPath))
                yield return installedModsPath;

            if (!string.IsNullOrWhiteSpace(config?.DFFolderPath))
            {
                string vanillaRoot = GetVanillaModsPath();
                string? vanillaPath = ResolveExistingDirectoryPath(vanillaRoot) ?? vanillaRoot;
                if (!string.IsNullOrWhiteSpace(vanillaPath))
                    yield return vanillaPath;
            }
        }

        private static IEnumerable<string> EnumerateModDirectoriesWithInfo(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                yield break;

            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            HashSet<string> seen = new HashSet<string>(comparer);

            foreach (string candidate in Directory.EnumerateDirectories(root))
            {
                if (!string.IsNullOrWhiteSpace(ResolveInfoFilePath(candidate)))
                {
                    if (seen.Add(candidate))
                        yield return candidate;
                    continue;
                }

                foreach (string nested in Directory.EnumerateDirectories(candidate))
                {
                    if (string.IsNullOrWhiteSpace(ResolveInfoFilePath(nested)))
                        continue;

                    if (seen.Add(nested))
                        yield return nested;
                }
            }
        }

        private static Dictionary<string, string> BuildModMemoryDataFromInfo(string infoPath, string modPath, out bool missingVersion)
        {
            missingVersion = false;
            string info = File.ReadAllText(infoPath);
            Dictionary<string, string> tags = ParseInfoTags(info);

            string id = GetInfoTag(tags, "ID") ?? string.Empty;
            string name = GetInfoTag(tags, "NAME") ?? Path.GetFileName(modPath);
            string author = GetInfoTag(tags, "AUTHOR") ?? string.Empty;
            string description = GetInfoTag(tags, "DESCRIPTION") ?? string.Empty;

            string displayedVersion = GetInfoTag(tags, "DISPLAYED_VERSION") ??
                                      GetInfoTag(tags, "VERSION") ?? string.Empty;
            string numericVersion = GetInfoTag(tags, "NUMERIC_VERSION") ??
                                    ExtractNumericVersion(displayedVersion) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(numericVersion))
            {
                numericVersion = "1";
                missingVersion = true;
            }

            string earliestCompatibleNumeric = GetInfoTag(tags, "EARLIEST_COMPATIBLE_NUMERIC_VERSION") ??
                                              GetInfoTag(tags, "EARLIEST_COMPATIBLE_VERSION") ??
                                              numericVersion;
            string earliestCompatibleDisplayed = GetInfoTag(tags, "EARLIEST_COMPATIBLE_DISPLAYED_VERSION") ??
                                                GetInfoTag(tags, "EARLIEST_COMPATIBLE_VERSION") ??
                                                displayedVersion;
            string steamFileId = (GetInfoTag(tags, "STEAM_FILE_ID") ?? string.Empty).Trim();
            if (!TryParsePositiveSteamId(steamFileId, out string normalizedSteamFileId))
            {
                if (TryExtractSteamWorkshopItemIdFromPath(modPath, out string steamItemIdFromPath))
                    steamFileId = steamItemIdFromPath;
            }
            else
            {
                steamFileId = normalizedSteamFileId;
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = id,
                ["numeric_version"] = numericVersion,
                ["displayed_version"] = displayedVersion,
                ["earliest_compatible_numeric_version"] = earliestCompatibleNumeric ?? numericVersion,
                ["earliest_compatible_displayed_version"] = earliestCompatibleDisplayed ?? displayedVersion,
                ["author"] = author,
                ["name"] = name,
                ["description"] = description,
                ["steam_file_id"] = steamFileId,
                ["steam_title"] = GetInfoTag(tags, "STEAM_TITLE") ?? string.Empty,
                ["steam_description"] = GetInfoTag(tags, "STEAM_DESCRIPTION") ?? string.Empty,
                ["src_dir"] = modPath
            };
        }

        private static Dictionary<string, string> ParseInfoTags(string info)
        {
            Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(info))
                return tags;

            foreach (Match match in Regex.Matches(info, @"\[(?<tag>[A-Z0-9_]+):(?<value>[^\]]*)\]", RegexOptions.IgnoreCase))
            {
                string tag = match.Groups["tag"].Value.Trim();
                string value = match.Groups["value"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(tag))
                    tags[tag] = value;
            }

            return tags;
        }

        private static string? GetInfoTag(Dictionary<string, string> tags, string key)
            => tags.TryGetValue(key, out string? value) ? value : null;

        private static string? ExtractNumericVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            Match match = Regex.Match(value, @"\d+(?:\.\d+)*");
            return match.Success ? match.Value : null;
        }

        private Dictionary<string, string> BuildModIdPathMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in EnumerateModRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string dir in EnumerateModDirectoriesWithInfo(root))
                {
                    string? infoPath = ResolveInfoFilePath(dir);
                    if (string.IsNullOrWhiteSpace(infoPath))
                        continue;
                    try
                    {
                        string info = File.ReadAllText(infoPath);
                        Match idMatch = Regex.Match(info, @"\[ID:([^\]]+)\]", RegexOptions.IgnoreCase);
                        if (idMatch.Success)
                        {
                            string id = idMatch.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(id))
                            {
                                if (!map.ContainsKey(id))
                                    map[id] = dir;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore unreadable info files.
                    }
                }
            }

            LogAdvancedSteam($"Mod ID path map entries: {map.Count}.");
            return map;
        }

        private string ResolveModPath(Dictionary<string, string> modDataEntry, Dictionary<string, string> modIdPathMap)
        {
            string rawSrcDir = modDataEntry.TryGetValue("src_dir", out string? srcDirValue)
                ? srcDirValue ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(rawSrcDir))
                return string.Empty;

            // Already absolute and valid.
            string? resolvedRawSrcDir = ResolveExistingDirectoryPath(rawSrcDir);
            if (!string.IsNullOrWhiteSpace(resolvedRawSrcDir))
                return resolvedRawSrcDir;

            string fullPath = string.IsNullOrWhiteSpace(config?.DFFolderPath)
                ? rawSrcDir
                : Path.Combine(config.DFFolderPath, rawSrcDir);

            string? resolvedFullPath = ResolveExistingDirectoryPath(fullPath);
            if (!string.IsNullOrWhiteSpace(resolvedFullPath))
                return resolvedFullPath;

            // Try matching the folder name in known roots.
            string rawFolderName = Path.GetFileName(rawSrcDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(rawFolderName))
            {
                foreach (string root in EnumerateModRoots())
                {
                    if (string.IsNullOrWhiteSpace(root))
                        continue;

                    string candidate = Path.Combine(root, rawFolderName);
                    string? resolvedCandidate = ResolveExistingDirectoryPath(candidate);
                    if (!string.IsNullOrWhiteSpace(resolvedCandidate))
                        return resolvedCandidate;
                }
            }

            // Fall back to ID-based lookup.
            if (modDataEntry.TryGetValue("id", out string? id) &&
                !string.IsNullOrWhiteSpace(id) &&
                modIdPathMap.TryGetValue(id, out string? mappedPath) &&
                !string.IsNullOrWhiteSpace(mappedPath))
            {
                string? resolvedMappedPath = ResolveExistingDirectoryPath(mappedPath);
                if (!string.IsNullOrWhiteSpace(resolvedMappedPath))
                    return resolvedMappedPath;
                return mappedPath;
            }

            if (DevMode.IsEnabled)
            {
                string modId = modDataEntry.TryGetValue("id", out string? unresolvedId)
                    ? (unresolvedId ?? string.Empty)
                    : string.Empty;
                LogAdvancedSteam(
                    $"ResolveModPath fallback unresolved. id='{modId}', raw_src='{rawSrcDir}', resolved='{NormalizeFileSystemPath(fullPath)}'.");
            }

            return NormalizeFileSystemPath(fullPath);
        }

        // Output a dictionary, that given a modID gets the true version.
        private HashSet<Dictionary<string, string>> GetModMemoryData()
        {
            HashSet<Dictionary<string, string>> modData = new HashSet<Dictionary<string, string>>();

            // Load raw memory data string, and parse it with regex
            string RawModData = LoadModMemoryData();

            if (RawModData.StartsWith('0'))
            {
                throw new UserActionRequiredException(
                    UserActionRequired.OpenWorldCreationScreen,
                    "Please navigate to the world creation screen so DFHack can read mod data.");
            }

            // Split into mods, then loop through and extract headers.
            string[] singleModDataPairs = RawModData.Split("___");
            Console.WriteLine("Mods found: " + singleModDataPairs.Length);
            foreach (string simpleModDataPair in singleModDataPairs)
            {
                // Split into headers and non headers. Deserialize headers into dict.
                string[] pairArr = simpleModDataPair.Split("===");
                string[] nonHeaders = pairArr[0].Split('|');
                Dictionary<string, string>? headers = JsonSerializer.Deserialize<Dictionary<string, string>>(pairArr[1]);
                if (headers == null)
                    continue;
                modData.Add(headers);
                // Mod found/registered logging happens after registration.

                // To see which headers there are to choose from.
                //foreach (string k in headers.Keys)
                //Console.WriteLine($"header found. k: {k}, v: {headers[k]}");

            }

            return modData;
        }

        // Use dfhack-run.exe and lua to get raw mod data.
        private string LoadModMemoryData()
        {
            // Get path to lua script.
            string luaPath = Path.Combine(AppContext.BaseDirectory, "lua", "GetModMemoryData.lua");
            if (!File.Exists(luaPath))
                throw new FileNotFoundException("GetModMemoryData.lua not found.", luaPath);

            // Try direct RPC first (faster, avoids spawning processes)
            string? rpcOutput = DFHackRpcClient.ExecuteDFHackCommandViaRpc("lua", new List<string> { "-f", luaPath }, config?.DFFolderPath, out string rpcError);
            if (rpcOutput != null)
            {
                return rpcOutput;
            }

            Console.WriteLine($"DFHack RPC failed ({rpcError}). Falling back to dfhack-run process.");

            string dfhackRunPath = GetDfhackRunPath();
            if (string.IsNullOrWhiteSpace(dfhackRunPath) || !File.Exists(dfhackRunPath))
                throw new FileNotFoundException("dfhack-run executable not found.", dfhackRunPath);

            // Set up dfhack process.
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = dfhackRunPath,
                WorkingDirectory = config!.DFFolderPath,
                Arguments = $"lua -f \"{luaPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Start dfhack process.
            Process process = new Process
            {
                StartInfo = processStartInfo
            };
            process.Start();

            // Get output string.
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            // Wait for the process to exit.
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(error))
                Console.WriteLine(error.TrimEnd());

            return output;
        }

        private string GetDfhackRunPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.DFFolderPath))
                return string.Empty;

            string exeName = OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run";
            string candidate = Path.Combine(config.DFFolderPath, exeName);
            if (File.Exists(candidate))
                return candidate;

            string altCandidate = Path.Combine(config.DFFolderPath, "hack", exeName);
            if (File.Exists(altCandidate))
                return altCandidate;

            return candidate;
        }

        // Check if DF is running.
        public bool DwarfFortressRunning()
        {
            HashSet<string> knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Dwarf Fortress",
                "df",
                "dwarfort"
            };

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (knownNames.Contains(process.ProcessName))
                        return true;

                    if (!string.IsNullOrWhiteSpace(config?.DFFolderPath))
                    {
                        string? fileName = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(fileName) &&
                            fileName.StartsWith(config.DFFolderPath, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                catch
                {
                    // Ignore processes we cannot inspect.
                }
            }

            // Supplemental check: Is DFHack RPC listening?
            if (DFHackRpcClient.IsDFHackRunning(config?.DFFolderPath))
                return true;

            return false;
        }

        // Alter the current modpack with enabledMods and save modpack list.
        public ModpackSaveResult SaveCurrentModpack()
        {
            SelectedModlist.modlist = new List<DFHMod>(enabledMods);

            return SaveAllModpacks();
        }

        // Save the DFHModpack list to the active backend file.
        public ModpackSaveResult SaveAllModpacks()
            => SaveAllModpacks(requestLiveReload: true);

        private ModpackSaveResult SaveAllModpacks(bool requestLiveReload)
        {
            Console.WriteLine("Modlists saved.");

            ResolveActiveModpackStorage();
            ModpackStorageBackend backend = activeModpackBackend;
            string path = activeModpackPath;

            try
            {
                WriteModpackFile(path, modpacks);
            }
            catch (Exception ex) when (backend == ModpackStorageBackend.DFHackConfig)
            {
                Console.WriteLine($"Failed to save DFHack modlist file: {ex.Message}. Falling back to local modpacks.");
                SetActiveModpackStorage(ModpackStorageBackend.LocalFallback, localFallbackModpacksPath);
                backend = activeModpackBackend;
                path = activeModpackPath;
                WriteModpackFile(path, modpacks);
            }

            bool liveReloadApplied = false;
            bool liveReloadDeferred = false;
            string liveReloadMessage = string.Empty;

            if (requestLiveReload)
            {
                liveReloadApplied = TryRequestModManagerReload(out liveReloadDeferred, out liveReloadMessage);
            }

            return new ModpackSaveResult(
                backend,
                path,
                liveReloadApplied,
                liveReloadDeferred,
                liveReloadMessage);
        }

        private void WriteModpackFile(string path, List<DFHModpack> modpacksToWrite)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Modpack path is not set.");

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string modlistJson = JsonSerializer.Serialize(modpacksToWrite, GetModpackJsonOptions());
            IsSavingModpacks = true;
            try
            {
                File.WriteAllText(path, modlistJson);
            }
            finally
            {
                IsSavingModpacks = false;
            }
        }

        private static JsonSerializerOptions GetModpackJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true
            };
        }

        private bool TryRequestModManagerReload(out bool deferred, out string message)
        {
            deferred = false;
            message = string.Empty;

            if (activeModpackBackend != ModpackStorageBackend.DFHackConfig)
            {
                message = "Saved locally. In-game apply requires DFHack.";
                return false;
            }

            if (!HasDfhack())
            {
                message = "DFHack not found. In-game apply skipped.";
                return false;
            }

            if (!DwarfFortressRunning())
            {
                message = "Dwarf Fortress is not running. In-game apply skipped.";
                return false;
            }

            const int initialChecks = 1;
            bool reloaded = ReloadDFHackModManagerScreen();
            if (reloaded)
            {
                Console.WriteLine("[ModHearth] Mod manager reload applied after 1 check.");
                CancelDeferredModManagerReload();
                message = "Modpack saved and applied to the DFHack mod manager.";
                return true;
            }

            ScheduleDeferredModManagerReload(initialChecks);
            deferred = true;
            message = "Modpack saved. In-game apply is waiting for the mod manager screen.";
            return false;
        }

        private bool ReloadDFHackModManagerScreen()
        {
            if (!DwarfFortressRunning())
                return false;

            string luaPath = Path.Combine(AppContext.BaseDirectory, "lua", "ReloadModManager.lua");
            if (!File.Exists(luaPath))
                return false;

            // Try direct RPC first (faster, avoids spawning processes)
            string? rpcOutput = DFHackRpcClient.ExecuteDFHackCommandViaRpc("lua", new List<string> { "-f", luaPath }, config?.DFFolderPath, out string rpcError);
            if (rpcOutput != null)
            {
                if (!string.IsNullOrWhiteSpace(rpcOutput))
                    Console.WriteLine(rpcOutput.TrimEnd());

                bool applied = !string.IsNullOrWhiteSpace(rpcOutput) &&
                    (rpcOutput.Contains("[ModHearth] Reloading mod manager screen.", StringComparison.OrdinalIgnoreCase) ||
                     rpcOutput.Contains("[ModHearth] Applying default modlist to Mods screen.", StringComparison.OrdinalIgnoreCase) ||
                     rpcOutput.Contains("[ReloadManager] Reloading mod manager screen.", StringComparison.OrdinalIgnoreCase) ||
                     rpcOutput.Contains("[ReloadManager] Applying default modlist to Mods screen.", StringComparison.OrdinalIgnoreCase));
                return applied;
            }

            Console.WriteLine($"DFHack RPC failed ({rpcError}). Falling back to dfhack-run process.");

            string dfhackRunPath = GetDfhackRunPath();
            if (string.IsNullOrWhiteSpace(dfhackRunPath) || !File.Exists(dfhackRunPath))
                return false;

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = dfhackRunPath,
                WorkingDirectory = config!.DFFolderPath,
                Arguments = $"lua -f \"{luaPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using Process process = new Process { StartInfo = processStartInfo };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output))
                    Console.WriteLine(output.TrimEnd());
                if (!string.IsNullOrWhiteSpace(error))
                    Console.WriteLine(error.TrimEnd());

                bool applied = !string.IsNullOrWhiteSpace(output) &&
                    (output.Contains("[ModHearth] Reloading mod manager screen.", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("[ModHearth] Applying default modlist to Mods screen.", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("[ReloadManager] Reloading mod manager screen.", StringComparison.OrdinalIgnoreCase) ||
                     output.Contains("[ReloadManager] Applying default modlist to Mods screen.", StringComparison.OrdinalIgnoreCase));
                return applied;
            }
            catch
            {
                // Ignore reload failures to avoid disrupting saving.
                return false;
            }
        }

        private void ScheduleDeferredModManagerReload(int initialChecks)
        {
            if (!DwarfFortressRunning())
                return;

            CancellationTokenSource cts = new CancellationTokenSource();
            lock (modManagerReloadGate)
            {
                deferredModManagerReloadCts?.Cancel();
                deferredModManagerReloadCts?.Dispose();
                deferredModManagerReloadCts = cts;
            }

            Console.WriteLine("[ModHearth] Mod manager reload deferred; waiting for moddable screen.");
            _ = Task.Run(() => DeferredModManagerReloadWorkerAsync(cts, initialChecks));
        }

        private void CancelDeferredModManagerReload()
        {
            lock (modManagerReloadGate)
            {
                deferredModManagerReloadCts?.Cancel();
                deferredModManagerReloadCts?.Dispose();
                deferredModManagerReloadCts = null;
            }
        }

        private async Task DeferredModManagerReloadWorkerAsync(CancellationTokenSource cts, int initialChecks)
        {
            DateTime deadline = DateTime.UtcNow + DeferredModManagerReloadTimeout;
            int checksPerformed = Math.Max(initialChecks, 0);
            bool applied = false;
            try
            {
                while (!cts.IsCancellationRequested && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        await Task.Delay(DeferredModManagerReloadInterval, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (cts.IsCancellationRequested)
                        break;

                    checksPerformed++;
                    if (ReloadDFHackModManagerScreen())
                    {
                        applied = true;
                        break;
                    }
                }
            }
            finally
            {
                if (applied)
                {
                    Console.WriteLine($"[ModHearth] Deferred mod manager reload applied after {checksPerformed} checks.");
                }
                else if (!cts.IsCancellationRequested && DateTime.UtcNow >= deadline)
                {
                    Console.WriteLine($"[ModHearth] Deferred mod manager reload timed out after {checksPerformed} checks.");
                }

                lock (modManagerReloadGate)
                {
                    if (ReferenceEquals(deferredModManagerReloadCts, cts))
                    {
                        deferredModManagerReloadCts.Dispose();
                        deferredModManagerReloadCts = null;
                    }
                    else
                    {
                        cts.Dispose();
                    }
                }
            }
        }

        public void SetSelectedModpack(int index)
        {
            // Regenerate enabled and disabled lists to match newly selected modpack.
            selectedModlistIndex = index;
            SetActiveMods(SelectedModlist.modlist);

            // Find problems with newly selected modpack.
            FindModlistProblems();
        }

        // Changes currently enabled and disabled mods based on the given list.
        // The only time this is called (other than SetSelectedModpack) is when overwriting a modpack due to importing.
        public void SetActiveMods(List<DFHMod> mods)
        {
            enabledMods = new List<DFHMod>();
            disabledMods = new HashSet<DFHMod>(modPool);
            for (int i = 0; i < mods.Count; i++)
            {
                enabledMods.Add(mods[i]);
                disabledMods.Remove(mods[i]);
            }
        }

        public void MoveMods(List<DFHMod> mods, int newIndex, bool sourceLeft, bool destinationLeft)
        {
            if (mods == null || mods.Count == 0)
                return;

            List<DFHMod> uniqueMods = new List<DFHMod>();
            HashSet<DFHMod> seen = new HashSet<DFHMod>();
            foreach (DFHMod mod in mods)
            {
                if (seen.Add(mod))
                    uniqueMods.Add(mod);
            }

            if (uniqueMods.Count == 0)
                return;

            bool changed = false;

            if (sourceLeft && destinationLeft)
            {
                return;
            }
            else if (!sourceLeft && !destinationLeft)
            {
                HashSet<DFHMod> selectedSet = new HashSet<DFHMod>(uniqueMods);
                List<DFHMod> selectedInOrder = enabledMods.Where(m => selectedSet.Contains(m)).ToList();
                if (selectedInOrder.Count == 0)
                    return;

                int clampedIndex = Math.Max(0, Math.Min(newIndex, enabledMods.Count));
                int selectedBefore = enabledMods.Take(clampedIndex).Count(m => selectedSet.Contains(m));
                int targetIndex = clampedIndex - selectedBefore;

                List<DFHMod> remaining = enabledMods.Where(m => !selectedSet.Contains(m)).ToList();
                targetIndex = Math.Max(0, Math.Min(targetIndex, remaining.Count));

                List<DFHMod> newList = new List<DFHMod>();
                newList.AddRange(remaining.Take(targetIndex));
                newList.AddRange(selectedInOrder);
                newList.AddRange(remaining.Skip(targetIndex));

                if (!enabledMods.SequenceEqual(newList))
                {
                    enabledMods = newList;
                    changed = true;
                }
            }
            else if (!sourceLeft && destinationLeft)
            {
                HashSet<DFHMod> selectedSet = new HashSet<DFHMod>(uniqueMods);
                int beforeCount = enabledMods.Count;
                enabledMods = enabledMods.Where(m => !selectedSet.Contains(m)).ToList();
                foreach (DFHMod mod in uniqueMods)
                    disabledMods.Add(mod);
                changed = enabledMods.Count != beforeCount;
            }
            else if (sourceLeft && !destinationLeft)
            {
                foreach (DFHMod mod in uniqueMods)
                    disabledMods.Remove(mod);

                int insertIndex = Math.Max(0, Math.Min(newIndex, enabledMods.Count));
                enabledMods.InsertRange(insertIndex, uniqueMods);
                changed = true;
            }

            if (changed)
                FindModlistProblems();
        }

        // Go through modlist and scan for problems.
        // Tuple representing problem has problem mod, int problemType (missing before, missing after, conflict present), and string modID.
        public void FindModlistProblems()
        {
            // Set up list of problems to return.
            modproblems = new List<ModProblem>();

            // Set up a hashset of scanned mods and unscanned mods, for determining load order.
            HashSet<string> scannedModIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> unscannedModIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add all enabled mod IDs to unscanned.
            foreach (DFHMod dfm in enabledMods)
            {
                unscannedModIDs.Add(dfm.id);
            }

            // Loop through enabled mods, doing a mock load.
            for (int i = 0; i < enabledMods.Count; i++)
            {
                DFHMod currentDFM = enabledMods[i];
                ModReference currentMod = GetRefFromDFHMod(currentDFM);

                // Check for problems.
                if (currentMod.problematic)
                {
                    foreach (string beforeID in currentMod.require_before_me)
                        if (!scannedModIDs.Contains(beforeID))
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, beforeID, ModProblem.ProblemType.MissingBefore));
                            //Console.WriteLine("Problem found: missing before mod with ID: " + beforeID + " mod needing is: " + currentDFM.id);
                        }
                    foreach (string afterID in currentMod.require_after_me)
                        if (!unscannedModIDs.Contains(afterID))
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, afterID, ModProblem.ProblemType.MissingAfter));
                            //Console.WriteLine("Problem found: missing after mod with ID: " + afterID + " mod needing is: " + currentDFM.id);
                        }
                    foreach (string conflictID in currentMod.conflicts_with)
                        if (scannedModIDs.Contains(conflictID) || unscannedModIDs.Contains(conflictID))
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, conflictID, ModProblem.ProblemType.ConflictPresent));
                            //Console.WriteLine("Problem found: conflict present mod with ID: " + conflictID + " mod needing is: " + currentDFM.id);
                        }
                }

                // Move to scanned.
                scannedModIDs.Add(currentDFM.id);
                unscannedModIDs.Remove(currentDFM.id);
            }
        }

        public IReadOnlyDictionary<string, List<string>> GetDuplicateWarningMap()
        {
            EnsureDuplicateWarningCache(logFound: true);
            return duplicateWarningMap;
        }

        private void EnsureDuplicateWarningCache(bool logFound)
        {
            string errorLogPath = GetErrorLogPath();
            bool exists = File.Exists(errorLogPath);
            if (logFound && exists &&
                (!string.Equals(lastLoggedErrorLogPath, errorLogPath, StringComparison.OrdinalIgnoreCase) || !lastLoggedErrorLogExists))
            {
                Console.WriteLine($"Error log found: {errorLogPath}");
            }

            lastLoggedErrorLogPath = errorLogPath;
            lastLoggedErrorLogExists = exists;

            if (!exists)
            {
                duplicateWarningLastWriteUtc = null;
                duplicateWarningMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                duplicateWarningGroups = new List<HashSet<string>>();
                return;
            }

            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(errorLogPath);
            if (duplicateWarningLastWriteUtc.HasValue && duplicateWarningLastWriteUtc.Value == lastWriteUtc)
                return;

            duplicateWarningLastWriteUtc = lastWriteUtc;
            try
            {
                ParseDuplicateWarnings(errorLogPath, out duplicateWarningMap, out duplicateWarningGroups);
            }
            catch
            {
                duplicateWarningMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                duplicateWarningGroups = new List<HashSet<string>>();
            }
        }

        private void ParseDuplicateWarnings(
            string errorLogPath,
            out Dictionary<string, List<string>> warningMap,
            out List<HashSet<string>> groups)
        {
            Dictionary<string, string> aliasMap = BuildDuplicateWarningAliasMap();
            Dictionary<string, HashSet<string>> map = new(StringComparer.OrdinalIgnoreCase);
            List<HashSet<string>> groupList = new();
            HashSet<string> groupKeys = new(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadLines(errorLogPath))
            {
                Match match = DuplicateWarningRegex.Match(line);
                if (!match.Success)
                    continue;

                string objectName = match.Groups["object"].Value.Trim();
                string offenders = match.Groups["mods"].Value.Trim();
                if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(offenders))
                    continue;

                HashSet<string> groupIds = new(StringComparer.OrdinalIgnoreCase);
                foreach (string entry in offenders.Split(','))
                {
                    string token = DuplicateWarningCountRegex.Replace(entry, string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(token))
                        continue;

                    if (!aliasMap.TryGetValue(token, out string? modId) || string.IsNullOrWhiteSpace(modId))
                        continue;

                    groupIds.Add(modId);

                    if (!map.TryGetValue(modId, out HashSet<string>? objects))
                    {
                        objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        map[modId] = objects;
                    }

                    objects.Add(objectName);
                }

                if (groupIds.Count >= 2)
                {
                    string key = string.Join("|", groupIds.OrderBy(value => value));
                    if (groupKeys.Add(key))
                        groupList.Add(groupIds);
                }
            }

            Dictionary<string, List<string>> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, HashSet<string>> entry in map)
                result[entry.Key] = entry.Value.OrderBy(value => value).ToList();

            warningMap = result;
            groups = groupList;
        }

        private Dictionary<string, string> BuildDuplicateWarningAliasMap()
        {
            Dictionary<string, string> aliasMap = new(StringComparer.OrdinalIgnoreCase);

            foreach (ModReference modref in modrefMap.Values)
            {
                AddAlias(aliasMap, modref.ID, modref.ID);
                AddAlias(aliasMap, modref.name, modref.ID);
                AddAlias(aliasMap, Path.GetFileName(modref.path), modref.ID);
            }

            return aliasMap;
        }

        private static void AddAlias(Dictionary<string, string> aliasMap, string? key, string modId)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(modId))
                return;
            if (!aliasMap.ContainsKey(key))
                aliasMap[key] = modId;
        }
        #region initialization file stuff

        // Find modpacks from dfhack mod-manager config file.
        public bool ReloadModpacksFromDisk(string? preferredModlistName)
        {
            return FindModpacks(preferredModlistName);
        }

        private bool FindModpacks(string? preferredModlistName)
        {
            ResolveActiveModpackStorage();

            bool shouldPersistActiveFile = false;
            bool loadedFromActive = TryReadModpackFile(activeModpackPath, out List<DFHModpack> loadedModpacks, out string loadError);
            if (!loadedFromActive)
            {
                Console.WriteLine($"Active modlist file unavailable: {loadError} Path: {activeModpackPath}");
                string alternatePath = activeModpackBackend == ModpackStorageBackend.DFHackConfig
                    ? localFallbackModpacksPath
                    : GetModManagerConfigPath();

                if (!string.IsNullOrWhiteSpace(alternatePath) &&
                    TryReadModpackFile(alternatePath, out List<DFHModpack> alternateModpacks, out _))
                {
                    Console.WriteLine($"Loaded modlists from alternate file: {alternatePath}");
                    loadedModpacks = alternateModpacks;
                    shouldPersistActiveFile = true;
                }
                else
                {
                    Console.WriteLine("Modlist file missing or invalid. Creating a default modlist.");
                    loadedModpacks = CreateDefaultModpacks();
                    shouldPersistActiveFile = true;
                }
            }

            modpacks = new List<DFHModpack>(loadedModpacks);

            Console.WriteLine();
            Console.WriteLine("Found modlists: ");

            // Handle mods missing.
            bool modMissing = false;
            string missingMessage = $"Some mods missing. \nModlists will be modified to not require lost mods. \nMissing mods: ";
            HashSet<DFHMod> notFound = new HashSet<DFHMod>();

            // If a default modpack exists.
            int defaultIndex = -1;
            int preferredIndex = -1;

            // Go through modpacks, and go through their modlists, looking for mods that we don't have.
            for (int i = 0; i < modpacks.Count; i++)
            {
                DFHModpack modlist = modpacks[i];

                HashSet<DFHMod> thisListMissingMods = new HashSet<DFHMod>();
                foreach (DFHMod mod in modlist.modlist)
                {
                    if (!modPool.Contains(mod))
                    {
                        modMissing = true;
                        notFound.Add(mod);
                        thisListMissingMods.Add(mod);
                        missingMessage += $"\n{mod}";
                    }
                }

                // Remove the missing mods from the modlist.
                foreach (DFHMod m in thisListMissingMods)
                {
                    modlist.modlist.Remove(m);
                }

                // Write out some info on the modpack
                Console.WriteLine("   Name: " + modlist.name);
                Console.WriteLine("   Default: " + modlist.@default);
                Console.WriteLine("   Mods count: " + modlist.modlist.Count);
                Console.WriteLine();

                if (modlist.@default && defaultIndex < 0)
                    defaultIndex = i;

                if (!string.IsNullOrWhiteSpace(preferredModlistName) &&
                    string.Equals(modlist.name, preferredModlistName, StringComparison.OrdinalIgnoreCase))
                    preferredIndex = i;

                // Set modpacks[i] back to this modpack. #FIXME: why is this necessary? Isn't modpack a reference type?
                modpacks[i] = modlist;
            }

            // Set default as backup.
            if (modpacks.Count > 0)
            {
                if (preferredIndex >= 0)
                {
                    SetSelectedModpack(preferredIndex);
                }
                else if (defaultIndex >= 0)
                {
                    SetSelectedModpack(defaultIndex);
                }
                else
                {
                    SetSelectedModpack(0);
                    modpacks[0].@default = true;
                    shouldPersistActiveFile = true;
                }
            }

            // Create default modpack if none present.
            if (modpacks.Count == 0)
            {
                modpacks = CreateDefaultModpacks();
                SetSelectedModpack(0);
                shouldPersistActiveFile = true;
            }

            if (shouldPersistActiveFile)
            {
                try
                {
                    SaveAllModpacks(requestLiveReload: false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to persist modlists after load: {ex.Message}");
                }
            }

            LastMissingModsMessage = modMissing ? missingMessage : string.Empty;
            return true;
        }

        // Generate a vanilla modlist by selecting mods physically located under data/vanilla.
        public List<DFHMod> GenerateVanillaModlist()
        {
            string? vanillaPath = GetVanillaModsPath();
            bool hasVanillaPath = !string.IsNullOrWhiteSpace(vanillaPath) && Directory.Exists(vanillaPath);
            if (hasVanillaPath)
                vanillaPath = Path.GetFullPath(vanillaPath!);

            List<ModReference> vanillaRefs = new List<ModReference>();
            foreach (ModReference modref in modrefMap.Values)
            {
                if (!hasVanillaPath || string.IsNullOrWhiteSpace(modref.path))
                    continue;

                string modPath = Path.GetFullPath(modref.path);
                if (!IsPathUnderRoot(modPath, vanillaPath!))
                    continue;

                vanillaRefs.Add(modref);
            }

            if (vanillaRefs.Count == 0)
            {
                if (hasVanillaPath)
                    Console.WriteLine($"No vanilla mods found under: {vanillaPath}");
                else
                    Console.WriteLine("No vanilla mods found.");
            }

            return vanillaRefs
                .OrderBy(modref => modref.ID, StringComparer.OrdinalIgnoreCase)
                .Select(modref => modref.ToDFHMod())
                .ToList();
        }

        // Get the theme from config.
        public int GetTheme()
        {
            return config.theme;
        }

        // Save the theme to config file.
        public void SetTheme(int theme)
        {
            config.theme = theme;
            SaveConfigFile();
        }

        public int GetAutoReloadIntervalSeconds()
        {
            if (config == null)
                return -1;
            return config.AutoReloadIntervalSeconds;
        }

        public void SetAutoReloadIntervalSeconds(int seconds)
        {
            if (config == null)
                config = new ModHearthConfig();
            config.AutoReloadIntervalSeconds = seconds;
            SaveConfigFile();
        }


        public enum ConfigIssueType
        {
            MissingDwarfFortressPath,
            MissingInstalledModsPath
        }

        public readonly record struct ConfigIssue(ConfigIssueType IssueType, string Message);

        public IReadOnlyList<ConfigIssue> GetConfigIssues()
        {
            if (config == null)
                config = new ModHearthConfig();

            List<ConfigIssue> issues = new List<ConfigIssue>();
            if (string.IsNullOrWhiteSpace(config.DFFolderPath))
            {
                issues.Add(new ConfigIssue(ConfigIssueType.MissingDwarfFortressPath, "Dwarf Fortress path is not set."));
            }
            else if (!Directory.Exists(config.DFFolderPath))
            {
                issues.Add(new ConfigIssue(ConfigIssueType.MissingDwarfFortressPath, $"Dwarf Fortress folder not found: {config.DFFolderPath}"));
            }

            string installedModsPath = GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath) || !Directory.Exists(installedModsPath))
            {
                issues.Add(new ConfigIssue(ConfigIssueType.MissingInstalledModsPath, "Installed mods path is not set or missing."));
            }

            return issues;
        }

        public void SetDwarfFortressExecutablePath(string path)
        {
            if (config == null)
                config = new ModHearthConfig();
            config.DFEXEPath = path;
            config.DFFolderPathOverride = string.Empty;
            config.ModsPathOverride = string.Empty;
            SaveConfigFile();
        }

        public void SetDwarfFortressFolderPath(string path)
        {
            if (config == null)
                config = new ModHearthConfig();
            config.DFFolderPathOverride = path;
            if (!string.IsNullOrWhiteSpace(path))
                config.DFEXEPath = string.Empty;
            config.ModsPathOverride = string.Empty;
            SaveConfigFile();
        }

        public void SetInstalledModsPath(string path)
        {
            if (config == null)
                config = new ModHearthConfig();
            config.InstalledModsPath = ResolveExistingDirectoryPath(path) ?? path;
            SaveConfigFile();
        }

        // Destroy the config file, to be remade.
        public void DestroyConfig()
        {
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }

        // Attempt loading config. If broken or failed, create a blank config.
        public void AttemptLoadConfig()
        {
            Console.WriteLine("Attempting config file load.");
            try
            {
                if (File.Exists(configPath))
                {
                    Console.WriteLine("Config file found.");
                    string jsonContent = File.ReadAllText(configPath);

                    // Deserialize the JSON content into an object
                    ModHearthConfig? loadedConfig = JsonSerializer.Deserialize<ModHearthConfig>(jsonContent);
                    config = loadedConfig ?? new ModHearthConfig();

                    if (loadedConfig == null)
                        Console.WriteLine("Config file borked.");
                }
                else
                {
                    Console.WriteLine("Config file missing.");
                    config = new ModHearthConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                config = new ModHearthConfig();
            }

            AutoDiscoverConfigPaths();

            if (!config.showConsole && !DevMode.IsEnabled)
            {
                RuntimeBootstrap.HideConsole();
            }
        }

        // Save the config to file.
        public void SaveConfigFile()
        {
            Console.WriteLine("Config saved.");
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true // Enable pretty formatting
            };
            string jsonContent = JsonSerializer.Serialize(config);
            File.WriteAllText(configPath, jsonContent);
        }

        // Try to load style file.
        public Style LoadStyle()
        {
            Style style = new Style();
            int theme = GetTheme();
            string stylePath = GetStylePathForTheme(theme);

            try
            {
                if (!File.Exists(stylePath))
                    throw new FileNotFoundException("Style file missing.", stylePath);

                Console.WriteLine("Style file found.");
                if (!TryLoadStyleFromPath(stylePath, out style))
                    throw new InvalidOperationException($"Style file invalid: {stylePath}");
            }
            catch (Exception ex)
            {
                string message = $"Style load failed: {ex.Message}\nMissing or invalid style file: {stylePath}";
                Console.WriteLine(message);
                throw new InvalidOperationException(message, ex);
            }


            // Set global instance and return.
            Style.instance = style;
            return style;
        }

        // Save the style to file.
        private void SaveStyle(Style style, string stylePath)
        {
            Console.WriteLine("Style saved.");
            string? styleDir = Path.GetDirectoryName(stylePath);
            if (!string.IsNullOrWhiteSpace(styleDir) && !Directory.Exists(styleDir))
            {
                Directory.CreateDirectory(styleDir);
            }
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true // Enable pretty formatting
            };
            string jsonContent = JsonSerializer.Serialize(style, options);
            File.WriteAllText(stylePath, jsonContent);
        }

        private string GetStylePathForTheme(int theme)
        {
            return theme == 0 ? styleLightPath : styleDarkPath;
        }

        private bool TryLoadStyleFromPath(string stylePath, out Style style)
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
                if (!IsStyleComplete(foundStyle))
                    return false;
                style = foundStyle;
                return true;
            }
            catch
            {
                return false;
            }
        }
        private static bool IsStyleComplete(Style style)
        {
            return style.IsComplete();
        }
        #endregion
    }
}
