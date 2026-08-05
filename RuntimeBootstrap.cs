using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ModHearth;

/// <summary>
/// Initializes runtime components (such as the console) and logging for the application
/// </summary>
internal static class RuntimeBootstrap
{
    private static bool initialized;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        AppLogging.Initialize();
        AppLogging.RegisterUnhandledExceptionHandlers();
    }

    public static void HideConsole()
    {
        if (OperatingSystem.IsWindows())
        {
            NativeMethods.HideConsoleWindow();
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;

        public static void HideConsoleWindow()
        {
            try
            {
                var handle = GetConsoleWindow();
                if (handle != IntPtr.Zero)
                    _ = ShowWindow(handle, SW_HIDE);
            }
            catch
            {
                // Ignore failures to hide console.
            }
        }
    }
}
