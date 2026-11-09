using System.Collections.Concurrent;
using ModHearth.Models;
using ModHearth.Utilities;
using ModHearth.Utilities.Logging;
using ModHearth.Utilities.Steam;
using ModHearth.Utilities.Workshop;

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
                Loggers.Steam.Log("Workshop manifest audit skipped: a previous audit is still running.");
                return;
            }

            try
            {
                if (!TryEnsureSteamSession([]))
                    return;

                SteamWorkshopService steam = new();
                if (!SteamWorkshopService.IsAvailable)
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

            if (ConfigManager.TryParsePositiveSteamId(modref.SteamID, out steamItemId))
                return true;

            return ConfigManager.TryExtractSteamWorkshopItemIdFromPath(modref.Path, out steamItemId);
        }

        public void SplitActionableMods(
            IEnumerable<ModReference>? mods,
            out List<ModReference> localDeletableMods,
            out List<ModReference> steamActionableMods)
        {
            localDeletableMods = [];
            steamActionableMods = [];

            if (mods == null)
                return;

            HashSet<string> uniqueLocalKeys = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> uniqueSteamIds = new(StringComparer.OrdinalIgnoreCase);

            ModSourceContext sourceContext = ModSourceContext.FromConfig();

            foreach (ModReference modref in mods)
            {
                if (modref == null)
                    continue;

                // Include all duplicates in the actionable set
                string key = modref.DFHackCompatibleString();
                IEnumerable<ModReference> allVersions = duplicateModRefs.TryGetValue(key, out List<ModReference>? list)
                    ? (IEnumerable<ModReference>)list
                    : [modref];

                foreach (ModReference version in allVersions)
                {
                    if (TryAddLocalActionableMod(version, sourceContext, uniqueLocalKeys, localDeletableMods))
                    {
                        // Note: a single ModReference might technically be both,
                        // but TryAddLocalActionableMod returning true means we handled it as local.
                    }

                    TryAddSteamActionableMod(version, sourceContext, uniqueSteamIds, steamActionableMods);
                }
            }
        }

        public List<string> UnsubscribeSteamMods(IEnumerable<ModReference>? mods)
        {
            using (BeginDeletionBatch())
            {
                if (!TryPrepareSteamBatch(mods, "unsubscribe", out List<string> failures, out Dictionary<string, ModReference> steamModRefs, out List<string> steamItemIds))
                    return failures;

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
                        Loggers.Steam.Log($"Requested Steam API unsubscribe for workshop item {steamItemId}.");

                        // Attempt to delete the mod folder
                        if (!string.IsNullOrWhiteSpace(modrefToDelete.Path) && Directory.Exists(modrefToDelete.Path))
                        {
                            try
                            {
                                Directory.Delete(modrefToDelete.Path, true);
                                string sourceFolder = Path.GetDirectoryName(modrefToDelete.Path) ?? string.Empty;
                                ModDeletionNotifier.NotifyDeleted((msg, icon) => ShowNotification(msg, icon), modrefToDelete.Path, sourceFolder);
                                Loggers.Steam.Log($"Deleted mod folder: {modrefToDelete.Path}");

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
                                failureBag.Add($"Failed to delete mod folder '{modrefToDelete.Path}': {ex.Message}");
                                Loggers.Steam.LogError($"Failed to delete mod folder '{modrefToDelete.Path}': {ex.Message}");
                            }
                        }
                        else
                        {
                            Loggers.Steam.LogWarning($"Mod folder not found or path is invalid for workshop item {steamItemId}: {modrefToDelete.Path}");
                        }
                    }
                });

                failures.AddRange(failureBag);
                List<ulong> successfullyUnsubscribedIds = [.. successBag];

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
                        HashSet<string> activeIds = new(enabledMods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
                        ModUpdateLogger.LogUnsubscribe(successfullyUnsubscribedMods, activeIds);
                    }
                }

                return CompleteSteamBatch("unsubscribe", "Unsubscribed", "steamRemoveIcon.svg", steamItemIds.Count, failures);
            }
        }

        public List<string> RedownloadSteamMods(IEnumerable<ModReference>? mods)
        {
            if (!TryPrepareSteamBatch(mods, "resubscribe", out List<string> failures, out Dictionary<string, ModReference> steamModRefs, out List<string> steamItemIds))
                return failures;

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
                    Loggers.Steam.Log($"Unsubscribed workshop item {steamItemId} (resubscribe stage).");
                    lock (installedCacheGate)
                    {
                        // Since we are unsubscribing, it's effectively deleted from installed_mods for now.
                        _ = (installedCacheModIds?.Remove(steamItemId));
                    }
                }

                Thread.Sleep(SteamResubscribeUnsubscribeWait);

                // Subscribe all in parallel
                _ = SteamWorkshopService.SubscribeMany(workshopIds);
                foreach (string steamItemId in steamItemIds)
                {
                    Loggers.Steam.Log($"Subscribed to workshop item {steamItemId} (resubscribe stage).");
                }

                Thread.Sleep(SteamResubscribeSubscribeWait);

                // Download all in parallel, reporting real progress to the shared main-window indicator.
                _ = DownloadSteamItemsWithProgress(workshopIds, failures);
            }

            if (failures.Count == 0)
            {
                List<ModReference> successfullyRedownloadedMods = [.. steamModRefs.Values];
                if (successfullyRedownloadedMods.Count > 0)
                {
                    HashSet<string> activeIds = new(enabledMods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
                    ModUpdateLogger.LogRedownload(successfullyRedownloadedMods, activeIds);
                }
            }

            return CompleteSteamBatch("resubscribe", "Resubscribed", "steamReloadIcon.svg", steamItemIds.Count, failures);
        }

        /// <summary>
        /// Resolves the requested mods to unique workshop ids and verifies that Steam is ready. Logs the
        /// batch start on success. Returns false (with <paramref name="failures"/> populated) when there is
        /// nothing to do or Steam is unavailable.
        /// </summary>
        private static bool TryPrepareSteamBatch(
            IEnumerable<ModReference>? mods,
            string verb,
            out List<string> failures,
            out Dictionary<string, ModReference> steamModRefs,
            out List<string> steamItemIds)
        {
            failures = [];
            steamModRefs = ResolveUniqueSteamWorkshopItemIds(mods, failures);
            steamItemIds = [.. steamModRefs.Keys];

            if (steamItemIds.Count == 0)
                return false;

            if (!TryEnsureSteamSession(failures))
                return false;

            if (!SteamWorkshopService.IsAvailable)
            {
                failures.Add("Steamworks API could not be initialized. Ensure Steam is running.");
                return false;
            }

            Loggers.Steam.Log($"Steam {verb} started for {steamItemIds.Count} workshop item(s): {string.Join(", ", steamItemIds)}.");
            return true;
        }

        /// <summary>
        /// Logs batch completion, triggers the shared reload when nothing failed, and shows the result
        /// notification. Returns <paramref name="failures"/> for the caller to hand back.
        /// </summary>
        private List<string> CompleteSteamBatch(
            string logVerb,
            string notificationVerb,
            string icon,
            int itemCount,
            List<string> failures)
        {
            Loggers.Steam.Log($"Steam {logVerb} completed for {itemCount} workshop item(s) with {failures.Count} failure(s).");

            if (failures.Count == 0)
            {
                _ = TryRequestModManagerReload(out _, out _);
                TriggerUIReload();
            }

            ShowNotification($"{notificationVerb} {itemCount} workshop items. {failures.Count} failure(s).", icon);
            return failures;
        }

        /// <summary>
        /// Downloads the given workshop items through the Steam client, reporting per-item progress to
        /// <see cref="WorkshopDownloads"/> so the main window can show it.
        /// </summary>
        private int DownloadSteamItemsWithProgress(List<ulong> workshopIds, List<string> failures)
        {
            ConcurrentBag<string> failureBag = [];
            int successCount = 0;

            CancellationToken token = deferredModManagerReloadCts?.Token ?? CancellationToken.None;

            using WorkshopDownloadSession session = WorkshopDownloads.BeginSession();

            _ = Parallel.ForEach(workshopIds, new ParallelOptions
            {
                MaxDegreeOfParallelism = 3,
                CancellationToken = token
            }, workshopId =>
            {
                long entryId = session.Track();

                void ReportProgress(ulong bytesDownloaded, ulong bytesTotal)
                {
                    if (bytesTotal == 0)
                        return;

                    session.Report(entryId, (double)bytesDownloaded / bytesTotal * 100);
                }

                if (SteamWorkshopService.Download(workshopId, onProgress: ReportProgress, cancellationToken: token))
                {
                    session.Complete(entryId);
                    _ = Interlocked.Increment(ref successCount);
                }
                else
                {
                    session.Drop(entryId);
                    failureBag.Add($"Failed to download workshop item {workshopId}.");
                }
            });

            failures.AddRange(failureBag);
            Loggers.Steam.Log($"Steam download finished for {successCount}/{workshopIds.Count} workshop item(s).");
            return successCount;
        }

        private static Dictionary<string, ModReference> ResolveUniqueSteamWorkshopItemIds(
            IEnumerable<ModReference>? mods,
            List<string> failures)
        {
            Dictionary<string, ModReference> steamModRefs = new(StringComparer.OrdinalIgnoreCase);
            if (mods == null)
                return steamModRefs;

            foreach (ModReference modref in mods)
            {
                if (modref == null)
                    continue;

                if (TryGetSteamWorkshopItemId(modref, out string steamItemId))
                {
                    _ = steamModRefs.TryAdd(steamItemId, modref);
                }
                else
                {
                    string modName = modref.Name ?? modref.ID ?? "Unknown mod";
                    failures.Add($"Steam Workshop item ID not available for \'{modName}\'.");
                }
            }

            return steamModRefs;
        }

        private static bool TryEnsureSteamSession(List<string> failures)
        {
            if (TryDetectSteamProcess(out List<string> processNames))
            {
                Loggers.Steam.Log($"Steam session detected. Active process(es): {string.Join(", ", processNames)}.");
                return true;
            }

            const string message = "Steam session not detected. Open Steam and keep it running, then retry.";
            failures.Add(message);
            Loggers.Steam.LogError(message);
            return false;
        }

        private static bool TryDetectSteamProcess(out List<string> processNames) => SteamProcessHelper.TryDetectSteamProcess(out processNames);

        private static bool TryAddLocalActionableMod(
            ModReference modref,
            ModSourceContext sourceContext,
            HashSet<string> uniqueLocalKeys,
            List<ModReference> localDeletableMods)
        {
            (_, bool isLocal, _, bool isSteamLocal) = ModSourceClassifier.Classify(modref, sourceContext);
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
            ModSourceContext sourceContext,
            HashSet<string> uniqueSteamIds,
            List<ModReference> steamActionableMods)
        {
            (_, _, bool isSteam, bool isSteamLocal) = ModSourceClassifier.Classify(modref, sourceContext);
            if (!isSteam && !isSteamLocal)
                return;
            if (!TryGetSteamWorkshopItemId(modref, out string steamId))
                return;

            if (uniqueSteamIds.Add(steamId))
                steamActionableMods.Add(modref);
        }

        private static string BuildLocalActionKey(ModReference modref)
        {
            string localPath = ConfigManager.NormalizeFileSystemPath(modref.Path ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(localPath))
                return localPath;

            return modref.ID?.Trim() ?? string.Empty;
        }

    }
}
