using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Steamworks;

namespace ModHearth.Utilities
{
    internal static class SteamManifestAuditor
    {
        public static void Audit(SteamWorkshopService steamService)
        {
            if (steamService == null || !steamService.IsAvailable)
                return;

            SteamConnectionLogger.Log("Starting Steam workshop manifest audit...");

            var acfPaths = ConfigManager.GetSteamWorkshopAcfPaths();
            int wakeUpCalls = 0;

            foreach (string acfPath in acfPaths)
            {
                try
                {
                    AuditManifest(acfPath, steamService, ref wakeUpCalls);
                }
                catch (Exception ex)
                {
                    SteamConnectionLogger.LogError($"Error auditing manifest '{acfPath}': {ex.Message}");
                }
            }

            if (wakeUpCalls > 0)
            {
                SteamConnectionLogger.Log($"Manifest audit completed. Sent {wakeUpCalls} wake-up call(s) to Steam.");
            }
            else
            {
                SteamConnectionLogger.Log("Manifest audit completed. No discrepancies found.");
            }
        }

        private static void AuditManifest(string acfPath, SteamWorkshopService steamService, ref int wakeUpCalls)
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

            string workshopContentDir;
            if (steamAppsDir.EndsWith("workshop", StringComparison.OrdinalIgnoreCase))
            {
                workshopContentDir = Path.Combine(steamAppsDir, "content", ConfigManager.DwarfFortressSteamAppId);
            }
            else
            {
                workshopContentDir = Path.Combine(steamAppsDir, "workshop", "content", ConfigManager.DwarfFortressSteamAppId);
            }

            foreach (var kvp in installedDict)
            {
                if (!ulong.TryParse(kvp.Key, out ulong itemId))
                    continue;

                string itemPath = Path.Combine(workshopContentDir, kvp.Key);
                
                // Discrepancy check: manifest says installed, but folder is missing or empty
                if (!Directory.Exists(itemPath) || !Directory.EnumerateFileSystemEntries(itemPath).Any())
                {
                    SteamConnectionLogger.LogWarning($"Discrepancy found: Workshop item {itemId} is marked as installed in manifest, but local folder is missing or empty. Sending wake-up call.");
                    
                    if (SteamWorkshopService.Download(itemId))
                    {
                        wakeUpCalls++;
                    }
                    else
                    {
                        SteamConnectionLogger.LogError($"Failed to send wake-up call for workshop item {itemId}.");
                    }
                }
            }
        }
    }
}
