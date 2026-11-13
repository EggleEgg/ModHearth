using ModHearth.Models;
using ModHearth.Utils.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// The DF errorlog is the only place Dwarf Fortress records many runtime raw errors. These tests
    /// pin down how those lines are grouped and attributed to mods, including the deliberately
    /// conservative rule that bare filenames are never used as attribution evidence.
    /// </summary>
    public class ModErrorLogAnalyzerTests
    {
        private static string TestRoot => OperatingSystem.IsWindows() ? "C:\\" : "/";

        [Fact]
        public void AttributesObjectPrefixedErrorsThroughDirectDefinitions()
        {
            using TempErrorLog log = new(
                "*** Error(s) found in the file \"creature_wild_elf.txt\"",
                "WILD_ELF:Unrecognized Creature Variation: STANDARD_BIPED_GAITS",
                "WILD_ELF:Unrecognized Creature Variation: STANDARD_BIPED_GAITS",
                "SQUID MAN:Unrecognized Creature Token: TISSUE_NAME");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("wild_elf"), Mod("squid_people")],
                [
                    Info("wild_elf", "CREATURE:WILD_ELF"),
                    Info("squid_people", "CREATURE:SQUID_MAN")
                ]);

            ModErrorLogIssue elfIssue = Assert.Single(analysis.Issues, issue => issue.ModId == "wild_elf");
            Assert.Equal("Unrecognized creature variation", elfIssue.Category);
            Assert.Equal("STANDARD_BIPED_GAITS", elfIssue.Detail);
            Assert.Equal(2, elfIssue.Count);
            Assert.Contains("WILD_ELF", elfIssue.Objects);

            // DF prints some IDs with spaces; raw IDs use underscores.
            ModErrorLogIssue squidIssue = Assert.Single(analysis.Issues, issue => issue.ModId == "squid_people");
            Assert.Equal("Unrecognized creature token", squidIssue.Category);
            Assert.Equal("TISSUE_NAME", squidIssue.Detail);
        }

        [Fact]
        public void AttributesFinalizingBlockToTheObjectBeingBuilt()
        {
            using TempErrorLog log = new(
                "*** Error(s) finalizing the entity SUCCUBUS",
                "Unrecognized permitted building token: MAGMA_DREDGER",
                "Unrecognized permitted reaction token: GRINDOBSIDIAN");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("succubus_dungeon")],
                [Info("succubus_dungeon", "ENTITY:SUCCUBUS")]);

            Assert.Equal(2, analysis.Issues.Count);
            Assert.All(analysis.Issues, issue => Assert.Equal("succubus_dungeon", issue.ModId));
            Assert.Contains(analysis.Issues, issue => issue.Category == "Unrecognized permitted building token" && issue.Detail == "MAGMA_DREDGER");
            Assert.Contains(analysis.Issues, issue => issue.Category == "Unrecognized permitted reaction token" && issue.Detail == "GRINDOBSIDIAN");
        }

        [Fact]
        public void AttributesGraphicsErrorsThroughInstalledModsPath()
        {
            string graphicsFile = Path.Combine(
                TestRoot,
                "Dwarf Fortress",
                "data",
                "installed_mods",
                "topples_cv_orc (168)",
                "graphics",
                "graphics_orc_fc.txt");

            using TempErrorLog log = new(
                $"*** Error(s) found in the file \"{graphicsFile}\"",
                "Unrecognized Graphics Token: LAYER",
                "Unrecognized Graphics Token: LAYER");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("topples_cv_orc", Path.Combine(TestRoot, "Mods", "topples_cv_orc"))],
                []);

            ModErrorLogIssue issue = Assert.Single(analysis.Issues);
            Assert.Equal("topples_cv_orc", issue.ModId);
            Assert.Equal("Unrecognized graphics token", issue.Category);
            Assert.Equal("LAYER", issue.Detail);
            Assert.Equal(2, issue.Count);
            Assert.Contains("graphics_orc_fc.txt", issue.SourceFiles);
        }

        [Fact]
        public void PrefersGraphicsFileOverCreatureDefinitionForCasteGraphicsErrors()
        {
            string graphicsFile = Path.Combine(
                TestRoot,
                "Dwarf Fortress",
                "data",
                "installed_mods",
                "topples_cv_orc (168)",
                "graphics",
                "graphics_orc_fc.txt");

            using TempErrorLog log = new(
                $"*** Error(s) found in the file \"{graphicsFile}\"",
                "Failed to find CREATURE_CASTE_GRAPHICS creature caste ORC:FILTH_CALLER");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("topples_cv_orc"), Mod("orc_creatures")],
                [
                    Info("topples_cv_orc"),
                    Info("orc_creatures", "CREATURE:ORC")
                ]);

            ModErrorLogIssue issue = Assert.Single(analysis.Issues);
            Assert.Equal("topples_cv_orc", issue.ModId);
            Assert.Equal("Missing creature caste graphics", issue.Category);
            Assert.Equal("FILTH_CALLER", issue.Detail);
        }

        [Fact]
        public void UsesSelectTargetsWhenAVanillaObjectIsPatchedByAMod()
        {
            using TempErrorLog log = new("WORM:Unrecognized Creature Token: ROTS");

            ModRawDependencyInfo patched = new()
            {
                ModId = "creature_tweaks",
                SelectTargetIds = ["CREATURE:WORM"]
            };

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("creature_tweaks")],
                [patched]);

            ModErrorLogIssue issue = Assert.Single(analysis.Issues);
            Assert.Equal("creature_tweaks", issue.ModId);
            Assert.Equal("Unrecognized creature token", issue.Category);
            Assert.Equal("ROTS", issue.Detail);
        }

        [Fact]
        public void ClassifiesTissueErrorsBehindCasteAndBodyPartPrefixes()
        {
            using TempErrorLog log = new(
                "OCTOPUS:FEMALE:ink sac, layer 1: Tissue GIZZARD was not found, using first tissue instead",
                "OCTOPUS:MALE:ink sac, layer 1: Tissue GIZZARD was not found, using first tissue instead");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("cephalopod_expansion")],
                [Info("cephalopod_expansion", "CREATURE:OCTOPUS")]);

            ModErrorLogIssue issue = Assert.Single(analysis.Issues);
            Assert.Equal("cephalopod_expansion", issue.ModId);
            Assert.Equal("Tissue not found", issue.Category);
            Assert.Equal("GIZZARD", issue.Detail);
            Assert.Equal(2, issue.Count);
            Assert.Contains("OCTOPUS", issue.Objects);
        }

        [Fact]
        public void ClassifiesTissueLayerBodyPartErrors()
        {
            using TempErrorLog log = new(
                "CANID:Tissue layer not added because no BP found: BY_CATEGORY:FINGER:NAIL");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("canidcanid")],
                [Info("canidcanid", "CREATURE:CANID")]);

            ModErrorLogIssue issue = Assert.Single(analysis.Issues);
            Assert.Equal("canidcanid", issue.ModId);
            Assert.Equal("Tissue layer missing body part", issue.Category);
            Assert.Equal("BY_CATEGORY:FINGER:NAIL", issue.Detail);
        }

        [Fact]
        public void CollapsesRepeatedPlantMaterialTokens()
        {
            using TempErrorLog log = new("undefined plant material set to default: PALM PALM");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("vanilla_plants")],
                [Info("vanilla_plants", "PLANT:PALM")]);

            ModErrorLogIssue issue = Assert.Single(analysis.Issues);
            Assert.Equal("Undefined plant material", issue.Category);
            Assert.Equal("PALM", issue.Detail);
        }

        [Fact]
        public void DoesNotAttributeHeaderlessTokensThroughAStaleGraphicsFileHeader()
        {
            string graphicsFile = Path.Combine(
                TestRoot,
                "Dwarf Fortress",
                "data",
                "installed_mods",
                "sm_cv_valkyrie_adventure (4)",
                "graphics",
                "graphics_valkyrie.txt");

            using TempErrorLog log = new(
                $"*** Error(s) found in the file \"{graphicsFile}\"",
                "Tile page token not recognized: BONE_PILE",
                "Unrecognized Inorganic Token: IRON",
                "Unrecognized Inorganic Token: STEEL");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("sm_cv_valkyrie_adventure", Path.Combine(TestRoot, "Mods", "sm_cv_valkyrie_adventure"))],
                [Info("sm_cv_valkyrie_adventure")]);

            // The tile page error is genuinely file-bound and belongs to the graphics mod. The
            // following inorganic tokens are vanilla finalize errors that merely inherited the
            // stale file header, so they must stay unattributed.
            ModErrorLogIssue issue = Assert.Single(analysis.Issues);
            Assert.Equal("sm_cv_valkyrie_adventure", issue.ModId);
            Assert.Equal("Unrecognized graphics token", issue.Category);
            Assert.Equal("BONE_PILE", issue.Detail);
            Assert.Equal(1, issue.Count);
            Assert.Equal(2, analysis.UnattributedLineCount);
        }

        [Fact]
        public void MapsPlantErrorsThroughPlantDefinitions()
        {
            using TempErrorLog log = new(
                "undefined plant material set to default: WILD_CARROT DRINK",
                "plant growth not found: POD in WEED_BLADE");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("vanilla_plants"), Mod("modest_plants")],
                [
                    Info("vanilla_plants", "PLANT:WILD_CARROT"),
                    Info("modest_plants", "PLANT:WEED_BLADE")
                ]);

            Assert.Equal(2, analysis.Issues.Count);
            Assert.Contains(analysis.Issues, issue => issue.ModId == "vanilla_plants" && issue.Category == "Undefined plant material");
            Assert.Contains(analysis.Issues, issue => issue.ModId == "modest_plants" && issue.Category == "Plant growth not found" && issue.Detail == "POD");
        }

        [Fact]
        public void DoesNotUseStaleBareFileNamesForAttribution()
        {
            using TempErrorLog log = new(
                "*** Error(s) found in the file \"creature_mau.txt\"",
                "Unrecognized Personality Facet Token: ARTWORK");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("the_mau", Path.Combine(TestRoot, "Mods", "the_mau"))],
                [Info("the_mau")]);

            // DF leaves the previous file header in place while finalizing unrelated vanilla objects.
            // Reporting this as a the_mau problem would be a false positive.
            Assert.Empty(analysis.Issues);
            Assert.Equal(1, analysis.ConsideredLineCount);
            Assert.Equal(1, analysis.UnattributedLineCount);
        }

        [Fact]
        public void MapsPrefixedModMetadataErrorsToTheModId()
        {
            using TempErrorLog log = new(
                "canidcanid: NUMERIC_VERSION is an integer, i.e. it should not have a decimal point. Please report this to the mod author.",
                "better_university: Unrecognized mod info token: /h3. This is usually cuased by descriptions containing reserved characters.");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("canidcanid"), Mod("better_university")],
                []);

            Assert.Equal(2, analysis.Issues.Count);
            Assert.Contains(analysis.Issues, issue => issue.ModId == "canidcanid" && issue.Category == "Invalid mod version metadata");
            Assert.Contains(analysis.Issues, issue => issue.ModId == "better_university" && issue.Category == "Unrecognized mod info token" && issue.Detail == "/h3");
        }

        [Fact]
        public void AttributesDuplicateInstalledModWarnings()
        {
            using TempErrorLog log = new(
                "Two uninstalled mods have the same id and numeric version: Bronze age world (6).  Using the first encountered: 3773754939.");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("Bronze age world")],
                []);

            ModErrorLogIssue issue = Assert.Single(analysis.Issues);
            Assert.Equal("Bronze age world", issue.ModId);
            Assert.Equal("Duplicate mod installation", issue.Category);
            Assert.Equal("version 6, using 3773754939", issue.Detail);
        }

        [Fact]
        public void SkipsDuplicateObjectLinesHandledByTheDuplicateWarningPipeline()
        {
            using TempErrorLog log = new(
                "Duplicate Object: inorganic IRON; Offending mods are Vanilla_Materials_Overhaul, vanilla_materials");

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("vanilla_materials")],
                [Info("vanilla_materials", "INORGANIC:IRON")]);

            Assert.Empty(analysis.Issues);
            Assert.Equal(0, analysis.ConsideredLineCount);
        }

        [Fact]
        public void ParallelAnalysisKeepsContextAcrossBlockAndSegmentBoundaries()
        {
            const int graphicsErrors = 1000;
            const int objectErrors = 17_000;
            string graphicsFile = Path.Combine(
                TestRoot,
                "Dwarf Fortress",
                "data",
                "installed_mods",
                "gfx_mod (1)",
                "graphics",
                "graphics_test.txt");

            using TempErrorLog log = new(
            [
                $"*** Error(s) found in the file \"{graphicsFile}\"",
                .. Enumerable.Repeat("Unrecognized Graphics Token: LAYER", graphicsErrors),
                "*** Error(s) finalizing the entity SUCCUBUS",
                .. Enumerable.Repeat("Unrecognized permitted building token: MAGMA_DREDGER", objectErrors)
            ]);

            ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(
                log.Path,
                [Mod("gfx_mod"), Mod("succubus_dungeon")],
                [Info("succubus_dungeon", "ENTITY:SUCCUBUS")]);

            // The log spans multiple read blocks (and many parallel segments); a header's context
            // must still apply to every line that follows it.
            Assert.Equal(graphicsErrors + objectErrors, analysis.ConsideredLineCount);
            Assert.Equal(graphicsErrors + objectErrors, analysis.AttributedLineCount);
            Assert.Equal(0, analysis.UnattributedLineCount);

            ModErrorLogIssue graphicsIssue = Assert.Single(analysis.Issues, issue => issue.ModId == "gfx_mod");
            Assert.Equal("Unrecognized graphics token", graphicsIssue.Category);
            Assert.Equal(graphicsErrors, graphicsIssue.Count);

            ModErrorLogIssue objectIssue = Assert.Single(analysis.Issues, issue => issue.ModId == "succubus_dungeon");
            Assert.Equal("Unrecognized permitted building token", objectIssue.Category);
            Assert.Equal(objectErrors, objectIssue.Count);
        }

        [Fact]
        public void ConcurrentAnalysesProduceIdenticalResults()
        {
            const int errorCount = 10_000;
            using TempErrorLog log = new(
            [
                "*** Error(s) finalizing the entity SUCCUBUS",
                .. Enumerable.Repeat("Unrecognized permitted building token: MAGMA_DREDGER", errorCount)
            ]);

            ModReference[] mods = [Mod("succubus_dungeon")];
            ModRawDependencyInfo[] infos = [Info("succubus_dungeon", "ENTITY:SUCCUBUS")];
            int[] issueCounts = new int[8];
            int[] consideredCounts = new int[8];

            _ = Parallel.For(0, issueCounts.Length, index =>
            {
                ModErrorLogAnalysis analysis = ModErrorLogAnalyzer.Analyze(log.Path, mods, infos);
                issueCounts[index] = analysis.Issues.Single().Count;
                consideredCounts[index] = analysis.ConsideredLineCount;
            });

            Assert.All(issueCounts, count => Assert.Equal(errorCount, count));
            Assert.All(consideredCounts, count => Assert.Equal(errorCount, count));
        }

        [Fact]
        public void DisplayStringAggregatesContextAndOccurrences()
        {
            ModErrorLogIssue issue = new(
                "a_mod",
                "Unrecognized creature variation",
                "STANDARD_BIPED_GAITS",
                42,
                ["A", "B", "C", "D", "E"],
                ["creature_a.txt"]);

            string text = issue.ToDisplayString(maxContextEntries: 3);

            Assert.Contains("STANDARD_BIPED_GAITS", text);
            Assert.Contains("A, B, C, +2 more", text);
            Assert.Contains("x42", text);
        }

        private static ModReference Mod(string id, string? path = null) => new()
        {
            ID = id,
            Name = id,
            NumericVersion = "1",
            DisplayedVersion = "1",
            Path = path ?? string.Empty
        };

        private static ModRawDependencyInfo Info(string modId, params string[] definitions) => new()
        {
            ModId = modId,
            NumericVersion = "1",
            DirectDefinitionIds = [.. definitions]
        };

        private sealed class TempErrorLog : IDisposable
        {
            private readonly TempFolder temp = new("modhearth-errorlog");

            public string Path { get; }

            public TempErrorLog(params string[] lines)
            {
                Path = temp.Write("errorlog.txt", string.Join(Environment.NewLine, lines));
            }

            public void Dispose() => temp.Dispose();
        }
    }
}
