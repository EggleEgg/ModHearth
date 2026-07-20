namespace ModHearth;
/// <summary>
/// Mostly used for UI logging
/// </summary>
internal static class InfoLogger
{
    public static void Log(string message)
    {
        if (DevMode.IsEnabled)
            Console.WriteLine($"[DIAG] {message}");
    }

    public static void LogRunDf(string message)
    {
        Console.WriteLine($"[DfRunner] {message}");
    }


}