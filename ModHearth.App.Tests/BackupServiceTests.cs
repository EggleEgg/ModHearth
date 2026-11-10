using System.Formats.Tar;
using System.IO.Compression;
using ModHearth.Utilities;
using Xunit;

namespace ModHearth.App.Tests
{
    public sealed class BackupServiceTests : IDisposable
    {
        private readonly string root;

        public BackupServiceTests()
        {
            root = Path.Combine(Path.GetTempPath(), $"ModHearthBackupTests_{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(root);
        }

        [Fact]
        public void CreateBackup_WritesArchiveWithMetadataAndRestoresData()
        {
            string sourceDir = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            File.WriteAllText(Path.Combine(sourceDir, "config.json"), /*lang=json,strict*/ "{\"theme\":1}");
            File.WriteAllText(Path.Combine(sourceDir, "modrules.json"), /*lang=json,strict*/ "{\"rules\":[]}");
            string metadataDir = Directory.CreateDirectory(Path.Combine(sourceDir, "metadata", "test_mod")).FullName;
            File.WriteAllText(Path.Combine(metadataDir, "info.json"), /*lang=json,strict*/ "{\"id\":\"test_mod\"}");

            string modManagerSource = Path.Combine(root, "mod-manager.json");
            File.WriteAllText(modManagerSource, /*lang=json,strict*/ "{\"mods\":[]}");

            BackupResult result = BackupService.CreateBackup(sourceDir, modManagerSource);
            Assert.True(result.Success, result.Message);

            string? archivePath = BackupService.FindArchive(sourceDir);
            Assert.NotNull(archivePath);

            BackupArchiveInfo? info = BackupService.TryReadInfo(archivePath);
            Assert.NotNull(info);
            Assert.Equal(ModHearthManager.GetBuildVersionString(), info.ModHearthVersion);
            Assert.True(info.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(-5));

            string restoreDir = Path.Combine(root, "restore");
            string modManagerTarget = Path.Combine(root, "restored-df", "dfhack-config", "mod-manager.json");

            BackupResult restoreResult = BackupService.RestoreBackup(archivePath, restoreDir, modManagerTarget);
            Assert.True(restoreResult.Success, restoreResult.Message);

            Assert.Equal(/*lang=json,strict*/ "{\"theme\":1}", File.ReadAllText(Path.Combine(restoreDir, "config.json")));
            Assert.Equal(/*lang=json,strict*/ "{\"rules\":[]}", File.ReadAllText(Path.Combine(restoreDir, "modrules.json")));
            Assert.Equal(/*lang=json,strict*/ "{\"id\":\"test_mod\"}",
                File.ReadAllText(Path.Combine(restoreDir, "metadata", "test_mod", "info.json")));
            Assert.Equal(/*lang=json,strict*/ "{\"mods\":[]}", File.ReadAllText(modManagerTarget));
        }

        [Theory]
        [InlineData(BackupArchiveFormat.Zip)]
        [InlineData(BackupArchiveFormat.TarGz)]
        public void RestoreBackup_ReadsBothArchiveFormats(BackupArchiveFormat format)
        {
            string stagingDir = Directory.CreateDirectory(Path.Combine(root, "staging")).FullName;
            File.WriteAllText(Path.Combine(stagingDir, "config.json"), /*lang=json,strict*/ "{\"theme\":0}");
            File.WriteAllText(Path.Combine(stagingDir, "modrules.json"), "{}");
            string metadataDir = Directory.CreateDirectory(Path.Combine(stagingDir, "metadata")).FullName;
            File.WriteAllText(Path.Combine(metadataDir, "data.txt"), "metadata");
            string dfhackConfigDir = Directory.CreateDirectory(Path.Combine(stagingDir, "dfhack-config")).FullName;
            File.WriteAllText(Path.Combine(dfhackConfigDir, "mod-manager.json"), /*lang=json,strict*/ "{\"mods\":[\"a\"]}");

            string archivePath = Path.Combine(root, format == BackupArchiveFormat.Zip ? "backup.zip" : "backup.tar.gz");
            if (format == BackupArchiveFormat.Zip)
            {
                ZipFile.CreateFromDirectory(stagingDir, archivePath, CompressionLevel.Optimal, false);
            }
            else
            {
                using FileStream fileStream = File.Create(archivePath);
                using GZipStream gzip = new(fileStream, CompressionMode.Compress);
                TarFile.CreateFromDirectory(stagingDir, gzip, includeBaseDirectory: false);
            }

            string restoreDir = Path.Combine(root, "restore_" + format);
            string modManagerTarget = Path.Combine(root, "restored-df_" + format, "dfhack-config", "mod-manager.json");

            BackupResult result = BackupService.RestoreBackup(archivePath, restoreDir, modManagerTarget);
            Assert.True(result.Success, result.Message);

            Assert.Equal(/*lang=json,strict*/ "{\"theme\":0}", File.ReadAllText(Path.Combine(restoreDir, "config.json")));
            Assert.Equal("{}", File.ReadAllText(Path.Combine(restoreDir, "modrules.json")));
            Assert.Equal("metadata", File.ReadAllText(Path.Combine(restoreDir, "metadata", "data.txt")));
            Assert.Equal(/*lang=json,strict*/ "{\"mods\":[\"a\"]}", File.ReadAllText(modManagerTarget));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // Test cleanup must not mask the assertion result.
            }
        }
    }
}
