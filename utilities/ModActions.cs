using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using Steamworks;
using ModHearth.Utilities;
using ModHearth.UI;

namespace ModHearth
{
    /// Handles the logic for performing actions on mods.
    public partial class ModHearthManager
    {
        private static readonly TimeSpan SteamActionGap = TimeSpan.FromMilliseconds(500);
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
                if (!TryEnsureSteamSession(new List<string>()))
                    return;

                SteamManager.Initialize();
                SteamWorkshopService steam = new SteamWorkshopService();
                if (!steam.IsAvailable)
                    return;

                SteamManifestAuditor.Audit(steam);
            }
            finally
            {
                Interlocked.Exchange(ref workshopAuditInProgress, 0);
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
            localDeletableMods = new List<ModReference>();
            steamActionableMods = new List<ModReference>();

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
                    : new[] { modref };

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
            List<string> failures = UnsubscribeSteamMods(new[] { modref });
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
            List<string> failures = RedownloadSteamMods(new[] { modref });
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
            List<string> failures = new List<string>();
            Dictionary<string, ModReference> steamModRefs = new Dictionary<string, ModReference>(StringComparer.OrdinalIgnoreCase);

            // Resolve unique Steam item IDs and associated ModReference objects
            if (mods != null)
            {
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
            }

            List<string> steamItemIds = steamModRefs.Keys.ToList();
            if (steamItemIds.Count == 0)
                return failures;

            if (!TryEnsureSteamSession(failures))
                return failures;

            SteamManager.Initialize();
            SteamWorkshopService steam = new SteamWorkshopService();
            if (!steam.IsAvailable)
            {
                failures.Add("Steamworks API could not be initialized. Ensure Steam is running.");
                return failures;
            }

            SteamConnectionLogger.Log($"Steam unsubscribe started for {steamItemIds.Count} workshop item(s): {string.Join(", ", steamItemIds)}.");

            for (int index = 0; index < steamItemIds.Count; index++)
            {
                string steamItemId = steamItemIds[index];
                ModReference modrefToDelete = steamModRefs[steamItemId];

                if (!ulong.TryParse(steamItemId, out ulong workshopId))
                {
                    failures.Add($"Invalid workshop id \'{steamItemId}\'.");
                    continue;
                }

                if (!steam.Unsubscribe(workshopId))
                {
                    failures.Add($"Failed to unsubscribe workshop item {steamItemId}.");
                }
                else
                {
                    SteamConnectionLogger.LogInfo(
                        $"Requested Steam API unsubscribe for workshop item {steamItemId}.");

                    // Attempt to delete the mod folder
                    if (!string.IsNullOrWhiteSpace(modrefToDelete.path) && Directory.Exists(modrefToDelete.path))
                    {
                        try
                        {
                            Directory.Delete(modrefToDelete.path, true);
                            SteamConnectionLogger.LogInfo($"Deleted mod folder: {modrefToDelete.path}");

                            if (!string.IsNullOrWhiteSpace(modrefToDelete.ID))
                            {
                                lock (installedCacheGate)
                                {
                                    installedCacheModIds?.Remove(modrefToDelete.ID);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"Failed to delete mod folder \'{modrefToDelete.path}\': {ex.Message}");
                            SteamConnectionLogger.LogError($"Failed to delete mod folder \'{modrefToDelete.path}\': {ex.Message}");
                        }
                    }
                    else
                    {
                        SteamConnectionLogger.LogWarning($"Mod folder not found or path is invalid for workshop item {steamItemId}: {modrefToDelete.path}");
                    }
                }

                if (index < steamItemIds.Count - 1)
                    Thread.Sleep(SteamActionGap);
            }

            SteamConnectionLogger.Log($"Steam unsubscribe completed for {steamItemIds.Count} workshop item(s) with {failures.Count} failure(s).");

            if (failures.Count == 0)
            {
                // Reload mod manager if all unsubscriptions and deletions were successful
                TryRequestModManagerReload(out _, out _);
                OnRequestUIReload();
            }

            ShowNotification($"Unsubscribed {steamItemIds.Count} workshop items. {failures.Count} failure(s).", "steamRemoveIcon.svg");
            return failures;
        }

        public List<string> RedownloadSteamMods(IEnumerable<ModReference>? mods)
        {
            List<string> failures = new List<string>();
            List<string> steamItemIds = ResolveUniqueSteamWorkshopItemIds(mods, failures);
            if (steamItemIds.Count == 0)
                return failures;

            if (!TryEnsureSteamSession(failures))
                return failures;

            SteamManager.Initialize();
            SteamWorkshopService steam = new SteamWorkshopService();
            if (!steam.IsAvailable)
            {
                failures.Add("Steamworks API could not be initialized. Ensure Steam is running.");
                return failures;
            }

            SteamConnectionLogger.Log(
                $"Steam resubscribe started for {steamItemIds.Count} workshop item(s): {string.Join(", ", steamItemIds)}.");

            // Staging: unsubscribe all -> wait -> subscribe all -> wait -> download/validate.
            for (int index = 0; index < steamItemIds.Count; index++)
            {
                string steamItemId = steamItemIds[index];
                if (!ulong.TryParse(steamItemId, out ulong workshopId))
                {
                    failures.Add($"Invalid workshop id \'{steamItemId}\'.");
                    continue;
                }

                if (!steam.Unsubscribe(workshopId))
                {
                    failures.Add($"Failed to unsubscribe workshop item {steamItemId} (resubscribe stage).");
                }
                else
                {
                    SteamConnectionLogger.LogInfo($"Unsubscribed workshop item {steamItemId} (resubscribe stage).");
                    lock (installedCacheGate)
                    {
                        // Since we are unsubscribing, it's effectively deleted from the installed mods for now.
                        installedCacheModIds?.Remove(steamItemId);
                    }
                }

                if (index < steamItemIds.Count - 1)
                    Thread.Sleep(SteamActionGap);
            }

            Thread.Sleep(SteamResubscribeUnsubscribeWait);

            for (int index = 0; index < steamItemIds.Count; index++)
            {
                string steamItemId = steamItemIds[index];
                if (!ulong.TryParse(steamItemId, out ulong workshopId))
                {
                    continue;
                }

                if (!steam.Subscribe(workshopId))
                {
                    failures.Add($"Failed to subscribe workshop item {steamItemId} (resubscribe stage).");
                }
                else
                {
                    SteamConnectionLogger.LogInfo($"Subscribed to workshop item {steamItemId} (resubscribe stage).");
                }

                if (index < steamItemIds.Count - 1)
                    Thread.Sleep(SteamActionGap);
            }

            Thread.Sleep(SteamResubscribeSubscribeWait);

            for (int index = 0; index < steamItemIds.Count; index++)
            {
                string steamItemId = steamItemIds[index];
                if (!ulong.TryParse(steamItemId, out ulong workshopId))
                {
                    continue;
                }

                if (!steam.Download(workshopId, highPriority: true))
                {
                    failures.Add($"Failed to trigger download/validation for workshop item {steamItemId}.");
                }
                else
                {
                    SteamConnectionLogger.LogInfo($"Requested Steam download/validation for workshop item {steamItemId}.");
                }
            }

            SteamConnectionLogger.Log($"Steam resubscribe completed for {steamItemIds.Count} workshop item(s) with {failures.Count} failure(s).");
            ShowNotification($"Resubscribed {steamItemIds.Count} workshop items. {failures.Count} failure(s).", "steamReloadIcon.svg");

            return failures;
        }

        private static List<string> ResolveUniqueSteamWorkshopItemIds(
            IEnumerable<ModReference>? mods,
            List<string> failures)
        {
            List<string> ids = new List<string>();
            if (mods == null)
                return ids;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModReference modref in mods)
            {
                if (modref == null)
                    continue;

                if (!TryGetSteamWorkshopItemId(modref, out string steamItemId))
                {
                    string modName = modref.name ?? modref.ID ?? "Unknown mod";
                    failures.Add($"Steam Workshop item ID not available for \'{modName}\'.");
                    continue;
                }

                if (seen.Add(steamItemId))
                    ids.Add(steamItemId);
            }

            return ids;
        }

        private static bool TryEnsureSteamSession(List<string> failures)
        {
            if (TryDetectSteamProcess(out List<string> processNames))
            {
                SteamConnectionLogger.Log(
                    $"Steam session detected. Active process(es): {string.Join(", ", processNames)}.");
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
            // Local mods take precedence even if they carry steam metadata.
            (_, bool isLocal, _) = ModSourceClassifier.Classify(
                modref,
                ConfigManager.Config.ModsPath,
                ConfigManager.GetVanillaModsPath());
            if (!isLocal || !CanDeleteModFromModsFolder(modref))
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
            if (ConfigManager.IsLikelySteamShadowCopy(modref.path, out _))
                return;

            (_, _, bool isSteam) = ModSourceClassifier.Classify(
                modref,
                ConfigManager.Config.ModsPath,
                ConfigManager.GetVanillaModsPath());
            if (!isSteam)
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

        private static void ShowNotification(string message, string icon)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow is UI.MainWindow mainWindow)
                {
                    mainWindow.ShowNotification(message, icon);
                }
            });
        }
    }
}
