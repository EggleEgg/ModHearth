using System.Collections.Concurrent;
using ModHearth.Utilities;

namespace ModHearth
{
    /// Handles the logic for performing actions on mods.
    public partial class ModHearthManager
    {
        private static readonly TimeSpan SteamResubscribeUnsubscribeWait = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan SteamResubscribeSubscribeWait = TimeSpan.FromSeconds(2);
        private static int workshopAuditInProgress;


        // To avoid spamming audit calls on autoreload
        public static void AuditWorkshopManifests()
        {
            if (Interlocked.CompareExchange(ref workshopAuditInProgress, 1, 0) != 0)
            {
                SteamConnectionLogger.LogInfo("Workshop manifest audit skipped: a previous audit is still running.");
                return;
            }

            try
            {
                if (!TryEnsureSteamSession([]))
                    return;

                SteamWorkshopService steam = new SteamWorkshopService();
                if (!steam.IsAvailable)
                    return;

                SteamManifestAuditor.Audit(steam);
            }
            finally
            {
                _ = Interlocked.Exchange(ref workshopAuditInProgress, 0);
            }
        }

        public static bool TryGetSteamWorkshopItemId(ModReference? modref, out string steamItemId)
        {
            steamItemId = string.Empty;
            if (modref == null)
                return false;

            if (ConfigManager.TryParsePositiveSteamId(modref.steamID, out steamItemId))
                return true;

            return ConfigManager.TryExtractSteamWorkshopItemIdFromPath(modref.path, out steamItemId);
        }

        public static bool CanUnsubscribeSteamMod(ModReference modref) => TryGetSteamWorkshopItemId(modref, out _);

        public void SplitActionableMods(
            IEnumerable<ModReference>? mods,
            out List<ModReference> localDeletableMods,
            out List<ModReference> steamActionableMods)
        {
            localDeletableMods = [];
            steamActionableMods = [];

            if (mods == null)
                return;

            HashSet<string> uniqueLocalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> uniqueSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ModReference modref in mods)
            {
                if (modref == null)
                    continue;

                // Include all duplicates in the actionable set
                string key = modref.DFHackCompatibleString();
                IEnumerable<ModReference> allVersions = duplicateModRefs.TryGetValue(key, out var list)
                    ? (IEnumerable<ModReference>)list
                    : [modref];

                foreach (ModReference version in allVersions)
                {
                    if (TryAddLocalActionableMod(version, uniqueLocalKeys, localDeletableMods))
                    {
                        // Note: a single ModReference might technically be both,
                        // but TryAddLocalActionableMod returning true means we handled it as local.
                    }

                    TryAddSteamActionableMod(version, uniqueSteamIds, steamActionableMods);
                }
            }
        }

        public bool UnsubscribeSteamMod(ModReference modref, out string message)
        {
            List<string> failures = UnsubscribeSteamMods([modref]);
            if (failures.Count == 0)
            {
                message = "Unsubscribe request sent to Steam.";
                SteamConnectionLogger.LogInfo(message);
                return true;
            }

            message = failures[0];
            return false;
        }

        public bool RedownloadSteamMod(ModReference modref, out string message)
        {
            List<string> failures = RedownloadSteamMods([modref]);
            if (failures.Count == 0)
            {
                message = "Redownload request sent to Steam.";
                SteamConnectionLogger.LogInfo(message);
                return true;
            }

            message = failures[0];
            return false;
        }

        public List<string> UnsubscribeSteamMods(IEnumerable<ModReference>? mods)
        {
            List<string> failures = [];
            Dictionary<string, ModReference> steamModRefs = ResolveUniqueSteamWorkshopItemIds(mods, failures);

            List<string> steamItemIds = steamModRefs.Keys.ToList();
            if (steamItemIds.Count == 0)
                return failures;

            if (!TryEnsureSteamSession(failures))
                return failures;

            SteamWorkshopService steam = new SteamWorkshopService();
            if (!steam.IsAvailable)
            {
                failures.Add("Steamworks API could not be initialized. Ensure Steam is running.");
                return failures;
            }

            SteamConnectionLogger.LogInfo($"Steam unsubscribe started for {steamItemIds.Count} workshop item(s): {string.Join(", ", steamItemIds)}.");

            ConcurrentBag<string> failureBag = [];
            ConcurrentBag<ulong> successBag = [];

            _ = Parallel.ForEach(steamItemIds, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, steamItemId =>
            {
                if (!steamModRefs.TryGetValue(steamItemId, out ModReference? modrefToDelete))
                    return;

                if (!ulong.TryParse(steamItemId, out ulong workshopId))
                {
                    failureBag.Add($"Invalid workshop id '{steamItemId}'.");
                    return;
                }

                if (!SteamWorkshopService.Unsubscribe(workshopId))
                {
                    failureBag.Add($"Failed to unsubscribe workshop item {steamItemId}.");
                }
                else
                {
                    successBag.Add(workshopId);
                    SteamConnectionLogger.LogInfo($"Requested Steam API unsubscribe for workshop item {steamItemId}.");

                    // Attempt to delete the mod folder
                    if (!string.IsNullOrWhiteSpace(modrefToDelete.path) && Directory.Exists(modrefToDelete.path))
                    {
                        try
                        {
                            Directory.Delete(modrefToDelete.path, true);
                            ShowNotification($"Deleted mod folder: {Path.GetFileName(modrefToDelete.path)}", "trashIcon.svg");
                            SteamConnectionLogger.LogInfo($"Deleted mod folder: {modrefToDelete.path}");

                            if (!string.IsNullOrWhiteSpace(modrefToDelete.ID))
                            {
                                lock (installedCacheGate)
                                {
                                    _ = (installedCacheModIds?.Remove(modrefToDelete.ID));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failureBag.Add($"Failed to delete mod folder '{modrefToDelete.path}': {ex.Message}");
                            SteamConnectionLogger.LogError($"Failed to delete mod folder '{modrefToDelete.path}': {ex.Message}");
                        }
                    }
                    else
                    {
                        SteamConnectionLogger.LogWarning($"Mod folder not found or path is invalid for workshop item {steamItemId}: {modrefToDelete.path}");
                    }
                }
            });

            failures.AddRange(failureBag);
            List<ulong> successfullyUnsubscribedIds = successBag.ToList();

            SteamConnectionLogger.LogInfo($"Steam unsubscribe completed for {steamItemIds.Count} workshop item(s) with {failures.Count} failure(s).");

            if (successfullyUnsubscribedIds.Count > 0)
            {
                SteamManifestAuditor.MarkAsUnsubscribed(successfullyUnsubscribedIds);

                List<ModReference> successfullyUnsubscribedMods = [];
                foreach (ulong workshopId in successfullyUnsubscribedIds)
                {
                    if (steamModRefs.TryGetValue(workshopId.ToString(), out ModReference? modref))
                    {
                        successfullyUnsubscribedMods.Add(modref);
                    }
                }

                if (successfullyUnsubscribedMods.Count > 0)
                {
                    HashSet<string> activeIds = new(enabledMods.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
                    ModUpdateLogger.LogUnsubscribe(successfullyUnsubscribedMods, activeIds);
                }
            }

            if (failures.Count == 0)
            {
                // Reload mod manager if all unsubscriptions and deletions were successful
                _ = TryRequestModManagerReload(out _, out _);
                TriggerUIReload();
            }

            ShowNotification($"Unsubscribed {steamItemIds.Count} workshop items. {failures.Count} failure(s).", "steamRemoveIcon.svg");
            return failures;
        }

        public List<string> RedownloadSteamMods(IEnumerable<ModReference>? mods)
        {
            List<string> failures = [];
            Dictionary<string, ModReference> steamModRefs = ResolveUniqueSteamWorkshopItemIds(mods, failures);

            List<string> steamItemIds = steamModRefs.Keys.ToList();
            if (steamItemIds.Count == 0)
                return failures;

            if (!TryEnsureSteamSession(failures))
                return failures;

            SteamWorkshopService steam = new SteamWorkshopService();
            if (!steam.IsAvailable)
            {
                failures.Add("Steamworks API could not be initialized. Ensure Steam is running.");
                return failures;
            }

            SteamConnectionLogger.LogInfo($"Steam resubscribe started for {steamItemIds.Count} workshop item(s): {string.Join(", ", steamItemIds)}.");

            List<ulong> workshopIds = [];
            foreach (string steamItemId in steamItemIds)
            {
                if (ulong.TryParse(steamItemId, out ulong workshopId))
                {
                    workshopIds.Add(workshopId);
                }
                else
                {
                    failures.Add($"Invalid workshop id '{steamItemId}'.");
                }
            }

            if (workshopIds.Count > 0)
            {
                // Unsubscribe all in parallel
                _ = SteamWorkshopService.UnsubscribeMany(workshopIds);
                foreach (string steamItemId in steamItemIds)
                {
                    SteamConnectionLogger.LogInfo($"Unsubscribed workshop item {steamItemId} (resubscribe stage).");
                    lock (installedCacheGate)
                    {
                        // Since we are unsubscribing, it's effectively deleted from the installed mods for now.
                        _ = (installedCacheModIds?.Remove(steamItemId));
                    }
                }

                Thread.Sleep(SteamResubscribeUnsubscribeWait);

                // Subscribe all in parallel
                _ = SteamWorkshopService.SubscribeMany(workshopIds);
                foreach (string steamItemId in steamItemIds)
                {
                    SteamConnectionLogger.LogInfo($"Subscribed to workshop item {steamItemId} (resubscribe stage).");
                }

                Thread.Sleep(SteamResubscribeSubscribeWait);

                // Download all in parallel
                _ = SteamWorkshopService.DownloadMany(workshopIds);
                foreach (string steamItemId in steamItemIds)
                {
                    SteamConnectionLogger.LogInfo($"Requested Steam download/validation for workshop item {steamItemId}.");
                }
            }

            SteamConnectionLogger.LogInfo($"Steam resubscribe completed for {steamItemIds.Count} workshop item(s) with {failures.Count} failure(s).");

            if (failures.Count == 0)
            {
                List<ModReference> successfullyRedownloadedMods = steamModRefs.Values.ToList();
                if (successfullyRedownloadedMods.Count > 0)
                {
                    HashSet<string> activeIds = new(enabledMods.Select(m => m.id), StringComparer.OrdinalIgnoreCase);
                    ModUpdateLogger.LogRedownload(successfullyRedownloadedMods, activeIds);
                }

                // Reload mod manager if all redownloads were successful
                _ = TryRequestModManagerReload(out _, out _);
                TriggerUIReload();
            }

            ShowNotification($"Resubscribed {steamItemIds.Count} workshop items. {failures.Count} failure(s).", "steamReloadIcon.svg");

            return failures;
        }

        private static Dictionary<string, ModReference> ResolveUniqueSteamWorkshopItemIds(
            IEnumerable<ModReference>? mods,
            List<string> failures)
        {
            Dictionary<string, ModReference> steamModRefs = new Dictionary<string, ModReference>(StringComparer.OrdinalIgnoreCase);
            if (mods == null)
                return steamModRefs;

            foreach (ModReference modref in mods)
            {
                if (modref == null)
                    continue;

                if (TryGetSteamWorkshopItemId(modref, out string steamItemId))
                {
                    if (!steamModRefs.ContainsKey(steamItemId))
                    {
                        steamModRefs.Add(steamItemId, modref);
                    }
                }
                else
                {
                    string modName = modref.name ?? modref.ID ?? "Unknown mod";
                    failures.Add($"Steam Workshop item ID not available for \'{modName}\'.");
                }
            }

            return steamModRefs;
        }

        private static bool TryEnsureSteamSession(List<string> failures)
        {
            if (TryDetectSteamProcess(out List<string> processNames))
            {
                SteamConnectionLogger.LogInfo($"Steam session detected. Active process(es): {string.Join(", ", processNames)}.");
                return true;
            }

            const string message = "Steam session not detected. Open Steam and keep it running, then retry.";
            failures.Add(message);
            SteamConnectionLogger.LogError(message);
            return false;
        }

        private static bool TryDetectSteamProcess(out List<string> processNames)
        {
            return SteamProcessHelper.TryDetectSteamProcess(out processNames);
        }

        private static bool TryAddLocalActionableMod(
            ModReference modref,
            HashSet<string> uniqueLocalKeys,
            List<ModReference> localDeletableMods)
        {
            (_, bool isLocal, _, bool isSteamLocal) = ModSourceClassifier.Classify(
                modref,
                ConfigManager.GetModsPath(),
                ConfigManager.GetVanillaModsPath());
            if ((!isLocal && !isSteamLocal) || !CanDeleteModFromModsFolder(modref))
                return false;

            string localKey = BuildLocalActionKey(modref);
            if (string.IsNullOrWhiteSpace(localKey))
                return true;

            if (uniqueLocalKeys.Add(localKey))
                localDeletableMods.Add(modref);

            return true;
        }

        private static void TryAddSteamActionableMod(
            ModReference modref,
            HashSet<string> uniqueSteamIds,
            List<ModReference> steamActionableMods)
        {
            (_, _, bool isSteam, bool isSteamLocal) = ModSourceClassifier.Classify(
                modref,
                ConfigManager.GetModsPath(),
                ConfigManager.GetVanillaModsPath());
            if (!isSteam && !isSteamLocal)
                return;
            if (!TryGetSteamWorkshopItemId(modref, out string steamId))
                return;

            if (uniqueSteamIds.Add(steamId))
                steamActionableMods.Add(modref);
        }

        private static string BuildLocalActionKey(ModReference modref)
        {
            string localPath = ConfigManager.NormalizeFileSystemPath(modref.path ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(localPath))
                return localPath;

            return modref.ID?.Trim() ?? string.Empty;
        }

        private void ShowNotification(string message, string icon)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                TriggerNotification(message, icon);
            });
        }
    }
}
