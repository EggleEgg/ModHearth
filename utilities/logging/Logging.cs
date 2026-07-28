using System;

namespace ModHearth.Utilities.Logging
{
    /// <summary>
    /// Logging for debugging search actions
    /// </summary>
    public static class SearchLogging
    {
        public static void Log(string message)
        {
            /* Disabled for now to avoid console spam
            if (!DevMode.IsEnabled)
                return;
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SearchFlow] {message}");*/
        }
    }
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
    public static class ReloadLogging
    {
        public static void Log(string message)
        {
            if (!DevMode.IsEnabled)
                return;
            Console.WriteLine($"[ReloadManager] {message}");
        }
    }

}