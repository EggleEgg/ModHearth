using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities.Steam
{
    internal static class SteamManifestAuditor
    {
        private static readonly HashSet<ulong> recentlyUnsubscribedIds = [];
        private static readonly object unsubscribedGate = new();

        public static void MarkAsUnsubscribed(IEnumerable<ulong> workshopIds)
        {
            lock (unsubscribedGate)
            {
                foreach (ulong id in workshopIds)
                {
                    _ = recentlyUnsubscribedIds.Add(id);
                }
            }
        }

        public static void Audit(SteamWorkshopService steamService)
        {
            if (steamService == null || !SteamWorkshopService.IsAvailable)
                return;

            Loggers.Steam.Log("Starting Steam workshop manifest audit...");

            IEnumerable<string> acfPaths = ConfigManager.GetSteamWorkshopAcfPaths();
            List<ulong> missingItemIds = [];

            foreach (string acfPath in acfPaths)
            {
                try
                {
                    CollectMissingItems(acfPath, missingItemIds);
                }
                catch (Exception ex)
                {
                    Loggers.Steam.LogError($"Error auditing manifest '{acfPath}': {ex.Message}");
                }
            }

            if (missingItemIds.Count == 0)
            {
                Loggers.Steam.Log("Manifest audit completed. No discrepancies found.");
                return;
            }

            Loggers.Steam.LogWarning(
                $"Manifest audit found {missingItemIds.Count} missing workshop item(s) across all manifests. Sending wake-up calls.");

            int wakeUpCalls = SteamWorkshopService.DownloadMany(missingItemIds);

            if (wakeUpCalls > 0)
                Loggers.Steam.Log($"Manifest audit completed. Sent {wakeUpCalls} wake-up call(s) to Steam.");
            else
                Loggers.Steam.LogError("Manifest audit completed. Failed to send any wake-up calls for missing items.");
        }

        private static void CollectMissingItems(string acfPath, List<ulong> missingItemIds)
        {
            if (!File.Exists(acfPath))
                return;

            string content = File.ReadAllText(acfPath);
            Dictionary<string, object> root = SteamAcfKeyValueParser.Parse(content);

            if (!root.TryGetValue("AppWorkshop", out object? appObj) || appObj is not Dictionary<string, object> appDict)
                return;

            if (!appDict.TryGetValue("WorkshopItemsInstalled", out object? installedObj) ||
                installedObj is not Dictionary<string, object> installedDict)
            {
                return;
            }

            // The ACF is usually in steamapps/ or steamapps/workshop/
            // The content is in steamapps/workshop/content/975370/
            string? steamAppsDir = Path.GetDirectoryName(acfPath);
            if (string.IsNullOrWhiteSpace(steamAppsDir)) return;

            string workshopContentDir = steamAppsDir.EndsWith("workshop", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(steamAppsDir, "content", ConfigManager.DwarfFortressSteamAppId)
                : Path.Combine(steamAppsDir, "workshop", "content", ConfigManager.DwarfFortressSteamAppId);

            foreach (KeyValuePair<string, object> kvp in installedDict)
            {
                if (!ulong.TryParse(kvp.Key, out ulong itemId))
                    continue;

                lock (unsubscribedGate)
                {
                    if (recentlyUnsubscribedIds.Contains(itemId))
                        continue;
                }

                string itemPath = Path.Combine(workshopContentDir, kvp.Key);

                if (!Directory.Exists(itemPath) || !Directory.EnumerateFileSystemEntries(itemPath).Any())
                {
                    Loggers.Steam.LogWarning(
                        $"Discrepancy found: Workshop item {itemId} is marked as installed in manifest, but local folder is missing or empty.");
                    missingItemIds.Add(itemId);
                }
            }
        }
    }
}
