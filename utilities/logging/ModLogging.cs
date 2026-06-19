using System;

namespace ModHearth;

internal sealed class Logger
{
    private readonly object gate = new();
    private readonly string tag;
    private readonly Action<string> sink;

    public Logger(string tag, Action<string> sink)
    {
        this.tag = tag;
        this.sink = sink;
    }

    public void Log(string message) => Write("INFO", message);
    public void LogError(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        try
        {
            lock (gate)
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string formatted = string.IsNullOrEmpty(tag)
                    ? $"[{timestamp}] {level}: {message}"
                    : $"[{timestamp}] [{tag}] {level}: {message}";
                sink(formatted);
            }
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}