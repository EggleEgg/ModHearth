namespace ModHearth;
/// <summary>
/// Mostly used for UI logging
/// </summary>
internal static class InfoLogger
{
    public static void Log(string message)
    {
        if (!DevMode.IsEnabled)
            return;

        Console.WriteLine($"[DIAG] {message}");
    }
}