using System.IO;
using Steamworks;

namespace ModHearth.Utilities;

public static class SteamManager
{
    public static bool Initialized { get; private set; }
    internal static readonly object Gate = new();

    public static bool Initialize()
    {
        lock (Gate)
        {
            if (Initialized)
                return true;

            try
            {
                const string appId = ConfigManager.DwarfFortressSteamAppId;
                string currentDirectory = Directory.GetCurrentDirectory();
                string appBaseDirectory = AppContext.BaseDirectory;

                SteamConnectionLogger.LogInfo($"SteamManager.Initialize() called.");
                SteamConnectionLogger.LogInfo($"Current Directory: {currentDirectory}");
                SteamConnectionLogger.LogInfo($"App Base Directory: {appBaseDirectory}");

                try
                {
                    string baseDirAppIdPath = Path.Combine(appBaseDirectory, "steam_appid.txt");
                    string currentDirAppIdPath = Path.Combine(currentDirectory, "steam_appid.txt");

                    if (!File.Exists(baseDirAppIdPath) || File.ReadAllText(baseDirAppIdPath).Trim() != appId)
                    {
                        File.WriteAllText(baseDirAppIdPath, appId);
                        SteamConnectionLogger.LogInfo($"Wrote steam_appid.txt in App Base Dir with ID: {appId}");
                    }

                    if (!File.Exists(currentDirAppIdPath) || File.ReadAllText(currentDirAppIdPath).Trim() != appId)
                    {
                        File.WriteAllText(currentDirAppIdPath, appId);
                        SteamConnectionLogger.LogInfo($"Wrote steam_appid.txt in Current Dir with ID: {appId}");
                    }
                }
                catch (Exception ex)
                {
                    SteamConnectionLogger.Log($"[SteamManager] Warning: failed to write steam_appid.txt: {ex.Message}");
                }

                Initialized = SteamAPI.Init();

                if (Initialized)
                {
                    SteamConnectionLogger.LogInfo("Steam API has started");
                }
                else
                {
                    SteamConnectionLogger.LogError("Steam API failed to initialize (SteamAPI.Init() returned false). Is Steam running?");
                }

                return Initialized;
            }
            catch (Exception ex)
            {
                SteamConnectionLogger.LogError($"Steam API not initialized: {ex.Message}");
                return false;
            }
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            if (!Initialized)
                return;

            SteamAPI.Shutdown();
            Initialized = false;
        }
    }

    public static void RunCallbacks()
    {
        lock (Gate)
        {
            if (Initialized)
                SteamAPI.RunCallbacks();
        }
    }
}