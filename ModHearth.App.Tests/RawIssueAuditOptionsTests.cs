using ModHearth.RawIssueAudit;
using ModHearth.Utils.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    public class RawIssueAuditOptionsTests
    {
        [Fact]
        public void DefaultsMatchTheDocumentedCommandLine()
        {
            AuditOptions options = AuditOptions.Parse([], out string? error);

            Assert.Null(error);
            Assert.False(options.HelpRequested);
            Assert.False(options.AllMods);
            Assert.False(options.ShowMatches);
            Assert.False(options.AutomateWorldgen);
            Assert.False(options.KeepDwarfFortressRunning);
            Assert.Equal(40, options.MaxDetails);
            Assert.Equal(180, options.AutomationTimeoutSeconds);
            Assert.Null(options.ErrorLogPath);
            Assert.Null(options.CachePath);
            Assert.Null(options.VanillaPath);
            Assert.Null(options.ModListPath);
            Assert.Null(options.ConfigPath);
            Assert.Null(options.DwarfFortressPath);
            Assert.Null(options.DfHackPath);
            Assert.Null(options.JsonOutputPath);
        }

        [Fact]
        public void ParsesEveryPathAndFlag()
        {
            AuditOptions options = AuditOptions.Parse(
            [
                "--df", @"C:\Dwarf Fortress",
                "--dfhack", @"C:\DFHack\hack",
                "--errorlog", @"C:\Dwarf Fortress\errorlog.txt",
                "--cache", @"C:\cache\mod_raw_dependency_cache.json",
                "--vanilla", @"C:\Dwarf Fortress\data\vanilla",
                "--modlist", @"C:\Dwarf Fortress\dfhack-config\mod-manager.json",
                "--config", @"C:\app\config.json",
                "--json", @"C:\out\audit.json",
                "--show-matches",
                "--automate",
                "--automation-timeout", "240",
                "--keep-df-running",
                "--max-details", "7"
            ], out string? error);

            Assert.Null(error);
            Assert.Equal(@"C:\Dwarf Fortress", options.DwarfFortressPath);
            Assert.Equal(@"C:\DFHack\hack", options.DfHackPath);
            Assert.Equal(@"C:\Dwarf Fortress\errorlog.txt", options.ErrorLogPath);
            Assert.Equal(@"C:\cache\mod_raw_dependency_cache.json", options.CachePath);
            Assert.Equal(@"C:\Dwarf Fortress\data\vanilla", options.VanillaPath);
            Assert.Equal(@"C:\Dwarf Fortress\dfhack-config\mod-manager.json", options.ModListPath);
            Assert.Equal(@"C:\app\config.json", options.ConfigPath);
            Assert.Equal(@"C:\out\audit.json", options.JsonOutputPath);
            Assert.False(options.AllMods);
            Assert.True(options.ShowMatches);
            Assert.True(options.AutomateWorldgen);
            Assert.True(options.KeepDwarfFortressRunning);
            Assert.Equal(240, options.AutomationTimeoutSeconds);
            Assert.Equal(7, options.MaxDetails);
        }

        [Fact]
        public void AutomateCannotBeCombinedWithAllMods()
        {
            _ = AuditOptions.Parse(
                ["--automate", "--all-mods"],
                out string? error);

            Assert.NotNull(error);
            Assert.Contains("cannot be combined with --all-mods", error, StringComparison.Ordinal);
        }

        [Fact]
        public void AutomateWorldgenAliasIsAccepted()
        {
            AuditOptions options = AuditOptions.Parse(["--automate-worldgen"], out string? error);

            Assert.Null(error);
            Assert.True(options.AutomateWorldgen);
        }

        [Fact]
        public void AutomationRuntimeFlagsRequireAutomation()
        {
            _ = AuditOptions.Parse(["--keep-df-running"], out string? keepError);
            _ = AuditOptions.Parse(["--automation-timeout", "60"], out string? timeoutError);

            Assert.NotNull(keepError);
            Assert.Contains("require --automate", keepError, StringComparison.Ordinal);
            Assert.NotNull(timeoutError);
            Assert.Contains("require --automate", timeoutError, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("29")]
        [InlineData("3601")]
        [InlineData("not-a-number")]
        public void InvalidAutomationTimeoutIsRejected(string value)
        {
            _ = AuditOptions.Parse(["--automate", "--automation-timeout", value], out string? error);

            Assert.NotNull(error);
            Assert.Contains("between 30 and 3600", error, StringComparison.Ordinal);
        }

        [Fact]
        public void HelpStopsParsingAndIgnoresLaterArguments()
        {
            AuditOptions options = AuditOptions.Parse(["--help", "--cache"], out string? error);

            Assert.Null(error);
            Assert.True(options.HelpRequested);
            Assert.Null(options.CachePath);
        }

        [Fact]
        public void MissingValueIsRejected()
        {
            _ = AuditOptions.Parse(["--cache"], out string? error);

            Assert.NotNull(error);
            Assert.Contains("--cache expects a value", error, StringComparison.Ordinal);
        }

        [Fact]
        public void UnknownOptionIsRejected()
        {
            _ = AuditOptions.Parse(["--unknown"], out string? error);

            Assert.NotNull(error);
            Assert.Contains("Unknown option '--unknown'", error, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("not-a-number")]
        public void InvalidMaxDetailsIsRejected(string value)
        {
            _ = AuditOptions.Parse(["--max-details", value], out string? error);

            Assert.NotNull(error);
            Assert.Contains("positive whole number", error, StringComparison.Ordinal);
        }
    }

    public class ErrorLogReferenceMappingTests
    {
        [Theory]
        [InlineData("Unrecognized permitted reaction token", "PermittedReaction")]
        [InlineData("unrecognized PERMITTED REACTION TOKEN", "PermittedReaction")]
        [InlineData("Unrecognized permitted reaction token in entity TEST_ENTITY", "PermittedReaction")]
        [InlineData("Unrecognized permitted building token", "PermittedBuilding")]
        [InlineData("Unrecognized permitted building token in entity TEST_ENTITY", "PermittedBuilding")]
        [InlineData("Unrecognized building token", "ReactionBuilding")]
        [InlineData("Unrecognized Building Token in reaction TEST_REACTION", "ReactionBuilding")]
        public void MapsKnownReferenceCategoriesIncludingSuffixes(string category, string expected)
        {
            Assert.True(ErrorLogReferenceMapping.TryMapCategory(category, out ReferenceAuditKind kind));
            Assert.Equal(Enum.Parse<ReferenceAuditKind>(expected), kind);
        }

        [Theory]
        [InlineData("Unrecognized reaction token")]
        [InlineData("Unrecognized graphics token")]
        [InlineData("Tissue not found")]
        [InlineData("")]
        [InlineData(null)]
        public void DoesNotMapUnrelatedCategories(string? category)
            => Assert.False(ErrorLogReferenceMapping.TryMapCategory(category, out _));

        [Fact]
        public void MapsReferenceKindsToAnalyzerKinds()
        {
            Assert.Equal(
                ModPossibleIssueKind.MissingPermittedReaction,
                ErrorLogReferenceMapping.ToIssueKind(ReferenceAuditKind.PermittedReaction));
            Assert.Equal(
                ModPossibleIssueKind.MissingPermittedBuilding,
                ErrorLogReferenceMapping.ToIssueKind(ReferenceAuditKind.PermittedBuilding));
            Assert.Equal(
                ModPossibleIssueKind.MissingReactionBuilding,
                ErrorLogReferenceMapping.ToIssueKind(ReferenceAuditKind.ReactionBuilding));
        }

        [Theory]
        [InlineData("PermittedReaction", "REACTION:MISSING")]
        [InlineData("PermittedBuilding", "BUILDING:MISSING")]
        [InlineData("ReactionBuilding", "BUILDING:MISSING")]
        public void BuildsTheSameCompositeKeyShapeAsTheAnalyzer(string kindName, string expected)
            => Assert.Equal(
                expected,
                ErrorLogReferenceMapping.ToTargetKey(Enum.Parse<ReferenceAuditKind>(kindName), "MISSING"));

        [Fact]
        public void OnlyPermittedAndReactionBuildingPredictionsAreVerifiable()
        {
            Assert.True(ErrorLogReferenceMapping.IsComparable(ModPossibleIssueKind.MissingPermittedReaction));
            Assert.True(ErrorLogReferenceMapping.IsComparable(ModPossibleIssueKind.MissingPermittedBuilding));
            Assert.True(ErrorLogReferenceMapping.IsComparable(ModPossibleIssueKind.MissingReactionBuilding));
            Assert.False(ErrorLogReferenceMapping.IsComparable(ModPossibleIssueKind.MissingSelectTarget));
            Assert.False(ErrorLogReferenceMapping.IsComparable(ModPossibleIssueKind.MissingCutTarget));
            Assert.False(ErrorLogReferenceMapping.IsComparable(ModPossibleIssueKind.MissingCopyTagsFromSource));
        }

        [Theory]
        [InlineData("Unrecognized permitted material token", true)]
        [InlineData("Unrecognized custom building token", true)]
        [InlineData("Unrecognized select token", true)]
        [InlineData("Unrecognized cut token", true)]
        [InlineData("Unrecognized copy tags token", true)]
        [InlineData("Unrecognized reaction token", false)]
        [InlineData("Unrecognized graphics token", false)]
        [InlineData("Unrecognized permitted reaction token", false)]
        public void FlagsOnlyLikelyReferenceDiagnosticsForMaintenance(string category, bool expected)
            => Assert.Equal(expected, ErrorLogReferenceMapping.LooksReferenceRelated(category));
    }
}
