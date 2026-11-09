using System.Globalization;
using System.Text;

namespace ModHearth.Utilities.Logging
{
    /// <summary>
    /// Handles application-wide logging, including unhandled exceptions and console output redirection to log files.
    /// </summary>
    internal static class AppLogging
    {
        private static readonly object gate = new();
        private static bool initialized;
        private static bool handlersRegistered;
        private static TextWriter? originalStdOut;
        private static TextWriter? originalStdErr;
        private static StreamWriter? logFileWriter;
        private static StreamWriter? errorFileWriter;
        private static TextWriter? teeStdOut;
        private static TextWriter? teeStdErr;

        public static void Initialize()
        {
            if (initialized)
                return;

            lock (gate)
            {
                if (initialized)
                    return;

                try
                {
                    string baseDir = AppContext.BaseDirectory;
                    string logDir = Path.Combine(baseDir, "logs");
                    _ = Directory.CreateDirectory(logDir);
                    string logPath = Path.Combine(logDir, "applog.txt");
                    string errPath = Path.Combine(logDir, "errorlog.txt");

                    originalStdOut = Console.Out;
                    originalStdErr = Console.Error;

                    logFileWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
                    errorFileWriter = new StreamWriter(errPath, append: false) { AutoFlush = true };

                    teeStdOut = new TeeTextWriter(originalStdOut, logFileWriter);
                    teeStdErr = new TeeTextWriter(originalStdErr, errorFileWriter);

                    Console.SetOut(teeStdOut);
                    Console.SetError(teeStdErr);
                }
                catch
                {
                    // If log setup fails, keep console output as-is.
                }

                initialized = true;
            }
        }

        public static void RegisterUnhandledExceptionHandlers()
        {
            if (handlersRegistered)
                return;

            handlersRegistered = true;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                LogException("Unhandled exception", e.ExceptionObject as Exception);
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogException("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
        }

        public static void LogException(string label, Exception? ex)
        {
            //See avalonia issues #17616, #18703, #4175. Likely wont be fixed ever, so we just ignore these.
            if (ex is AggregateException aggregate &&
                aggregate.InnerException?.GetType().FullName == "Tmds.DBus.Protocol.DBusException" &&
                aggregate.InnerException.Message.Contains("AppMenu.Registrar", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Initialize();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string details = ex?.ToString() ?? "No exception details available.";
            Console.Error.WriteLine($"[{timestamp}] {label}");
            Console.Error.WriteLine(details);
        }

        public static void LogError(string message)
        {
            Initialize();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Console.Error.WriteLine($"[{timestamp}] ERROR: {message}");
        }

        public static void Shutdown()
        {
            lock (gate)
            {
                if (!initialized)
                    return;

                try
                {
                    if (originalStdOut != null)
                        Console.SetOut(originalStdOut);
                    if (originalStdErr != null)
                        Console.SetError(originalStdErr);
                }
                catch
                {
                    // Ignore teardown failures.
                }

                logFileWriter?.Dispose();
                errorFileWriter?.Dispose();
                teeStdOut?.Dispose();
                teeStdErr?.Dispose();
                logFileWriter = null;
                errorFileWriter = null;
                teeStdOut = null;
                teeStdErr = null;
                originalStdOut = null;
                originalStdErr = null;
                initialized = false;
            }
        }

        private sealed class TeeTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
        {
            private readonly object gateTee = new();
            private readonly TextWriter primary = primary;
            private readonly TextWriter secondary = secondary;

            public override Encoding Encoding => primary.Encoding;

            public override void Write(char value)
            {
                lock (gateTee)
                {
                    primary.Write(value);
                    secondary.Write(value);
                }
            }

            public override void Write(char[] buffer, int index, int count)
            {
                lock (gateTee)
                {
                    primary.Write(buffer, index, count);
                    secondary.Write(buffer, index, count);
                }
            }

            public override void Write(string? value)
            {
                lock (gateTee)
                {
                    primary.Write(value);
                    secondary.Write(value);
                }
            }

            public override void WriteLine(string? value)
            {
                lock (gateTee)
                {
                    primary.WriteLine(value);
                    secondary.WriteLine(value);
                }
            }

            public override void Flush()
            {
                lock (gateTee)
                {
                    primary.Flush();
                    secondary.Flush();
                }
            }
        }
    }
}
