using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ModHearth;

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
        SetupNativeLibrarySearchPaths();
        SetupDllFolderResolver();
    }

    public static void HideConsole()
    {
        if (OperatingSystem.IsWindows())
        {
            NativeMethods.HideConsoleWindow();
        }
    }

    private static void SetupNativeLibrarySearchPaths()
    {
        string baseDir = AppContext.BaseDirectory;
        List<string> paths = new List<string>();

        string dllFolder = Path.Combine(baseDir, "dlls");
        if (Directory.Exists(dllFolder))
            paths.Add(dllFolder);

        string nativeFolder = Path.Combine(baseDir, "native");
        if (Directory.Exists(nativeFolder))
            paths.Add(nativeFolder);

        string runtimesFolder = Path.Combine(baseDir, "runtimes");
        if (Directory.Exists(runtimesFolder))
        {
            foreach (string ridDir in Directory.EnumerateDirectories(runtimesFolder))
            {
                string ridNative = Path.Combine(ridDir, "native");
                if (Directory.Exists(ridNative))
                    paths.Add(ridNative);
            }
        }

        string? existing = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            foreach (string entry in existing.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!paths.Contains(entry))
                    paths.Add(entry);
            }
        }

        if (paths.Count > 0)
            AppContext.SetData("NATIVE_DLL_SEARCH_DIRECTORIES", string.Join(Path.PathSeparator, paths));
    }

    private static void SetupDllFolderResolver()
    {
        string dllFolder = Path.Combine(AppContext.BaseDirectory, "dlls");
        if (!Directory.Exists(dllFolder))
            return;

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string candidate = Path.Combine(dllFolder, $"{name.Name}.dll");
            if (File.Exists(candidate))
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            return null;
        };
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
                    ShowWindow(handle, SW_HIDE);
            }
            catch
            {
                // Ignore failures to hide console.
            }
        }
    }
}
