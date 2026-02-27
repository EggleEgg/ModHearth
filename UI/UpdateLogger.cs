using System;
using System.IO;

namespace ModHearth.UI;

internal static class UpdateLogger
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
                string baseDir = AppContext.BaseDirectory;
                string logDir = Path.Combine(baseDir, "logs");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "updatelog.txt");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.AppendAllText(logPath, $"[{timestamp}] {level}: {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
