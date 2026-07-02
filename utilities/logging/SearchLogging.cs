using System;

namespace ModHearth.Utilities.Logging
{
    public static class SearchLogging
    {
        public static void Log(string message)
        {
            if (!DevMode.IsEnabled)
                return;

            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [SearchFlow] {message}");
        }
    }
}