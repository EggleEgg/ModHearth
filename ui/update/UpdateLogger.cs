namespace ModHearth.UI;

internal static class UpdateLogger
{
    private static readonly Logger logger = new Logger(string.Empty, line =>
    {
        string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        _ = Directory.CreateDirectory(logDir);
        File.AppendAllText(Path.Combine(logDir, "updatelog.txt"), line + Environment.NewLine);
    });

    public static void Log(string message) => logger.Log(message);
    public static void LogError(string message) => logger.LogError(message);
}