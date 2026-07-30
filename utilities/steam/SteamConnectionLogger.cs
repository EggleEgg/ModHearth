namespace ModHearth.Utilities;

internal static class SteamConnectionLogger
{
    private static readonly Logger logger = new Logger("Steam", Console.WriteLine);

    public static void Log(string message) => logger.Log(message);
    public static void LogError(string message) => logger.LogError(message);
    public static void LogWarning(string message) => logger.LogWarning(message);

    public static void LogInfo(string message)
    {
        Log($"{message}");
    }
}