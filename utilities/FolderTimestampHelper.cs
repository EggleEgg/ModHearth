namespace ModHearth.Utilities;

/// <summary>
/// Returns the latest last-write time for a directory and its files.
/// </summary>
internal static class FolderTimestampHelper
{
    public static DateTime? GetLatestModifiedTimeUtc(string? directoryPath, Action<Exception>? onError = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return null;

        try
        {
            DateTime latest = Directory.GetLastWriteTimeUtc(directoryPath);
            foreach (string file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                DateTime fileTime = File.GetLastWriteTimeUtc(file);
                if (fileTime > latest)
                    latest = fileTime;
            }
            return latest;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return null;
        }
    }
}
