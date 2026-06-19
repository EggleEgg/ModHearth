using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace ModHearth
{
    public partial class ModHearthManager
    {
        private static readonly TimeSpan SteamActionGap = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SteamResubscribeUnsubscribeWait = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan SteamResubscribeSubscribeWait = TimeSpan.FromSeconds(2);

        public static bool TryGetSteamWorkshopItemId(ModReference? modref, out string steamItemId)
        {
            steamItemId = string.Empty;
            if (modref == null)
                return false;

            if (TryParsePositiveSteamId(modref.steamID, out steamItemId))
                return true;

            return TryExtractSteamWorkshopItemIdFromPath(modref.path, out steamItemId);
        }

        public bool CanUnsubscribeSteamMod(ModReference modref)
        {
            return TryGetSteamWorkshopItemId(modref, out _);
        }

        public bool IsLocalModSource(ModReference? modref)
        {
            if (modref == null)
                return false;

            (bool _, bool isLocal, bool __) = ModSourceClassifier.Classify(
                modref,
                config?.ModsPath,
                GetVanillaModsPath());
            return isLocal;
        }

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

                if (TryAddLocalActionableMod(modref, uniqueLocalKeys, localDeletableMods))
                    continue;

                TryAddSteamActionableMod(modref, uniqueSteamIds, steamActionableMods);
            }
        }

        public bool UnsubscribeSteamMod(ModReference modref, out string message)
        {
            List<string> failures = UnsubscribeSteamMods(new[] { modref });
            if (failures.Count == 0)
            {
                message = "Unsubscribe request sent to Steam.";
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
                return true;
            }

            message = failures[0];
            return false;
        }

        public List<string> UnsubscribeSteamMods(IEnumerable<ModReference>? mods)
        {
            List<string> failures = new List<string>();
            List<string> steamItemIds = ResolveUniqueSteamWorkshopItemIds(mods, failures);
            if (steamItemIds.Count == 0)
                return failures;

            if (!TryEnsureSteamSession(failures))
                return failures;

            SteamConnectionLogger.Log(
                $"Steam unsubscribe started for {steamItemIds.Count} workshop item(s): {string.Join(", ", steamItemIds)}.");

            for (int index = 0; index < steamItemIds.Count; index++)
            {
                string steamItemId = steamItemIds[index];
                if (!TryRunSteamProtocolAction(
                        BuildSteamUnsubscribeUri(steamItemId),
                        steamItemId,
                        "unsubscribe",
                        out string message))
                {
                    failures.Add(message);
                }

                if (index < steamItemIds.Count - 1)
                    Thread.Sleep(SteamActionGap);
            }

            SteamConnectionLogger.Log(
                $"Steam unsubscribe completed for {steamItemIds.Count} workshop item(s) with {failures.Count} failure(s).");
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

            SteamConnectionLogger.Log(
                $"Steam resubscribe started for {steamItemIds.Count} workshop item(s): {string.Join(", ", steamItemIds)}.");

            // RimSort staging: unsubscribe all -> wait -> subscribe all -> wait -> validate/download.
            for (int index = 0; index < steamItemIds.Count; index++)
            {
                string steamItemId = steamItemIds[index];
                if (!TryRunSteamProtocolAction(
                        BuildSteamUnsubscribeUri(steamItemId),
                        steamItemId,
                        "unsubscribe (resubscribe stage)",
                        out string message))
                {
                    failures.Add(message);
                }

                if (index < steamItemIds.Count - 1)
                    Thread.Sleep(SteamActionGap);
            }

            Thread.Sleep(SteamResubscribeUnsubscribeWait);

            for (int index = 0; index < steamItemIds.Count; index++)
            {
                string steamItemId = steamItemIds[index];
                if (!TryRunSteamProtocolAction(
                        BuildSteamSubscribeUri(steamItemId),
                        steamItemId,
                        "subscribe (resubscribe stage)",
                        out string message))
                {
                    failures.Add(message);
                }

                if (index < steamItemIds.Count - 1)
                    Thread.Sleep(SteamActionGap);
            }

            Thread.Sleep(SteamResubscribeSubscribeWait);

            for (int index = 0; index < steamItemIds.Count; index++)
            {
                string steamItemId = steamItemIds[index];
                if (!TryTriggerSteamValidate(steamItemId, out string message))
                    failures.Add(message);
            }

            SteamConnectionLogger.Log(
                $"Steam resubscribe completed for {steamItemIds.Count} workshop item(s) with {failures.Count} failure(s).");
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
                    failures.Add($"Steam Workshop item ID not available for '{modName}'.");
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
            processNames = new List<string>();
            string[] candidates = { "steam", "Steam", "steamwebhelper", "SteamWebHelper" };
            foreach (string candidate in candidates)
            {
                try
                {
                    if (Process.GetProcessesByName(candidate).Length == 0)
                        continue;
                    if (!processNames.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                        processNames.Add(candidate);
                }
                catch
                {
                    // Ignore process query failures for one process name.
                }
            }

            return processNames.Count > 0;
        }

        private static bool TryRunSteamProtocolAction(
            string steamUri,
            string steamItemId,
            string actionLabel,
            out string message)
        {
            if (TryLaunchUri(steamUri, out Exception? steamException))
            {
                message = $"Requested Steam to {actionLabel} workshop item {steamItemId}.";
                return true;
            }

            string fallbackUrl = BuildSteamWorkshopPageUrl(steamItemId);
            bool openedFallback = TryLaunchUri(fallbackUrl, out Exception? fallbackException);
            if (openedFallback)
            {
                message = $"Failed to {actionLabel} workshop item {steamItemId} via Steam URI ({steamException?.Message ?? "unknown"}). Opened workshop page.";
                return false;
            }

            message = $"Failed to {actionLabel} workshop item {steamItemId}: {fallbackException?.Message ?? steamException?.Message ?? "unknown error"}";
            return false;
        }

        private static bool TryTriggerSteamValidate(string steamItemId, out string message)
        {
            string primaryUri = BuildSteamValidateUriWithApp(steamItemId);
            if (TryLaunchUri(primaryUri, out _))
            {
                message = $"Requested Steam validation for workshop item {steamItemId}.";
                return true;
            }

            string fallbackUri = BuildSteamValidateUriLegacy(steamItemId);
            if (TryLaunchUri(fallbackUri, out _))
            {
                message = $"Requested Steam validation (legacy URI) for workshop item {steamItemId}.";
                return true;
            }

            message = $"Failed to trigger Steam validation for workshop item {steamItemId}.";
            return false;
        }

        private static bool TryLaunchUri(string uri, out Exception? exception)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
                exception = null;
                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        private static string BuildSteamUnsubscribeUri(string steamItemId)
        {
            return $"steam://url/UnsubscribeItem/{DwarfFortressSteamAppId}/{steamItemId}";
        }

        private static string BuildSteamSubscribeUri(string steamItemId)
        {
            return $"steam://url/SubscribeItem/{DwarfFortressSteamAppId}/{steamItemId}";
        }

        private static string BuildSteamValidateUriWithApp(string steamItemId)
        {
            return $"steam://validate/{DwarfFortressSteamAppId}/{steamItemId}";
        }

        private static string BuildSteamValidateUriLegacy(string steamItemId)
        {
            return $"steam://validate/{steamItemId}";
        }

        private bool TryAddLocalActionableMod(
            ModReference modref,
            HashSet<string> uniqueLocalKeys,
            List<ModReference> localDeletableMods)
        {
            // Local mods take precedence even if they carry steam metadata.
            if (!IsLocalModSource(modref) || !CanDeleteModFromModsFolder(modref))
                return false;

            string localKey = BuildLocalActionKey(modref);
            if (string.IsNullOrWhiteSpace(localKey))
                return true;

            if (uniqueLocalKeys.Add(localKey))
                localDeletableMods.Add(modref);

            return true;
        }

        private void TryAddSteamActionableMod(
            ModReference modref,
            HashSet<string> uniqueSteamIds,
            List<ModReference> steamActionableMods)
        {
            if (!IsSteamModSource(modref))
                return;
            if (!TryGetSteamWorkshopItemId(modref, out string steamId))
                return;

            if (uniqueSteamIds.Add(steamId))
                steamActionableMods.Add(modref);
        }

        private bool IsSteamModSource(ModReference modref)
        {
            (bool _, bool __, bool isSteam) = ModSourceClassifier.Classify(
                modref,
                config?.ModsPath,
                GetVanillaModsPath());
            return isSteam;
        }

        private static string BuildLocalActionKey(ModReference modref)
        {
            string localPath = NormalizeFileSystemPath(modref.path ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(localPath))
                return localPath;

            return modref.ID?.Trim() ?? string.Empty;
        }

        private static string BuildSteamWorkshopPageUrl(string steamItemId)
        {
            return $"https://steamcommunity.com/sharedfiles/filedetails/?id={steamItemId}";
        }

        private static bool TryParsePositiveSteamId(string? rawSteamId, out string steamItemId)
        {
            steamItemId = string.Empty;
            string normalized = rawSteamId?.Trim() ?? string.Empty;
            if (!long.TryParse(normalized, out long parsedSteamId) || parsedSteamId <= 0)
                return false;

            steamItemId = parsedSteamId.ToString();
            return true;
        }
    }
}
