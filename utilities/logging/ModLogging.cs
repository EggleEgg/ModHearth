namespace ModHearth.Utilities.Logging
{
    internal sealed class Logger(string tag, Action<string> sink)
    {
        private readonly object gate = new();
        private readonly string tag = tag;
        private readonly Action<string> sink = sink;

        public void Log(string message) => Write("INFO", message);

        public void LogWarning(string message) => Write("WARNING", message);

        public void LogError(string message) => Write("ERROR", message);

        public void Dev(ref DevLogHandler message)
        {
            if (message.IsEnabled)
                Write("DEV", message.ToStringAndClear());
        }

        private void Write(string level, string message)
        {
            try
            {
                lock (gate)
                {
                    string formatted = string.IsNullOrEmpty(tag)
                        ? $"{level}: {message}"
                        : $"[{tag}] {level}: {message}";
                    sink(formatted);
                }
            }
            catch
            {
                // Ignore logging failures.
            }
        }
    }
}