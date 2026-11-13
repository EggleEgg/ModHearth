using System.Security.Cryptography;
using System.Text;
using ModHearth.Utils.IO;

namespace ModHearth.Utils.Logging
{
    public static partial class ModUpdateLogger
    {
        private static bool IsLocalSource(string? sourceType) => string.Equals(sourceType?.Trim(), "Local", StringComparison.OrdinalIgnoreCase);

        internal static string BuildLocalQuickStamp(string? modPath)
        {
            string normalizedPath = ResolveCanonicalPath(modPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
                return string.Empty;

            try
            {
                long dirTicks = Directory.GetLastWriteTimeUtc(normalizedPath).Ticks;
                string infoPath = Path.Combine(normalizedPath, "info.txt");
                long infoTicks = 0;
                long infoSize = 0;
                if (File.Exists(infoPath))
                {
                    FileInfo info = new(infoPath);
                    infoTicks = info.LastWriteTimeUtc.Ticks;
                    infoSize = info.Length;
                }

                return $"{dirTicks}:{infoTicks}:{infoSize}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildLocalDeepStamp(string? modPath)
        {
            string normalizedPath = ResolveCanonicalPath(modPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
                return string.Empty;

            try
            {
                List<string> metadata = [];
                foreach (string filePath in Directory.EnumerateFiles(normalizedPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fileInfo = new(filePath);
                        string relativePath = Path.GetRelativePath(normalizedPath, filePath);
                        if (Path.IsPathRooted(relativePath) && !relativePath.StartsWith("..", StringComparison.Ordinal))
                        {
                            relativePath = Path.GetFileName(filePath);
                        }
                        relativePath = relativePath
                            .Replace('\\', '/')
                            .Trim();
                        metadata.Add($"{relativePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}");
                    }
                    catch (Exception ex)
                    {
                        // Continue fingerprinting; one unreadable file must not abort the scan.
                        Loggers.Diag.Dev($"Skipped inaccessible file while fingerprinting: {ex.Message}");
                    }
                }

                StringComparer comparer = PathHelper.Comparer;
                metadata.Sort(comparer);

                using SHA256 sha256 = SHA256.Create();
                foreach (string line in metadata)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                    _ = sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }

                _ = sha256.TransformFinalBlock([], 0, 0);
                return Convert.ToHexString(sha256.Hash ?? []);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
