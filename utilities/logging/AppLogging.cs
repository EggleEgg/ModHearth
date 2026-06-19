using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ModHearth;

internal static class AppLogging
{
    private static readonly object gate = new();
    private static bool initialized;
    private static bool handlersRegistered;
    private static TextWriter? originalStdOut;
    private static TextWriter? originalStdErr;
    private static StreamWriter? logFileWriter;
    private static StreamWriter? errorFileWriter;

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
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "gamelog.txt");
                string errPath = Path.Combine(logDir, "errorlog.txt");

                originalStdOut = Console.Out;
                originalStdErr = Console.Error;

                logFileWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
                errorFileWriter = new StreamWriter(errPath, append: false) { AutoFlush = true };

                Console.SetOut(new TeeTextWriter(originalStdOut, logFileWriter));
                Console.SetError(new TeeTextWriter(originalStdErr, errorFileWriter));
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
        Initialize();
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string details = ex?.ToString() ?? "No exception details available.";
        Console.Error.WriteLine($"[{timestamp}] {label}");
        Console.Error.WriteLine(details);
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
            logFileWriter = null;
            errorFileWriter = null;
            originalStdOut = null;
            originalStdErr = null;
            initialized = false;
        }
    }

    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter primary;
        private readonly TextWriter secondary;

        public TeeTextWriter(TextWriter primary, TextWriter secondary)
        {
            this.primary = primary;
            this.secondary = secondary;
        }

        public override Encoding Encoding => primary.Encoding;

        public override void Write(char value)
        {
            primary.Write(value);
            secondary.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            primary.Write(buffer, index, count);
            secondary.Write(buffer, index, count);
        }

        public override void Write(string? value)
        {
            primary.Write(value);
            secondary.Write(value);
        }

        public override void Flush()
        {
            primary.Flush();
            secondary.Flush();
        }
    }
}
