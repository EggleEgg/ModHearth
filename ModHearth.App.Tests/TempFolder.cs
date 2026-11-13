namespace ModHearth.App.Tests
{
    /// <summary>
    /// Disposable temp directory for tests that need real files. Cleanup is best-effort so a locked
    /// artifact cannot fail the test that produced it, and write helpers create parent folders.
    /// </summary>
    internal sealed class TempFolder(string prefix) : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory(prefix.EndsWith('-') ? prefix : prefix + "-").FullName;

        public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

        public string CreateDirectory(string relativePath)
            => Directory.CreateDirectory(Combine(relativePath)).FullName;

        /// <summary>
        /// Writes <paramref name="contents"/> to <paramref name="relativePath"/> (creating parent
        /// folders) and returns the absolute path.
        /// </summary>
        public string Write(string relativePath, string contents)
        {
            string path = Combine(relativePath);
            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                _ = Directory.CreateDirectory(directory);

            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for a test artifact.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup for a test artifact.
            }
        }
    }
}
