using ModHearth.RawIssueAudit;
using Xunit;

namespace ModHearth.App.Tests
{
    public class RawIssueAuditAutomationTests
    {
        [Fact]
        public void PathResolverAllowsMissingErrorLogOnlyForAutomation()
        {
            using TempAutomationFixture fixture = TempAutomationFixture.Create();
            AuditOptions options = fixture.Options();

            List<string> warnings = [];
            AuditPathSet paths = AuditPathResolver.Resolve(
                options,
                warnings,
                allowMissingErrorLog: true);

            Assert.Equal(Path.GetFullPath(fixture.ErrorLogPath), paths.ErrorLogPath);
            Assert.False(File.Exists(paths.ErrorLogPath));

            _ = Assert.Throws<AuditInputException>(
                () => AuditPathResolver.Resolve(options, warnings));
        }

        [Fact]
        public void PathResolverReadsDfHackFolderFromConfig()
        {
            using TempAutomationFixture fixture = TempAutomationFixture.Create();
            File.WriteAllText(fixture.ErrorLogPath, string.Empty);
            AuditOptions options = fixture.Options(useConfig: true);

            List<string> warnings = [];
            AuditPathSet paths = AuditPathResolver.Resolve(options, warnings);

            Assert.Equal(Path.GetFullPath(fixture.DfPath), paths.DwarfFortressPath);
            Assert.Equal(Path.GetFullPath(fixture.DfHackPath), paths.DfHackPath);
        }

        [Fact]
        public void StatusParserRecognizesDoneFailureAndProgress()
        {
            WorldgenAutomationStatus done = WorldgenAutomationRunner.ParseStatus(
                ["START", "TICK opening title=true", "DONE"]);

            Assert.Equal(WorldgenAutomationOutcome.Done, done.Outcome);
            Assert.Null(done.Error);
            Assert.Equal("TICK opening title=true", done.LastStatus);

            WorldgenAutomationStatus failed = WorldgenAutomationRunner.ParseStatus(
                ["START", "TICK loading", "ERROR: the Create world button did not respond"]);

            Assert.Equal(WorldgenAutomationOutcome.Failed, failed.Outcome);
            Assert.Equal("the Create world button did not respond", failed.Error);

            WorldgenAutomationStatus running = WorldgenAutomationRunner.ParseStatus(
                ["START", "TICK opening title=true"]);

            Assert.Equal(WorldgenAutomationOutcome.Running, running.Outcome);
            Assert.Equal("TICK opening title=true", running.LastStatus);
        }

        [Theory]
        [InlineData("STATE=starting", "Starting", null)]
        [InlineData("noise\nSTATE=title\n", "Title", null)]
        [InlineData("STATE=worldgen", "WorldGen", null)]
        [InlineData("STATE=worldgen-active", "WorldGenActive", null)]
        [InlineData("STATE=game", "Game", null)]
        public void StateParserRecognizesKnownScreens(
            string output,
            string expectedState,
            string? expectedScreen)
        {
            DfScreenProbe probe = WorldgenAutomationRunner.ParseState(output);

            Assert.Equal(Enum.Parse<DfScreenState>(expectedState), probe.State);
            Assert.Equal(expectedScreen, probe.ScreenName);
        }

        [Fact]
        public void StateParserKeepsUnknownScreenNameForDiagnostics()
        {
            DfScreenProbe probe = WorldgenAutomationRunner.ParseState(
                "STATE=other:<type: viewscreen_game_cleanerst>");

            Assert.Equal(DfScreenState.Other, probe.State);
            Assert.Equal("<type: viewscreen_game_cleanerst>", probe.ScreenName);
        }

        [Fact]
        public void BackupErrorLogCopiesAndClearsTheOriginal()
        {
            using TempAutomationFixture fixture = TempAutomationFixture.Create();
            _ = Directory.CreateDirectory(Path.GetDirectoryName(fixture.ErrorLogPath)!);
            File.WriteAllText(fixture.ErrorLogPath, "old diagnostic");

            string? backupPath = WorldgenAutomationRunner.BackupErrorLog(fixture.ErrorLogPath);

            Assert.NotNull(backupPath);
            Assert.Equal("errorlog.audit-backup.txt", Path.GetFileName(backupPath));
            Assert.Equal("old diagnostic", File.ReadAllText(backupPath));
            Assert.True(File.Exists(fixture.ErrorLogPath));
            Assert.Equal(0, new FileInfo(fixture.ErrorLogPath).Length);

            Assert.Null(WorldgenAutomationRunner.BackupErrorLog(fixture.ErrorLogPath));
        }

        [Fact]
        public void GeneratedWorldCleanupOnlyDeletesNewRegionFolders()
        {
            string root = Directory.CreateTempSubdirectory("modhearth-audit-saves-").FullName;
            try
            {
                string dfPath = Path.Combine(root, "Dwarf Fortress");
                string savePath = Path.Combine(dfPath, "save");
                string oldRegion = Path.Combine(savePath, "region1");
                string current = Path.Combine(savePath, "current");
                _ = Directory.CreateDirectory(oldRegion);
                _ = Directory.CreateDirectory(current);
                File.WriteAllText(Path.Combine(savePath, "notes.txt"), "keep");

                HashSet<string>? snapshot =
                    WorldgenAutomationRunner.TrySnapshotWorldSaves(dfPath, []);
                Assert.NotNull(snapshot);

                string newRegion = Path.Combine(savePath, "region2");
                _ = Directory.CreateDirectory(newRegion);
                List<string> warningList = [];
                IReadOnlyList<string> removed =
                    WorldgenAutomationRunner.RemoveGeneratedWorldSaves(dfPath, snapshot, warningList);

                Assert.Equal([newRegion], removed);
                Assert.False(Directory.Exists(newRegion));
                Assert.True(Directory.Exists(oldRegion));
                Assert.True(Directory.Exists(current));
                Assert.True(File.Exists(Path.Combine(savePath, "notes.txt")));
                Assert.Contains(
                    warningList,
                    warning => warning.Contains("Removed generated world save", StringComparison.Ordinal));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void GeneratedWorldCleanupIsSkippedWhenSnapshotFails()
        {
            string root = Directory.CreateTempSubdirectory("modhearth-audit-saves-").FullName;
            try
            {
                string dfPath = Path.Combine(root, "Dwarf Fortress");
                string savePath = Path.Combine(dfPath, "save");
                string region = Path.Combine(savePath, "region1");
                _ = Directory.CreateDirectory(region);

                List<string> warnings = [];
                IReadOnlyList<string> removed =
                    WorldgenAutomationRunner.RemoveGeneratedWorldSaves(dfPath, null, warnings);

                Assert.Empty(removed);
                Assert.True(Directory.Exists(region));
                Assert.Empty(warnings);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void WaitForErrorLogCreatesAnEmptyFileWhenDfWroteNothing()
        {
            using TempAutomationFixture fixture = TempAutomationFixture.Create();
            List<string> warnings = [];

            WorldgenAutomationRunner.WaitForErrorLog(
                fixture.ErrorLogPath,
                warnings,
                TimeSpan.FromMilliseconds(100));

            Assert.True(File.Exists(fixture.ErrorLogPath));
            Assert.Equal(0, new FileInfo(fixture.ErrorLogPath).Length);
            Assert.Contains(warnings, warning => warning.Contains("created an empty log", StringComparison.Ordinal));
        }

        [Fact]
        public void DfHackRunPathFindsTheHackSubfolderAndSiblingDfHackInstall()
        {
            string root = Directory.CreateTempSubdirectory("modhearth-audit-dfhack-").FullName;
            try
            {
                string dfPath = Path.Combine(root, "Dwarf Fortress");
                string hackPath = Path.Combine(dfPath, "hack");
                _ = Directory.CreateDirectory(hackPath);

                string executableName = OperatingSystem.IsWindows() ? "dfhack-run.exe" : "dfhack-run";
                string expected = Path.Combine(hackPath, executableName);
                File.WriteAllText(expected, string.Empty);

                AuditPathSet paths = Paths(dfPath);
                Assert.Equal(
                    Path.GetFullPath(expected),
                    WorldgenAutomationRunner.ResolveDfHackRunPath(paths));

                // Prefer the DF-local copy, then prove the sibling Steam-style layout is found.
                File.Delete(expected);

                string siblingHack = Path.Combine(root, "DFHack", "hack");
                _ = Directory.CreateDirectory(siblingHack);
                string sibling = Path.Combine(siblingHack, executableName);
                File.WriteAllText(sibling, string.Empty);

                Assert.Equal(
                    Path.GetFullPath(sibling),
                    WorldgenAutomationRunner.ResolveDfHackRunPath(paths));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
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

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
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

        private static AuditPathSet Paths(string dfPath) => new(
            Path.Combine(dfPath, "errorlog.txt"),
            Path.Combine(dfPath, "cache.json"),
            Path.Combine(dfPath, "data", "vanilla"),
            null,
            null,
            dfPath,
            null);

        private sealed class TempAutomationFixture : IDisposable
        {
            private readonly TempFolder temp;

            private TempAutomationFixture(TempFolder temp)
            {
                this.temp = temp;
                DfPath = temp.Combine("Dwarf Fortress");
                DfHackPath = temp.Combine("DFHack", "hack");
                CachePath = temp.Combine("metadata", "mod_raw_dependency_cache.json");
                VanillaPath = temp.Combine("Dwarf Fortress", "data", "vanilla");
                ErrorLogPath = temp.Combine("Dwarf Fortress", "errorlog.txt");
                ConfigPath = temp.Combine("config.json");
            }

            public string Root => temp.Path;
            public string DfPath { get; }
            public string DfHackPath { get; }
            public string CachePath { get; }
            public string VanillaPath { get; }
            public string ErrorLogPath { get; }
            public string ConfigPath { get; }

            public static TempAutomationFixture Create()
            {
                TempFolder temp = new("modhearth-raw-audit-automation");
                TempAutomationFixture fixture = new(temp);

                try
                {
                    _ = Directory.CreateDirectory(fixture.DfPath);
                    _ = Directory.CreateDirectory(fixture.DfHackPath);
                    _ = Directory.CreateDirectory(fixture.VanillaPath);
                    _ = Directory.CreateDirectory(Path.GetDirectoryName(fixture.CachePath)!);
                    File.WriteAllText(fixture.CachePath, "{}");
                    File.WriteAllText(
                        fixture.ConfigPath,
                        $$"""
                        {
                          "DFFolderPath": {{System.Text.Json.JsonSerializer.Serialize(fixture.DfPath)}},
                          "DFFolderPathOverride": "",
                          "DFHackFolderPath": {{System.Text.Json.JsonSerializer.Serialize(fixture.DfHackPath)}}
                        }
                        """);
                    return fixture;
                }
                catch
                {
                    fixture.Dispose();
                    throw;
                }
            }

            public AuditOptions Options(bool useConfig = false)
            {
                List<string> args =
                [
                    "--cache", CachePath,
                    "--vanilla", VanillaPath,
                    "--errorlog", ErrorLogPath,
                    "--all-mods"
                ];

                if (useConfig)
                {
                    args.Add("--config");
                    args.Add(ConfigPath);
                }
                else
                {
                    args.Add("--df");
                    args.Add(DfPath);
                }

                AuditOptions options = AuditOptions.Parse([.. args], out string? error);
                Assert.Null(error);
                return options;
            }

            public void Dispose() => temp.Dispose();
        }
    }
}
