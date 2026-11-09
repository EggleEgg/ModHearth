using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities
{
    internal enum BackupArchiveFormat
    {
        Zip,
        TarGz
    }

    internal sealed record BackupArchiveInfo(
        string ArchivePath,
        DateTimeOffset CreatedUtc,
        string ModHearthVersion,
        BackupArchiveFormat Format);

    internal sealed record BackupResult(bool Success, string Message)
    {
        public static BackupResult Failed(string message) => new(false, message);

        public static BackupResult Succeeded(string message) => new(true, message);
    }

    /// <summary>
    /// Creates and restores the ModHearth data backup archive. Both supported formats
    /// (<c>backup.zip</c> and <c>backup.tar.gz</c>) can be read on every platform, even though new
    /// archives are written in the platform's native format.
    /// </summary>
    internal static class BackupService
    {
        public const string ZipFileName = "backup.zip";
        public const string TarGzFileName = "backup.tar.gz";

        private const string ManifestEntryName = "backup-info.json";
        private const string ModManagerRelativePath = "dfhack-config/mod-manager.json";

        // Paths stored in the archive, relative to the ModHearth installation root.
        private static readonly (string RelativePath, bool IsDirectory)[] DataPathsToBackup =
        [
            ("config.json", false),
            ("modrules.json", false),
            ("metadata", true)
        ];

        public static BackupArchiveFormat GetDefaultFormat()
            => OperatingSystem.IsWindows() ? BackupArchiveFormat.Zip : BackupArchiveFormat.TarGz;

        public static string GetArchivePath(BackupArchiveFormat format, string? baseDir = null)
        {
            string directory = baseDir ?? AppContext.BaseDirectory;
            return Path.Combine(directory, format == BackupArchiveFormat.Zip ? ZipFileName : TarGzFileName);
        }

        /// <summary>
        /// Returns the existing backup archive, preferring the newest one when both formats are present.
        /// </summary>
        public static string? FindArchive(string? baseDir = null)
        {
            string directory = baseDir ?? AppContext.BaseDirectory;
            string zipPath = Path.Combine(directory, ZipFileName);
            string tarGzPath = Path.Combine(directory, TarGzFileName);

            bool zipExists = File.Exists(zipPath);
            bool tarGzExists = File.Exists(tarGzPath);

            if (zipExists && tarGzExists)
            {
                return File.GetLastWriteTimeUtc(zipPath) >= File.GetLastWriteTimeUtc(tarGzPath)
                    ? zipPath
                    : tarGzPath;
            }

            return zipExists ? zipPath : tarGzExists ? tarGzPath : null;
        }

        public static BackupArchiveFormat DetectFormat(string archivePath)
            => archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? BackupArchiveFormat.Zip
                : BackupArchiveFormat.TarGz;

        /// <summary>
        /// Reads the archive manifest. Archives created by older builds have no manifest, so the
        /// archive's own timestamp is used and the version is reported as unknown.
        /// </summary>
        public static BackupArchiveInfo? TryReadInfo(string archivePath)
        {
            if (!File.Exists(archivePath))
                return null;

            BackupArchiveFormat format = DetectFormat(archivePath);
            DateTimeOffset? createdUtc = null;
            string version = "unknown";

            try
            {
                BackupManifest? manifest = ReadManifest(archivePath, format);
                if (manifest != null)
                {
                    createdUtc = manifest.CreatedUtc;
                    if (!string.IsNullOrWhiteSpace(manifest.ModHearthVersion))
                        version = manifest.ModHearthVersion.Trim();
                }
            }
            catch (Exception ex)
            {
                Loggers.Backup.LogError($"Failed to read backup metadata from '{archivePath}': {ex.Message}");
            }

            createdUtc ??= new DateTimeOffset(File.GetLastWriteTimeUtc(archivePath), TimeSpan.Zero);
            return new BackupArchiveInfo(archivePath, createdUtc.Value, version, format);
        }

        /// <summary>
        /// Writes a fresh backup archive using the platform's native format and removes the
        /// opposite-format archive so restore always sees a single unambiguous backup.
        /// </summary>
        public static BackupResult CreateBackup(string? baseDir = null, string? modManagerConfigPath = null)
        {
            string directory = baseDir ?? AppContext.BaseDirectory;
            BackupArchiveFormat format = GetDefaultFormat();
            string archivePath = GetArchivePath(format, directory);
            string alternatePath = GetArchivePath(
                format == BackupArchiveFormat.Zip ? BackupArchiveFormat.TarGz : BackupArchiveFormat.Zip,
                directory);

            string tempBackupDir = Path.Combine(Path.GetTempPath(), $"ModHearth_backup_{Guid.NewGuid():N}");

            try
            {
                DeleteArchive(alternatePath);
                DeleteArchive(archivePath);

                _ = Directory.CreateDirectory(tempBackupDir);

                int entryCount = CollectBackupEntries(directory, tempBackupDir, modManagerConfigPath);
                if (entryCount == 0)
                    return BackupResult.Failed("There was nothing to back up.");

                WriteManifest(tempBackupDir);
                CreateArchive(tempBackupDir, archivePath, format);

                Loggers.Backup.Log($"Created backup archive '{archivePath}'.");
                return BackupResult.Succeeded($"Backup created at '{archivePath}'.");
            }
            catch (Exception ex)
            {
                Loggers.Backup.LogError($"Failed to create backup archive: {ex.Message}");
                return BackupResult.Failed(ex.Message);
            }
            finally
            {
                TryDeleteDirectory(tempBackupDir);
            }
        }

        /// <summary>
        /// Extracts a backup archive into the ModHearth installation and the configured DFHack
        /// mod-manager location. The caller is responsible for restarting the application.
        /// </summary>
        public static BackupResult RestoreBackup(
            string archivePath,
            string? baseDir = null,
            string? modManagerConfigPath = null)
        {
            if (!File.Exists(archivePath))
                return BackupResult.Failed($"Backup archive '{archivePath}' was not found.");

            string directory = baseDir ?? AppContext.BaseDirectory;
            string tempRestoreDir = Path.Combine(Path.GetTempPath(), $"ModHearth_restore_{Guid.NewGuid():N}");

            try
            {
                _ = Directory.CreateDirectory(tempRestoreDir);
                ExtractArchive(archivePath, tempRestoreDir, DetectFormat(archivePath));

                List<string> restoredEntries = [];
                List<string> warnings = [];

                RestoreFile(tempRestoreDir, "config.json", Path.Combine(directory, "config.json"), restoredEntries);
                RestoreFile(tempRestoreDir, "modrules.json", Path.Combine(directory, "modrules.json"), restoredEntries);

                string? metadataSource = PathHelper.ResolveChild(tempRestoreDir, "metadata", expectDirectory: true);
                if (metadataSource != null)
                {
                    CopyDirectory(metadataSource, Path.Combine(directory, "metadata"));
                    restoredEntries.Add("metadata/");
                }

                RestoreModManagerConfig(tempRestoreDir, modManagerConfigPath, restoredEntries, warnings);

                if (restoredEntries.Count == 0)
                    return BackupResult.Failed("The backup did not contain any recognized ModHearth data.");

                string message = $"Restored {string.Join(", ", restoredEntries)}.";
                if (warnings.Count > 0)
                    message += " " + string.Join(" ", warnings);

                Loggers.Backup.Log(message);
                return BackupResult.Succeeded(message);
            }
            catch (Exception ex)
            {
                Loggers.Backup.LogError($"Failed to restore backup '{archivePath}': {ex.Message}");
                return BackupResult.Failed(ex.Message);
            }
            finally
            {
                TryDeleteDirectory(tempRestoreDir);
            }
        }

        private static int CollectBackupEntries(string baseDir, string tempBackupDir, string? modManagerConfigPath)
        {
            int entryCount = 0;

            foreach ((string relativePath, bool isDirectory) in DataPathsToBackup)
            {
                string sourcePath = Path.Combine(baseDir, relativePath);
                if (isDirectory)
                {
                    if (!Directory.Exists(sourcePath))
                        continue;

                    CopyDirectory(sourcePath, Path.Combine(tempBackupDir, relativePath));
                    entryCount++;
                }
                else
                {
                    if (!File.Exists(sourcePath))
                        continue;

                    File.Copy(sourcePath, Path.Combine(tempBackupDir, relativePath), true);
                    entryCount++;
                }
            }

            string modManagerPath = string.IsNullOrWhiteSpace(modManagerConfigPath)
                ? ConfigManager.GetModManagerConfigPath()
                : modManagerConfigPath;

            if (!string.IsNullOrWhiteSpace(modManagerPath) && File.Exists(modManagerPath))
            {
                string destination = Path.Combine(tempBackupDir, ModManagerRelativePath);
                _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(modManagerPath, destination, true);
                entryCount++;
            }

            return entryCount;
        }

        private static void RestoreFile(
            string restoreDir,
            string fileName,
            string destinationPath,
            List<string> restoredEntries)
        {
            string? sourcePath = PathHelper.ResolveFile(restoreDir, fileName);
            if (sourcePath == null)
                return;

            _ = Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, true);
            restoredEntries.Add(fileName);
        }

        private static void RestoreModManagerConfig(
            string restoreDir,
            string? modManagerConfigPath,
            List<string> restoredEntries,
            List<string> warnings)
        {
            string? sourcePath = PathHelper.ResolveChild(
                Path.Combine(restoreDir, "dfhack-config"),
                "mod-manager.json",
                expectDirectory: false);
            if (sourcePath == null)
                return;

            string destinationPath = string.IsNullOrWhiteSpace(modManagerConfigPath)
                ? ConfigManager.GetModManagerConfigPath()
                : modManagerConfigPath;

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                warnings.Add("The DFHack mod-manager.json was not restored because the Dwarf Fortress folder is not configured.");
                return;
            }

            _ = Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, true);
            restoredEntries.Add(ModManagerRelativePath);
        }

        private static void WriteManifest(string tempBackupDir)
        {
            BackupManifest manifest = new()
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                ModHearthVersion = ModHearthManager.GetBuildVersionString()
            };

            _ = JsonFileStore.TrySave(
                Path.Combine(tempBackupDir, ManifestEntryName),
                manifest,
                "Backup manifest");
        }

        private static BackupManifest? ReadManifest(string archivePath, BackupArchiveFormat format)
        {
            byte[]? manifestBytes = format == BackupArchiveFormat.Zip
                ? ReadZipEntry(archivePath, ManifestEntryName)
                : ReadTarEntry(archivePath, ManifestEntryName);

            if (manifestBytes == null || manifestBytes.Length == 0)
                return null;

            return JsonSerializer.Deserialize<BackupManifest>(manifestBytes, JsonFileStore.CaseInsensitive);
        }

        private static byte[]? ReadZipEntry(string archivePath, string entryName)
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
                candidate => string.Equals(NormalizeEntryName(candidate.FullName), entryName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                return null;

            using Stream stream = entry.Open();
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private static byte[]? ReadTarEntry(string archivePath, string entryName)
        {
            using FileStream fileStream = File.OpenRead(archivePath);
            using GZipStream gzip = new(fileStream, CompressionMode.Decompress);
            using TarReader reader = new(gzip);

            while (reader.GetNextEntry() is TarEntry entry)
            {
                if (!string.Equals(NormalizeEntryName(entry.Name), entryName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.DataStream == null)
                    return null;

                using MemoryStream buffer = new();
                entry.DataStream.CopyTo(buffer);
                return buffer.ToArray();
            }

            return null;
        }

        private static void CreateArchive(string sourceDir, string archivePath, BackupArchiveFormat format)
        {
            if (format == BackupArchiveFormat.Zip)
            {
                ZipFile.CreateFromDirectory(sourceDir, archivePath, CompressionLevel.Optimal, false);
                return;
            }

            using FileStream fileStream = File.Create(archivePath);
            using GZipStream gzip = new(fileStream, CompressionMode.Compress);
            TarFile.CreateFromDirectory(sourceDir, gzip, includeBaseDirectory: false);
        }

        private static void ExtractArchive(string archivePath, string destinationDir, BackupArchiveFormat format)
        {
            if (format == BackupArchiveFormat.Zip)
            {
                ZipFile.ExtractToDirectory(archivePath, destinationDir, true);
                return;
            }

            ExtractTarGz(archivePath, destinationDir);
        }

        private static void ExtractTarGz(string archivePath, string destinationDir)
        {
            using FileStream fileStream = File.OpenRead(archivePath);
            using GZipStream gzip = new(fileStream, CompressionMode.Decompress);
            using TarReader reader = new(gzip);

            while (reader.GetNextEntry() is TarEntry entry)
            {
                // Some tar writers emit an explicit root entry; it has nothing to extract.
                if (NormalizeEntryName(entry.Name).Length == 0)
                    continue;

                string destinationPath = GetSafeExtractionPath(destinationDir, entry.Name);

                if (entry.EntryType == TarEntryType.Directory)
                {
                    _ = Directory.CreateDirectory(destinationPath);
                    continue;
                }

                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    continue;

                _ = Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                using Stream entryStream = entry.DataStream ?? Stream.Null;
                using FileStream output = File.Create(destinationPath);
                entryStream.CopyTo(output);
            }
        }

        private static string GetSafeExtractionPath(string destinationDir, string entryName)
        {
            string root = Path.GetFullPath(destinationDir);
            string destinationPath = Path.GetFullPath(Path.Combine(root, entryName));

            if (!PathHelper.IsUnderRoot(destinationPath, root, includeRoot: false))
                throw new InvalidDataException($"Backup archive contains an unsafe path '{entryName}'.");

            return destinationPath;
        }

        private static string NormalizeEntryName(string entryName)
            => entryName.Replace('\\', '/').TrimStart('/', '.').TrimStart('/');

        private static void DeleteArchive(string archivePath)
        {
            if (!File.Exists(archivePath))
                return;

            try
            {
                File.Delete(archivePath);
            }
            catch (Exception ex)
            {
                Loggers.Backup.LogError($"Failed to delete stale backup archive '{archivePath}': {ex.Message}");
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            _ = Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);

            foreach (string directory in Directory.GetDirectories(sourceDir))
                CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }

        private static void TryDeleteDirectory(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                Loggers.Backup.LogError($"Failed to delete temporary backup directory '{directory}': {ex.Message}");
            }
        }

        private sealed class BackupManifest
        {
            public DateTimeOffset CreatedUtc { get; set; }

            public string ModHearthVersion { get; set; } = string.Empty;
        }
    }
}
