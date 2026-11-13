using ModHearth.Utils.Logging;

namespace ModHearth.Utils.IO
{
    /// <summary>
    /// Shared file-system operations used by backups, workshop downloads and log migration. Failures
    /// are logged here instead of escaping into callers that can only ignore them.
    /// </summary>
    internal static class FileSystemHelper
    {
        /// <summary>
        /// Deletes <paramref name="path"/> recursively when it exists. Returns false only when the
        /// deletion was attempted and failed (missing path counts as success).
        /// </summary>
        public static bool TryDeleteDirectory(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return true;

            try
            {
                Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex)
            {
                Loggers.Storage.LogError($"Failed to delete {label} '{path}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Recursively copies <paramref name="sourceDir"/> into <paramref name="destinationDir"/>,
        /// overwriting existing files.
        /// </summary>
        public static void CopyDirectory(string sourceDir, string destinationDir)
        {
            _ = Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);

            foreach (string directory in Directory.GetDirectories(sourceDir))
                CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }

        /// <summary>
        /// Moves a file, falling back to copy+delete for cross-volume moves (which throw
        /// <see cref="IOException"/>).
        /// </summary>
        public static void MoveFile(string sourcePath, string destinationPath)
        {
            try
            {
                File.Move(sourcePath, destinationPath);
            }
            catch (IOException)
            {
                File.Copy(sourcePath, destinationPath, true);
                File.Delete(sourcePath);
            }
        }

        /// <summary>
        /// Moves a directory, falling back to copy+delete for cross-volume moves.
        /// </summary>
        public static void MoveDirectory(string sourceDir, string destinationDir)
        {
            try
            {
                Directory.Move(sourceDir, destinationDir);
            }
            catch (IOException)
            {
                CopyDirectory(sourceDir, destinationDir);
                Directory.Delete(sourceDir, true);
            }
        }
    }
}
