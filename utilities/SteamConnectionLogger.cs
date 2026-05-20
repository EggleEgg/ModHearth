using System;

namespace ModHearth;

internal static class SteamConnectionLogger
{
    private static readonly object gate = new();

    public static void Log(string message)
    {
        Write("INFO", message);
    }

    public static void LogError(string message)
    {
        Write("ERROR", message);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (gate)
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Console.WriteLine($"[{timestamp}] [Steam] {level}: {message}");
            }
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
