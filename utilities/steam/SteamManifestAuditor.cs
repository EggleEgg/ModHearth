namespace ModHearth.Utilities
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
            if (steamService == null || !steamService.IsAvailable)
                return;

            SteamConnectionLogger.LogInfo("Starting Steam workshop manifest audit...");

            var acfPaths = ConfigManager.GetSteamWorkshopAcfPaths();
            List<ulong> missingItemIds = [];

            foreach (string acfPath in acfPaths)
            {
                try
                {
                    CollectMissingItems(acfPath, missingItemIds);
                }
                catch (Exception ex)
                {
                    SteamConnectionLogger.LogError($"Error auditing manifest '{acfPath}': {ex.Message}");
                }
            }

            if (missingItemIds.Count == 0)
            {
                SteamConnectionLogger.LogInfo("Manifest audit completed. No discrepancies found.");
                return;
            }

            SteamConnectionLogger.LogWarning(
                $"Manifest audit found {missingItemIds.Count} missing workshop item(s) across all manifests. Sending wake-up calls.");

            int wakeUpCalls = SteamWorkshopService.DownloadMany(missingItemIds);

            switch (wakeUpCalls)
            {
                case > 0:
                    SteamConnectionLogger.LogInfo($"Manifest audit completed. Sent {wakeUpCalls} wake-up call(s) to Steam.");
                    break;
                default:
                    SteamConnectionLogger.LogError("Manifest audit completed. Failed to send any wake-up calls for missing items.");
                    break;
            }
        }

        private static void CollectMissingItems(string acfPath, List<ulong> missingItemIds)
        {
            if (!File.Exists(acfPath))
                return;

            string content = File.ReadAllText(acfPath);
            var root = SteamAcfKeyValueParser.Parse(content);

            if (!root.TryGetValue("AppWorkshop", out object? appObj) || appObj is not Dictionary<string, object> appDict)
                return;

            if (!appDict.TryGetValue("WorkshopItemsInstalled", out object? installedObj) ||
                installedObj is not Dictionary<string, object> installedDict)
                return;

            // The ACF is usually in steamapps/ or steamapps/workshop/
            // The content is in steamapps/workshop/content/975370/
            string? steamAppsDir = Path.GetDirectoryName(acfPath);
            if (string.IsNullOrWhiteSpace(steamAppsDir)) return;

            string workshopContentDir = steamAppsDir.EndsWith("workshop", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(steamAppsDir, "content", ConfigManager.DwarfFortressSteamAppId)
                : Path.Combine(steamAppsDir, "workshop", "content", ConfigManager.DwarfFortressSteamAppId);

            foreach (var kvp in installedDict)
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
                    SteamConnectionLogger.LogWarning(
                        $"Discrepancy found: Workshop item {itemId} is marked as installed in manifest, but local folder is missing or empty.");
                    missingItemIds.Add(itemId);
                }
            }
        }
    }
}
