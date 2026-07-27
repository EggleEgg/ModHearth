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

            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SearchFlow] {message}"); */
        }
    }
}