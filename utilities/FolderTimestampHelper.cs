using System.IO.Enumeration;

namespace ModHearth.Utilities
{
    /// <summary>
    /// Returns the latest last-write time for a directory and its files.
    /// </summary>
    internal static class FolderTimestampHelper
    {
        // Same semantics as Directory.EnumerateFiles(path, "*", AllDirectories): hidden/system files
        // included, inaccessible entries throw (callers already handle that).
        private static readonly EnumerationOptions AllFilesRecursive = new()
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            MatchType = MatchType.Win32,
            ReturnSpecialDirectories = false
        };

        public static DateTime? GetLatestModifiedTimeUtc(string? directoryPath, Action<Exception>? onError = null)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return null;

            try
            {
                long latestTicks = Directory.GetLastWriteTimeUtc(directoryPath).Ticks;

                FileSystemEnumerable<long> fileTimes = new(
                    directoryPath,
                    static (ref FileSystemEntry entry) => entry.LastWriteTimeUtc.UtcTicks,
                    AllFilesRecursive)
                {
                    ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory
                };

                foreach (long ticks in fileTimes)
                {
                    if (ticks > latestTicks)
                        latestTicks = ticks;
                }

                return new DateTime(latestTicks, DateTimeKind.Utc);
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                return null;
            }
        }
    }
}
