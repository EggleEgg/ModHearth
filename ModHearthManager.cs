using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using ModHearth.UI;
using ModHearth.Utilities;
using System.Collections.Concurrent;
using ModHearth.Utilities.Logging;

namespace ModHearth
{
    [Serializable]
    public struct ModProblem
    {
        public string problemThrowerID;
        public string problemID;

        public enum ProblemType
        {
            MissingBefore,
            MissingAfter,
            ConflictPresent,
            DuplicateMod,
            MissingRequired
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
                case ProblemType.DuplicateMod:
                    return $"Duplicate mod folder found: '{problemThrowerID}' (Folders: {problemID}).\n\nPlease remove one to avoid issues.";
                case ProblemType.MissingRequired:
                    return $"Mod '{problemThrowerID}' requires mod '{problemID}' to be enabled.";
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
        public event Action? RequestUIReload;

        public void TriggerUIReload()
        {
            RequestUIReload?.Invoke();
        }

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
        private volatile Dictionary<string, ModReference> modrefMap = new(StringComparer.OrdinalIgnoreCase);

        // Get all currently loaded ModReferences.
        public IReadOnlyCollection<ModReference> LoadedMods => modrefMap.Values;

        // Get a ModReference given a string key.
        public ModReference GetModRef(string key) => modrefMap[key];

        // Get a DFHMod given a string key.
        public DFHMod GetDFHackMod(string key) => modrefMap[key].ToDFHMod();

        // Get a ModReference given a DFHMod key.
        public ModReference GetRefFromDFHMod(DFHMod dfmod) => modrefMap[dfmod.ToString()];

        public IReadOnlyDictionary<string, List<ModReference>> GetDuplicateModRefs() => duplicateModRefs;

        // The sorted list of enabled DFHmods. This list is modified by the form, and when saved it overwrites the list of a ModPack.
        public volatile List<DFHMod> enabledMods = new();

        // The unsorted list of disabled DFHmods
        public volatile HashSet<DFHMod> disabledMods = new();

        // The unsorted list of all available DFHmods
        public volatile HashSet<DFHMod> modPool = new();

        // Get the currently selected modpack
        public DFHModpack SelectedModlist => modpacks[selectedModlistIndex];

        // List of all modpacks. After a modpack in this list is modified the list is saved to file.
        public volatile List<DFHModpack> modpacks = new();

        // The index of the currently selected modpack.
        public volatile int selectedModlistIndex;

        // The file Config for this class.
        public static ModHearthConfig Config => ConfigManager.Config;
        public static IEnumerable<string> EnumerateModRoots() => ConfigManager.EnumerateModRoots();
        public static string GetModsPath() => ConfigManager.GetModsPath();
        public static string GetInstalledModsPath() => ConfigManager.GetInstalledModsPath();
        public static string GetVanillaModsPath() => ConfigManager.GetVanillaModsPath();
        public static string GetModManagerConfigPath() => ConfigManager.GetModManagerConfigPath();
        public static string GetDfhackRunPath() => ConfigManager.GetDfhackRunPath();
        public static string NormalizeFileSystemPath(string path) => ConfigManager.NormalizeFileSystemPath(path);
        public static string? ResolveExistingDirectoryPath(string path) => ConfigManager.ResolveExistingDirectoryPath(path);
        public static string? ResolveInfoFilePath(string modDirectory) => ConfigManager.ResolveInfoFilePath(modDirectory);

        // Paths.
        private static readonly string baseDir = AppContext.BaseDirectory;
        private static readonly string modSortRulesPath = Path.Combine(baseDir, "modsort_rules.json");
        private static readonly string modRelationshipRulesPath = Path.Combine(baseDir, "modrules.json");
        private static readonly string localFallbackModpacksPath = Path.Combine(baseDir, "metadata", "modpacks.local.json");
        private static readonly Regex DuplicateWarningRegex = new("^Duplicate Object:\\s*(?<object>.+?);\\s*Offending mods are\\s*(?<mods>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DuplicateWarningCountRegex = new("\\s*\\(x\\d+\\)\\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Mod problem tracker.
        public volatile List<ModProblem> modproblems = new();
        private volatile Dictionary<string, List<ModReference>> duplicateModRefs = new(StringComparer.OrdinalIgnoreCase);
        private volatile Dictionary<string, List<string>> duplicateWarningMap = new(StringComparer.OrdinalIgnoreCase);
        private volatile Dictionary<string, List<string>> cacheDuplicateMap = new(StringComparer.OrdinalIgnoreCase);
        private volatile List<HashSet<string>> duplicateWarningGroups = new();
        private volatile List<HashSet<string>> cacheDuplicateGroups = new();
        private DateTime savingModpacksCooldownUntilUtc = DateTime.MinValue;
        public bool IsSavingModpacks
        {
            get
            {
                lock (stateGate)
                    return DateTime.UtcNow < savingModpacksCooldownUntilUtc;
            }
        }

        private readonly object installedCacheGate = new();
        private HashSet<string>? installedCacheModIds;
        private Dictionary<string, ModRelationshipRule> relationshipRules = new(StringComparer.OrdinalIgnoreCase);
        private List<ModSortRule> sortRules = new();
        private List<ModSortRule> communitySortRules = new();
        public string LastMissingModsMessage { get; private set; } = string.Empty;
        private DateTime? duplicateWarningLastWriteUtc;

        private string? lastLoggedErrorLogPath;
        private bool lastLoggedErrorLogExists;
        private readonly object modManagerReloadGate = new();
        private readonly object stateGate = new();
        private int reloadInProgress;
        public bool IsReloadingMods => reloadInProgress != 0;
        private CancellationTokenSource? deferredModManagerReloadCts;
        private volatile ModpackStorageBackend activeModpackBackend = ModpackStorageBackend.LocalFallback;
        private volatile string activeModpackPath = localFallbackModpacksPath;
        private static readonly TimeSpan DeferredModManagerReloadInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DeferredModManagerReloadTimeout = TimeSpan.FromMinutes(5);

        public ModHearthManager()
        {
            Console.WriteLine($"Crafting Hearth v{GetBuildVersionString()}");

            // Get and load Config file, fix if needed.
            MigrateLocalModpacks();
            ConfigManager.AttemptLoadConfigAndDiscover();
            LoadSortRules();
            LoadCommunitySortRules();
        }

        private static void MigrateLocalModpacks()
        {
            string oldPath = Path.Combine(baseDir, "modpacks.local.json");
            string newPath = localFallbackModpacksPath;

            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                try
                {
                    string? directory = Path.GetDirectoryName(newPath);
                    if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.Move(oldPath, newPath);
                    Console.WriteLine($"Moved modpacks.local.json to {newPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to migrate modpacks.local.json: {ex.Message}");
                }
            }
        }

        public bool Initialize(string? preferredModlistName = null)
        {
            if (Interlocked.CompareExchange(ref reloadInProgress, 1, 0) != 0)
            {
                Console.WriteLine("[ModHearth] Initialize() called while a reload was already in progress; ignoring.");
                return false;
            }

            try
            {
                try
                {
                    Task.Run(() => AuditWorkshopManifests());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Steam] Error starting background manifest audit: {ex.Message}");
                }

                FindAllModsDFHackLua();
                FindModpacks(preferredModlistName);

                Console.WriteLine($"Found {modrefMap.Count} mods and {modpacks.Count} modlists");
                Console.WriteLine();

                ModUpdateLogger.RecordChanges(modrefMap.Values, enabledMods, ConfigManager.GetSteamWorkshopAcfPaths());

                // Preload the vanilla raw baseline once during initialization so
                // later scans can evaluate vanilla membership by set containment.
                _ = GetVanillaBaseline();

                return true;
            }
            finally
            {
                Interlocked.Exchange(ref reloadInProgress, 0);
            }
        }

        public IReadOnlyList<ModSortRule> GetSortRules()
        {
            return sortRules;
        }
        public static string GetSortRulesPath() => modSortRulesPath;
        public static string GetModRelationshipRulesPath() => modRelationshipRulesPath;

        public IReadOnlyDictionary<string, ModRelationshipRule> GetModRelationshipRules()
        {
            return CloneRelationshipRules(relationshipRules);
        }

        public void SetModRelationshipRules(IDictionary<string, ModRelationshipRule>? rules)
        {
            relationshipRules = NormalizeRelationshipRules(rules);
            sortRules = RelationshipRulesToSortRules(relationshipRules);
            SaveRelationshipRules();
        }

        public void SetModRelationshipRule(string modId, ModRelationshipRule? rule)
        {
            Dictionary<string, ModRelationshipRule> next = CloneRelationshipRules(relationshipRules);
            string key = modId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (rule == null || rule.IsEmpty)
                next.Remove(key);
            else
                next[key] = rule.Clone();

            SetModRelationshipRules(next);
        }

        public bool TryAddModRelationship(string modId, ModRelationshipKind kind, string targetId)
        {
            string key = modId?.Trim() ?? string.Empty;
            string target = targetId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(target) ||
                string.Equals(key, target, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Dictionary<string, ModRelationshipRule> next = CloneRelationshipRules(relationshipRules);
            if (!next.TryGetValue(key, out ModRelationshipRule? rule))
            {
                rule = new ModRelationshipRule();
                next[key] = rule;
            }

            List<string> list = GetRelationshipList(rule, kind);
            if (list.Any(id => string.Equals(id, target, StringComparison.OrdinalIgnoreCase)))
                return false;

            list.Add(target);
            SetModRelationshipRules(next);
            return true;
        }

        public void SetSortRules(IEnumerable<ModSortRule> rules)
        {
            sortRules = NormalizeSortRules(rules);
            SaveSortRules();
        }

        private static Dictionary<string, ModRelationshipRule> CloneRelationshipRules(
            IDictionary<string, ModRelationshipRule>? rules)
        {
            Dictionary<string, ModRelationshipRule> clone = new(StringComparer.OrdinalIgnoreCase);
            if (rules == null)
                return clone;

            foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
            {
                string key = kvp.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key) || kvp.Value == null)
                    continue;
                clone[key] = kvp.Value.Clone();
            }

            return clone;
        }

        private static List<string> GetRelationshipList(ModRelationshipRule rule, ModRelationshipKind kind)
        {
            return kind switch
            {
                ModRelationshipKind.Before => rule.BeforeIds,
                ModRelationshipKind.After => rule.AfterIds,
                ModRelationshipKind.Required => rule.RequiredIds,
                ModRelationshipKind.Incompatible => rule.IncompatibleIds,
                _ => rule.BeforeIds
            };
        }

        private static Dictionary<string, ModRelationshipRule> NormalizeRelationshipRules(
            IDictionary<string, ModRelationshipRule>? rules)
        {
            Dictionary<string, ModRelationshipRule> normalized = new(StringComparer.OrdinalIgnoreCase);
            if (rules == null)
                return normalized;

            foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
            {
                string key = kvp.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key) || kvp.Value == null)
                    continue;

                ModRelationshipRule rule = new ModRelationshipRule
                {
                    BeforeIds = NormalizeIdList(kvp.Value.BeforeIds),
                    AfterIds = NormalizeIdList(kvp.Value.AfterIds),
                    RequiredIds = NormalizeIdList(kvp.Value.RequiredIds),
                    IncompatibleIds = NormalizeIdList(kvp.Value.IncompatibleIds)
                };

                if (!rule.IsEmpty)
                    normalized[key] = rule;
            }

            return normalized;
        }

        private static List<string> NormalizeIdList(IEnumerable<string>? ids)
        {
            List<string> normalized = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            if (ids == null)
                return normalized;

            foreach (string id in ids)
            {
                string trimmed = id?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(trimmed) && seen.Add(trimmed))
                    normalized.Add(trimmed);
            }

            return normalized;
        }

        private static List<ModSortRule> RelationshipRulesToSortRules(
            IDictionary<string, ModRelationshipRule> rules)
        {
            List<ModSortRule> converted = new();
            foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
            {
                string ownerId = kvp.Key.Trim();
                foreach (string targetId in kvp.Value.BeforeIds)
                    converted.Add(new ModSortRule { BeforeId = ownerId, AfterId = targetId });
                foreach (string targetId in kvp.Value.AfterIds)
                    converted.Add(new ModSortRule { BeforeId = targetId, AfterId = ownerId });
                foreach (string targetId in kvp.Value.RequiredIds)
                    converted.Add(new ModSortRule { BeforeId = targetId, AfterId = ownerId });
            }

            return NormalizeSortRules(converted);
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
            relationshipRules = LoadRelationshipRules();
            if (File.Exists(modRelationshipRulesPath))
            {
                sortRules = RelationshipRulesToSortRules(relationshipRules);
                return;
            }

            sortRules = new List<ModSortRule>();
            if (!File.Exists(modSortRulesPath))
                return;

            try
            {
                string jsonContent = File.ReadAllText(modSortRulesPath);
                List<ModSortRule>? loadedRules = JsonSerializer.Deserialize<List<ModSortRule>>(jsonContent);
                if (loadedRules != null)
                    sortRules = NormalizeSortRules(loadedRules);
                relationshipRules = LegacySortRulesToRelationshipRules(sortRules);
            }
            catch
            {
                sortRules = new List<ModSortRule>();
                relationshipRules = new Dictionary<string, ModRelationshipRule>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static Dictionary<string, ModRelationshipRule> LoadRelationshipRules()
        {
            if (!File.Exists(modRelationshipRulesPath))
                return new Dictionary<string, ModRelationshipRule>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string jsonContent = File.ReadAllText(modRelationshipRulesPath);
                Dictionary<string, ModRelationshipRule>? loadedRules =
                    JsonSerializer.Deserialize<Dictionary<string, ModRelationshipRule>>(jsonContent);
                return NormalizeRelationshipRules(loadedRules);
            }
            catch
            {
                return new Dictionary<string, ModRelationshipRule>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static Dictionary<string, ModRelationshipRule> LegacySortRulesToRelationshipRules(IEnumerable<ModSortRule> rules)
        {
            Dictionary<string, ModRelationshipRule> converted = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModSortRule rule in NormalizeSortRules(rules))
            {
                string before = rule.BeforeId?.Trim() ?? string.Empty;
                string after = rule.AfterId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after))
                    continue;

                if (!converted.TryGetValue(before, out ModRelationshipRule? relationshipRule))
                {
                    relationshipRule = new ModRelationshipRule();
                    converted[before] = relationshipRule;
                }

                relationshipRule.BeforeIds.Add(after);
            }

            return NormalizeRelationshipRules(converted);
        }

        private void SaveRelationshipRules()
        {
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string jsonContent = JsonSerializer.Serialize(relationshipRules, options);
                File.WriteAllText(modRelationshipRulesPath, jsonContent);
            }
            catch
            {
                // Ignore relationship rule save failures.
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

        public IReadOnlyList<ModSortRule> GetCommunitySortRules()
        {
            return communitySortRules;
        }

        public async Task<bool> FetchCommunitySortRulesAsync(string repositoryUrl)
        {
            string? rawUrl = GitHubUrlParser.ToRawFileUrl(repositoryUrl);
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                communitySortRules = new List<ModSortRule>();
                return false;
            }

            try
            {
                using HttpResponseMessage response = await GitHubFileClient.Instance.GetAsync(rawUrl);
                if (!response.IsSuccessStatusCode)
                {
                    communitySortRules = new List<ModSortRule>();
                    return false;
                }

                string jsonContent = await response.Content.ReadAsStringAsync();
                List<ModSortRule>? loadedRules = JsonSerializer.Deserialize<List<ModSortRule>>(jsonContent);
                communitySortRules = NormalizeSortRules(loadedRules);
                return true;
            }
            catch
            {
                communitySortRules = new List<ModSortRule>();
                return false;
            }
        }

        public static void SetCommunitySortRulesUrl(string repositoryUrl)
        {
            ConfigManager.Config.CommunitySortRulesUrl = repositoryUrl?.Trim() ?? string.Empty;
            ConfigManager.SaveConfigFile("Url");
        }

        private void LoadCommunitySortRules()
        {
            string url = ConfigManager.Config.CommunitySortRulesUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                communitySortRules = new List<ModSortRule>();
                return;
            }

            try
            {
                Task.Run(async () => await FetchCommunitySortRulesAsync(url)).Wait();
            }
            catch
            {
                communitySortRules = new List<ModSortRule>();
            }
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

        public static bool HasDfhackExecutable()
        {
            string dfhackRunPath = GetDfhackRunPath();
            return !string.IsNullOrWhiteSpace(dfhackRunPath) && File.Exists(dfhackRunPath);
        }

        public static bool IsDfhackRpcRunning()
        {
            return DFHackRpcClient.IsDFHackRunning(Config?.DFFolderPath);
        }

        public static bool IsDFHackInstalled()
        {
            return !string.IsNullOrWhiteSpace(Config?.DFHackFolderPath) && Directory.Exists(Config.DFHackFolderPath);
        }
        private void ResolveActiveModpackStorage()
        {
            string dfhackPath = GetModManagerConfigPath();
            if (HasDfhackExecutable() && !string.IsNullOrWhiteSpace(dfhackPath))
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

        private static bool TryReadModpackFile(string path, out List<DFHModpack> loadedModpacks, out string error)
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

        public static bool CanDeleteModFromModsFolder(ModReference modref)
        {
            if (modref == null || string.IsNullOrWhiteSpace(modref.path) || Config == null)
                return false;
            string modsPath = GetModsPath();
            if (string.IsNullOrWhiteSpace(modsPath))
                return false;
            string modPath = ConfigManager.ResolveCanonicalPath(modref.path);
            modsPath = ConfigManager.ResolveCanonicalPath(modsPath);
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
                ShowNotification($"Deleted mod folder: {Path.GetFileName(modPath)}", "trashIcon.svg");
            }
            catch (Exception ex)
            {
                message = $"Failed to delete mod folder: {ex.Message}";
                return false;
            }

            DFHMod dfm = modref.ToDFHMod();
            string modrefKey = modref.DFHackCompatibleString();

            HashSet<DFHMod> newModPool = new HashSet<DFHMod>(modPool);
            newModPool.Remove(dfm);
            List<DFHMod> newEnabledMods = enabledMods.Where(m => m != dfm).ToList();
            HashSet<DFHMod> newDisabledMods = new HashSet<DFHMod>(disabledMods);
            newDisabledMods.Remove(dfm);
            Dictionary<string, ModReference> newModrefMap = new Dictionary<string, ModReference>(modrefMap, StringComparer.OrdinalIgnoreCase);
            newModrefMap.Remove(modrefKey);

            lock (stateGate)
            {
                modPool = newModPool;
                enabledMods = newEnabledMods;
                disabledMods = newDisabledMods;
                modrefMap = newModrefMap;
            }

            RefreshInstalledCacheModIds();
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
                    {
                        Directory.Delete(entry, true);
                        ShowNotification($"Deleted folder: {Path.GetFileName(entry)}", "trashIcon.svg");
                    }
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
            lock (installedCacheGate)
            {
                installedCacheModIds ??= BuildInstalledCacheModIds();
                return installedCacheModIds;
            }
        }

        public void RefreshInstalledCacheModIds()
        {
            lock (installedCacheGate)
            {
                installedCacheModIds = BuildInstalledCacheModIds();
            }
        }

        private static HashSet<string> BuildInstalledCacheModIds()
        {
            List<string> roots = new List<string>();

            string installedModsPath = GetInstalledModsPath();
            if (!string.IsNullOrWhiteSpace(installedModsPath))
                roots.Add(ResolveExistingDirectoryPath(installedModsPath) ?? installedModsPath);

            if (!string.IsNullOrWhiteSpace(Config?.DFFolderPath))
            {
                string? vanillaPath = GetVanillaModsPath();
                if (!string.IsNullOrWhiteSpace(vanillaPath))
                    roots.Add(ResolveExistingDirectoryPath(vanillaPath) ?? vanillaPath);
            }

            List<string> candidateDirs = new List<string>();
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                candidateDirs.AddRange(EnumerateModDirectoriesWithInfo(root));
            }

            // A plain membership set has no "which duplicate wins" concern (unlike BuildModIdPathMap's
            // dir-per-id map), so a ConcurrentDictionary-backed set is enough
            ConcurrentDictionary<string, byte> ids = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            Parallel.ForEach(candidateDirs, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, dir =>
            {
                string? infoPath = ResolveInfoFilePath(dir);
                if (string.IsNullOrWhiteSpace(infoPath))
                    return;

                try
                {
                    string info = File.ReadAllText(infoPath);
                    Match idMatch = Regex.Match(info, @"\[ID:([^\]]+)\]", RegexOptions.IgnoreCase);
                    if (idMatch.Success)
                    {
                        string id = idMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(id))
                            ids.TryAdd(id, 0);
                    }
                }
                catch
                {
                    // Ignore unreadable info files.
                }
            });

            return new HashSet<string>(ids.Keys, StringComparer.OrdinalIgnoreCase);
        }

        private static string scrDir = "src_dir";

        /// <summary>
        /// Will attempt to find mods from DFHack's Lua memory interface, otherwise fallbacks to filesystem scan
        /// </summary>
        private void FindAllModsDFHackLua()
        {
            if (!DwarfFortressRunning())
            {
                Console.WriteLine("DF not running. Falling back to filesystem scan.");
                FindAllModsFromDisk();
                return;
            }

            Console.WriteLine("Finding all mods... ");

            HashSet<Dictionary<string, string>> modData;
            try
            {
                modData = GetModMemoryData();
            }
            catch (UserActionRequiredException)
            {
                Console.WriteLine("DF not on world creation screen. Falling back to filesystem scan.");
                FindAllModsFromDisk();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DFHack memory query failed. Falling back to filesystem scan: {ex.Message}");
                FindAllModsFromDisk();
                return;
            }

            Console.WriteLine($"DFHack memory mod entries received: {modData.Count}.");
            Dictionary<string, string> modIdPathMap = BuildModIdPathMap();

            // Enumerate. Pin down an explicit order, so the parallel compute and sequential merge phases below are deterministic
            List<Dictionary<string, string>> modDataList = modData.ToList();

            // Parallel compute. Path resolution, ModReference construction and LastModifiedTime stamp are all independent per entry work
            ModReference?[] results = new ModReference?[modDataList.Count];
            Parallel.For(0, modDataList.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
            {
                Dictionary<string, string> modDataEntry = modDataList[i];
                modDataEntry[scrDir] = ResolveModPath(modDataEntry, modIdPathMap);

                // Mod setup and registry.
                ModReference modRef = new ModReference(modDataEntry);
                modRef.LastModifiedTime = GetLatestModifiedTimestampCached(modDataEntry["src_dir"]);

                ModSourceClassifier.Classify(modRef, GetModsPath(), GetVanillaModsPath());
                if (modRef.IsIgnored)
                {
                    results[i] = null; // Mark as null to be filtered out later
                }
                else
                {
                    results[i] = modRef;
                }
            });

            // Sequential merge. Preserves "first occurrence wins" for duplicates, same as before.
            Dictionary<string, ModReference> newModrefMap = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ModReference>> newDuplicateModRefs = new(StringComparer.OrdinalIgnoreCase);
            HashSet<DFHMod> newModPool = new();

            foreach (ModReference? modRef in results.Where(m => m != null))
            {
                if (modRef == null)
                    continue;

                string key = modRef.DFHackCompatibleString();
                if (newModrefMap.ContainsKey(key))
                {
                    if (!newDuplicateModRefs.TryGetValue(key, out var list))
                    {
                        list = new List<ModReference> { newModrefMap[key] };
                        newDuplicateModRefs[key] = list;
                    }
                    list.Add(modRef);
                    continue;
                }

                Console.WriteLine($"   Mod found + registered: {modRef.name}.");
                newModrefMap.Add(key, modRef);
                newModPool.Add(modRef.ToDFHMod());
            }

            PublishModCatalog(newModrefMap, newModPool, newDuplicateModRefs);
        }

        /// <summary>
        /// Usually the main method for finding mods
        /// </summary>
        public void FindAllModsFromDisk()
        {
            Console.WriteLine("Finding all mods (filesystem)...");

            List<(string Root, string Dir)> candidates = new List<(string, string)>();
            foreach (string root in EnumerateModRoots())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                foreach (string dir in EnumerateModDirectoriesWithInfo(root))
                    candidates.Add((root, dir));
            }

            ModReference?[] results = new ModReference?[candidates.Count];
            Parallel.For(0, candidates.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
            {
                results[i] = TryBuildModReferenceFromDirectory(candidates[i].Dir);
            });


            Dictionary<string, ModReference> newModrefMap = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ModReference>> newDuplicateModRefs = new(StringComparer.OrdinalIgnoreCase);
            HashSet<DFHMod> newModPool = new();

            string? currentRoot = null;
            int candidateCount = 0;
            int addedCount = 0;
            int duplicateCount = 0;

            void FlushRootDiagnostics()
            {
                if (currentRoot != null)
                {
                    InfoLogger.Log($"Disk mod scan root: '{NormalizeFileSystemPath(currentRoot)}' candidates: {candidateCount}, added: {addedCount}, duplicates: {duplicateCount}, total registered: {newModrefMap.Count}.");
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                (string root, _) = candidates[i];
                if (root != currentRoot)
                {
                    FlushRootDiagnostics();
                    currentRoot = root;
                    candidateCount = 0;
                    addedCount = 0;
                    duplicateCount = 0;
                }

                candidateCount++;
                ModReference? modRef = results[i];
                if (modRef == null)
                    continue;

                string key = modRef.DFHackCompatibleString();
                if (newModrefMap.ContainsKey(key))
                {
                    duplicateCount++;
                    if (!newDuplicateModRefs.TryGetValue(key, out var list))
                    {
                        list = new List<ModReference> { newModrefMap[key] };
                        newDuplicateModRefs[key] = list;
                    }
                    list.Add(modRef);
                    continue;
                }

                Console.WriteLine($"   Mod found + registered: {modRef.name}.");
                newModrefMap.Add(key, modRef);
                newModPool.Add(modRef.ToDFHMod());
                addedCount++;
            }

            FlushRootDiagnostics();

            PublishModCatalog(newModrefMap, newModPool, newDuplicateModRefs);

            InfoLogger.Log($"Disk mod scan completed. Total registered mods: {newModrefMap.Count}.");
        }

        // Parallel safe
        private ModReference? TryBuildModReferenceFromDirectory(string dir)
        {
            string? infoPath = ResolveInfoFilePath(dir);
            if (string.IsNullOrWhiteSpace(infoPath))
                return null;

            Dictionary<string, string> modData = BuildModMemoryDataFromInfo(infoPath, dir, out bool missingVersion);
            if (!modData.TryGetValue("id", out string? id) || string.IsNullOrWhiteSpace(id))
                return null;

            ModReference modRef = new ModReference(modData)
            {
                MissingVersion = missingVersion,
                LastModifiedTime = GetLatestModifiedTimestampCached(dir)
            };

            ModSourceClassifier.Classify(modRef, GetModsPath(), GetVanillaModsPath());
            if (modRef.IsIgnored)
                return null;

            return modRef;
        }

        // A reader that checks modPool for a key must be able to find it in modrefMap too.
        // Grouping the reassignment under one lock stops two writers (e.g. a manual reload racing the auto-reload timer) from interleaving; readers stay lock-free by design.
        private void PublishModCatalog(
            Dictionary<string, ModReference> newModrefMap,
            HashSet<DFHMod> newModPool,
            Dictionary<string, List<ModReference>> newDuplicateModRefs)
        {
            lock (stateGate)
            {
                modrefMap = newModrefMap;
                modPool = newModPool;
                duplicateModRefs = newDuplicateModRefs;
            }
        }

        // Thread-safe: written to concurrently from FindAllModsFromDisk's Parallel.For
        private readonly ConcurrentDictionary<string, (string QuickStamp, DateTime? LastModified)> lastModifiedTimestampCache =
            new(StringComparer.OrdinalIgnoreCase);

        // Call GetLatestModifiedTimestampCached instead of this
        private static DateTime? GetLatestModifiedTimestamp(string directoryPath)
        {
            DateTime? result = FolderTimestampHelper.GetLatestModifiedTimeUtc(
                directoryPath,
                ex => Console.WriteLine($"Warning: Failed to enumerate files in {directoryPath} for timestamp: {ex.Message}"));
            return result ?? (Directory.Exists(directoryPath) ? Directory.GetLastWriteTimeUtc(directoryPath) : null);
        }

        // Cached wrapper around GetLatestModifiedTimestamp
        private DateTime? GetLatestModifiedTimestampCached(string directoryPath)
        {
            string canonicalPath = ConfigManager.ResolveCanonicalPath(directoryPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(canonicalPath) && !string.IsNullOrEmpty(directoryPath))
                return GetLatestModifiedTimestamp(directoryPath);

            string quickStamp = ModUpdateLogger.BuildLocalQuickStamp(canonicalPath);
            if (!string.IsNullOrEmpty(quickStamp) &&
                lastModifiedTimestampCache.TryGetValue(canonicalPath, out (string QuickStamp, DateTime? LastModified) cached) &&
                cached.QuickStamp == quickStamp)
            {
                return cached.LastModified;
            }

            DateTime? computed = GetLatestModifiedTimestamp(canonicalPath);
            if (!string.IsNullOrEmpty(quickStamp))
                lastModifiedTimestampCache[canonicalPath] = (quickStamp, computed);
            return computed;
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

            // Check for Steam description files
            string steamDescriptionContent = string.Empty;
            string steamDescriptionTxtPath = Path.Combine(modPath, "steam_description.txt");
            string steamTxtPath = Path.Combine(modPath, "steam.txt");

            if (File.Exists(steamDescriptionTxtPath))
            {
                steamDescriptionContent = File.ReadAllText(steamDescriptionTxtPath);
            }
            else if (File.Exists(steamTxtPath))
            {
                steamDescriptionContent = File.ReadAllText(steamTxtPath);
            }

            // Prioritize Steam description if available
            string finalDescription = string.IsNullOrWhiteSpace(steamDescriptionContent) ? description : steamDescriptionContent;
            string finalSteamDescription = steamDescriptionContent; // Keep original steamDescription for steam_description field

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
            if (!ConfigManager.TryParsePositiveSteamId(steamFileId, out string normalizedSteamFileId))
            {
                if (ConfigManager.TryExtractSteamWorkshopItemIdFromPath(modPath, out string steamItemIdFromPath))
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
                ["description"] = finalDescription,
                ["steam_file_id"] = steamFileId,
                ["steam_title"] = GetInfoTag(tags, "STEAM_TITLE") ?? string.Empty,
                ["steam_description"] = finalSteamDescription,
                [scrDir] = modPath
            };
        }

        private static Dictionary<string, string> ParseInfoTags(string info)
        {
            Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(info))
                return tags;

            int i = 0;
            while (i < info.Length)
            {
                int tagStart = info.IndexOf('[', i);
                if (tagStart == -1)
                    break;

                int colonIndex = info.IndexOf(':', tagStart);
                if (colonIndex != -1 && colonIndex > tagStart + 1)
                {
                    bool isValid = true;
                    for (int k = tagStart + 1; k < colonIndex; k++)
                    {
                        char c = info[k];
                        if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'))
                        {
                            isValid = false;
                            break;
                        }
                    }

                    if (isValid)
                    {
                        string tag = info.Substring(tagStart + 1, colonIndex - tagStart - 1);
                        int valueStart = colonIndex + 1;
                        int depth = 1;
                        StringBuilder valueBuilder = new StringBuilder();

                        int j = valueStart;
                        while (j < info.Length)
                        {
                            char c = info[j];
                            if (c == '[')
                            {
                                if (IsNewTagStart(info, j))
                                {
                                    break;
                                }
                                depth++;
                                valueBuilder.Append(c);
                                j++;
                            }
                            else if (c == ']')
                            {
                                depth--;
                                if (depth == 0)
                                {
                                    j++;
                                    break;
                                }
                                valueBuilder.Append(c);
                                j++;
                            }
                            else
                            {
                                valueBuilder.Append(c);
                                j++;
                            }
                        }

                        tags[tag] = valueBuilder.ToString().Trim();
                        i = j;
                        continue;
                    }
                }

                i = tagStart + 1;
            }

            return tags;
        }

        private static bool IsNewTagStart(string info, int index)
        {
            if (index >= info.Length || info[index] != '[')
                return false;

            int nextColon = info.IndexOf(':', index);
            if (nextColon == -1 || nextColon <= index + 1)
                return false;

            for (int k = index + 1; k < nextColon; k++)
            {
                char c = info[k];
                if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'))
                    return false;
            }
            return true;
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

        private static Dictionary<string, string> BuildModIdPathMap()
        {
            // Enumerate phase: preserve root/dir order so the merge below reproduces the original "first occurrence wins" semantics.
            List<string> candidateDirs = new List<string>();
            foreach (string root in EnumerateModRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string dir in EnumerateModDirectoriesWithInfo(root))
                    candidateDirs.Add(dir);
            }

            // Parallel-compute phase: reading + regex-matching each info.txt is independent per directory.
            (string? Id, string Dir)?[] results = new (string?, string)?[candidateDirs.Count];
            Parallel.For(0, candidateDirs.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
            {
                string dir = candidateDirs[i];
                string? infoPath = ResolveInfoFilePath(dir);
                if (string.IsNullOrWhiteSpace(infoPath))
                    return;

                try
                {
                    string info = File.ReadAllText(infoPath);
                    Match idMatch = Regex.Match(info, @"\[ID:([^\]]+)\]", RegexOptions.IgnoreCase);
                    if (idMatch.Success)
                    {
                        string id = idMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(id))
                            results[i] = (id, dir);
                    }
                }
                catch
                {
                    // Ignore unreadable info files.
                }
            });

            // Sequential-merge phase: same "first occurrence across roots/dirs wins" as the original loop.
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string? Id, string Dir)? entry in results)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Value.Id))
                    continue;
                if (!map.ContainsKey(entry.Value.Id))
                    map[entry.Value.Id] = entry.Value.Dir;
            }

            InfoLogger.Log($"Mod ID path map entries: {map.Count}.");
            return map;
        }

        private static string ResolveModPath(Dictionary<string, string> modDataEntry, Dictionary<string, string> modIdPathMap)
        {
            string rawSrcDir = modDataEntry.TryGetValue(scrDir, out string? srcDirValue)
                ? srcDirValue ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(rawSrcDir))
                return string.Empty;

            // Already absolute and valid.
            string? resolvedRawSrcDir = ResolveExistingDirectoryPath(rawSrcDir);
            if (!string.IsNullOrWhiteSpace(resolvedRawSrcDir))
                return resolvedRawSrcDir;

            string fullPath = string.IsNullOrWhiteSpace(Config?.DFFolderPath)
                ? rawSrcDir
                : Path.Combine(Config.DFFolderPath, rawSrcDir);

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
                InfoLogger.Log($"ResolveModPath fallback unresolved. id: '{modId}', raw_src: '{rawSrcDir}', resolved: '{NormalizeFileSystemPath(fullPath)}'.");
            }

            return NormalizeFileSystemPath(fullPath);
        }

        // Output a dictionary, that given a modID gets the true version.
        // Because this relies on the DFHack codebase there is a non 0 chance of becoming incompatible with future versions of DFHack.
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

        // Use dfhack-run and lua to get raw mod data.
        private string LoadModMemoryData()
        {
            // Get path to lua script.
            string luaPath = Path.Combine(AppContext.BaseDirectory, "lua", "GetModMemoryData.lua");
            if (!File.Exists(luaPath))
                throw new FileNotFoundException("GetModMemoryData.lua not found.", luaPath);

            // Try direct RPC first (faster, avoids spawning processes)
            string? rpcOutput = DFHackRpcClient.ExecuteDFHackCommandViaRpc("lua", new List<string> { "-f", luaPath }, Config?.DFFolderPath, out string rpcError);
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
                WorkingDirectory = Config!.DFFolderPath,
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

        // Check if DF is running.
        public static bool DwarfFortressRunning()
        {
            string[] candidateNames = { "Dwarf Fortress", "df", "dwarfort", "dwarfort.exe", "df.exe", "dwarf_fortress" };
            foreach (string name in candidateNames)
            {
                Process[]? procs = null;
                try
                {
                    procs = Process.GetProcessesByName(name);
                    if (procs.Length > 0)
                        return true;
                }
                catch
                {
                    // Ignore query failures for this candidate name.
                }
                finally
                {
                    if (procs != null)
                    {
                        foreach (var p in procs)
                        {
                            p?.Dispose();
                        }
                    }
                }
            }

            // Fallback: iterate all processes for robust matching across OS (especially Linux/macOS where comm names might differ or truncate)
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string pName = p.ProcessName;
                        if (candidateNames.Any(c => string.Equals(pName, c, StringComparison.OrdinalIgnoreCase) ||
                                                    pName.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
                        {
                            p.Dispose();
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore access denied or exited processes
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore global process enumeration failures
            }

            return false;
        }

        // Run Dwarf Fortress executable or trigger Steam launch.
        public async Task<(bool Success, string Message)> RunDwarfFortressAsync()
        {
            if (DwarfFortressRunning())
                return (false, "Dwarf Fortress is already running.");

            if (Config == null || string.IsNullOrWhiteSpace(Config.DFFolderPath))
                return (false, "Dwarf Fortress folder path is not configured.");

            string dfFolderPath = Config.DFFolderPath;
            if (!Directory.Exists(dfFolderPath))
                return (false, $"Dwarf Fortress folder not found: {dfFolderPath}");

            if (!DwarfFortressExecutableLocator.TryResolvePath(dfFolderPath, out string executablePath))
                return (false, "Dwarf Fortress executable not found in the configured folder.");

            try
            {
                // Check if Dwarf Fortress is a Steam installation
                bool isSteamInstallation = !string.IsNullOrWhiteSpace(ConfigManager.TryFindSteamDwarfFortressFolder());

                if (isSteamInstallation)
                {
                    InfoLogger.LogRunDf("Launching Dwarf Fortress through Steam (App ID: 975370)");

                    bool launched = false;
                    try
                    {
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = "steam://run/975370",
                            UseShellExecute = true
                        };

                        // Trigger Steam protocol handler
                        using (Process? launcherProcess = Process.Start(startInfo))
                        {
                            if (launcherProcess != null)
                            {
                                InfoLogger.LogRunDf($"Steam protocol handler invoked (PID = {launcherProcess.Id})");
                                launched = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        InfoLogger.LogRunDf($"Failed to launch via steam:// protocol handler: {ex.Message}");
                    }

                    if (!launched && OperatingSystem.IsLinux())
                    {
                        // Fallback on Linux to direct steam command
                        try
                        {
                            InfoLogger.LogRunDf("Trying fallback Steam CLI launch (steam -applaunch 975370)...");
                            ProcessStartInfo fallbackStartInfo = new ProcessStartInfo
                            {
                                FileName = "steam",
                                Arguments = "-applaunch 975370",
                                UseShellExecute = true
                            };
                            using (Process? fallbackProcess = Process.Start(fallbackStartInfo))
                            {
                                if (fallbackProcess != null)
                                {
                                    InfoLogger.LogRunDf($"Steam CLI invoked (PID = {fallbackProcess.Id})");
                                    launched = true;
                                }
                            }
                        }
                        catch (Exception ex2)
                        {
                            InfoLogger.LogRunDf($"Fallback Steam CLI launch failed: {ex2.Message}");
                        }
                    }

                    if (!launched)
                    {
                        return (false, "Failed to trigger Steam launcher or protocol handler.");
                    }

                    // Poll for the actual Dwarf Fortress process to appear in the OS process list (increased to 60s for Steam startup overhead)
                    const int pollIntervalMs = 1000;
                    const int maxWaitTimeoutMs = 60000;
                    int totalElapsedMs = 0;

                    while (totalElapsedMs < maxWaitTimeoutMs)
                    {
                        await Task.Delay(pollIntervalMs);
                        totalElapsedMs += pollIntervalMs;

                        if (DwarfFortressRunning())
                        {
                            InfoLogger.LogRunDf($"Dwarf Fortress process detected active after {totalElapsedMs}ms.");
                            return (true, "Dwarf Fortress launched successfully through Steam.");
                        }
                    }

                    return (false, $"Steam launch was triggered, but Dwarf Fortress failed to launch within {maxWaitTimeoutMs / 1000} seconds.");
                }
                else
                {
                    InfoLogger.LogRunDf($"Launching Dwarf Fortress executable directly: {executablePath}");

                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        WorkingDirectory = dfFolderPath,
                        UseShellExecute = false
                    };

                    if (OperatingSystem.IsLinux() &&
                        string.Equals(Path.GetFileName(executablePath), "dwarfort", StringComparison.Ordinal))
                    {
                        string? bundledLibs = DwarfFortressExecutableLocator.TryResolveBundledLibraryPath(dfFolderPath);
                        if (!string.IsNullOrWhiteSpace(bundledLibs))
                        {
                            string existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty;
                            startInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existing)
                                ? bundledLibs
                                : $"{bundledLibs}:{existing}";
                            InfoLogger.LogRunDf($"LD_LIBRARY_PATH set to: {startInfo.EnvironmentVariables["LD_LIBRARY_PATH"]}");
                        }
                    }

                    Process? started = Process.Start(startInfo);
                    if (started == null)
                        return (false, "Failed to launch Dwarf Fortress directly: process could not be created.");

                    InfoLogger.LogRunDf($"Started direct process PID = {started.Id}");

                    // Verify the executable didn't crash on startup (e.g., missing dynamic libraries/DLLs)
                    await Task.Delay(2000);

                    if (started.HasExited)
                    {
                        int exitCode = started.ExitCode;
                        started.Dispose();
                        return (false, $"Dwarf Fortress exited immediately (Exit code {exitCode}). Check terminal output or error logs for missing dynamic libraries.");
                    }

                    started.Dispose();
                    return (true, "Dwarf Fortress launched successfully.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Failed to launch Dwarf Fortress: {ex.Message}");
            }
        }

        public static bool IsDwarfFortressFound()
        {
            if (Config == null || string.IsNullOrWhiteSpace(Config.DFFolderPath))
            {
                return false;
            }

            string dfFolderPath = Config.DFFolderPath;
            if (!Directory.Exists(dfFolderPath))
            {
                return false;
            }

            return DwarfFortressExecutableLocator.TryResolvePath(dfFolderPath, out _);

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

            lock (stateGate)
                savingModpacksCooldownUntilUtc = DateTime.UtcNow.AddSeconds(2);

            File.WriteAllText(path, modlistJson);
        }

        private static JsonSerializerOptions GetModpackJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true
            };
        }

        ///<summary> Only used for local and live modpack saving, not ui or mods</summary>
        private bool TryRequestModManagerReload(out bool deferred, out string message)
        {
            deferred = false;
            message = string.Empty;

            if (activeModpackBackend != ModpackStorageBackend.DFHackConfig)
            {
                message = "Saved locally. In-game apply requires DFHack.";
                return false;
            }

            if (!HasDfhackExecutable())
            {
                message = "DFHack not found! In-game apply skipped.";
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
                ShowNotification("DFHack screen reload applied", "saveDfHackIcon.svg");
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
            string? rpcOutput = DFHackRpcClient.ExecuteDFHackCommandViaRpc("lua", new List<string> { "-f", luaPath }, Config?.DFFolderPath, out string rpcError);
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
                WorkingDirectory = Config!.DFFolderPath,
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
                    ShowNotification("DFHack screen reload failed", "saveDfHackIcon.svg");
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
            lock (stateGate)
                selectedModlistIndex = index;

            // Regenerate enabled and disabled lists to match newly selected modpack.
            SetActiveMods(SelectedModlist.modlist);

            // Find problems with newly selected modpack.
            FindModlistProblems();
        }

        // Changes currently enabled and disabled mods based on the given list.
        // The only time this is called (other than SetSelectedModpack) is when overwriting a modpack due to importing.
        public void SetActiveMods(List<DFHMod> mods)
        {
            List<DFHMod> newEnabledMods = new List<DFHMod>(mods);
            HashSet<DFHMod> newDisabledMods = new HashSet<DFHMod>(modPool);
            foreach (DFHMod mod in newEnabledMods)
                newDisabledMods.Remove(mod);

            lock (stateGate)
            {
                enabledMods = newEnabledMods;
                disabledMods = newDisabledMods;
            }
            //dont show stale problems in the inactive list
            FindModlistProblems();
        }

        public void MoveMods(List<DFHMod> mods, int newIndex, bool sourceLeft, bool destinationLeft)
        {
            if (mods == null || mods.Count == 0)
                return;

            List<DFHMod> uniqueMods = new List<DFHMod>();
            HashSet<DFHMod> seen = new HashSet<DFHMod>();
            foreach (DFHMod mod in mods.Where(thisMod => seen.Add(thisMod)))
            {
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
                    lock (stateGate)
                        enabledMods = newList;
                    changed = true;
                }
            }
            else if (!sourceLeft && destinationLeft)
            {
                HashSet<DFHMod> selectedSet = new HashSet<DFHMod>(uniqueMods);
                int beforeCount = enabledMods.Count;
                List<DFHMod> newEnabledMods = enabledMods.Where(m => !selectedSet.Contains(m)).ToList();

                HashSet<DFHMod> newDisabledMods = new HashSet<DFHMod>(disabledMods);
                foreach (DFHMod mod in uniqueMods)
                    newDisabledMods.Add(mod);

                lock (stateGate)
                {
                    enabledMods = newEnabledMods;
                    disabledMods = newDisabledMods;
                }
                changed = newEnabledMods.Count != beforeCount;
            }
            else if (sourceLeft && !destinationLeft)
            {
                HashSet<DFHMod> newDisabledMods = new HashSet<DFHMod>(disabledMods);
                foreach (DFHMod mod in uniqueMods)
                    newDisabledMods.Remove(mod);

                List<DFHMod> newEnabledMods = new List<DFHMod>(enabledMods);
                int insertIndex = Math.Max(0, Math.Min(newIndex, newEnabledMods.Count));
                newEnabledMods.InsertRange(insertIndex, uniqueMods);

                lock (stateGate)
                {
                    disabledMods = newDisabledMods;
                    enabledMods = newEnabledMods;
                }
                changed = true;
            }

            if (changed)
                FindModlistProblems();
        }
        // Go through modlist and scan for problems.
        // Tuple representing problem has problem mod, int problemType (missing before, missing after, conflict present), and string modID.
        public void FindModlistProblems()
        {
            List<ModProblem> newModProblems = new List<ModProblem>();

            string installedModsPath = GetInstalledModsPath();
            bool hasInstalledModsPath = !string.IsNullOrWhiteSpace(installedModsPath);

            foreach (DFHMod dfm in modPool)
            {
                if (duplicateModRefs.TryGetValue(dfm.ToString(), out var duplicates))
                {
                    var relevantDuplicates = duplicates.Where(m =>
                    {
                        if (hasInstalledModsPath && !string.IsNullOrWhiteSpace(m.path) && IsPathUnderRoot(m.path, installedModsPath))
                            return false;

                        if (ConfigManager.IsLikelySteamShadowCopy(m.path, m.steamID, out _))
                            return false;

                        return true;
                    }).ToList();

                    if (relevantDuplicates.Count > 1)
                    {
                        var paths = string.Join(", ", relevantDuplicates.Select(m => $"'{m.path}'"));
                        newModProblems.Add(new ModProblem(dfm.id, paths, ModProblem.ProblemType.DuplicateMod));
                    }
                }
            }

            HashSet<string> scannedModIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> unscannedModIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DFHMod dfm in enabledMods)
                unscannedModIDs.Add(dfm.id);

            HashSet<string> allEnabledIDs = new HashSet<string>(unscannedModIDs, StringComparer.OrdinalIgnoreCase);
            string modNeedIsStr = " mod needing is: ";

            for (int i = 0; i < enabledMods.Count; i++)
            {
                DFHMod currentDFM = enabledMods[i];
                ModReference currentMod = GetRefFromDFHMod(currentDFM);

                if (currentMod.problematic)
                {
                    foreach (string beforeID in currentMod.require_before_me)
                    {
                        string trimmedId = beforeID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId))
                            continue;
                        if (!scannedModIDs.Contains(trimmedId))
                        {
                            newModProblems.Add(new ModProblem(currentDFM.id, trimmedId, ModProblem.ProblemType.MissingBefore));
                            InfoLogger.Log("Problem found: missing before mod with ID: " + trimmedId + modNeedIsStr + currentDFM.id);
                        }
                    }
                    foreach (string afterID in currentMod.require_after_me)
                    {
                        string trimmedId = afterID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId))
                            continue;
                        if (!unscannedModIDs.Contains(trimmedId))
                        {
                            newModProblems.Add(new ModProblem(currentDFM.id, trimmedId, ModProblem.ProblemType.MissingAfter));
                            InfoLogger.Log("Problem found: missing after mod with ID: " + trimmedId + modNeedIsStr + currentDFM.id);
                        }
                    }
                    foreach (string conflictID in currentMod.conflicts_with)
                    {
                        string trimmedId = conflictID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId))
                            continue;
                        if (allEnabledIDs.Contains(trimmedId))
                        {
                            newModProblems.Add(new ModProblem(currentDFM.id, trimmedId, ModProblem.ProblemType.ConflictPresent));
                            InfoLogger.Log("Problem found: conflict present mod with ID: " + trimmedId + modNeedIsStr + currentDFM.id);
                        }
                    }
                    foreach (string requiredID in currentMod.require_ids)
                    {
                        string trimmedId = requiredID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId))
                            continue;
                        if (!scannedModIDs.Contains(trimmedId) && !unscannedModIDs.Contains(trimmedId))
                        {
                            newModProblems.Add(new ModProblem(currentDFM.id, trimmedId, ModProblem.ProblemType.MissingRequired));
                            InfoLogger.Log("Problem found: missing required mod with ID: " + trimmedId + modNeedIsStr + currentDFM.id);
                        }

                    }
                }

                if (relationshipRules.TryGetValue(currentDFM.id, out ModRelationshipRule? customRule))
                {
                    foreach (string beforeID in customRule.BeforeIds)
                    {
                        string trimmedId = beforeID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId) || !allEnabledIDs.Contains(trimmedId))
                            continue;
                        if (!unscannedModIDs.Contains(trimmedId))
                        {
                            newModProblems.Add(new ModProblem(currentDFM.id, trimmedId, ModProblem.ProblemType.MissingAfter));
                            InfoLogger.Log("Problem found: custom before rule violated for ID: " + trimmedId + modNeedIsStr + currentDFM.id);
                        }
                    }

                    foreach (string afterID in customRule.AfterIds)
                    {
                        string trimmedId = afterID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId) || !allEnabledIDs.Contains(trimmedId))
                            continue;
                        if (!scannedModIDs.Contains(trimmedId))
                        {
                            newModProblems.Add(new ModProblem(currentDFM.id, trimmedId, ModProblem.ProblemType.MissingBefore));
                            InfoLogger.Log("Problem found: custom after rule violated for ID: " + trimmedId + modNeedIsStr + currentDFM.id);
                        }
                    }

                    foreach (string conflictID in customRule.IncompatibleIds)
                    {
                        string trimmedId = conflictID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId))
                            continue;
                        if (allEnabledIDs.Contains(trimmedId))
                        {
                            newModProblems.Add(new ModProblem(currentDFM.id, trimmedId, ModProblem.ProblemType.ConflictPresent));
                            InfoLogger.Log("Problem found: custom incompatible rule present for ID: " + trimmedId + modNeedIsStr + currentDFM.id);
                        }
                    }

                    foreach (string requiredID in customRule.RequiredIds)
                    {
                        string trimmedId = requiredID?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId))
                            continue;
                        if (!allEnabledIDs.Contains(trimmedId))
                        {
                            newModProblems.Add(new ModProblem(currentDFM.id, trimmedId, ModProblem.ProblemType.MissingRequired));
                            InfoLogger.Log("Problem found: custom required mod missing with ID: " + trimmedId + modNeedIsStr + currentDFM.id);
                        }
                    }
                }

                scannedModIDs.Add(currentDFM.id);
                unscannedModIDs.Remove(currentDFM.id);
            }

            lock (stateGate)
                modproblems = newModProblems;
        }

        public IReadOnlyDictionary<string, List<string>> GetDuplicateWarningMap()
        {
            EnsureDuplicateWarningCache(logFound: true);

            HashSet<string> activeModIds = new(enabledMods.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
            HashSet<string> liveConflictedIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (HashSet<string> group in duplicateWarningGroups)
            {
                if (group.Count(activeModIds.Contains) > 1)
                    liveConflictedIds.UnionWith(group.Where(activeModIds.Contains));
            }

            Dictionary<string, List<string>> combinedMap = new(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in duplicateWarningMap)
            {
                if (liveConflictedIds.Contains(kvp.Key))
                    combinedMap[kvp.Key] = new List<string>(kvp.Value);
            }
            foreach (var kvp in cacheDuplicateMap)
            {
                if (combinedMap.TryGetValue(kvp.Key, out var list))
                    list.AddRange(kvp.Value);
                else
                    combinedMap[kvp.Key] = new List<string>(kvp.Value);
            }
            return combinedMap;
        }

        public bool HasErrorLogDuplicateWarning(string modId)
        {
            EnsureDuplicateWarningCache(logFound: false);
            if (string.IsNullOrWhiteSpace(modId))
                return false;

            HashSet<string> activeModIds = new(enabledMods.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
            HashSet<string> liveConflictedIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (HashSet<string> group in duplicateWarningGroups)
            {
                if (group.Count(activeModIds.Contains) > 1)
                    liveConflictedIds.UnionWith(group.Where(activeModIds.Contains));
            }

            return duplicateWarningMap.ContainsKey(modId) && liveConflictedIds.Contains(modId);
        }

        public IReadOnlyList<HashSet<string>> GetDuplicateWarningGroups()
        {
            EnsureDuplicateWarningCache(logFound: true);

            HashSet<string> activeModIds = new(enabledMods.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
            List<HashSet<string>> combinedGroups = new();
            foreach (HashSet<string> group in duplicateWarningGroups)
            {
                HashSet<string> activeMembers = new(group.Where(activeModIds.Contains), StringComparer.OrdinalIgnoreCase);
                if (activeMembers.Count > 1)
                    combinedGroups.Add(activeMembers);
            }
            combinedGroups.AddRange(cacheDuplicateGroups);
            return combinedGroups;
        }

        private void EnsureDuplicateWarningCache(bool logFound)
        {
            RefreshCacheDuplicateMap();

            string errorLogPath = ConfigManager.GetErrorLogPath();
            bool exists = File.Exists(errorLogPath);
            if (logFound && exists &&
                (!string.Equals(lastLoggedErrorLogPath, errorLogPath, StringComparison.OrdinalIgnoreCase) || !lastLoggedErrorLogExists))
            {
                Console.WriteLine($"Dwarf Fortress error log found: {errorLogPath}");
            }

            lastLoggedErrorLogPath = errorLogPath;
            lastLoggedErrorLogExists = exists;

            if (!exists)
            {
                lock (stateGate)
                {
                    duplicateWarningLastWriteUtc = null;
                    duplicateWarningMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    duplicateWarningGroups = new List<HashSet<string>>();
                }
                return;
            }

            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(errorLogPath);
            bool upToDate;
            lock (stateGate)
                upToDate = duplicateWarningLastWriteUtc.HasValue && duplicateWarningLastWriteUtc.Value == lastWriteUtc;

            if (upToDate)
                return;

            Dictionary<string, List<string>> newMap;
            List<HashSet<string>> newGroups;
            try
            {
                ParseDuplicateWarnings(errorLogPath, out newMap, out newGroups);
            }
            catch
            {
                newMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                newGroups = new List<HashSet<string>>();
            }

            lock (stateGate)
            {
                duplicateWarningLastWriteUtc = lastWriteUtc;
                duplicateWarningMap = newMap;
                duplicateWarningGroups = newGroups;
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

        private void RefreshCacheDuplicateMap()
        {
            var cache = ModRawDependencyCacheStore.Load();
            HashSet<string> activeModIds = new(enabledMods.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> newMap = new(StringComparer.OrdinalIgnoreCase);
            List<HashSet<string>> newGroups = new();
            Dictionary<string, List<string>> definitionToMods = new(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in cache.Values)
            {
                if (!activeModIds.Contains(entry.ModId))
                    continue;

                foreach (var defId in entry.DirectDefinitionIds)
                {
                    if (!definitionToMods.TryGetValue(defId, out var mods))
                    {
                        mods = new List<string>();
                        definitionToMods[defId] = mods;
                    }
                    mods.Add(entry.ModId);
                }
            }

            foreach (var kvp in definitionToMods)
            {
                if (kvp.Value.Count > 1)
                {
                    HashSet<string> group = new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
                    newGroups.Add(group);

                    string displayLabel = ObjectKey.FormatForDisplay(kvp.Key);
                    foreach (var modId in kvp.Value)
                    {
                        if (!newMap.TryGetValue(modId, out var objects))
                        {
                            objects = new List<string>();
                            newMap[modId] = objects;
                        }
                        objects.Add($"[Cache] Duplicate raw definition: {displayLabel} (also in: {string.Join(", ", kvp.Value.Where(id => !string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)))})");
                    }
                }
            }

            lock (stateGate)
            {
                cacheDuplicateMap = newMap;
                cacheDuplicateGroups = newGroups;
            }
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

        private void FindModpacks(string? preferredModlistName)
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

            // Everything below works on this local list. loadedModpacks and its DFHModpack entries are freshly deserialized/created and not visible to
            // any other reader yet, so trimming them here is safe. The old bug was publishing straight to the `modpacks` field before this trim loop ran,
            // so a reader could observe a modpack list mid-trim (missing some now-removed mods, still containing others). Now `modpacks` is assigned
            // exactly once, fully formed.
            List<DFHModpack> newModpacks = new List<DFHModpack>(loadedModpacks);

            Console.WriteLine();
            Console.WriteLine("Found modlists: ");

            // Handle mods missing.
            bool modMissing = false;
            string missingMessage = $"Some mods missing. \nModlists will be modified to not require lost mods. \nMissing mods: ";
            HashSet<DFHMod> notFound = new HashSet<DFHMod>();

            // If a default modpack exists.
            int defaultIndex = -1;
            int preferredIndex = -1;

            StringBuilder messageBuilder = new StringBuilder(missingMessage);

            // Go through modpacks, and go through their modlists, looking for mods that we don't have.
            for (int i = 0; i < newModpacks.Count; i++)
            {
                DFHModpack modlist = newModpacks[i];

                // Remove the missing mods from the modlist.
                HashSet<DFHMod> thisListMissingMods = new HashSet<DFHMod>();
                for (int mIndex = 0; mIndex < modlist.modlist.Count; mIndex++)
                {
                    DFHMod mod = modlist.modlist[mIndex];
                    if (!modPool.Contains(mod))
                    {
                        // Check if a mod with the same ID is in modPool
                        DFHMod? matchingIdMod = modPool.FirstOrDefault(m => string.Equals(m.id, mod.id, StringComparison.OrdinalIgnoreCase));
                        if (matchingIdMod is not null)
                        {
                            Console.WriteLine($"[ModHearth] Modpack '{modlist.name}' contains mod '{mod.id}' with version {mod.version}, but version {matchingIdMod.version} is installed. Updating modpack to version {matchingIdMod.version}.");
                            mod.version = matchingIdMod.version;
                            shouldPersistActiveFile = true;
                        }
                        else
                        {
                            modMissing = true;
                            notFound.Add(mod);
                            thisListMissingMods.Add(mod);

                            messageBuilder.Append('\n').Append(mod);
                        }
                    }
                }
                missingMessage = messageBuilder.ToString();

                // modlist isn't published anywhere yet, so replacing its .modlist with a trimmed copy (instead of mutating the shared List<DFHMod>
                // in place via .Remove()) is just cheap extra insurance, not a hard requiremen. Keeps the habit consistent though.
                if (thisListMissingMods.Count > 0)
                    modlist.modlist = modlist.modlist.Where(m => !thisListMissingMods.Contains(m)).ToList();

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
            }

            int? indexToSelect = null;

            if (newModpacks.Count > 0)
            {
                if (preferredIndex >= 0)
                {
                    indexToSelect = preferredIndex;
                }
                else if (defaultIndex >= 0)
                {
                    indexToSelect = defaultIndex;
                }
                else
                {
                    newModpacks[0].@default = true;
                    shouldPersistActiveFile = true;
                    indexToSelect = 0;
                }
            }

            // Create default modpack if none present.
            if (newModpacks.Count == 0)
            {
                newModpacks = CreateDefaultModpacks();
                shouldPersistActiveFile = true;
                indexToSelect = 0;
            }

            // Single publish point, modpacks becomes visible to every other reader
            // only once loading, trimming, and defaulting are fully done.
            lock (stateGate)
                modpacks = newModpacks;

            if (indexToSelect.HasValue)
                SetSelectedModpack(indexToSelect.Value);

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

        public enum ConfigIssueType
        {
            MissingDwarfFortressPath,
            MissingInstalledModsPath,
            MissingDFHackPath
        }

        public readonly record struct ConfigIssue(ConfigIssueType IssueType, string Message);

        public static IReadOnlyList<ConfigIssue> GetConfigIssues()
        {
            var issues = ConfigManager.GetConfigIssues();
            return issues.Select(i => new ConfigIssue(i.IssueType, i.Message)).ToList();
        }

        #endregion

    }
}
