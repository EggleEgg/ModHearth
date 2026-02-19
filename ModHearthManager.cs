using ModHearth.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;

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

        public string ModsPath => string.IsNullOrWhiteSpace(DFFolderPath)
            ? string.Empty
            : Path.Combine(DFFolderPath, "Mods");

        // Path to installed mods cache.
        public string InstalledModsPath { get; set; } = string.Empty;

        // Should this be in lightmode?
        public int theme { get; set; }

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
            switch(problemType)
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
        StartDwarfFortress,
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

    public class ModHearthManager
    {
        public static string GetBuildVersionString()
        {
            string? runNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
            if (!string.IsNullOrWhiteSpace(runNumber))
                return runNumber;

            string? infoVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                int plusIndex = infoVersion.IndexOf('+');
                if (plusIndex > 0)
                    infoVersion = infoVersion.Substring(0, plusIndex);
                if (!string.IsNullOrWhiteSpace(infoVersion))
                    return infoVersion;
            }

            return " none";
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
        private static readonly string styleLegacyPath = Path.Combine(baseDir, "styles", "style.json");
        private static readonly string styleLegacyRootPath = Path.Combine(baseDir, "style.json");
        private static readonly Regex SteamLibraryPathRegex = new("\"path\"\\s+\"(?<path>.*?)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SteamLibraryLegacyPathRegex = new("^\\s*\"\\d+\"\\s+\"(?<path>.*?)\"", RegexOptions.Compiled);

        // Mod problem tracker.
        public List<ModProblem> modproblems = new();
        public bool IsSavingModpacks { get; private set; }
        private HashSet<string> installedCacheModIds = new(StringComparer.OrdinalIgnoreCase);
        public string LastMissingModsMessage { get; private set; } = string.Empty;

        public ModHearthManager() 
        {
            Console.WriteLine($"Crafting Hearth v{GetBuildVersionString()}");

            // Get and load config file, fix if needed.
            AttemptLoadConfig();
        }

        public void Initialize()
        {
            // Find all mods and add to the lists.
            FindAllModsDFHackLua();

            // Find DFHModpacks, and fix them if needed.
            FindModpacks(null);

            // Write some info on found things.
            Console.WriteLine();
            Console.WriteLine($"Found {modrefMap.Count} mods and {modpacks.Count} modlists");
            Console.WriteLine();
        }


        //??
        public ModHearthConfig GetConfig()
        {
            return config;
        }

        public string GetInstalledModsPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.InstalledModsPath))
                return GetDefaultInstalledModsPath();
            return config.InstalledModsPath;
        }

        public string GetModManagerConfigPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.DFFolderPath))
                return string.Empty;
            return Path.Combine(config.DFFolderPath, "dfhack-config", "mod-manager.json");
        }

        public bool CanDeleteModFromModsFolder(ModReference modref)
        {
            if (modref == null || string.IsNullOrWhiteSpace(modref.path) || config == null)
                return false;
            if (string.IsNullOrWhiteSpace(config.ModsPath))
                return false;
            string modPath = Path.GetFullPath(modref.path);
            string modsPath = Path.GetFullPath(config.ModsPath);
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

            string modPath = Path.GetFullPath(modref.path);
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
            ReloadDFHackModManagerScreen();

            message = $"Deleted {modPath}";
            return true;
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            string normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            normalizedRoot += Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private string GetDefaultInstalledModsPath()
        {
            foreach (string candidate in GetInstalledModsPathCandidates(config?.DFFolderPath))
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

            if (string.IsNullOrWhiteSpace(config.InstalledModsPath))
            {
                string? installedMods = TryFindInstalledModsPath(config.DFFolderPath);
                if (!string.IsNullOrWhiteSpace(installedMods))
                {
                    config.InstalledModsPath = installedMods;
                    updated = true;
                }
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
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                return string.Empty;

            if (IsLikelyDwarfFortressFolder(candidate))
                return candidate;

            if (OperatingSystem.IsMacOS())
            {
                string appResources = Path.Combine(candidate, "Dwarf Fortress.app", "Contents", "Resources");
                if (IsLikelyDwarfFortressFolder(appResources))
                    return appResources;
            }

            return string.Empty;
        }

        private IEnumerable<string> EnumerateSteamLibraryRoots()
        {
            HashSet<string> libraries = new HashSet<string>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            foreach (string root in GetSteamRootCandidates())
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                if (!Directory.Exists(root))
                    continue;

                if (Directory.Exists(Path.Combine(root, "steamapps")))
                    libraries.Add(root);

                foreach (string library in ReadSteamLibraryFolders(root))
                {
                    if (string.IsNullOrWhiteSpace(library))
                        continue;

                    if (Directory.Exists(Path.Combine(library, "steamapps")))
                        libraries.Add(library);
                }
            }

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
                return libraries;

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
            catch
            {
                // Ignore errors when reading Steam library folders.
            }

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
                normalized = normalized.Replace("\\\\", "\\");
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

        private string? TryFindInstalledModsPath(string? dfFolderPath)
        {
            foreach (string candidate in GetInstalledModsPathCandidates(dfFolderPath))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (Directory.Exists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static IEnumerable<string> GetInstalledModsPathCandidates(string? dfFolderPath)
        {
            if (!string.IsNullOrWhiteSpace(dfFolderPath))
                yield return Path.Combine(dfFolderPath, "data", "installed_mods");

            foreach (string basePath in GetAppDataBasePaths())
                yield return Path.Combine(basePath, "Bay 12 Games", "Dwarf Fortress", "data", "installed_mods");
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
                roots.Add(installedModsPath);

            if (!string.IsNullOrWhiteSpace(config?.DFFolderPath))
                roots.Add(Path.Combine(config.DFFolderPath, "data", "vanilla"));

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                foreach (string dir in Directory.EnumerateDirectories(root))
                {
                    string infoPath = Path.Combine(dir, "info.txt");
                    if (!File.Exists(infoPath))
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
            // If game not running, prompt user to run it and force restart.
            if (!DwarfFortressRunning())
            {
                Console.WriteLine("DF not running");
                throw new UserActionRequiredException(
                    UserActionRequired.StartDwarfFortress,
                    "Please launch Dwarf Fortress and navigate to the world creation screen.");
            }

            // Initialize relevant variables.
            modrefMap = new Dictionary<string, ModReference>();
            modPool = new HashSet<DFHMod>();

            // Get all mod folders.
            Console.WriteLine("Finding all mods... ");

            HashSet<Dictionary<string, string>> modData = GetModMemoryData();
            Dictionary<string, string> modIdPathMap = BuildModIdPathMap();

            foreach (Dictionary<string, string> modDataEntry in modData)
            {
                // Directory correction.
                modDataEntry["src_dir"] = ResolveModPath(modDataEntry, modIdPathMap);

                // Mod setup and registry.
                ModReference modRef = new ModReference(modDataEntry);
                string key = modRef.DFHackCompatibleString();
                Console.WriteLine($"   Mod registered: {modRef.name}.");
                modrefMap.Add(key, modRef);
                modPool.Add(modRef.ToDFHMod());
            }
        }

        private Dictionary<string, string> BuildModIdPathMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string installedModsPath = Path.Combine(config.DFFolderPath, "data", "installed_mods");
            List<string> roots = new List<string> { config.ModsPath, installedModsPath };

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string dir in Directory.EnumerateDirectories(root))
                {
                    string infoPath = Path.Combine(dir, "info.txt");
                    if (!File.Exists(infoPath))
                        continue;

                    try
                    {
                        string info = File.ReadAllText(infoPath);
                        Match idMatch = Regex.Match(info, @"\[ID:([^\]]+)\]", RegexOptions.IgnoreCase);
                        if (idMatch.Success)
                        {
                            string id = idMatch.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(id))
                                map[id] = dir;
                        }
                    }
                    catch
                    {
                        // Ignore unreadable info files.
                    }
                }
            }

            return map;
        }

        private string ResolveModPath(Dictionary<string, string> modDataEntry, Dictionary<string, string> modIdPathMap)
        {
            string rawSrcDir = modDataEntry["src_dir"];
            string fullPath = Path.Combine(config.DFFolderPath, rawSrcDir);

            if (Directory.Exists(fullPath))
                return fullPath;

            // Try matching the folder name in known roots.
            string rawFolderName = Path.GetFileName(rawSrcDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(rawFolderName))
            {
                string candidate = Path.Combine(config.ModsPath, rawFolderName);
                if (Directory.Exists(candidate))
                    return candidate;

                string installedModsPath = Path.Combine(config.DFFolderPath, "data", "installed_mods");
                candidate = Path.Combine(installedModsPath, rawFolderName);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            // Fall back to ID-based lookup.
            if (modDataEntry.TryGetValue("id", out string? id) &&
                !string.IsNullOrWhiteSpace(id) &&
                modIdPathMap.TryGetValue(id, out string? mappedPath) &&
                !string.IsNullOrWhiteSpace(mappedPath))
            {
                return mappedPath;
            }

            return fullPath;
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
                if (headers.TryGetValue("name", out string? headerName))
                    Console.WriteLine("   Mod Found: " + headerName);

                // To see which headers there are to choose from.
                //foreach (string k in headers.Keys)
                    //Console.WriteLine($"header found. k: {k}, v: {headers[k]}");

            }

            return modData;
        }

        // Use dfhack-run.exe and lua to get raw mod data.
        private string LoadModMemoryData()
        {
            string dfhackRunPath = GetDfhackRunPath();
            if (string.IsNullOrWhiteSpace(dfhackRunPath) || !File.Exists(dfhackRunPath))
                throw new FileNotFoundException("dfhack-run executable not found.", dfhackRunPath);

            // Get path to lua script.
            string luaPath = Path.Combine(AppContext.BaseDirectory, "lua", "GetModMemoryData.lua");
            if (!File.Exists(luaPath))
                throw new FileNotFoundException("GetModMemoryData.lua not found.", luaPath);

            // Set up dfhack process.
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = dfhackRunPath,
                WorkingDirectory = config.DFFolderPath,
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

            return false;
        }

        // Alter the current modpack with enabledMods and save modpack list to dfhack file.
        public void SaveCurrentModpack()
        {
            SelectedModlist.modlist = new List<DFHMod>(enabledMods);

            SaveAllModpacks();
        }

        // Save the DFHModpack list to file.
        public void SaveAllModpacks()
        {
            Console.WriteLine("Modlists saved.");

            // Get the path, serialize with right options, and write to file.
            string dfHackModlistPath = GetModManagerConfigPath();
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true // Enable pretty formatting
            };
            string modlistJson = JsonSerializer.Serialize(modpacks, options);
            IsSavingModpacks = true;
            try
            {
                File.WriteAllText(dfHackModlistPath, modlistJson);
            }
            finally
            {
                IsSavingModpacks = false;
            }

            ReloadDFHackModManagerScreen();
        }

        private void ReloadDFHackModManagerScreen()
        {
            if (!DwarfFortressRunning())
                return;

            string dfhackRunPath = GetDfhackRunPath();
            if (string.IsNullOrWhiteSpace(dfhackRunPath) || !File.Exists(dfhackRunPath))
                return;

            string luaPath = Path.Combine(AppContext.BaseDirectory, "lua", "ReloadModManager.lua");
            if (!File.Exists(luaPath))
                return;

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = dfhackRunPath,
                WorkingDirectory = config.DFFolderPath,
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
            }
            catch
            {
                // Ignore reload failures to avoid disrupting saving.
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

        // Move a mod from one place to another. Has four cases, depending on source and destination.
        public void MoveMod(ModReference mod, int newIndex, bool sourceLeft, bool destinationLeft)
        {
            // Convert mod to DFHMod.
            DFHMod dfm = mod.ToDFHMod();
            Console.WriteLine($"Mod '{dfm.id}' moved from " + (sourceLeft ? "dis" : "en") + "abled to " + (destinationLeft ? "dis" : "en") + "abled.");

            if (sourceLeft && destinationLeft)
            {
                // Do nothing since the order of disabled mods doesn't matter.
                return;
            }
            else if (!sourceLeft && !destinationLeft)
            {
                // Get the old index of the mod
                int oldIndex = enabledMods.IndexOf(dfm);

                // If the mod is removed from the old index, and it would shift the whole list down, account for that.
                if (oldIndex < newIndex)
                    newIndex--;

                if (oldIndex < 0)
                {
                    Console.WriteLine("Clicking too fast, attempted to disable same mod twice.");
                    return;
                }

                // Remove from old index and insert at the new index (or add if at end of list).
                enabledMods.RemoveAt(oldIndex);
                if (newIndex == enabledMods.Count)
                    enabledMods.Add(dfm);
                else
                    enabledMods.Insert(newIndex, dfm);
            }
            else if (!sourceLeft && destinationLeft)
            {
                // Remove the mod from enabled list, and toss into disabled list (order doesn't matter).
                enabledMods.Remove(dfm);
                disabledMods.Add(dfm);
            }
            else if (sourceLeft && !destinationLeft)
            {
                // Insert/add from disabled to enabled.
                disabledMods.Remove(dfm);
                if (newIndex == enabledMods.Count)
                    enabledMods.Add(dfm);
                else
                    enabledMods.Insert(newIndex, dfm);
            }

            // Refind mod problems since mod order changed.
            FindModlistProblems();
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
            HashSet<string> scannedModIDs = new HashSet<string>();
            HashSet<string> unscannedModIDs = new HashSet<string>();

            // Add all enabled mod IDs to unscanned.
            foreach (DFHMod dfm in enabledMods)
            {
                unscannedModIDs.Add(dfm.id.ToLower());
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
                        if (!scannedModIDs.Contains(beforeID.ToLower()))
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, beforeID, ModProblem.ProblemType.MissingBefore));
                            //Console.WriteLine("Problem found: missing before mod with ID: " + beforeID + " mod needing is: " + currentDFM.id);
                        }
                    foreach (string afterID in currentMod.require_after_me)
                        if (!unscannedModIDs.Contains(afterID.ToLower()))
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, afterID, ModProblem.ProblemType.MissingAfter));
                            //Console.WriteLine("Problem found: missing after mod with ID: " + afterID + " mod needing is: " + currentDFM.id);
                        }
                    foreach (string conflictID in currentMod.conflicts_with)
                        if (scannedModIDs.Contains(conflictID.ToLower()) || unscannedModIDs.Contains(conflictID.ToLower()) )
                        {
                            modproblems.Add(new ModProblem(currentDFM.id, conflictID, ModProblem.ProblemType.ConflictPresent));
                            //Console.WriteLine("Problem found: conflict present mod with ID: " + conflictID + " mod needing is: " + currentDFM.id);
                        }
                }

                // Move to scanned.
                scannedModIDs.Add(currentDFM.id.ToLower());
                unscannedModIDs.Remove(currentDFM.id.ToLower());
            }
        }

        public bool AutoSortEnabledMods()
        {
            Dictionary<string, ModReference> idMap = new Dictionary<string, ModReference>(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in modrefMap.Values)
                if (!idMap.ContainsKey(modref.ID))
                    idMap.Add(modref.ID, modref);

            Dictionary<string, int> originalIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<ModReference> enabledRefs = new List<ModReference>();
            for (int i = 0; i < enabledMods.Count; i++)
            {
                if (modrefMap.TryGetValue(enabledMods[i].ToString(), out ModReference? modref) && modref != null)
                {
                    enabledRefs.Add(modref);
                    if (!originalIndex.ContainsKey(modref.ID))
                        originalIndex[modref.ID] = i;
                }
            }

            HashSet<string> enabledIds = new HashSet<string>(enabledRefs.Select(m => m.ID), StringComparer.OrdinalIgnoreCase);
            Queue<ModReference> queue = new Queue<ModReference>(enabledRefs);
            while (queue.Count > 0)
            {
                ModReference current = queue.Dequeue();
                foreach (string dep in current.require_before_me.Concat(current.require_after_me))
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId))
                        continue;
                    if (enabledIds.Contains(depId))
                        continue;
                    if (idMap.TryGetValue(depId, out ModReference? depRef) && depRef != null)
                    {
                        enabledIds.Add(depRef.ID);
                        queue.Enqueue(depRef);
                    }
                }
            }

            List<ModReference> allEnabled = new List<ModReference>();
            foreach (string id in enabledIds)
                if (idMap.TryGetValue(id, out ModReference? modref) && modref != null)
                    allEnabled.Add(modref);

            Dictionary<string, (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla)> traitCache =
                new Dictionary<string, (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla)>(StringComparer.OrdinalIgnoreCase);

            List<ModReference> baseOrder = allEnabled
                .OrderBy(m => GetAutoSortGroup(m, traitCache))
                .ThenBy(m => GetReactionPriority(m))
                .ThenBy(m => originalIndex.TryGetValue(m.ID, out int idx) ? idx : int.MaxValue)
                .ThenBy(m => m.name ?? m.ID)
                .ToList();

            Dictionary<string, int> baseIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < baseOrder.Count; i++)
                baseIndex[baseOrder[i].ID] = i;

            Dictionary<string, List<string>> edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in allEnabled)
            {
                edges[modref.ID] = new List<string>();
                indegree[modref.ID] = 0;
            }

            foreach (ModReference modref in allEnabled)
            {
                foreach (string dep in modref.require_before_me)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    edges[depId].Add(modref.ID);
                    indegree[modref.ID]++;
                }
                foreach (string dep in modref.require_after_me)
                {
                    string? depId = dep?.Trim();
                    if (string.IsNullOrEmpty(depId) || !enabledIds.Contains(depId))
                        continue;
                    edges[modref.ID].Add(depId);
                    indegree[depId]++;
                }
            }

            List<string> available = new List<string>();
            foreach (KeyValuePair<string, int> kv in indegree)
                if (kv.Value == 0)
                    available.Add(kv.Key);

            List<string> sortedIds = new List<string>();
            while (available.Count > 0)
            {
                string next = available.OrderBy(id => baseIndex.TryGetValue(id, out int idx) ? idx : int.MaxValue).First();
                available.Remove(next);
                sortedIds.Add(next);
                foreach (string dest in edges[next])
                {
                    indegree[dest]--;
                    if (indegree[dest] == 0)
                        available.Add(dest);
                }
            }

            if (sortedIds.Count != enabledIds.Count)
                sortedIds = baseOrder.Select(m => m.ID).ToList();

            List<DFHMod> sortedMods = new List<DFHMod>();
            foreach (string id in sortedIds)
                if (idMap.TryGetValue(id, out ModReference? modref) && modref != null)
                    sortedMods.Add(modref.ToDFHMod());

            bool changed = sortedMods.Count != enabledMods.Count;
            if (!changed)
                for (int i = 0; i < sortedMods.Count; i++)
                    if (sortedMods[i] != enabledMods[i])
                    {
                        changed = true;
                        break;
                    }

            if (changed)
            {
                SetActiveMods(sortedMods);
                FindModlistProblems();
            }

            return changed;
        }

        private int GetAutoSortGroup(ModReference modref, Dictionary<string, (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla)> traitCache)
        {
            var traits = GetModTraits(modref, traitCache);
            if (traits.beforeVanilla || (modref.name ?? "").IndexOf("better instruments", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0;
            if (traits.vanillaEntity)
                return 0;
            if (traits.newEntity)
                return 1;
            if (traits.reaction)
                return 4;
            if (traits.graphics)
                return 2;
            if (traits.newStuff)
                return 5;
            return 3;
        }

        private int GetReactionPriority(ModReference modref)
        {
            string label = ((modref.name ?? "") + " " + modref.ID).ToLowerInvariant();
            if (label.Contains("set production"))
                return 0;
            if (label.Contains("smelt ore by product"))
                return 1;
            if (label.Contains("stone beds") || label.Contains("stoneworking expanded"))
                return 2;
            if (label.Contains("specific decoration"))
                return 3;
            if (label.Contains("fermented milk"))
                return 4;
            return 100;
        }

        private (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla) GetModTraits(
            ModReference modref,
            Dictionary<string, (bool vanillaEntity, bool newEntity, bool reaction, bool creature, bool newStuff, bool graphics, bool beforeVanilla)> traitCache)
        {
            if (traitCache.TryGetValue(modref.ID, out var cached))
                return cached;

            bool vanillaEntity = false;
            bool newEntity = false;
            bool reaction = false;
            bool creature = false;
            bool newStuff = false;
            bool graphics = false;
            bool beforeVanilla = false;

            string infoPath = Path.Combine(modref.path, "info.txt");
            if (File.Exists(infoPath))
            {
                string info = File.ReadAllText(infoPath);
                if (info.IndexOf("before vanilla", StringComparison.OrdinalIgnoreCase) >= 0)
                    beforeVanilla = true;
                if (info.IndexOf("graphics", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("tileset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("tile set", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("portrait", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("landscape", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("stone variation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    info.IndexOf("rounded hills", StringComparison.OrdinalIgnoreCase) >= 0)
                    graphics = true;
            }

            if (Directory.Exists(Path.Combine(modref.path, "graphics")) ||
                Directory.Exists(Path.Combine(modref.path, "raw", "graphics")))
                graphics = true;

            if (Directory.Exists(modref.path))
            {
                foreach (string file in Directory.EnumerateFiles(modref.path, "*.txt", SearchOption.AllDirectories))
                {
                    string lowerPath = file.ToLowerInvariant();
                    if (lowerPath.Contains("\\graphics\\") || lowerPath.Contains("/graphics/"))
                        graphics = true;
                    if (!lowerPath.Contains("\\raw\\") && !lowerPath.Contains("/raw/"))
                        continue;

                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!reaction && text.IndexOf("[REACTION:", StringComparison.OrdinalIgnoreCase) >= 0)
                        reaction = true;
                    if (!creature && text.IndexOf("[CREATURE:", StringComparison.OrdinalIgnoreCase) >= 0)
                        creature = true;
                    if (!newStuff &&
                        (text.IndexOf("[INORGANIC:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("[PLANT:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("[ITEM_", StringComparison.OrdinalIgnoreCase) >= 0))
                        newStuff = true;

                    if (!vanillaEntity || !newEntity)
                    {
                        MatchCollection entityMatches = Regex.Matches(text, @"\[ENTITY:([^\]]+)\]", RegexOptions.IgnoreCase);
                        foreach (Match match in entityMatches)
                        {
                            string ent = match.Groups[1].Value.Trim();
                            if (IsVanillaEntity(ent))
                                vanillaEntity = true;
                            else if (!string.IsNullOrEmpty(ent))
                                newEntity = true;
                        }
                    }

                    if (reaction && creature && newStuff && graphics && (vanillaEntity || newEntity))
                        break;
                }
            }

            if (creature)
                newStuff = true;

            var result = (vanillaEntity, newEntity, reaction, creature, newStuff, graphics, beforeVanilla);
            traitCache[modref.ID] = result;
            return result;
        }

        private bool IsVanillaEntity(string id)
        {
            switch (id.ToUpperInvariant())
            {
                case "DWARF":
                case "ELF":
                case "HUMAN":
                case "GOBLIN":
                case "KOBOLD":
                    return true;
            }
            return false;
        }

        #region initialization file stuff

        // Find modpacks from dfhack mod-manager config file.
        public bool ReloadModpacksFromDisk(string? preferredModlistName)
        {
            return FindModpacks(preferredModlistName);
        }

        private bool FindModpacks(string? preferredModlistName)
        {
            // Get paths and read file. #TODO: handling the file not existing.
            string dfHackModpackPath = GetModManagerConfigPath();
            if (string.IsNullOrWhiteSpace(dfHackModpackPath) || !File.Exists(dfHackModpackPath))
            {
                Console.WriteLine("Modlist file missing.");
                modpacks = new List<DFHModpack>();
                return false;
            }

            string dfHackModpackJson = File.ReadAllText(dfHackModpackPath);
            List<DFHModpack>? loadedModpacks = JsonSerializer.Deserialize<List<DFHModpack>>(dfHackModpackJson);
            if (loadedModpacks == null)
            {
                Console.WriteLine("Modlist file borked.");
                modpacks = new List<DFHModpack>();
                return false;
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
                foreach(DFHMod mod in modlist.modlist)
                {
                    if(!modPool.Contains(mod))
                    {
                        modMissing = true;
                        notFound.Add(mod);
                        thisListMissingMods.Add(mod);
                        missingMessage += $"\n{mod}";
                    }
                }
                
                // Remove the missing mods from the modlist.
                foreach(DFHMod m in thisListMissingMods)
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
                    SaveAllModpacks();
                }
            }

            // Create default modpack if none present.
            if(modpacks.Count == 0)
            {
                DFHModpack newPack = new DFHModpack(true, new List<DFHMod>(), "Default");
                // FIXME: generate vanilla modpack in a better way than this
                newPack.modlist = GenerateVanillaModlist();
                modpacks.Add(newPack);
                SetSelectedModpack(0);
                SaveAllModpacks();
            }

            LastMissingModsMessage = modMissing ? missingMessage : string.Empty;
            return true;
        }

        // Generated a vanilla modlist using manually generated mod ID list.
        public List<DFHMod> GenerateVanillaModlist()
        {
            // Manually made vanilla modlist.
            List<string> vanillaModIDList = new List<string>()
                {
                    "vanilla_text",
                    "vanilla_languages",
                    "vanilla_descriptors",
                    "vanilla_materials",
                    "vanilla_environment",
                    "vanilla_plants",
                    "vanilla_items",
                    "vanilla_buildings",
                    "vanilla_bodies",
                    "vanilla_creatures",
                    "vanilla_entities",
                    "vanilla_reactions",
                    "vanilla_interactions",
                    "vanilla_descriptors_graphics",
                    "vanilla_plants_graphics",
                    "vanilla_items_graphics",
                    "vanilla_buildings_graphics",
                    "vanilla_creatures_graphics",
                    "vanilla_world_map",
                    "vanilla_interface",
                    "vanilla_music",
                };

            // Get all mods that are probably vanilla.
            Dictionary<string, DFHMod> vanillaPool = new Dictionary<string, DFHMod>();
            foreach (DFHMod dfm in modPool)
            {
                if (dfm.id.ToLower().Contains("vanilla"))
                {
                    vanillaPool.Add(dfm.id, dfm);
                    Console.WriteLine("added mod with id " +  dfm.id);
                }
            }

            // Load vanilla mods into pack.
            List<DFHMod> vanillaList = new List<DFHMod>();
            for (int i = 0; i < vanillaModIDList.Count; i++)
            {
                Console.WriteLine("looking for mod with id " + vanillaModIDList[i]);
                vanillaList.Add(vanillaPool[vanillaModIDList[i]]);
            }

            return vanillaList;
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

            if (string.IsNullOrWhiteSpace(config.InstalledModsPath) || !Directory.Exists(config.InstalledModsPath))
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
            SaveConfigFile();
        }

        public void SetDwarfFortressFolderPath(string path)
        {
            if (config == null)
                config = new ModHearthConfig();
            config.DFFolderPathOverride = path;
            if (!string.IsNullOrWhiteSpace(path))
                config.DFEXEPath = string.Empty;
            SaveConfigFile();
        }

        public void SetInstalledModsPath(string path)
        {
            if (config == null)
                config = new ModHearthConfig();
            config.InstalledModsPath = path;
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
                if (File.Exists(stylePath))
                {
                    Console.WriteLine("Style file found.");
                    if (!TryLoadStyleFromPath(stylePath, out style))
                    {
                        Console.WriteLine("Style file borked. Style regenerated.");
                        style = GetDefaultStyleForTheme(theme);
                        SaveStyle(style, stylePath);
                    }
                }
                else if (TryLoadLegacyStyle(out style))
                {
                    Console.WriteLine("Legacy style file found. Migrating.");
                    SaveStyle(style, stylePath);
                }
                else
                {
                    Console.WriteLine("Style file missing. New style reated.");
                    style = GetDefaultStyleForTheme(theme);
                    SaveStyle(style, stylePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
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

        private Style GetDefaultStyleForTheme(int theme)
        {
            string stylePath = GetStylePathForTheme(theme);
            if (TryLoadStyleFromPath(stylePath, out Style style))
                return style;

            if (TryLoadLegacyStyle(out style))
                return style;

            return GetFallbackStyle();
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
                style = Style.EnsureDefaults(foundStyle, GetFallbackStyle());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Style? fallbackStyle;

        private Style GetFallbackStyle()
        {
            if (fallbackStyle != null)
                return fallbackStyle;

            try
            {
                fallbackStyle = Style.GetFallback();
                return fallbackStyle;
            }
            catch
            {
                // Ignore and fall back to legacy style if embedded style is unavailable.
            }

            if (TryLoadLegacyStyleRaw(out Style legacy))
            {
                fallbackStyle = legacy;
                return fallbackStyle;
            }

            throw new InvalidOperationException("Fallback style missing.");
        }

        private bool TryLoadLegacyStyle(out Style style)
        {
            if (TryLoadStyleFromPath(styleLegacyPath, out style))
                return true;
            if (TryLoadStyleFromPath(styleLegacyRootPath, out style))
                return true;
            return false;
        }

        private bool TryLoadLegacyStyleRaw(out Style style)
        {
            if (TryLoadStyleRawFromPath(styleLegacyPath, out style))
                return true;
            if (TryLoadStyleRawFromPath(styleLegacyRootPath, out style))
                return true;
            return false;
        }

        private bool TryLoadStyleRawFromPath(string stylePath, out Style style)
        {
            style = null!;
            if (!File.Exists(stylePath))
                return false;

            try
            {
                string jsonContent = File.ReadAllText(stylePath);
                Style? loadedStyle = JsonSerializer.Deserialize<Style>(jsonContent);
                if (loadedStyle == null)
                    return false;
                style = loadedStyle;
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion
    }
}
