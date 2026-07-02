namespace ModHearth.Utilities;

internal static class SteamConnectionLogger
{
    private static readonly Logger logger = new Logger("Steam", Console.WriteLine);

    public static void Log(string message) => logger.Log(message);
    public static void LogError(string message) => logger.LogError(message);
    public static void LogWarning(string message) => logger.LogWarning(message);

    //Only works with dev mode, otherwise simply use Console.WriteLine()
    public static void LogInfo(string message)
    {
        if (!DevMode.IsEnabled)
            return;

        Log($"{message}");
    }
}