using System.Text;
using System.Text.Json;
using ModHearth.Models;
using ModHearth.RawIssueAudit;
using ModHearth.Utils.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// End-to-end coverage for the development-only raw issue audit: a synthetic raw cache,
    /// vanilla baseline, modlist, and errorlog are fed through the same runner the console tool
    /// uses. This keeps the cache/modlist wiring and the production analyzers under test without
    /// requiring a Dwarf Fortress installation.
    /// </summary>
    public class RawIssueAuditRunnerTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        [Fact]
        public void RunnerMatchesPredictionAndNormalizesDottedCacheVersion()
        {
            using TempAuditFixture fixture = TempAuditFixture.Create(
                cache:
                [
                    Info(
                        "mod_entity",
                        version: "0.96",
                        definitions: ["ENTITY:TEST_ENTITY"],
                        permittedReactions: ["MISSING_REACTION"])
                ],
                mods: [("mod_entity", 96)],
                errorLog:
                [
                    "*** Error(s) finalizing the entity TEST_ENTITY",
                    "Unrecognized permitted reaction token: MISSING_REACTION"
                ]);

            AuditReport report = AuditRunner.Run(Options(fixture));

            Assert.Equal(0, report.Summary.MismatchCount);
            Assert.Equal(1, report.Summary.PredictedComparable);
            Assert.Equal(1, report.Summary.PredictedModCount);
            Assert.Equal(1, report.Summary.ReportedPairCount);
            Assert.Equal(1, report.Summary.ReportedLineCount);
            Assert.Equal(1, report.Summary.MatchedPairCount);
            Assert.Equal(1, report.Summary.MatchedLineCount);
            Assert.Equal(1, report.Summary.ConsideredLineCount);
            Assert.Equal(1, report.Summary.AttributedLineCount);
            Assert.Equal(0, report.Summary.UnattributedLineCount);
            Assert.Equal(1, report.Inputs.ActiveModCount);
            Assert.Equal(5, report.Inputs.CacheSchemaVersion);
            Assert.Equal("default", report.Inputs.ModListName);

            MatchedReference match = Assert.Single(report.Matches);
            Assert.Equal("mod_entity", match.ModId);
            Assert.Equal(ModPossibleIssueKind.MissingPermittedReaction, match.Kind);
            Assert.Equal("REACTION:MISSING_REACTION", match.TargetKey);
            Assert.DoesNotContain(
                report.Warnings,
                warning => warning.Contains("is not in the raw cache", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void RunnerReportsBothMismatchDirections()
        {
            using TempAuditFixture fixture = TempAuditFixture.Create(
                cache:
                [
                    Info(
                        "mod_entity",
                        definitions: ["ENTITY:TEST_ENTITY"],
                        permittedReactions: ["PREDICTED_ONLY"])
                ],
                mods: [("mod_entity", 1)],
                errorLog:
                [
                    "*** Error(s) finalizing the entity TEST_ENTITY",
                    "Unrecognized permitted reaction token: REPORTED_ONLY"
                ]);

            AuditReport report = AuditRunner.Run(Options(fixture));

            Assert.Equal(2, report.Summary.MismatchCount);
            Assert.Equal(1, report.Summary.PredictedOnlyCount);
            Assert.Equal(1, report.Summary.ReportedOnlyCount);
            Assert.Equal(0, report.Summary.MatchedPairCount);

            ReferenceMismatch predictedOnly = Assert.Single(
                report.Mismatches,
                mismatch => mismatch.Status == AuditMismatchStatus.PredictedNotReported);
            Assert.Equal("REACTION:PREDICTED_ONLY", predictedOnly.TargetKey);

            ReferenceMismatch reportedOnly = Assert.Single(
                report.Mismatches,
                mismatch => mismatch.Status == AuditMismatchStatus.ReportedNotPredicted);
            Assert.Equal("REACTION:REPORTED_ONLY", reportedOnly.TargetKey);
            Assert.Equal(1, reportedOnly.ReportedLineCount);
        }

        [Fact]
        public void RunnerKeepsUnverifiableSelectCutPredictionsOutOfMismatches()
        {
            using TempAuditFixture fixture = TempAuditFixture.Create(
                cache:
                [
                    Info(
                        "patcher",
                        definitions: ["ENTITY:PATCHER_ENTITY"],
                        selects: ["CREATURE:MISSING_SELECT"],
                        cuts: ["CREATURE:MISSING_CUT"])
                ],
                mods: [("patcher", 1)],
                errorLog: []);

            AuditReport report = AuditRunner.Run(Options(fixture));

            Assert.Equal(0, report.Summary.MismatchCount);
            Assert.Equal(2, report.Summary.PredictedUnverifiable);
            Assert.Equal(2, report.UnverifiablePredictions.Count);
            Assert.Contains(
                report.UnverifiablePredictions,
                prediction => prediction.Kind == ModPossibleIssueKind.MissingCutTarget
                    && prediction.TargetKey == "CREATURE:MISSING_CUT");
        }

        [Fact]
        public void RunnerRejectsAnEmptyModListWithoutAllMods()
        {
            using TempAuditFixture fixture = TempAuditFixture.Create(
                cache: [Info("mod_entity", definitions: ["ENTITY:TEST_ENTITY"])],
                mods: [("mod_entity", 1)],
                errorLog: []);

            File.WriteAllText(fixture.ModListPath, "[]");

            AuditInputException exception = Assert.Throws<AuditInputException>(
                () => AuditRunner.Run(Options(fixture)));

            Assert.Contains("contains no mod packs", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void RunnerUsesAllCachedModsWithAllModsWhenModListIsEmpty()
        {
            using TempAuditFixture fixture = TempAuditFixture.Create(
                cache: [Info("mod_entity", definitions: ["ENTITY:TEST_ENTITY"])],
                mods: [("mod_entity", 1)],
                errorLog: []);

            File.WriteAllText(fixture.ModListPath, "[]");

            AuditReport report = AuditRunner.Run(Options(fixture, "--all-mods"));

            Assert.Equal(1, report.Inputs.ActiveModCount);
            Assert.Equal("all cached mods (--all-mods)", report.Inputs.ActiveModSource);
        }

        [Fact]
        public void AutomationRequiresAModList()
        {
            using TempAuditFixture fixture = TempAuditFixture.Create(
                cache: [Info("mod_entity", definitions: ["ENTITY:TEST_ENTITY"])],
                mods: [("mod_entity", 1)],
                errorLog: []);

            File.Delete(fixture.ModListPath);

            AuditOptions options = AuditOptions.Parse(
            [
                "--config", fixture.ConfigPath,
                "--cache", fixture.CachePath,
                "--vanilla", fixture.VanillaPath,
                "--errorlog", fixture.ErrorLogPath,
                "--automate"
            ], out string? error);
            Assert.Null(error);

            AuditInputException exception = Assert.Throws<AuditInputException>(
                () => AuditRunner.Run(options));

            Assert.Contains("mod-manager.json", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void RunnerRejectsUnsupportedCacheSchema()
        {
            using TempAuditFixture fixture = TempAuditFixture.Create(
                cache: [Info("mod_entity", definitions: ["ENTITY:TEST_ENTITY"])],
                mods: [("mod_entity", 1)],
                errorLog: [],
                schemaVersion: 3);

            AuditInputException exception = Assert.Throws<AuditInputException>(
                () => AuditRunner.Run(Options(fixture)));

            Assert.Contains("schema 3 is not supported", exception.Message, StringComparison.Ordinal);
        }

        private static AuditOptions Options(TempAuditFixture fixture, params string[] extra)
        {
            AuditOptions options = AuditOptions.Parse(
            [
                "--config", fixture.ConfigPath,
                "--cache", fixture.CachePath,
                "--vanilla", fixture.VanillaPath,
                "--errorlog", fixture.ErrorLogPath,
                "--modlist", fixture.ModListPath,
                .. extra
            ], out string? error);

            Assert.Null(error);
            return options;
        }

        private static ModRawDependencyInfo Info(
            string modId,
            string version = "1",
            string[]? definitions = null,
            string[]? selects = null,
            string[]? cuts = null,
            string[]? permittedReactions = null) => new()
            {
                ModId = modId,
                NumericVersion = version,
                DirectDefinitionIds = [.. definitions ?? []],
                SelectTargetIds = [.. selects ?? []],
                CutTargetIds = [.. cuts ?? []],
                PermittedReactionIds = [.. permittedReactions ?? []]
            };

        private sealed class TempAuditFixture : IDisposable
        {
            private readonly TempFolder temp;

            private TempAuditFixture(TempFolder temp)
            {
                this.temp = temp;
                ConfigPath = temp.Combine("config.json");
                CachePath = temp.Combine("metadata", "mod_raw_dependency_cache.json");
                VanillaPath = temp.Combine("vanilla");
                ErrorLogPath = temp.Combine("df", "errorlog.txt");
                ModListPath = temp.Combine("df", "dfhack-config", "mod-manager.json");
            }

            public string Root => temp.Path;
            public string ConfigPath { get; }
            public string CachePath { get; }
            public string VanillaPath { get; }
            public string ErrorLogPath { get; }
            public string ModListPath { get; }

            public static TempAuditFixture Create(
                IReadOnlyList<ModRawDependencyInfo> cache,
                IReadOnlyList<(string Id, int Version)> mods,
                IReadOnlyList<string> errorLog,
                int schemaVersion = 5)
            {
                TempFolder temp = new("modhearth-raw-audit");
                TempAuditFixture fixture = new(temp);

                try
                {
                    fixture.WriteConfig();
                    fixture.WriteVanilla();
                    fixture.WriteCache(cache, schemaVersion);
                    fixture.WriteModList(mods);
                    fixture.WriteErrorLog(errorLog);
                    return fixture;
                }
                catch
                {
                    fixture.Dispose();
                    throw;
                }
            }

            public void Dispose() => temp.Dispose();

            private void WriteConfig()
                => Write(ConfigPath, """{ "DFFolderPath": "", "DFFolderPathOverride": "" }""");

            private void WriteVanilla()
                => Write(
                    Path.Combine(VanillaPath, "vanilla_core", "objects", "objects.txt"),
                    """
                    [OBJECT:REACTION]
                    [REACTION:VANILLA_REACTION]

                    [OBJECT:CREATURE]
                    [CREATURE:VANILLA_CREATURE]
                    """);

            private void WriteCache(IReadOnlyList<ModRawDependencyInfo> cache, int schemaVersion)
                => Write(
                    CachePath,
                    JsonSerializer.Serialize(
                        new { SchemaVersion = schemaVersion, Entries = cache },
                        JsonOptions));

            private void WriteModList(IReadOnlyList<(string Id, int Version)> mods)
            {
                object[] packs =
                [
                    new
                    {
                        @default = true,
                        name = "default",
                        modlist = mods
                            .Select(mod => new { id = mod.Id, version = mod.Version })
                            .ToArray()
                    }
                ];

                Write(
                    ModListPath,
                    JsonSerializer.Serialize(packs, JsonOptions));
            }

            private void WriteErrorLog(IReadOnlyList<string> lines)
                => Write(ErrorLogPath, string.Join(Environment.NewLine, lines), appendTrailingNewline: true);

            private static void Write(string path, string contents, bool appendTrailingNewline = false)
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path,
                    appendTrailingNewline ? contents + Environment.NewLine : contents,
                    Encoding.UTF8);
            }
        }
    }
}
