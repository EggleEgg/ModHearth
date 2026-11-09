namespace ModHearth.Utilities.Logging
{
    /// Every channel is a Logger: Log/LogWarning/LogError always print; Dev(...) prints only in dev mode
    /// and never builds its message otherwise, so call sites need no `if (DevMode.IsEnabled)` guard.
    internal static class Loggers
    {
        public static readonly Logger Diag = new("DIAG", Console.WriteLine);
        public static readonly Logger DfRunner = new("DfRunner", Console.WriteLine);
        public static readonly Logger Workshop = new("Workshop", Console.WriteLine);
        public static readonly Logger Steam = new("Steam", Console.WriteLine);
        public static readonly Logger Reload = new("ReloadManager", Console.WriteLine);
        public static readonly Logger Dock = new("DockRebalance", Console.WriteLine);
        public static readonly Logger Drag = new("ModDragDrop", Console.WriteLine);
        public static readonly Logger Updates = new("UpdateCheck", Console.WriteLine);
        public static readonly Logger Backup = new("Backup", Console.WriteLine);
        public static readonly Logger Search = new("SearchFlow",
            line => Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {line}"));
    }
}
