using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModHearth.Models;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;
using ModHearth.Utilities.ModRaws;
using ModHearth.Utilities.Workshop;
using static ModHearth.ConfigManager;

namespace ModHearth
{
    [Serializable]
    public struct ModProblem(
        string problemThrowerID,
        string problemID,
        ModProblem.ProblemType type,
        ModIssueTier tier = ModIssueTier.None)
    {
        public string problemThrowerID = problemThrowerID;
        public string problemID = problemID;

        public enum ProblemType
        {
            MissingBefore,
            MissingAfter,
            ConflictPresent,
            DuplicateMod,
            MissingRequired,
            ErrorLogIssue,
            PossibleIssue
        };

        public ProblemType issueType = type;

        /// <summary>
        /// Guaranteed severity used by the mod-list indicator. Potential-only warnings use
        /// <see cref="ModIssueTier.None"/> so they keep their tooltip without showing an icon.
        /// </summary>
        public ModIssueTier tier = tier;

        /// <summary>
        /// True when this problem means the mod must be left out of the active list when sorting.
        /// Mirrors <see cref="ModSortExclusion.Excludes"/> so callers do not duplicate the rule.
        /// </summary>
        public readonly bool IsSortExcluded => ModSortExclusion.Excludes(this);

        public override readonly string ToString() => issueType switch
        {
            ProblemType.MissingBefore => $"Mod '{problemThrowerID}' requires mod '{problemID}' to be loaded before it.",
            ProblemType.MissingAfter => $"Mod '{problemThrowerID}' requires mod '{problemID}' to be loaded after it.",
            ProblemType.ConflictPresent => $"Mod '{problemThrowerID}' is incompatible with mod '{problemID}'.",
            ProblemType.DuplicateMod => $"Duplicate mod folder found: '{problemThrowerID}' (Folders: {problemID}).\n\nPlease remove one to avoid issues.",
            ProblemType.MissingRequired => $"Mod '{problemThrowerID}' requires mod '{problemID}' to be enabled.",
            ProblemType.ErrorLogIssue => problemID,
            ProblemType.PossibleIssue => problemID,
            _ => "",
        };
    }

    public enum UserActionRequired
    {
        OpenWorldCreationScreen
    }

    public sealed class UserActionRequiredException(UserActionRequired actionRequired, string message) : Exception(message)
    {
        public UserActionRequired ActionRequired { get; } = actionRequired;
    }

    public partial class ModHearthManager
    {
        public event Action? RequestUIReload;
        public event Action<string, string>? RequestNotification;

        /// <summary>Aggregate progress for every workshop download path surfaced in the main window.</summary>
        public WorkshopDownloadProgressTracker WorkshopDownloads { get; } = new();

        public void TriggerUIReload() => RequestUIReload?.Invoke();

        public void ShowNotification(string message, string icon) => RequestNotification?.Invoke(message, icon);

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
                    infoVersion = infoVersion[..plusIndex];
                if (!string.IsNullOrWhiteSpace(infoVersion))
                    return infoVersion;
            }

            return "none";
        }

        // Maps strings to ModReferences. The keys match DFHMods.ToString() perfectly. Given a value V, V.ToDFHMod.ToString() returns it's key.
        private volatile Dictionary<string, ModReference> modrefMap = new(StringComparer.OrdinalIgnoreCase);

        public ModCapabilities GetModCapabilities(ModReference? modref) => modref != null ? GetRawDependencyInfo(modref)?.Capabilities ?? ModCapabilities.None : ModCapabilities.None;

        // Get all currently loaded ModReferences.

        public IReadOnlyCollection<ModReference> LoadedMods => modrefMap.Values;

        // Get a ModReference given a string key.
        public ModReference GetModRef(string key) => modrefMap[key];

        // Non-throwing lookup for callers that iterate a snapshot (modPool, enabledMods,
        // disabledMods) which a concurrent or later scan may no longer fully contain. A mod can
        // legitimately disappear from the scan (e.g. a cache-only mod DF no longer reports, or the
        // DFHack memory scan being replaced by a disk scan), while the modlist still references it.
        public ModReference? FindModRef(string key)
            => modrefMap.TryGetValue(key, out ModReference? modref) ? modref : null;

        // Get a ModReference given a DFHMod key.

        public ModReference GetRefFromDFHMod(DfhMod dfmod) => modrefMap[dfmod.ToString()];

        public ModReference? FindModRef(DfhMod dfmod) => FindModRef(dfmod.ToString());

        public IReadOnlyDictionary<string, List<ModReference>> GetDuplicateModRefs() => filteredDuplicateModRefs;

        // The sorted list of enabled DFHmods. This list is modified by the form, and when saved it overwrites the list of a ModPack.

        public volatile List<DfhMod> enabledMods = [];

        // The unsorted list of disabled DFHmods
        public volatile HashSet<DfhMod> disabledMods = [];

        // The unsorted list of all available DFHmods
        public volatile HashSet<DfhMod> modPool = [];

        // Get the currently selected modpack
        public DfhModpack SelectedModlist => modpacks[selectedModlistIndex];

        // List of all modpacks. After a modpack in this list is modified the list is saved to file.
        public volatile List<DfhModpack> modpacks = [];

        // The index of the currently selected modpack.
        public volatile int selectedModlistIndex;

        // Paths.

        private static readonly string baseDir = AppContext.BaseDirectory;
        private static readonly string modSortRulesPath = Path.Combine(baseDir, "modsort_rules.json");
        private static readonly string modRelationshipRulesPath = Path.Combine(baseDir, "modrules.json");
        private static readonly string localFallbackModpacksPath = Path.Combine(baseDir, "metadata", "modpacks.local.json");

        // Mod problem tracker.
        public volatile List<ModProblem> modproblems = [];
        private volatile Dictionary<string, List<ModReference>> duplicateModRefs = new(StringComparer.OrdinalIgnoreCase);
        // Duplicate mod reference dictionary filtered to exclude SteamLocal sources for UI duplicate deletion context menus.
        private volatile Dictionary<string, List<ModReference>> filteredDuplicateModRefs = new(StringComparer.OrdinalIgnoreCase);
        private volatile Dictionary<string, List<string>> duplicateWarningMap = new(StringComparer.OrdinalIgnoreCase);
        private volatile Dictionary<string, List<string>> cacheDuplicateMap = new(StringComparer.OrdinalIgnoreCase);
        private volatile List<HashSet<string>> duplicateWarningGroups = [];
        private volatile List<HashSet<string>> cacheDuplicateGroups = [];
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
        private List<ModSortRule> sortRules = [];
        private List<ModSortRule> communitySortRules = [];
        public string LastMissingModsMessage { get; private set; } = string.Empty;
        private DateTime? duplicateWarningLastWriteUtc;
        private DateTime? cacheDuplicateLastWriteUtc;
        private HashSet<string>? cacheDuplicateActiveModIds;
        private int relationshipRulesVersion;
        private Dictionary<string, ModRelationshipRule>? cachedClonedRelationshipRules;

        private string? lastLoggedErrorLogPath;
        private bool lastLoggedErrorLogExists;
        private readonly object modManagerReloadGate = new();
        private readonly object stateGate = new();
        private int reloadInProgress;
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
            AttemptLoadConfigAndDiscover();
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
                        _ = Directory.CreateDirectory(directory);

                    try
                    {
                        File.Move(oldPath, newPath);
                    }
                    catch (IOException)
                    {
                        File.Copy(oldPath, newPath, true);
                        File.Delete(oldPath);
                    }
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
                    _ = Task.Run(AuditWorkshopManifests, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Steam] Error starting background manifest audit: {ex.Message}");
                }

                FindAllModsDFHackLua();
                FindModpacks(preferredModlistName);

                Console.WriteLine($"Found {modrefMap.Count} mods and {modpacks.Count} modlists");
                Console.WriteLine();

                ModUpdateLogger.RecordChanges(modrefMap.Values, enabledMods, GetSteamWorkshopAcfPaths());

                // Preload the vanilla raw baseline once during initialization so
                // later scans can evaluate vanilla membership by set containment.
                _ = GetVanillaBaseline();

                return true;
            }
            finally
            {
                _ = Interlocked.Exchange(ref reloadInProgress, 0);
            }
        }

        public static string GetModRelationshipRulesPath() => modRelationshipRulesPath;

        public IReadOnlyDictionary<string, ModRelationshipRule> GetModRelationshipRules()
        {
            lock (stateGate)
            {
                if (cachedClonedRelationshipRules != null)
                    return cachedClonedRelationshipRules;
                cachedClonedRelationshipRules = ModRelationshipRule.CloneAll(relationshipRules);
                return cachedClonedRelationshipRules;
            }
        }

        public int GetRelationshipRulesVersion()
        {
            lock (stateGate)
            {
                return relationshipRulesVersion;
            }
        }

        public void SetModRelationshipRules(IDictionary<string, ModRelationshipRule>? rules)
        {
            lock (stateGate)
            {
                relationshipRules = ModRelationshipRule.NormalizeAll(rules);
                sortRules = RelationshipRulesToSortRules(relationshipRules);
                cachedClonedRelationshipRules = null;
                relationshipRulesVersion++;
            }
            SaveRelationshipRules();
        }

        private static List<ModSortRule> RelationshipRulesToSortRules(
            IDictionary<string, ModRelationshipRule> rules)
        {
            List<ModSortRule> converted = [];
            foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
            {
                string ownerId = kvp.Key.Trim();
                foreach (string targetId in kvp.Value.BeforeIds)
                    converted.Add(new ModSortRule { BeforeId = ownerId, AfterId = targetId });
                foreach (string targetId in kvp.Value.AfterIds)
                    converted.Add(new ModSortRule { BeforeId = targetId, AfterId = ownerId });
            }

            return NormalizeSortRules(converted);
        }

        private static List<ModSortRule> NormalizeSortRules(IEnumerable<ModSortRule>? rules)
        {
            List<ModSortRule> normalized = [];
            if (rules == null)
                return normalized;

            HashSet<string> seenEdges = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenRequires = new(StringComparer.OrdinalIgnoreCase);
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

            sortRules = [];
            if (!JsonFileStore.TryLoad(modSortRulesPath, "SortRules", out List<ModSortRule>? loadedRules))
                return;

            sortRules = NormalizeSortRules(loadedRules);
            relationshipRules = LegacySortRulesToRelationshipRules(sortRules);
        }

        private static Dictionary<string, ModRelationshipRule> LoadRelationshipRules()
        {
            _ = JsonFileStore.TryLoad(modRelationshipRulesPath, "Rules", out Dictionary<string, ModRelationshipRule>? loaded);
            return ModRelationshipRule.NormalizeAll(loaded);
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

            return ModRelationshipRule.NormalizeAll(converted);
        }

        private void SaveRelationshipRules()
            => _ = JsonFileStore.TrySave(modRelationshipRulesPath, relationshipRules, "Rules");

        public IReadOnlyList<ModSortRule> GetCommunitySortRules() => communitySortRules;

        public async Task<bool> FetchCommunitySortRulesAsync(string repositoryUrl, CancellationToken cancellationToken = default)
        {
            string? rawUrl = GitHubUrlParser.ToRawFileUrl(repositoryUrl);
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                communitySortRules = [];
                return false;
            }

            try
            {
                using HttpResponseMessage response = await GitHubFileClient.Instance.GetAsync(rawUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    communitySortRules = [];
                    return false;
                }

                string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                List<ModSortRule>? loadedRules = JsonSerializer.Deserialize<List<ModSortRule>>(jsonContent, JsonFileStore.CaseInsensitive);
                communitySortRules = NormalizeSortRules(loadedRules);
                return true;
            }
            catch (Exception ex)
            {
                AppLogging.LogError($"[CommunityRules] {ex.Message}");
                communitySortRules = [];
                return false;
            }
        }

        private void LoadCommunitySortRules()
        {
            string url = Config.CommunitySortRulesUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                communitySortRules = [];
                return;
            }

            try
            {
                _ = Task.Run(async () =>
                {
                    bool success = await FetchCommunitySortRulesAsync(url, deferredModManagerReloadCts?.Token ?? CancellationToken.None);
                    if (success)
                    {
                        TriggerUIReload();
                    }
                }, deferredModManagerReloadCts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLogging.LogError($"[CommunityRules] {ex.Message}");
                communitySortRules = [];
            }
        }

        public string GetActiveModpackPath() => activeModpackPath;

        public static bool HasDfhackExecutable()
        {
            string dfhackRunPath = GetDfhackRunPath();
            return !string.IsNullOrWhiteSpace(dfhackRunPath) && File.Exists(dfhackRunPath);
        }

        public static bool IsDfhackRpcRunning() => DFHackRpcClient.IsDFHackRunning(Config?.DFFolderPath);

        public static bool IsDFHackInstalled() => !string.IsNullOrWhiteSpace(Config?.DFHackFolderPath) && Directory.Exists(Config.DFHackFolderPath);

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

        private static bool TryReadModpackFile(string path, out List<DfhModpack> loadedModpacks, out string error)
        {
            loadedModpacks = [];
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = "Modpack file missing.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                List<DfhModpack>? parsed = JsonSerializer.Deserialize<List<DfhModpack>>(json, JsonFileStore.CaseInsensitive);
                if (parsed == null)
                {
                    error = "Modpack file could not be parsed.";
                    return false;
                }

                loadedModpacks = [.. parsed.Where(modpack => modpack != null)];
                foreach (DfhModpack modpack in loadedModpacks)
                {
                    modpack.Name ??= "Unnamed";
                    modpack.Modlist ??= [];
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                loadedModpacks = [];
                return false;
            }
        }

        private List<DfhModpack> CreateDefaultModpacks()
        {
            DfhModpack newPack = new(true, GenerateVanillaModlist(), "Default");
            return [newPack];
        }

        public static bool CanDeleteModFromModsFolder(ModReference modref)
        {
            if (modref == null || string.IsNullOrWhiteSpace(modref.Path) || Config == null)
                return false;
            string modsPath = GetModsPath();
            if (string.IsNullOrWhiteSpace(modsPath))
                return false;
            string modPath = ResolveCanonicalPath(modref.Path);
            modsPath = ResolveCanonicalPath(modsPath);
            if (string.IsNullOrWhiteSpace(modPath) || string.IsNullOrWhiteSpace(modsPath))
                return false;
            if (!PathHelper.IsUnderRoot(modPath, modsPath))
                return false;
            return Directory.Exists(modPath);
        }

        /// <summary>
        /// True when <paramref name="modref"/> points at a real folder inside Dwarf Fortress's
        /// installed_mods cache. The cache is disposable, so these entries can be deleted even
        /// though they are never actionable as a normal local mod.
        /// </summary>
        public static bool CanDeleteCacheOnlyMod(ModReference modref)
        {
            if (modref == null || string.IsNullOrWhiteSpace(modref.Path) || Config == null)
                return false;
            if (!ModSourceClassifier.IsCacheOnly(modref))
                return false;

            string installedModsPath = GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath))
                return false;

            string modPath = ResolveCanonicalPath(modref.Path);
            installedModsPath = ResolveCanonicalPath(installedModsPath);
            if (string.IsNullOrWhiteSpace(modPath) || string.IsNullOrWhiteSpace(installedModsPath))
                return false;
            if (!PathHelper.IsUnderRoot(modPath, installedModsPath))
                return false;
            return Directory.Exists(modPath);
        }

        public ModDeletionBatchScope BeginDeletionBatch() => new(ShowNotification);

        public bool DeleteModFromModsFolder(ModReference modref, out string message, bool requestUIReload = true)
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

            return TryDeleteModFolder(modref, requestUIReload, out message);
        }

        /// <summary>
        /// Deletes a cache-only mod's copy from Dwarf Fortress's disposable installed_mods folder.
        /// The real source (if any) is untouched; Dwarf Fortress recreates the cache entry the next
        /// time the mod is enabled.
        /// </summary>
        public bool DeleteCacheOnlyMod(ModReference modref, out string message, bool requestUIReload = true)
        {
            if (modref == null)
            {
                message = "No mod selected.";
                return false;
            }

            if (!CanDeleteCacheOnlyMod(modref))
            {
                message = "Mod is not a deletable installed_mods cache entry or was already removed.";
                return false;
            }

            return TryDeleteModFolder(modref, requestUIReload, out message);
        }

        /// <summary>
        /// Deletes the mod's folder, then drops the reference from the catalog and reports the change.
        /// Callers are responsible for validating that the path is deletable.
        /// </summary>
        private bool TryDeleteModFolder(ModReference modref, bool requestUIReload, out string message)
        {
            string modPath = NormalizeFileSystemPath(modref.Path);
            if (string.IsNullOrWhiteSpace(modPath))
            {
                message = "Mod path is invalid.";
                return false;
            }

            try
            {
                Directory.Delete(modPath, true);
                string sourceFolder = Path.GetDirectoryName(modPath) ?? string.Empty;
                ModDeletionNotifier.NotifyDeleted(ShowNotification, modPath, sourceFolder);
            }
            catch (Exception ex)
            {
                message = $"Failed to delete mod folder: {ex.Message}";
                return false;
            }

            ForgetDeletedMod(modref, requestUIReload);
            message = $"Deleted {modPath}";
            return true;
        }

        /// <summary>
        /// Removes a deleted mod from the in-memory catalog after its folder is gone, so the UI stops
        /// showing it without waiting for a full disk rescan.
        /// </summary>
        private void ForgetDeletedMod(ModReference modref, bool requestUIReload)
        {
            DfhMod dfm = modref.ToDFHMod();
            string modrefKey = modref.DFHackCompatibleString();

            HashSet<DfhMod> newModPool = [.. modPool];
            _ = newModPool.Remove(dfm);
            List<DfhMod> newEnabledMods = [.. enabledMods.Where(m => m != dfm)];
            HashSet<DfhMod> newDisabledMods = [.. disabledMods];
            _ = newDisabledMods.Remove(dfm);
            Dictionary<string, ModReference> newModrefMap = new(modrefMap, StringComparer.OrdinalIgnoreCase);
            _ = newModrefMap.Remove(modrefKey);

            lock (stateGate)
            {
                modPool = newModPool;
                enabledMods = newEnabledMods;
                disabledMods = newDisabledMods;
                modrefMap = newModrefMap;
            }

            RefreshInstalledCacheModIds();
            FindModlistProblems();
            _ = TryRequestModManagerReload(out _, out _);
            if (requestUIReload)
            {
                TriggerUIReload();
            }
        }

        public bool ClearInstalledModsFolder(out string message)
        {
            string installedModsPath = GetInstalledModsPath();
            if (string.IsNullOrWhiteSpace(installedModsPath))
            {
                message = "installed_mods path is not set.";
                return false;
            }

            if (!Directory.Exists(installedModsPath))
            {
                message = $"installed_mods folder not found:\n{installedModsPath}";
                return false;
            }

            int deleted = 0;
            List<string> failures = [];
            using (BeginDeletionBatch())
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(installedModsPath))
                {
                    try
                    {
                        if (Directory.Exists(entry))
                        {
                            Directory.Delete(entry, true);
                            ModDeletionNotifier.NotifyDeleted(ShowNotification, entry, installedModsPath);
                        }
                        else if (File.Exists(entry))
                        {
                            File.Delete(entry);
                        }

                        deleted++;
                    }
                    catch
                    {
                        failures.Add(Path.GetFileName(entry));
                    }
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

        public bool ClearErrorLog(out string message)
        {
            string errorLogPath = GetErrorLogPath();
            if (string.IsNullOrWhiteSpace(errorLogPath))
            {
                message = "Error log path is not set.";
                return false;
            }

            if (!File.Exists(errorLogPath))
            {
                message = $"Error log file not found:\n{errorLogPath}";
                return false;
            }

            try
            {
                File.Delete(errorLogPath);
                ClearErrorLogProblemCache();
                message = $"Deleted {errorLogPath}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to delete error log:\n{ex.Message}";
                return false;
            }
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
            List<string> roots = [];

            string installedModsPath = GetInstalledModsPath();
            if (!string.IsNullOrWhiteSpace(installedModsPath))
                roots.Add(ResolveExistingDirectoryPath(installedModsPath) ?? installedModsPath);

            if (!string.IsNullOrWhiteSpace(Config?.DFFolderPath))
            {
                string? vanillaPath = GetVanillaModsPath();
                if (!string.IsNullOrWhiteSpace(vanillaPath))
                    roots.Add(ResolveExistingDirectoryPath(vanillaPath) ?? vanillaPath);
            }

            List<string> candidateDirs = [];
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                candidateDirs.AddRange(EnumerateModDirectoriesWithInfo(root));
            }

            // A plain membership set has no "which duplicate wins" concern (unlike BuildModPathIndex's
            // dir-per-id map), so a ConcurrentDictionary-backed set is enough
            ConcurrentDictionary<string, byte> ids = new(StringComparer.OrdinalIgnoreCase);
            _ = Parallel.ForEach(candidateDirs, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, dir =>
            {
                string? id = TryReadModIdFromInfo(ResolveInfoFilePath(dir));
                if (id != null)
                    _ = ids.TryAdd(id, 0);
            });

            return new HashSet<string>(ids.Keys, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads the trimmed "[ID:...]" value from a mod's info.txt. Returns null when the file is
        /// missing, unreadable, has no id tag, or the tag is blank.
        /// </summary>
        private static string? TryReadModIdFromInfo(string? infoPath)
        {
            if (string.IsNullOrWhiteSpace(infoPath))
                return null;

            try
            {
                Match idMatch = InfoIdRegex().Match(File.ReadAllText(infoPath));
                if (!idMatch.Success)
                    return null;

                string id = idMatch.Groups[1].Value.Trim();
                return string.IsNullOrEmpty(id) ? null : id;
            }
            catch
            {
                // Ignore unreadable info files.
                return null;
            }
        }

        private static readonly string scrDir = "src_dir";

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
            ModPathIndex pathIndex = BuildModPathIndex();

            // Enumerate. Pin down an explicit order, so the parallel compute and sequential merge phases below are deterministic
            List<Dictionary<string, string>> modDataList = [.. modData];

            // Resolve the configured mod roots once; the parallel body used to re-query (and could
            // re-save) them per mod from multiple threads.
            ModSourceContext sourceContext = ModSourceContext.FromConfig();

            // Parallel compute. Path resolution, ModReference construction and LastModifiedTime stamp are all independent per entry work
            ModReference?[] results = new ModReference?[modDataList.Count];
            _ = Parallel.For(0, modDataList.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
            {
                Dictionary<string, string> modDataEntry = modDataList[i];
                modDataEntry[scrDir] = ResolveModPath(modDataEntry, pathIndex);

                // Mod setup and registry.
                ModReference modRef = new(modDataEntry)
                {
                    LastModifiedTime = GetLatestModifiedTimestampCached(modDataEntry["src_dir"])
                };

                // Classify (which flags installed_mods cache paths) but keep the ref: a mod that only
                // exists in DF's cache must still be visible in the UI. Cache refs are merged in a
                // second, low-priority pass below.
                _ = ModSourceClassifier.Classify(modRef, sourceContext);
                results[i] = modRef;
            });

            // Sequential merge. Preserves "first occurrence wins" for duplicates, same as before.
            ModCatalogBuilder catalog = new();

            // Real source folders first, so a cache copy can never shadow them or be reported as a
            // duplicate; then cache-only mods as a low-priority fallback.
            foreach (ModReference? modRef in results)
            {
                if (modRef is { IsIgnored: false })
                    _ = catalog.TryAddScanned(modRef);
            }

            foreach (ModReference? modRef in results)
            {
                if (modRef is { IsIgnored: true })
                    _ = catalog.TryAddCache(modRef);
            }

            catalog.Publish(this, sourceContext);
        }

        /// <summary>
        /// Usually the main method for finding mods
        /// </summary>
        public void FindAllModsFromDisk()
        {
            Console.WriteLine("Finding all mods (filesystem)...");

            List<(string Root, string Dir)> candidates = [];
            foreach (string root in GetEffectiveModRoots())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                foreach (string dir in EnumerateModDirectoriesWithInfo(root))
                    candidates.Add((root, dir));
            }

            // Resolve the configured mod roots once; re-querying them inside the parallel body read
            // (and could write) shared config from multiple threads.
            ModSourceContext sourceContext = ModSourceContext.FromConfig();

            ModReference?[] results = new ModReference?[candidates.Count];
            _ = Parallel.For(0, candidates.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
            {
                results[i] = TryBuildModReferenceFromDirectory(candidates[i].Dir, sourceContext);
            });

            ModCatalogBuilder catalog = new();

            string? currentRoot = null;
            int candidateCount = 0;
            int addedCount = 0;
            int duplicateCount = 0;
            int cacheCount = 0;
            List<ModReference> cacheOnlyRefs = [];

            void FlushRootDiagnostics()
            {
                if (currentRoot != null)
                    Loggers.Diag.Log($"Disk mod scan root: '{NormalizeFileSystemPath(currentRoot)}' candidates: {candidateCount}, added: {addedCount}, duplicates: {duplicateCount}, cache-only: {cacheCount}, total registered: {catalog.ModrefMap.Count}.");
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
                    cacheCount = 0;
                }

                candidateCount++;
                ModReference? modRef = results[i];
                if (modRef == null)
                    continue;

                // Defer installed_mods cache refs: they are a low-priority fallback and must be
                // merged only after every canonical root has had a chance to register.
                if (modRef.IsIgnored)
                {
                    cacheOnlyRefs.Add(modRef);
                    cacheCount++;
                    continue;
                }

                if (catalog.TryAddScanned(modRef))
                    addedCount++;
                else
                    duplicateCount++;
            }

            FlushRootDiagnostics();

            int registeredFromCache = 0;
            foreach (ModReference cacheRef in cacheOnlyRefs)
            {
                if (catalog.TryAddCache(cacheRef))
                    registeredFromCache++;
            }

            if (registeredFromCache > 0)
                Loggers.Diag.Log($"Registered {registeredFromCache} cache-only mod(s) from the installed_mods fallback.");

            catalog.Publish(this, sourceContext);

            Loggers.Diag.Log($"Disk mod scan completed. Total registered mods: {catalog.ModrefMap.Count}.");
        }

        // Parallel safe
        private ModReference? TryBuildModReferenceFromDirectory(string dir, ModSourceContext sourceContext)
        {
            string? infoPath = ResolveInfoFilePath(dir);
            if (string.IsNullOrWhiteSpace(infoPath))
                return null;

            Dictionary<string, string> modData = BuildModMemoryDataFromInfo(infoPath, dir, out bool missingVersion, out string info);
            if (!modData.TryGetValue("id", out string? id) || string.IsNullOrWhiteSpace(id))
                return null;

            ModReference modRef = new(modData, info)
            {
                MissingVersion = missingVersion,
                LastModifiedTime = GetLatestModifiedTimestampCached(dir)
            };

            // Keep installed_mods refs; the merge phase registers them as a low-priority fallback.
            _ = ModSourceClassifier.Classify(modRef, sourceContext);
            return modRef;
        }

        /// <summary>
        /// Accumulates one scan's catalog collections and applies the shared merge rules (canonical
        /// sources first, then installed_mods cache fallbacks) before publishing the snapshot.
        /// </summary>
        private sealed class ModCatalogBuilder
        {
            public Dictionary<string, ModReference> ModrefMap { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<ModReference>> DuplicateModRefs { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<DfhMod> ModPool { get; } = [];

            public bool TryAddScanned(ModReference modRef)
                => TryAddScannedModReference(modRef, ModrefMap, DuplicateModRefs, ModPool);

            public bool TryAddCache(ModReference modRef)
                => TryAddCacheModReference(modRef, ModrefMap, ModPool);

            public void Publish(ModHearthManager owner, ModSourceContext sourceContext)
                => owner.PublishModCatalog(ModrefMap, ModPool, DuplicateModRefs, sourceContext);
        }

        /// <summary>
        /// Adds a scanned mod to the catalog, preserving "first occurrence wins" for duplicates.
        /// Returns false when the mod was a duplicate of an already-registered one.
        /// </summary>
        private static bool TryAddScannedModReference(
            ModReference modRef,
            Dictionary<string, ModReference> newModrefMap,
            Dictionary<string, List<ModReference>> newDuplicateModRefs,
            HashSet<DfhMod> newModPool)
        {
            string key = modRef.DFHackCompatibleString();
            if (newModrefMap.TryGetValue(key, out ModReference? value))
            {
                // Two DFHack rows can resolve to the same physical folder (e.g. a cache row remapped
                // onto its real source). That is the same mod folder, not a duplicate installation.
                if (PathHelper.PathsEqual(value.Path, modRef.Path))
                    return false;

                if (!newDuplicateModRefs.TryGetValue(key, out List<ModReference>? list))
                {
                    list = [value];
                    newDuplicateModRefs[key] = list;
                }
                list.Add(modRef);
                return false;
            }

            Console.WriteLine($"   Mod found + registered: {modRef.Name}.");
            newModrefMap.Add(key, modRef);
            _ = newModPool.Add(modRef.ToDFHMod());
            return true;
        }

        /// <summary>
        /// Registers a mod that only exists in DF's installed_mods cache. Never records a duplicate:
        /// the cache is a disposable copy that may coexist with a real source folder, and a cached
        /// copy must never shadow (or be reported as a conflict with) the real one.
        /// Returns false when the id/version is already registered from a higher-priority source.
        /// </summary>
        internal static bool TryAddCacheModReference(
            ModReference modRef,
            Dictionary<string, ModReference> newModrefMap,
            HashSet<DfhMod> newModPool)
        {
            string key = modRef.DFHackCompatibleString();
            if (newModrefMap.ContainsKey(key))
                return false;

            Console.WriteLine($"   Mod found (cache-only) + registered: {modRef.Name}.");
            newModrefMap.Add(key, modRef);
            _ = newModPool.Add(modRef.ToDFHMod());
            return true;
        }

        // A reader that checks modPool for a key must be able to find it in modrefMap too.
        // Grouping the reassignment under one lock stops two writers (e.g. a manual reload racing the auto-reload timer) from interleaving; readers stay lock-free by design.
        private void PublishModCatalog(
            Dictionary<string, ModReference> newModrefMap,
            HashSet<DfhMod> newModPool,
            Dictionary<string, List<ModReference>> newDuplicateModRefs,
            ModSourceContext sourceContext)
        {
            Dictionary<string, List<ModReference>> newFilteredDuplicateModRefs = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<ModReference>> kvp in newDuplicateModRefs)
            {
                List<ModReference> nonSteamLocalList = [.. kvp.Value.Where(m =>
                {
                    // installed_mods is DF's disposable cache; a cached copy is never a real second
                    // installation, so it must not surface as a duplicate-mod warning.
                    if (m.IsIgnored || IsInstalledModsPath(m.Path))
                        return false;

                    (_, _, _, bool isSteamLocal, _) = ModSourceClassifier.Classify(m, sourceContext);
                    return !isSteamLocal;
                })];

                if (nonSteamLocalList.Count > 1)
                {
                    newFilteredDuplicateModRefs[kvp.Key] = nonSteamLocalList;
                }
            }

            lock (stateGate)
            {
                modrefMap = newModrefMap;
                modPool = newModPool;
                duplicateModRefs = newDuplicateModRefs;
                filteredDuplicateModRefs = newFilteredDuplicateModRefs;
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
            string canonicalPath = ResolveCanonicalPath(directoryPath ?? string.Empty);
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

            StringComparer comparer = PathHelper.Comparer;
            HashSet<string> seen = new(comparer);

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

        private static Dictionary<string, string> BuildModMemoryDataFromInfo(string infoPath, string modPath, out bool missingVersion, out string info)
        {
            missingVersion = false;
            info = File.ReadAllText(infoPath);
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
                        if (c is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
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
                        StringBuilder valueBuilder = new();

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
                                _ = valueBuilder.Append(c);
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
                                _ = valueBuilder.Append(c);
                                j++;
                            }
                            else
                            {
                                _ = valueBuilder.Append(c);
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
                if (c is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
                    return false;
            }
            return true;
        }

        private static string? GetInfoTag(Dictionary<string, string> tags, string key) => tags.TryGetValue(key, out string? value) ? value : null;

        private static string? ExtractNumericVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            Match match = NumericVersionRegex().Match(value);
            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// Index of the canonical (non-cache) mod folders discovered on disk. <see cref="ById"/>
        /// preserves the historical "first occurrence across roots wins" id lookup.
        /// <see cref="ByReferenceKey"/> is keyed exactly like <see cref="DfhMod.ToString"/>
        /// (id|version) so a disposable installed_mods folder can be mapped back to the real source.
        /// </summary>
        private sealed record ModPathIndex(
            Dictionary<string, string> ById,
            Dictionary<string, string> ByReferenceKey);

        private sealed record ModIdentity(string Id, string ReferenceKey);

        private static ModPathIndex BuildModPathIndex()
        {
            // Enumerate phase: preserve root/dir order so the merge below reproduces the original "first occurrence wins" semantics.
            List<string> candidateDirs = [];
            foreach (string root in GetEffectiveModRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                if (IsInstalledModsPath(root))
                    continue;

                foreach (string dir in EnumerateModDirectoriesWithInfo(root))
                    candidateDirs.Add(dir);
            }

            // Parallel-compute phase: reading + regex-matching each info.txt is independent per directory.
            ModIdentity?[] results = new ModIdentity?[candidateDirs.Count];
            _ = Parallel.For(0, candidateDirs.Count, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, i =>
            {
                results[i] = TryReadModIdentityFromInfo(ResolveInfoFilePath(candidateDirs[i]));
            });

            // Sequential-merge phase: same "first occurrence across roots/dirs wins" as the original loop.
            Dictionary<string, string> byId = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> byReferenceKey = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < results.Length; i++)
            {
                ModIdentity? identity = results[i];
                if (identity == null)
                    continue;

                _ = byId.TryAdd(identity.Id, candidateDirs[i]);
                _ = byReferenceKey.TryAdd(identity.ReferenceKey, candidateDirs[i]);
            }

            Loggers.Diag.Log($"Mod ID path map entries: {byId.Count}. Mod reference path map entries: {byReferenceKey.Count}.");
            return new ModPathIndex(byId, byReferenceKey);
        }

        /// <summary>
        /// Reads the [ID] and numeric version tags needed to build a mod reference key, mirroring
        /// <see cref="BuildModMemoryDataFromInfo"/>'s version fallbacks. Null when unreadable or id-less.
        /// </summary>
        private static ModIdentity? TryReadModIdentityFromInfo(string? infoPath)
        {
            if (string.IsNullOrWhiteSpace(infoPath))
                return null;

            try
            {
                Dictionary<string, string> tags = ParseInfoTags(File.ReadAllText(infoPath));
                string id = GetInfoTag(tags, "ID")?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(id))
                    return null;

                string numericVersion = GetInfoTag(tags, "NUMERIC_VERSION")
                    ?? ExtractNumericVersion(GetInfoTag(tags, "DISPLAYED_VERSION") ?? GetInfoTag(tags, "VERSION") ?? string.Empty)
                    ?? "1";

                string? referenceKey = TryBuildModReferenceKey(id, numericVersion);
                return referenceKey == null ? null : new ModIdentity(id, referenceKey);
            }
            catch (Exception ex)
            {
                Loggers.Diag.Dev($"Failed to read mod identity from '{infoPath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds the same "id|version" key DFHack's mod manager uses (see <see cref="DfhMod.ToString"/>).
        /// Null when the version is not numeric, matching what DfhMod's constructor would reject.
        /// </summary>
        internal static string? TryBuildModReferenceKey(string? id, string? numericVersion)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(numericVersion))
                return null;

            string compactVersion = numericVersion.Replace(".", string.Empty).Trim();
            return int.TryParse(compactVersion, out int version) ? id.Trim() + "|" + version : null;
        }

        private static string ResolveModPath(Dictionary<string, string> modDataEntry, ModPathIndex pathIndex)
            => TryRemapInstalledModsPath(
                ResolveModPathCore(modDataEntry, pathIndex.ById),
                modDataEntry,
                pathIndex.ByReferenceKey);

        /// <summary>
        /// Dwarf Fortress reports src_dir as a folder inside its installed_mods cache for enabled
        /// mods. When a real source folder with the same id/version is known, prefer it so the
        /// ModReference points at the folder the user actually installs, updates or deletes.
        /// </summary>
        internal static string TryRemapInstalledModsPath(
            string resolvedPath,
            Dictionary<string, string> modDataEntry,
            Dictionary<string, string> canonicalByReferenceKey)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath) || !IsInstalledModsPath(resolvedPath))
                return resolvedPath;

            string? referenceKey = TryBuildModReferenceKey(
                modDataEntry.GetValueOrDefault("id"),
                modDataEntry.GetValueOrDefault("numeric_version"));

            if (referenceKey == null ||
                !canonicalByReferenceKey.TryGetValue(referenceKey, out string? canonicalPath) ||
                string.IsNullOrWhiteSpace(canonicalPath))
            {
                return resolvedPath;
            }

            return ResolveExistingDirectoryPath(canonicalPath) ?? canonicalPath;
        }

        private static string ResolveModPathCore(Dictionary<string, string> modDataEntry, Dictionary<string, string> modIdPathMap)
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
                foreach (string root in GetEffectiveModRoots())
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

            Loggers.Diag.Dev($"ResolveModPath fallback unresolved. id: '{modDataEntry.GetValueOrDefault("id")}', raw_src: '{rawSrcDir}', resolved: '{NormalizeFileSystemPath(fullPath)}'.");

            return NormalizeFileSystemPath(fullPath);
        }

        // Output a dictionary, that given a modID gets the true version.
        // Because this relies on the DFHack codebase there is a non 0 chance of becoming incompatible with future versions of DFHack.
        private static HashSet<Dictionary<string, string>> GetModMemoryData()
        {
            HashSet<Dictionary<string, string>> modData = [];

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
                _ = modData.Add(headers);
                // Mod found/registered logging happens after registration.

                // To see which headers there are to choose from.
                //foreach (string k in headers.Keys)
                //Console.WriteLine($"header found. k: {k}, v: {headers[k]}");

            }

            return modData;
        }

        // Use dfhack-run and lua to get raw mod data.
        private static string LoadModMemoryData()
        {
            // Get path to lua script.
            string luaPath = Path.Combine(AppContext.BaseDirectory, "lua", "GetModMemoryData.lua");
            if (!File.Exists(luaPath))
                throw new FileNotFoundException("GetModMemoryData.lua not found.", luaPath);

            // Try direct RPC first (faster, avoids spawning processes)
            string? rpcOutput = DFHackRpcClient.ExecuteDFHackCommandViaRpc("lua", ["-f", luaPath], Config?.DFFolderPath, out string rpcError);
            if (rpcOutput != null)
            {
                return rpcOutput;
            }

            if (rpcError == "Dwarf Fortress is not running")
            {
                throw new InvalidOperationException("Dwarf Fortress is not running.");
            }

            Console.WriteLine($"DFHack RPC failed ({rpcError}). Falling back to dfhack-run process.");

            string dfhackRunPath = GetDfhackRunPath();
            if (string.IsNullOrWhiteSpace(dfhackRunPath) || !File.Exists(dfhackRunPath))
                throw new FileNotFoundException("dfhack-run executable not found.", dfhackRunPath);

            string output = RunDfhackRunLua(dfhackRunPath, luaPath, out string error);
            if (!string.IsNullOrWhiteSpace(error))
                Console.WriteLine(error.TrimEnd());

            return output;
        }

        private static bool isDfSessionActive;
        private static bool hasDfBeenSeenActive;
        private static DateTime lastDfLaunchTime = DateTime.MinValue;

        // Check if DF is running
        public static bool DwarfFortressRunning()
        {
            if (DFMonitor.Shared.IsProcessRunning())
            {
                isDfSessionActive = true;
                hasDfBeenSeenActive = true;
                return true;
            }

            if (IsDfhackRpcRunning())
            {
                isDfSessionActive = true;
                hasDfBeenSeenActive = true;
                return true;
            }

            // Account for initialization slowness if DF was recently launched and has not been seen active yet
            if (!hasDfBeenSeenActive && (DFMonitor.Shared.IsBooting() || (isDfSessionActive && (DateTime.UtcNow - lastDfLaunchTime).TotalSeconds < 60)))
            {
                return true;
            }

            // Both process monitor and RPC checks failed, and process is not booting (or was already seen active). DF is not running.
            isDfSessionActive = false;
            hasDfBeenSeenActive = false;
            return false;
        }

        // Run Dwarf Fortress executable or trigger Steam launch.
        public static async Task<(bool Success, bool IsSteam, string Message)> RunDwarfFortressAsync()
        {
            if (DwarfFortressRunning())
                return (false, false, "Dwarf Fortress is already running.");

            if (Config == null || string.IsNullOrWhiteSpace(Config.DFFolderPath))
                return (false, false, "Dwarf Fortress folder path is not configured.");

            string dfFolderPath = Config.DFFolderPath;
            if (!Directory.Exists(dfFolderPath))
                return (false, false, $"Dwarf Fortress folder not found: {dfFolderPath}");

            if (!DwarfFortressExecutableLocator.TryResolvePath(dfFolderPath, out string executablePath))
                return (false, false, "Dwarf Fortress executable not found in the configured folder.");

            try
            {
                // Check if Dwarf Fortress is a Steam installation by comparing paths. No Steam copy
                // (null) is the normal case for non-Steam installs, not an error.
                string? steamFolder = TryFindSteamDwarfFortressFolder();
                bool isSteamInstallation =
                    !string.IsNullOrWhiteSpace(steamFolder) &&
                    string.Equals(
                        ResolveCanonicalPath(steamFolder),
                        ResolveCanonicalPath(dfFolderPath),
                        PathHelper.Comparison);

                if (isSteamInstallation)
                {
                    Loggers.DfRunner.Log("Launching Dwarf Fortress through Steam (App ID: 975370)");

                    bool launched = false;
                    try
                    {
                        ProcessStartInfo startInfo = new()

                        {
                            FileName = "steam://run/975370",
                            UseShellExecute = true
                        };

                        // Trigger Steam protocol handler
                        using Process? launcherProcess = Process.Start(startInfo);
                        if (launcherProcess != null)
                        {
                            Loggers.DfRunner.Log($"Steam protocol handler invoked (PID = {launcherProcess.Id})");
                            launched = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Loggers.DfRunner.Log($"Failed to launch via steam:// protocol handler: {ex.Message}");
                    }

                    if (!launched && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
                    {
                        // Fallback on Linux/macOS to direct steam command line binaries ("steam", "steam-runtime")
                        foreach (string steamExecutable in new[] { "steam", "steam-runtime" })
                        {
                            try
                            {
                                Loggers.DfRunner.Log($"Trying fallback Steam CLI launch ({steamExecutable} -applaunch 975370)...");
                                ProcessStartInfo fallbackStartInfo = new()

                                {
                                    FileName = steamExecutable,
                                    Arguments = "-applaunch 975370",
                                    UseShellExecute = true
                                };
                                using Process? fallbackProcess = Process.Start(fallbackStartInfo);
                                if (fallbackProcess != null)
                                {
                                    Loggers.DfRunner.Log($"Steam CLI invoked via {steamExecutable} (PID = {fallbackProcess.Id})");
                                    launched = true;
                                    break;
                                }
                            }
                            catch (Exception ex2)
                            {
                                Loggers.DfRunner.Log($"Fallback Steam CLI launch ({steamExecutable}) failed: {ex2.Message}");
                            }
                        }
                    }

                    if (!launched)
                    {
                        return (false, false, "Failed to trigger Steam launcher or protocol handler.");
                    }

                    isDfSessionActive = true;
                    hasDfBeenSeenActive = false;
                    lastDfLaunchTime = DateTime.UtcNow;

                    if (OperatingSystem.IsLinux())
                    {
                        Loggers.DfRunner.Log("Steam launch triggered successfully on Linux. Skipping process polling.");
                        return (true, true, "Dwarf Fortress launched successfully through Steam.");
                    }

                    // Poll for the actual Dwarf Fortress process to appear in the OS process list
                    const int pollIntervalMs = 500;
                    const int maxWaitTimeoutMs = 60000;
                    int totalElapsedMs = 0;

                    while (totalElapsedMs < maxWaitTimeoutMs)
                    {
                        await Task.Delay(pollIntervalMs);
                        totalElapsedMs += pollIntervalMs;

                        if (DwarfFortressRunning())
                        {
                            Loggers.DfRunner.Log($"Dwarf Fortress process detected active after {totalElapsedMs}ms.");
                            return (true, true, "Dwarf Fortress launched successfully through Steam.");
                        }
                    }

                    return (false, false, $"Steam launch was triggered, but Dwarf Fortress failed to launch within {maxWaitTimeoutMs / 1000} seconds.");
                }
                else
                {
                    Loggers.DfRunner.Log($"Launching Dwarf Fortress executable directly: {executablePath}");

                    ProcessStartInfo startInfo = new()

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
                            Loggers.DfRunner.Log($"LD_LIBRARY_PATH set to: {startInfo.EnvironmentVariables["LD_LIBRARY_PATH"]}");
                        }
                    }

                    Process? started = Process.Start(startInfo);
                    if (started == null)
                        return (false, false, "Failed to launch Dwarf Fortress directly: process could not be created.");

                    DFMonitor.Shared.RegisterLaunchedPid(started.Id);
                    isDfSessionActive = true;
                    hasDfBeenSeenActive = false;
                    lastDfLaunchTime = DateTime.UtcNow;
                    Loggers.DfRunner.Log($"Started direct process PID = {started.Id}");

                    // Verify the executable didn't crash on startup (e.g., missing dynamic libraries/DLLs)
                    await Task.Delay(2000);

                    if (started.HasExited)
                    {
                        int exitCode = started.ExitCode;
                        started.Dispose();
                        return (false, false, $"Dwarf Fortress exited immediately (Exit code {exitCode}). Check terminal output or error logs for missing dynamic libraries.");
                    }

                    started.Dispose();
                    return (true, false, "Dwarf Fortress launched successfully.");
                }
            }
            catch (Exception ex)
            {
                return (false, false, $"Failed to launch Dwarf Fortress: {ex.Message}");
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
            SelectedModlist.Modlist = [.. enabledMods];

            return SaveAllModpacks();
        }

        // Save the DFHModpack list to the active backend file.
        public ModpackSaveResult SaveAllModpacks() => SaveAllModpacks(requestLiveReload: true);

        private ModpackSaveResult SaveAllModpacks(bool requestLiveReload, bool backUpExistingFile = false)
        {
            Console.WriteLine("Modlists saved.");

            ResolveActiveModpackStorage();
            ModpackStorageBackend backend = activeModpackBackend;
            string path = activeModpackPath;

            if (backUpExistingFile)
                TryBackUpModpackFile(path);

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

        private void WriteModpackFile(string path, List<DfhModpack> modpacksToWrite)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Modpack path is not set.");

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                _ = Directory.CreateDirectory(directory);

            string modlistJson = JsonFileStore.Serialize(modpacksToWrite);

            lock (stateGate)
                savingModpacksCooldownUntilUtc = DateTime.UtcNow.AddSeconds(2);

            File.WriteAllText(path, modlistJson);
        }

        /// <summary>
        /// Copies the current modpack file to "&lt;path&gt;.bak" before a load-time repair overwrites it.
        /// A load-time repair can be caused by a parse/compatibility problem rather than by the user,
        /// so the previous contents must stay recoverable. Never clobbers an existing backup.
        /// </summary>
        private static void TryBackUpModpackFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            string backupPath = path + ".bak";
            if (File.Exists(backupPath))
                return;

            try
            {
                File.Copy(path, backupPath);
                Loggers.Diag.Log($"Backed up modpack file '{path}' to '{backupPath}' before rewriting it.");
            }
            catch (Exception ex)
            {
                AppLogging.LogException($"Failed to back up modpack file '{path}'", ex);
            }
        }

        ///<summary> Only used for local and live modpack saving, not ui or mods</summary>
        private bool TryRequestModManagerReload(out bool deferred, out string message)
        {
            deferred = false;

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
                ShowNotification("DFHack screen reload applied", "dfHack.png");
                CancelDeferredModManagerReload();
                message = "Modpack saved and applied to the DFHack mod manager.";
                return true;
            }

            ScheduleDeferredModManagerReload(initialChecks);
            deferred = true;
            message = "Modpack saved. In-game apply is waiting for the mod manager screen.";
            return false;
        }

        private static bool IsModManagerReloadOutputSuccessful(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return false;

            try
            {
                // Try parsing JSON response from ReloadModManager.lua first
                using JsonDocument doc = JsonDocument.Parse(output.Trim());
                if (doc.RootElement.TryGetProperty("success", out JsonElement successElem) && successElem.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore JSON parse errors for fallback string checking
            }

            return output.Contains("[ReloadManager] Reloading mod manager screen.", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("[ReloadManager] Applying default modlist to Mods screen.", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("[ModHearth] Reloading mod manager screen.", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("[ModHearth] Applying default modlist to Mods screen.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReloadDFHackModManagerScreen()
        {
            if (!DwarfFortressRunning())
                return false;

            string luaPath = Path.Combine(AppContext.BaseDirectory, "lua", "ReloadModManager.lua");
            if (!File.Exists(luaPath))
                return false;

            // Try direct RPC first (faster, avoids spawning processes)
            string? rpcOutput = DFHackRpcClient.ExecuteDFHackCommandViaRpc("lua", ["-f", luaPath], Config?.DFFolderPath, out string rpcError);
            if (rpcOutput != null)
            {
                if (!string.IsNullOrWhiteSpace(rpcOutput))
                    Console.WriteLine(rpcOutput.TrimEnd());

                return IsModManagerReloadOutputSuccessful(rpcOutput);
            }

            if (rpcError == "Dwarf Fortress is not running")
            {
                return false;
            }

            Console.WriteLine($"DFHack RPC failed ({rpcError}). Falling back to dfhack-run process.");

            string dfhackRunPath = GetDfhackRunPath();
            if (string.IsNullOrWhiteSpace(dfhackRunPath) || !File.Exists(dfhackRunPath))
                return false;

            try
            {
                string output = RunDfhackRunLua(dfhackRunPath, luaPath, out string error);
                if (!string.IsNullOrWhiteSpace(output))
                    Console.WriteLine(output.TrimEnd());
                if (!string.IsNullOrWhiteSpace(error))
                    Console.WriteLine(error.TrimEnd());

                return IsModManagerReloadOutputSuccessful(output);
            }
            catch (Exception ex)
            {
                // Don't disrupt saving, but don't swallow the reason either.
                Console.WriteLine($"[ModHearth] dfhack-run fallback failed: {ex.Message}");
                return false;
            }
        }

        private static string RunDfhackRunLua(string dfhackRunPath, string luaPath, out string error)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = dfhackRunPath,
                WorkingDirectory = Config.DFFolderPath,
                Arguments = $"lua -f \"{luaPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            SetDfHackRunEnvironmentVariables(startInfo);

            using Process process = new() { StartInfo = startInfo };
            _ = process.Start();

            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            error = errorTask.GetAwaiter().GetResult();
            process.WaitForExit();
            return output;
        }

        // Fix for Linux 'locale::facet::_S_create_c_locale' errors
        public static void SetDfHackRunEnvironmentVariables(ProcessStartInfo process)
        {
            if (!OperatingSystem.IsLinux()) return;
            process.EnvironmentVariables["LC_ALL"] = "C.UTF-8";
            process.EnvironmentVariables["LANG"] = "C.UTF-8";
        }

        private void ScheduleDeferredModManagerReload(int initialChecks)
        {
            if (!DwarfFortressRunning())
                return;

            CancellationTokenSource cts = new();
            lock (modManagerReloadGate)
            {
                deferredModManagerReloadCts?.Cancel();
                deferredModManagerReloadCts?.Dispose();
                deferredModManagerReloadCts = cts;
            }

            Console.WriteLine("[ModHearth] Mod manager reload deferred; waiting for moddable screen.");
            _ = Task.Run(() => DeferredModManagerReloadWorkerAsync(cts, initialChecks), CancellationToken.None);
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
                    ShowNotification("DFHack screen reload failed", "dfHack.png");
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
            SetActiveMods(SelectedModlist.Modlist);

            // Find problems with newly selected modpack.
            FindModlistProblems();
        }

        // Changes currently enabled and disabled mods based on the given list.
        // The only time this is called (other than SetSelectedModpack) is when overwriting a modpack due to importing.
        public void SetActiveMods(List<DfhMod> mods)
        {
            List<DfhMod> newEnabledMods = [.. mods];
            HashSet<DfhMod> newDisabledMods = [.. modPool];
            foreach (DfhMod mod in newEnabledMods)
                _ = newDisabledMods.Remove(mod);

            lock (stateGate)
            {
                enabledMods = newEnabledMods;
                disabledMods = newDisabledMods;
            }
            //dont show stale problems in the inactive list
            FindModlistProblems();
        }

        public void MoveMods(List<DfhMod> mods, int newIndex, bool sourceLeft, bool destinationLeft, bool refreshProblems = true)
        {
            if (mods == null || mods.Count == 0)
                return;

            List<DfhMod> uniqueMods = [];
            HashSet<DfhMod> seen = [];
            foreach (DfhMod mod in mods.Where(seen.Add))
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
                HashSet<DfhMod> selectedSet = [.. uniqueMods];
                List<DfhMod> selectedInOrder = [.. enabledMods.Where(selectedSet.Contains)];
                if (selectedInOrder.Count == 0)
                    return;

                int clampedIndex = Math.Max(0, Math.Min(newIndex, enabledMods.Count));
                int selectedBefore = enabledMods.Take(clampedIndex).Count(selectedSet.Contains);
                int targetIndex = clampedIndex - selectedBefore;

                List<DfhMod> remaining = [.. enabledMods.Where(m => !selectedSet.Contains(m))];
                targetIndex = Math.Max(0, Math.Min(targetIndex, remaining.Count));

                List<DfhMod> newList =
                [
                    .. remaining.Take(targetIndex),
                    .. selectedInOrder,
                    .. remaining.Skip(targetIndex),
                ];

                if (!enabledMods.SequenceEqual(newList))
                {
                    lock (stateGate)
                        enabledMods = newList;
                    changed = true;
                }
            }
            else if (!sourceLeft && destinationLeft)
            {
                HashSet<DfhMod> selectedSet = [.. uniqueMods];
                int beforeCount = enabledMods.Count;
                List<DfhMod> newEnabledMods = [.. enabledMods.Where(m => !selectedSet.Contains(m))];

                HashSet<DfhMod> newDisabledMods = [.. disabledMods];
                foreach (DfhMod mod in uniqueMods)
                    _ = newDisabledMods.Add(mod);

                lock (stateGate)
                {
                    enabledMods = newEnabledMods;
                    disabledMods = newDisabledMods;
                }
                changed = newEnabledMods.Count != beforeCount;
            }
            else if (sourceLeft && !destinationLeft)
            {
                HashSet<DfhMod> newDisabledMods = [.. disabledMods];
                foreach (DfhMod mod in uniqueMods)
                    _ = newDisabledMods.Remove(mod);

                List<DfhMod> newEnabledMods = [.. enabledMods];
                int insertIndex = Math.Max(0, Math.Min(newIndex, newEnabledMods.Count));
                newEnabledMods.InsertRange(insertIndex, uniqueMods);

                lock (stateGate)
                {
                    disabledMods = newDisabledMods;
                    enabledMods = newEnabledMods;
                }
                changed = true;
            }

            if (changed && refreshProblems)
                FindModlistProblems();
        }
        // Go through modlist and scan for problems.
        // Tuple representing problem has problem mod, int problemType (missing before, missing after, conflict present), and string modID.
        public void FindModlistProblems()
        {
            List<ModProblem> newModProblems = [];

            string installedModsPath = GetInstalledModsPath();
            bool hasInstalledModsPath = !string.IsNullOrWhiteSpace(installedModsPath);

            // Snapshot every shared collection under the same lock the writers use. Without this a
            // concurrent SetActiveMods/MoveMods could replace enabledMods between the Count check and
            // the indexer and throw, or make dependency checks mix two different modpack states.
            List<DfhMod> modPoolSnapshot;
            List<DfhMod> enabledModsSnapshot;
            Dictionary<string, List<ModReference>> duplicateModRefsSnapshot;
            Dictionary<string, ModRelationshipRule> relationshipRulesSnapshot;
            Dictionary<string, ModReference> modrefMapSnapshot;
            lock (stateGate)
            {
                modPoolSnapshot = [.. modPool];
                enabledModsSnapshot = [.. enabledMods];
                duplicateModRefsSnapshot = duplicateModRefs;
                relationshipRulesSnapshot = relationshipRules;
                modrefMapSnapshot = modrefMap;
            }

            foreach (DfhMod dfm in modPoolSnapshot) //works with both lists, not just the active mods
            {
                if (duplicateModRefsSnapshot.TryGetValue(dfm.ToString(), out List<ModReference>? duplicates))
                {
                    List<ModReference> relevantDuplicates = [.. duplicates.Where(m =>
                    {
                        if (hasInstalledModsPath && !string.IsNullOrWhiteSpace(m.Path) && PathHelper.IsUnderRoot(m.Path, installedModsPath))
                            return false;

                        if (IsLikelySteamShadowCopy(m.Path, m.SteamID, out _))
                            return false;

                        return true;
                    })];

                    if (relevantDuplicates.Count > 1)
                    {
                        string paths = string.Join(", ", relevantDuplicates.Select(m => $"'{m.Path}'"));
                        newModProblems.Add(new ModProblem(
                            dfm.Id,
                            paths,
                            ModProblem.ProblemType.DuplicateMod,
                            ModIssueClassifier.ForProblemType(ModProblem.ProblemType.DuplicateMod)));
                    }
                }
            }

            HashSet<string> scannedModIDs = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> unscannedModIDs = new(StringComparer.OrdinalIgnoreCase);

            foreach (DfhMod dfm in enabledModsSnapshot)
                _ = unscannedModIDs.Add(dfm.Id);

            HashSet<string> allEnabledIDs = new(unscannedModIDs, StringComparer.OrdinalIgnoreCase);
            string modNeedIsStr = " needs mod: ";

            for (int i = 0; i < enabledModsSnapshot.Count; i++)
            {
                DfhMod currentDFM = enabledModsSnapshot[i];
                _ = modrefMapSnapshot.TryGetValue(currentDFM.ToString(), out ModReference? currentMod);
                if (currentMod == null)
                {
                    // The modlist can reference a mod the current catalog no longer contains (e.g. a
                    // cache-only mod that a later disk scan could not rediscover). Nothing to scan.
                    Loggers.Diag.Dev($"Skipping problem scan for enabled mod '{currentDFM}': no ModReference registered.");
                    _ = scannedModIDs.Add(currentDFM.Id);
                    _ = unscannedModIDs.Remove(currentDFM.Id);
                    continue;
                }

                void ReportIds(
                    IEnumerable<string> ids,
                    Func<string, bool> isViolation,
                    ModProblem.ProblemType problemType,
                    string logPrefix)
                {
                    foreach (string rawId in ids)
                    {
                        string trimmedId = rawId?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(trimmedId) || !isViolation(trimmedId))
                            continue;

                        newModProblems.Add(new ModProblem(
                            currentDFM.Id,
                            trimmedId,
                            problemType,
                            ModIssueClassifier.ForProblemType(problemType)));
                        Loggers.Diag.Dev($"{logPrefix}{trimmedId}{modNeedIsStr}{currentDFM.Id}");
                    }
                }

                if (currentMod.Problematic)
                {
                    ReportIds(
                        currentMod.RequireBeforeMe,
                        id => !scannedModIDs.Contains(id),
                        ModProblem.ProblemType.MissingBefore,
                        "Problem found: missing before mod with ID: ");
                    ReportIds(
                        currentMod.RequireAfterMe,
                        id => !unscannedModIDs.Contains(id),
                        ModProblem.ProblemType.MissingAfter,
                        "Problem found: missing after mod with ID: ");
                    ReportIds(
                        currentMod.ConflictsWith,
                        allEnabledIDs.Contains,
                        ModProblem.ProblemType.ConflictPresent,
                        "Problem found: conflict present mod with ID: ");
                    ReportIds(
                        currentMod.RequireIds,
                        id => !scannedModIDs.Contains(id) && !unscannedModIDs.Contains(id),
                        ModProblem.ProblemType.MissingRequired,
                        "Problem found: missing required mod with ID: ");
                }

                if (relationshipRulesSnapshot.TryGetValue(currentDFM.Id, out ModRelationshipRule? customRule))
                {
                    ReportIds(
                        customRule.BeforeIds,
                        id => allEnabledIDs.Contains(id) && !unscannedModIDs.Contains(id),
                        ModProblem.ProblemType.MissingAfter,
                        "Problem found: custom before rule violated for ID: ");
                    ReportIds(
                        customRule.AfterIds,
                        id => allEnabledIDs.Contains(id) && !scannedModIDs.Contains(id),
                        ModProblem.ProblemType.MissingBefore,
                        "Problem found: custom after rule violated for ID: ");
                    ReportIds(
                        customRule.IncompatibleIds,
                        allEnabledIDs.Contains,
                        ModProblem.ProblemType.ConflictPresent,
                        "Problem found: custom incompatible rule present for ID: ");
                    ReportIds(
                        customRule.RequiredIds,
                        id => !allEnabledIDs.Contains(id),
                        ModProblem.ProblemType.MissingRequired,
                        "Problem found: custom required mod missing with ID: ");
                }

                _ = scannedModIDs.Add(currentDFM.Id);
                _ = unscannedModIDs.Remove(currentDFM.Id);
            }

            AppendErrorLogProblems(modPoolSnapshot, newModProblems);
            AppendPossibleIssueProblems(modPoolSnapshot, newModProblems);

            lock (stateGate)
                modproblems = newModProblems;
        }

        private HashSet<string> GetActiveModIds() => new(enabledMods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

        private HashSet<string> GetLiveConflictedIds(HashSet<string> activeModIds)
        {
            HashSet<string> liveConflictedIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (HashSet<string>? group in duplicateWarningGroups.Where(group => group.Count(activeModIds.Contains) > 1))
            {
                liveConflictedIds.UnionWith(group.Where(activeModIds.Contains));
            }
            return liveConflictedIds;
        }

        public IReadOnlyDictionary<string, List<string>> GetDuplicateWarningMap()
        {
            EnsureDuplicateWarningCache(logFound: true);

            HashSet<string> activeModIds = GetActiveModIds();
            HashSet<string> liveConflictedIds = GetLiveConflictedIds(activeModIds);

            Dictionary<string, List<string>> combinedMap = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> kvp in duplicateWarningMap.Where(kvp => liveConflictedIds.Contains(kvp.Key)))
            {
                combinedMap[kvp.Key] = [.. kvp.Value];
            }

            foreach (KeyValuePair<string, List<string>> kvp in cacheDuplicateMap)
            {
                if (combinedMap.TryGetValue(kvp.Key, out List<string>? list))
                    list.AddRange(kvp.Value);
                else
                    combinedMap[kvp.Key] = [.. kvp.Value];
            }
            return combinedMap;
        }

        public bool HasErrorLogDuplicateWarning(string modId)
        {
            EnsureDuplicateWarningCache(logFound: false);
            if (string.IsNullOrWhiteSpace(modId))
                return false;

            HashSet<string> activeModIds = GetActiveModIds();
            HashSet<string> liveConflictedIds = GetLiveConflictedIds(activeModIds);

            return duplicateWarningMap.ContainsKey(modId) && liveConflictedIds.Contains(modId);
        }

        public IReadOnlyList<HashSet<string>> GetDuplicateWarningGroups()
        {
            EnsureDuplicateWarningCache(logFound: true);

            HashSet<string> activeModIds = GetActiveModIds();
            List<HashSet<string>> combinedGroups = [];
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

            bool errorLogSearchEnabled = GetSearchErrorLogIssues();
            string errorLogPath = GetErrorLogPath();
            bool exists = errorLogSearchEnabled && File.Exists(errorLogPath);
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
                    duplicateWarningGroups = [];
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
                newGroups = [];
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
            List<HashSet<string>> groupList = [];
            HashSet<string> groupKeys = new(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadLines(errorLogPath))
            {
                Match match = DuplicateWarningRegex().Match(line);
                if (!match.Success)
                    continue;

                string objectName = match.Groups["object"].Value.Trim();
                string offenders = match.Groups["mods"].Value.Trim();
                if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(offenders))
                    continue;

                HashSet<string> groupIds = new(StringComparer.OrdinalIgnoreCase);
                foreach (string entry in offenders.Split(','))
                {
                    string token = DuplicateWarningCountRegex().Replace(entry, string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(token))
                        continue;

                    if (!aliasMap.TryGetValue(token, out string? modId) || string.IsNullOrWhiteSpace(modId))
                        continue;

                    _ = groupIds.Add(modId);

                    if (!map.TryGetValue(modId, out HashSet<string>? objects))
                    {
                        objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        map[modId] = objects;
                    }

                    _ = objects.Add(objectName);
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
                result[entry.Key] = [.. entry.Value.OrderBy(value => value)];

            warningMap = result;
            groups = groupList;
        }

        private void RefreshCacheDuplicateMap()
        {
            if (!GetSearchPossibleIssues())
            {
                lock (stateGate)
                {
                    cacheDuplicateMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    cacheDuplicateGroups = [];
                    cacheDuplicateLastWriteUtc = null;
                    cacheDuplicateActiveModIds = null;
                }

                return;
            }

            string cachePath = ModRawDependencyCacheStore.CachePath;
            bool exists = File.Exists(cachePath);
            DateTime lastWriteUtc = exists ? File.GetLastWriteTimeUtc(cachePath) : DateTime.MinValue;
            HashSet<string> activeModIds = new(enabledMods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

            bool upToDate;
            lock (stateGate)
            {
                upToDate = cacheDuplicateLastWriteUtc.HasValue &&
                           cacheDuplicateLastWriteUtc.Value == lastWriteUtc &&
                           cacheDuplicateActiveModIds != null &&
                           cacheDuplicateActiveModIds.SetEquals(activeModIds);
            }

            if (upToDate)
                return;

            Dictionary<string, ModRawDependencyInfo> cache = ModRawDependencyCacheStore.Load();
            Dictionary<string, List<string>> newMap = new(StringComparer.OrdinalIgnoreCase);
            List<HashSet<string>> newGroups = [];

            // DF treats the official raw folders as ordinary mods, so a direct definition that
            // collides with vanilla produces the same "Duplicate Object" line as a mod-vs-mod
            // collision. The baseline is loaded from those folders by the same scanner.
            List<ModDuplicateDefinitionGroup> groups = ModDuplicateDefinitionAnalyzer.Analyze(
                cache.Values,
                activeModIds,
                GetVanillaBaseline());

            foreach (ModDuplicateDefinitionGroup duplicate in groups)
            {
                HashSet<string> group = new(duplicate.ModIds, StringComparer.OrdinalIgnoreCase);
                if (duplicate.IncludesVanilla)
                    _ = group.Add(ModDuplicateDefinitionAnalyzer.VanillaOffender);
                newGroups.Add(group);

                string displayLabel = ObjectKey.FormatForDisplay(duplicate.DefinitionKey);
                foreach (string modId in duplicate.ModIds)
                {
                    if (!newMap.TryGetValue(modId, out List<string>? objects))
                    {
                        objects = [];
                        newMap[modId] = objects;
                    }

                    List<string> otherMods = [.. duplicate.ModIds
                        .Where(id => !string.Equals(id, modId, StringComparison.OrdinalIgnoreCase))];

                    if (duplicate.IncludesVanilla)
                        otherMods.Add(ModDuplicateDefinitionAnalyzer.VanillaOffender);

                    objects.Add($"[Cache] Duplicate raw definition: {displayLabel} (also in: {string.Join(", ", otherMods)})");
                }
            }

            lock (stateGate)
            {
                cacheDuplicateMap = newMap;
                cacheDuplicateGroups = newGroups;
                cacheDuplicateLastWriteUtc = lastWriteUtc;
                cacheDuplicateActiveModIds = activeModIds;
            }
        }

        private Dictionary<string, string> BuildDuplicateWarningAliasMap()
        {
            Dictionary<string, string> aliasMap = new(StringComparer.OrdinalIgnoreCase);

            foreach (ModReference modref in modrefMap.Values)
            {
                AddAlias(aliasMap, modref.ID, modref.ID);
                AddAlias(aliasMap, modref.Name, modref.ID);
                AddAlias(aliasMap, Path.GetFileName(modref.Path), modref.ID);
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
            bool loadedFromActive = TryReadModpackFile(activeModpackPath, out List<DfhModpack> loadedModpacks, out string loadError);
            if (!loadedFromActive)
            {
                Console.WriteLine($"Active modlist file unavailable: {loadError} Path: {activeModpackPath}");
                string alternatePath = activeModpackBackend == ModpackStorageBackend.DFHackConfig
                    ? localFallbackModpacksPath
                    : GetModManagerConfigPath();

                if (!string.IsNullOrWhiteSpace(alternatePath) &&
                    TryReadModpackFile(alternatePath, out List<DfhModpack> alternateModpacks, out _))
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

            List<DfhModpack> newModpacks = [.. loadedModpacks];

            Console.WriteLine();
            Console.WriteLine("Found modlists: ");

            // Handle mods missing.
            bool modMissing = false;
            string missingMessage = $"Some mods missing. \nModlists will be modified to not require lost mods. \nMissing mods: ";
            HashSet<DfhMod> notFound = [];

            // If a default modpack exists.
            int defaultIndex = -1;
            int preferredIndex = -1;

            StringBuilder messageBuilder = new(missingMessage);

            // Go through modpacks, and go through their modlists, looking for mods that we don't have.
            for (int i = 0; i < newModpacks.Count; i++)
            {
                DfhModpack modlist = newModpacks[i];

                // Remove the missing mods from the modlist.
                HashSet<DfhMod> thisListMissingMods = [];
                for (int mIndex = 0; mIndex < modlist.Modlist.Count; mIndex++)
                {
                    DfhMod mod = modlist.Modlist[mIndex];
                    if (!modPool.Contains(mod))
                    {
                        // Check if a mod with the same ID is in modPool
                        DfhMod? matchingIdMod = modPool.FirstOrDefault(m => string.Equals(m.Id, mod.Id, StringComparison.OrdinalIgnoreCase));
                        if (matchingIdMod is not null)
                        {
                            Console.WriteLine($"[ModHearth] Modpack '{modlist.Name}' contains mod '{mod.Id}' with version {mod.Version}, but version {matchingIdMod.Version} is installed. Updating modpack to version {matchingIdMod.Version}.");
                            modlist.Modlist[mIndex] = new DfhMod(mod.Id, matchingIdMod.Version);
                            shouldPersistActiveFile = true;
                        }
                        else
                        {
                            modMissing = true;
                            _ = notFound.Add(mod);
                            _ = thisListMissingMods.Add(mod);

                            _ = messageBuilder.Append('\n').Append(mod);
                        }
                    }
                }
                missingMessage = messageBuilder.ToString();

                // modlist isn't published anywhere yet, so replacing its .modlist with a trimmed copy (instead of mutating the shared List<DFHMod>
                // in place via .Remove()) is just cheap extra insurance, not a hard requiremen. Keeps the habit consistent though.
                if (thisListMissingMods.Count > 0)
                    modlist.Modlist = [.. modlist.Modlist.Where(m => !thisListMissingMods.Contains(m))];

                // Write out some info on the modpack
                Console.WriteLine("   Name: " + modlist.Name);
                Console.WriteLine("   Default: " + modlist.Default);
                Console.WriteLine("   Mods count: " + modlist.Modlist.Count);
                Console.WriteLine();

                if (modlist.Default && defaultIndex < 0)
                    defaultIndex = i;

                if (!string.IsNullOrWhiteSpace(preferredModlistName) &&
                    string.Equals(modlist.Name, preferredModlistName, StringComparison.OrdinalIgnoreCase))
                {
                    preferredIndex = i;
                }

            }

            int? indexToSelect = null;

            if (newModpacks.Count > 0)
            {
                if (preferredIndex >= 0)
                    indexToSelect = preferredIndex;
                else if (defaultIndex >= 0)
                    indexToSelect = defaultIndex;
                else
                {
                    newModpacks[0].Default = true;
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
                    _ = SaveAllModpacks(requestLiveReload: false, backUpExistingFile: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to persist modlists after load: {ex.Message}");
                }
            }

            LastMissingModsMessage = modMissing ? missingMessage : string.Empty;
        }

        // Generate a vanilla modlist by selecting mods physically located under data/vanilla.
        public List<DfhMod> GenerateVanillaModlist()
        {
            string? vanillaPath = GetVanillaModsPath();
            bool hasVanillaPath = !string.IsNullOrWhiteSpace(vanillaPath) && Directory.Exists(vanillaPath);
            if (hasVanillaPath)
                vanillaPath = Path.GetFullPath(vanillaPath);

            List<ModReference> vanillaRefs = [];
            foreach (ModReference modref in modrefMap.Values)
            {
                if (!hasVanillaPath || string.IsNullOrWhiteSpace(modref.Path))
                    continue;

                string modPath = Path.GetFullPath(modref.Path);
                if (!PathHelper.IsUnderRoot(modPath, vanillaPath))
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

            return [.. vanillaRefs
                .OrderBy(modref => modref.ID, StringComparer.OrdinalIgnoreCase)
                .Select(modref => modref.ToDFHMod())];
        }

        public enum ConfigIssueType
        {
            MissingDwarfFortressPath,
            MissingInstalledModsPath,
            MissingDFHackPath
        }

        public readonly record struct ConfigIssue(ConfigIssueType IssueType, string Message);

        [GeneratedRegex("^Duplicate Object:\\s*(?<object>.+?);\\s*Offending mods are\\s*(?<mods>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex DuplicateWarningRegex();
        [GeneratedRegex("\\s*\\(x\\d+\\)\\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex DuplicateWarningCountRegex();
        [GeneratedRegex(@"\[ID:([^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex InfoIdRegex();
        [GeneratedRegex(@"\d+(?:\.\d+)*")]
        private static partial Regex NumericVersionRegex();

        #endregion
    }
}
