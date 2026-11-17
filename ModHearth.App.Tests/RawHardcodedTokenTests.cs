using System.Text;
using System.Text.Json;
using ModHearth.Models;
using ModHearth.RawIssueAudit;
using ModHearth.Utils.DwarfFortress;
using ModHearth.Utils.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Coverage for the DF-hardcoded token catalog, the material-slot parser, and the read-only
    /// hardcoded report/verification workflow. These tests do not need a Dwarf Fortress install:
    /// the executable and raw folders are synthetic temp folders.
    /// </summary>
    public class RawHardcodedTokenTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static readonly string[] SampleDyeReactionIds =
        [
            "ALDER_CONE_DYE",
            "WILLOW_BARK_DYE"
        ];

        [Fact]
        public void CatalogGroupsHaveTheExpectedScopeAndUniqueTokens()
        {
            Assert.Equal(4, DwarfFortressHardcodedTokens.Groups.Count);

            Assert.Equal(31, DwarfFortressHardcodedTokens.TokensOf(
                HardcodedTokenGroupId.ReactionBuildings).Count);
            Assert.Empty(DwarfFortressHardcodedTokens.TokensOf(
                HardcodedTokenGroupId.CutImmuneDyeReactions));
            Assert.Equal(18, DwarfFortressHardcodedTokens.TokensOf(
                HardcodedTokenGroupId.ConcreteBuiltInMaterials).Count);
            Assert.Equal(10, DwarfFortressHardcodedTokens.TokensOf(
                HardcodedTokenGroupId.MaterialGrammarTokens).Count);

            string[] allTokens = [.. DwarfFortressHardcodedTokens.Groups
                .SelectMany(group => group.Tokens)];
            Assert.Equal(allTokens.Length, allTokens.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            Assert.All(DwarfFortressHardcodedTokens.Groups, group =>
            {
                if (group.Id != HardcodedTokenGroupId.CutImmuneDyeReactions)
                    Assert.NotEmpty(group.Tokens);

                Assert.False(string.IsNullOrWhiteSpace(group.Description));
                Assert.False(string.IsNullOrWhiteSpace(group.Source));
                Assert.Equal(group, DwarfFortressHardcodedTokens.GroupOf(group.Id));
            });

            Assert.Equal(
                HardcodedTokenVerification.VanillaRawUsage,
                DwarfFortressHardcodedTokens.GroupOf(
                    HardcodedTokenGroupId.ReactionBuildings).Verification);
            Assert.All(
                DwarfFortressHardcodedTokens.Groups.Where(group =>
                    group.Id != HardcodedTokenGroupId.ReactionBuildings),
                group => Assert.Equal(
                    HardcodedTokenVerification.ExecutableString,
                    group.Verification));
        }

        [Fact]
        public void CatalogLookupsAreScopedAndCaseInsensitive()
        {
            Assert.True(DwarfFortressHardcodedTokens.IsReactionBuildingId(" carpenter "));
            Assert.False(DwarfFortressHardcodedTokens.IsReactionBuildingId("CARPENTERS"));

            Assert.True(DwarfFortressHardcodedTokens.IsConcreteBuiltInMaterial("water"));
            Assert.False(DwarfFortressHardcodedTokens.IsConcreteBuiltInMaterial("GRANITE"));

            Assert.True(DwarfFortressHardcodedTokens.IsMaterialGrammarToken("LOCAL_PLANT_MAT"));
            Assert.False(DwarfFortressHardcodedTokens.IsMaterialGrammarToken("WATER"));
        }

        [Fact]
        public void VanillaDyeCatalogIntersectsDefinitionsAndPermissions()
        {
            using TempFolder temp = new("modhearth-vanilla-dyes");
            _ = temp.Write(
                Path.Combine("vanilla_reactions", "objects", "reaction_dyes.txt"),
                """
                [OBJECT:REACTION]
                [REACTION:ALDER_CONE_DYE]
                [REACTION:WILLOW_BARK_DYE]
                [REACTION:DEFINED_BUT_NOT_PERMITTED_DYE]
                """);
            _ = temp.Write(
                Path.Combine("vanilla_entities", "objects", "entity_default.txt"),
                """
                [OBJECT:ENTITY]
                [PERMITTED_REACTION:alder_cone_dye]
                [PERMITTED_REACTION:WILLOW_BARK_DYE]
                [PERMITTED_REACTION:PRESS_OIL_FRUIT]
                """);

            List<string> warnings = [];
            IReadOnlySet<string> ids = VanillaDyeReactionCatalog.Load(temp.Path, warnings);

            Assert.Empty(warnings);
            Assert.Equal(2, ids.Count);
            Assert.Contains("ALDER_CONE_DYE", ids);
            Assert.Contains("WILLOW_BARK_DYE", ids);
            Assert.DoesNotContain("DEFINED_BUT_NOT_PERMITTED_DYE", ids);
            Assert.DoesNotContain("PRESS_OIL_FRUIT", ids);
        }

        [Fact]
        public void VanillaBaselineCarriesTheDerivedDyeReactionSet()
        {
            using TempFolder temp = new("modhearth-vanilla-dyes");
            _ = temp.Write(
                Path.Combine("vanilla_reactions", "objects", "reaction_dyes.txt"),
                """
                [OBJECT:REACTION]
                [REACTION:ALDER_CONE_DYE]
                """);
            _ = temp.Write(
                Path.Combine("vanilla_entities", "objects", "entity_default.txt"),
                """
                [OBJECT:ENTITY]
                [PERMITTED_REACTION:ALDER_CONE_DYE]
                """);

            List<string> warnings = [];
            VanillaRawBaseline baseline = VanillaRawBaseline.Load(temp.Path, warnings);

            Assert.Empty(warnings);
            Assert.True(baseline.IsCutIgnoredDyeReaction("alder_cone_dye"));
            Assert.False(baseline.IsCutIgnoredDyeReaction("PRESS_OIL_FRUIT"));
        }

        [Fact]
        public void VanillaDyeCatalogWarnsWhenTheRawFilesAreMissing()
        {
            using TempFolder temp = new("modhearth-vanilla-dyes");
            List<string> warnings = [];

            IReadOnlySet<string> ids = VanillaDyeReactionCatalog.Load(temp.Path, warnings);

            Assert.Empty(ids);
            Assert.NotEmpty(warnings);
        }

        [Theory]
        [InlineData("", (int)DwarfFortressMaterialTokenKind.Empty, "", null, null)]
        [InlineData("NONE", (int)DwarfFortressMaterialTokenKind.None, "NONE", null, null)]
        [InlineData("WATER", (int)DwarfFortressMaterialTokenKind.BuiltIn, "WATER", null, null)]
        [InlineData("INORGANIC:GRANITE", (int)DwarfFortressMaterialTokenKind.Inorganic, "INORGANIC", "GRANITE", null)]
        [InlineData("METAL:IRON", (int)DwarfFortressMaterialTokenKind.Metal, "METAL", "IRON", null)]
        [InlineData("PLANT_MAT:OAK:WOOD", (int)DwarfFortressMaterialTokenKind.Plant, "PLANT_MAT", "OAK", "WOOD")]
        [InlineData("PLANT:OAK:WOOD", (int)DwarfFortressMaterialTokenKind.Plant, "PLANT_MAT", "OAK", "WOOD")]
        [InlineData("CREATURE_MAT:COW:LEATHER", (int)DwarfFortressMaterialTokenKind.Creature, "CREATURE_MAT", "COW", "LEATHER")]
        [InlineData("CREATURE:COW:FAT", (int)DwarfFortressMaterialTokenKind.Creature, "CREATURE_MAT", "COW", "FAT")]
        [InlineData("LOCAL_PLANT_MAT:WOOD", (int)DwarfFortressMaterialTokenKind.LocalPlant, "LOCAL_PLANT_MAT", null, "WOOD")]
        [InlineData("LOCAL_CREATURE_MAT:LEATHER", (int)DwarfFortressMaterialTokenKind.LocalCreature, "LOCAL_CREATURE_MAT", null, "LEATHER")]
        [InlineData("GET_MATERIAL_FROM_REAGENT:REAGENT:MAT", (int)DwarfFortressMaterialTokenKind.FromReagent, "GET_MATERIAL_FROM_REAGENT", "REAGENT", "MAT")]
        [InlineData("NOT_A_REAL_TOKEN", (int)DwarfFortressMaterialTokenKind.Unknown, "NOT_A_REAL_TOKEN", null, null)]
        [InlineData("INORGANIC", (int)DwarfFortressMaterialTokenKind.Unknown, "INORGANIC", null, null)]
        public void MaterialTokenParserClassifiesTheFirstToken(
            string raw,
            int expectedKind,
            string expectedHead,
            string? expectedObjectId,
            string? expectedMaterialId)
        {
            DwarfFortressMaterialToken token = DwarfFortressMaterialToken.Parse(raw);

            Assert.Equal((DwarfFortressMaterialTokenKind)expectedKind, token.Kind);
            Assert.Equal(expectedHead, token.Head);
            Assert.Equal(expectedObjectId, token.ObjectId);
            Assert.Equal(expectedMaterialId, token.MaterialId);
        }

        [Theory]
        [InlineData("INORGANIC:NONE:NONE", "INORGANIC")]
        [InlineData("INORGANIC:NO_MATGLOSS", "INORGANIC")]
        [InlineData("METAL:NONE", "INORGANIC")]
        [InlineData("METAL:NO_MATGLOSS", "INORGANIC")]
        public void MaterialTokenParserRecognizesTheInorganicWildcards(
            string raw,
            string expectedObjectType)
        {
            DwarfFortressMaterialToken token = DwarfFortressMaterialToken.Parse(raw);

            Assert.True(token.IsInorganicWildcard);
            Assert.Equal(expectedObjectType, token.ReferencedObjectType);
            Assert.Null(token.ReferencedObjectId);
        }

        [Fact]
        public void HardcodedReportAndVerificationFlagsParseTogether()
        {
            AuditOptions options = AuditOptions.Parse(
                ["--hardcoded-report", "--verify-hardcoded", "--json", "out.json"],
                out string? error);

            Assert.Null(error);
            Assert.True(options.HardcodedReport);
            Assert.True(options.VerifyHardcoded);
            Assert.Equal("out.json", options.JsonOutputPath);
        }

        [Fact]
        public void VerifyHardcodedRequiresTheReportMode()
        {
            _ = AuditOptions.Parse(["--verify-hardcoded"], out string? error);

            Assert.NotNull(error);
            Assert.Contains("requires --hardcoded-report", error, StringComparison.Ordinal);
        }

        [Fact]
        public void HardcodedReportCannotAutomateWorldgen()
        {
            _ = AuditOptions.Parse(["--hardcoded-report", "--automate"], out string? error);

            Assert.NotNull(error);
            Assert.Contains("cannot be combined with --automate", error, StringComparison.Ordinal);
        }

        [Fact]
        public void MaterialScannerFindsUsageUnresolvedReferencesAndUnknownForms()
        {
            using TempFolder temp = new("modhearth-hardcoded-scan");
            string modPath = temp.CreateDirectory("mod_test");
            _ = temp.Write(
                Path.Combine("mod_test", "objects", "reactions.txt"),
                """
                [OBJECT:REACTION]
                [REACTION:TEST_REACTION]
                	[REAGENT:A:1:WOOD:NONE:PLANT_MAT:KNOWN_PLANT:WOOD]
                	[REAGENT:B:1:WOOD:NONE:PLANT_MAT:MISSING_PLANT:WOOD]
                	[REAGENT:C:1:BAR:NONE:INORGANIC:MISSING_STONE:NONE]
                	[REAGENT:D:1:LEATHER:NONE:CREATURE_MAT:MISSING_CREATURE:LEATHER]
                	[REAGENT:E:1:BAR:NONE:WATER:NONE]
                	[REAGENT:F:1:BAR:NONE:LOCAL_PLANT_MAT:WOOD]
                	[REAGENT:G:1:BAR:NONE:GET_MATERIAL_FROM_REAGENT:E:WATER]
                	[REAGENT:H:1:GET_ITEM_DATA_FROM_REAGENT:item:material]
                	[REAGENT:I:1:FISH_RAW:FISH:NONE]
                	[REAGENT:J:1:BAR:NONE:INORGANIC:ACTIVE_STONE:NONE]
                	[PRODUCT:100:1:POWDER_MISC:NONE:WEIRD_MATERIAL]
                """);

            ModRawDependencyInfo entry = new()
            {
                ModId = "mod_test",
                NumericVersion = "1",
                DirectDefinitionIds = ["INORGANIC:ACTIVE_STONE"]
            };

            Dictionary<string, AuditRunner.SnapshotEntry> snapshot = new(StringComparer.OrdinalIgnoreCase)
            {
                ["mod_test"] = new AuditRunner.SnapshotEntry
                {
                    ModId = "mod_test",
                    ModName = "Test Mod",
                    Path = modPath
                }
            };

            VanillaRawBaseline vanilla = new(
            [
                new ObjectKey("PLANT", "KNOWN_PLANT"),
                new ObjectKey("CREATURE", "KNOWN_CREATURE")
            ]);

            List<string> warnings = [];
            HardcodedMaterialScanResult result = HardcodedMaterialScanner.Scan(
                [entry],
                snapshot,
                vanilla,
                warnings);

            Assert.Empty(warnings);
            Assert.Equal(1, result.ScannedModCount);
            Assert.Equal(1, result.ScannedFileCount);

            Assert.Contains(result.Usage, usage =>
                usage.GroupId == HardcodedTokenGroupId.ConcreteBuiltInMaterials
                && usage.Token == "WATER");
            Assert.Contains(result.Usage, usage =>
                usage.GroupId == HardcodedTokenGroupId.MaterialGrammarTokens
                && usage.Token == "LOCAL_PLANT_MAT");
            Assert.Contains(result.Usage, usage =>
                usage.GroupId == HardcodedTokenGroupId.MaterialGrammarTokens
                && usage.Token == "GET_MATERIAL_FROM_REAGENT");

            Assert.Contains(result.UnresolvedMaterials, reference =>
                reference.Token == "PLANT_MAT:MISSING_PLANT:WOOD"
                && reference.Status == HardcodedMaterialReferenceStatus.MissingPlant);
            Assert.Contains(result.UnresolvedMaterials, reference =>
                reference.Token == "INORGANIC:MISSING_STONE:NONE"
                && reference.Status == HardcodedMaterialReferenceStatus.MissingInorganic);
            Assert.Contains(result.UnresolvedMaterials, reference =>
                reference.Token == "CREATURE_MAT:MISSING_CREATURE:LEATHER"
                && reference.Status == HardcodedMaterialReferenceStatus.MissingCreature);
            Assert.DoesNotContain(result.UnresolvedMaterials, reference =>
                reference.Token.Contains("KNOWN_PLANT", StringComparison.Ordinal)
                || reference.Token.Contains("ACTIVE_STONE", StringComparison.Ordinal)
                || reference.Token.Contains("KNOWN_CREATURE", StringComparison.Ordinal));

            HardcodedMaterialSample sample = Assert.Single(result.UnclassifiedMaterials);
            Assert.Equal("WEIRD_MATERIAL", sample.Head);
            Assert.Contains(sample.Examples, example =>
                example.Contains("WEIRD_MATERIAL", StringComparison.Ordinal));
        }

        [Fact]
        public void DriftVerifierIsCleanWhenTheExecutableContainsTheCatalog()
        {
            using TempFolder temp = new("modhearth-hardcoded-verify");
            string executablePath = temp.Combine("Dwarf Fortress.exe");
            WriteExecutable(
                executablePath,
                [
                    .. DwarfFortressHardcodedTokens.Groups.SelectMany(group => group.Tokens),
                    .. SampleDyeReactionIds
                ]);

            HardcodedVerificationResult result = HardcodedDriftVerifier.Verify(
                executablePath,
                SampleDyeReactionIds,
                SampleDyeReactionIds);

            Assert.True(result.IsClean);
            Assert.Equal(SampleDyeReactionIds.Length, result.VanillaReactionCount);
            Assert.Equal(SampleDyeReactionIds.Length, result.DerivedCutIgnoredReactionCount);
            Assert.Equal(SampleDyeReactionIds.Length, result.ExecutableVanillaReactionCount);
        }

        [Fact]
        public void DriftVerifierReportsUnexpectedAndMissingReactionIds()
        {
            using TempFolder temp = new("modhearth-hardcoded-verify");
            string executablePath = temp.Combine("Dwarf Fortress.exe");

            string missing = SampleDyeReactionIds[0];
            string[] presentTokens =
            [
                .. DwarfFortressHardcodedTokens.Groups.SelectMany(group => group.Tokens),
                .. SampleDyeReactionIds.Where(id =>
                    !string.Equals(id, missing, StringComparison.OrdinalIgnoreCase)),
                "NEW_HARDCODED_REACTION"
            ];
            WriteExecutable(executablePath, presentTokens);

            List<string> vanillaReactions =
            [
                .. SampleDyeReactionIds,
                "NEW_HARDCODED_REACTION",
                "ORDINARY_VANILLA_REACTION"
            ];

            HardcodedVerificationResult result = HardcodedDriftVerifier.Verify(
                executablePath,
                vanillaReactions,
                SampleDyeReactionIds);

            Assert.False(result.IsClean);
            Assert.Contains(result.Checks, check =>
                check.Token == "NEW_HARDCODED_REACTION"
                && check.Status == HardcodedCatalogCheckStatus.UnexpectedReactionIdInExecutable);
            Assert.Contains(result.Checks, check =>
                check.Token == missing
                && check.Status == HardcodedCatalogCheckStatus.VanillaReactionMissingFromExecutable);
            Assert.DoesNotContain(result.Checks, check =>
                check.Token == "ORDINARY_VANILLA_REACTION");
        }

        [Fact]
        public void DriftVerifierReportsMissingNonReactionCatalogTokens()
        {
            using TempFolder temp = new("modhearth-hardcoded-verify");
            string executablePath = temp.Combine("Dwarf Fortress.exe");

            string[] presentTokens =
            [
                .. DwarfFortressHardcodedTokens.Groups
                    .SelectMany(group => group.Tokens)
                    .Where(token => !string.Equals(token, "WATER", StringComparison.OrdinalIgnoreCase)),
                .. SampleDyeReactionIds
            ];
            WriteExecutable(executablePath, presentTokens);

            HardcodedVerificationResult result = HardcodedDriftVerifier.Verify(
                executablePath,
                SampleDyeReactionIds,
                SampleDyeReactionIds);

            HardcodedCatalogCheck check = Assert.Single(result.Checks);
            Assert.Equal("WATER", check.Token);
            Assert.Equal(HardcodedTokenGroupId.ConcreteBuiltInMaterials, check.GroupId);
            Assert.Equal(HardcodedCatalogCheckStatus.CatalogTokenMissingFromExecutable, check.Status);
        }

        [Fact]
        public void DriftVerifierDoesNotRequireReactionBuildingStrings()
        {
            using TempFolder temp = new("modhearth-hardcoded-verify");
            string executablePath = temp.Combine("Dwarf Fortress.exe");

            string[] presentTokens =
            [
                .. DwarfFortressHardcodedTokens.Groups
                    .Where(group => group.Verification == HardcodedTokenVerification.ExecutableString)
                    .SelectMany(group => group.Tokens),
                .. SampleDyeReactionIds
            ];
            WriteExecutable(executablePath, presentTokens);

            HardcodedVerificationResult result = HardcodedDriftVerifier.Verify(
                executablePath,
                SampleDyeReactionIds,
                SampleDyeReactionIds);

            Assert.True(result.IsClean);
        }

        [Fact]
        public void VanillaReactionScannerCollectsOnlyReactionBuildingIds()
        {
            using TempFolder temp = new("modhearth-hardcoded-vanilla");
            _ = temp.Write(
                Path.Combine("vanilla_core", "objects", "objects.txt"),
                """
                [OBJECT:REACTION]
                [REACTION:VANILLA_REACTION]
                	[BUILDING:LAVAMILL]
                	[BUILDING:BOWYER:CUSTOM_H]
                	[BUILDING:VANILLA_CUSTOM]

                [OBJECT:BUILDING]
                [BUILDING:VANILLA_CUSTOM]
                """);

            List<string> warnings = [];
            IReadOnlySet<string> ids = HardcodedVanillaReactionScanner.CollectReactionBuildingIds(
                temp.Path,
                warnings);

            Assert.Empty(warnings);
            Assert.Contains("LAVAMILL", ids);
            Assert.Contains("BOWYER", ids);
            Assert.Contains("VANILLA_CUSTOM", ids);
            Assert.Equal(3, ids.Count);
        }

        [Fact]
        public void VanillaChecksFlagAnUncatalogedReactionBuilding()
        {
            using TempFolder temp = new("modhearth-hardcoded-vanilla");
            _ = temp.Write(
                Path.Combine("vanilla_core", "objects", "reactions.txt"),
                """
                [OBJECT:REACTION]
                [REACTION:VANILLA_REACTION]
                	[BUILDING:LAVAMILL]
                	[BUILDING:NEW_BUILTIN]
                	[BUILDING:KNOWN_CUSTOM]

                [OBJECT:BUILDING]
                [BUILDING:KNOWN_CUSTOM]
                """);

            List<ObjectKey> keys = [new ObjectKey("BUILDING", "KNOWN_CUSTOM")];

            List<string> warnings = [];
            List<HardcodedCatalogCheck> checks = HardcodedReportRunner.BuildVanillaChecks(
                new VanillaRawBaseline(keys),
                temp.Path,
                warnings);

            HardcodedCatalogCheck check = Assert.Single(checks);
            Assert.Equal("NEW_BUILTIN", check.Token);
            Assert.Equal(HardcodedTokenGroupId.ReactionBuildings, check.GroupId);
            Assert.Equal(
                HardcodedCatalogCheckStatus.CatalogMissingReactionBuildingUsedByVanilla,
                check.Status);
            Assert.Empty(warnings);
        }

        [Fact]
        public void DriftVerifierRejectsAMissingExecutable()
        {
            using TempFolder temp = new("modhearth-hardcoded-verify");

            _ = Assert.Throws<AuditInputException>(() =>
                HardcodedDriftVerifier.Verify(temp.Combine("missing.exe"), [], []));
        }

        // Runs the full report pipeline through the platform-specific executable locator. CI has no
        // Dwarf Fortress installation, so keep this in the local-only category used by the workflows.
        [Fact]
        [Trait(TestCategories.TraitName, TestCategories.RequiresDwarfFortress)]
        public void ReportRunnerBuildsUsageContextsMaterialsAndVerification()
        {
            using TempFolder temp = new("modhearth-hardcoded-report");
            string configPath = temp.Write(
                "config.json",
                """{ "DFFolderPath": "", "DFFolderPathOverride": "" }""");

            string modPath = temp.CreateDirectory("mod_test");
            _ = temp.Write(
                Path.Combine("mod_test", "objects", "reactions.txt"),
                """
                [OBJECT:REACTION]
                [REACTION:TEST_REACTION]
                	[REAGENT:A:1:WOOD:NONE:PLANT_MAT:KNOWN_PLANT:WOOD]
                	[REAGENT:B:1:WOOD:NONE:PLANT_MAT:MISSING_PLANT:WOOD]
                	[REAGENT:C:1:BAR:NONE:INORGANIC:MISSING_STONE:NONE]
                	[REAGENT:D:1:LEATHER:NONE:CREATURE_MAT:MISSING_CREATURE:LEATHER]
                	[REAGENT:E:1:BAR:NONE:WATER:NONE]
                	[REAGENT:F:1:BAR:NONE:LOCAL_PLANT_MAT:WOOD]
                	[PRODUCT:100:1:POWDER_MISC:NONE:WEIRD_MATERIAL]
                """);

            string vanillaPath = temp.Combine("vanilla");
            string vanillaObjects = temp.Combine("vanilla", "vanilla_core", "objects", "objects.txt");
            List<string> vanillaLines =
            [
                "[OBJECT:REACTION]",
                .. SampleDyeReactionIds.Select(id => $"[REACTION:{id}]"),
                string.Empty,
                "[OBJECT:PLANT]",
                "[PLANT:KNOWN_PLANT]",
                string.Empty,
                "[OBJECT:CREATURE]",
                "[CREATURE:KNOWN_CREATURE]"
            ];
            _ = temp.Write(
                Path.GetRelativePath(temp.Path, vanillaObjects),
                string.Join(Environment.NewLine, vanillaLines));

            _ = temp.Write(
                Path.Combine("vanilla", "vanilla_reactions", "objects", "reaction_dyes.txt"),
                string.Join(
                    Environment.NewLine,
                    [
                        "[OBJECT:REACTION]",
                        .. SampleDyeReactionIds.Select(id => $"[REACTION:{id}]")
                    ]));
            _ = temp.Write(
                Path.Combine("vanilla", "vanilla_entities", "objects", "entity_default.txt"),
                string.Join(
                    Environment.NewLine,
                    [
                        "[OBJECT:ENTITY]",
                        .. SampleDyeReactionIds.Select(id => $"[PERMITTED_REACTION:{id}]")
                    ]));

            ModRawDependencyInfo entry = new()
            {
                ModId = "mod_test",
                NumericVersion = "1",
                DirectDefinitionIds = ["INORGANIC:ACTIVE_STONE"],
                PermittedBuildingIds = ["CARPENTER", "CUSTOM_BUILDING"],
                ReactionBuildingIds = ["SMELTER", "CUSTOM_BUILDING"],
                CutTargetIds = ["REACTION:WILLOW_BARK_DYE", "REACTION:PRESS_OIL_FRUIT"]
            };

            string cachePath = temp.Combine("metadata", "mod_raw_dependency_cache.json");
            _ = temp.Write(
                Path.GetRelativePath(temp.Path, cachePath),
                JsonSerializer.Serialize(
                    new { SchemaVersion = 5, Entries = new[] { entry } },
                    JsonOptions));

            string snapshotPath = temp.Combine("metadata", "mod_folder_snapshot.json");
            _ = temp.Write(
                Path.GetRelativePath(temp.Path, snapshotPath),
                JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            ModId = "mod_test",
                            ModName = "Test Mod",
                            Path = modPath
                        }
                    },
                    JsonOptions));

            string modListPath = temp.Combine("df", "dfhack-config", "mod-manager.json");
            _ = temp.Write(
                Path.GetRelativePath(temp.Path, modListPath),
                JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            @default = true,
                            name = "default",
                            modlist = new[] { new { id = "mod_test", version = 1 } }
                        }
                    },
                    JsonOptions));

            string dfPath = temp.CreateDirectory("df");
            string errorLogPath = temp.Combine("df", "errorlog.txt");
            _ = temp.Write(Path.GetRelativePath(temp.Path, errorLogPath), string.Empty);

            List<string> executableTokens =
            [
                .. DwarfFortressHardcodedTokens.Groups.SelectMany(group => group.Tokens),
                .. SampleDyeReactionIds
            ];
            WriteExecutable(GetDwarfFortressExecutablePath(dfPath), executableTokens);

            AuditOptions options = AuditOptions.Parse(
            [
                "--config", configPath,
                "--df", dfPath,
                "--cache", cachePath,
                "--vanilla", vanillaPath,
                "--modlist", modListPath,
                "--hardcoded-report",
                "--verify-hardcoded"
            ], out string? error);
            Assert.Null(error);

            HardcodedReport report = HardcodedReportRunner.Run(options);

            Assert.Equal(4, report.Catalog.Count);
            Assert.Equal(1, report.Inputs.ActiveModCount);
            Assert.Equal(1, report.Inputs.ScannedModCount);
            Assert.Equal(1, report.Inputs.ScannedFileCount);
            Assert.Empty(report.VanillaChecks);

            HardcodedCatalogGroupSummary dyeGroup = Assert.Single(
                report.Catalog,
                group => group.Id == HardcodedTokenGroupId.CutImmuneDyeReactions);
            Assert.Equal(SampleDyeReactionIds.Length, dyeGroup.Tokens.Count);
            Assert.Contains("WILLOW_BARK_DYE", dyeGroup.Tokens);

            Assert.Contains(report.Usage, usage =>
                usage.GroupId == HardcodedTokenGroupId.ReactionBuildings
                && usage.Token == "SMELTER");
            Assert.Contains(report.Usage, usage =>
                usage.GroupId == HardcodedTokenGroupId.CutImmuneDyeReactions
                && usage.Token == "WILLOW_BARK_DYE");
            Assert.Contains(report.Usage, usage =>
                usage.GroupId == HardcodedTokenGroupId.ConcreteBuiltInMaterials
                && usage.Token == "WATER");

            HardcodedInvalidContext invalid = Assert.Single(report.InvalidContexts);
            Assert.Equal("CARPENTER", invalid.Token);

            Assert.Contains(report.UnresolvedMaterials, reference =>
                reference.Token == "PLANT_MAT:MISSING_PLANT:WOOD");
            Assert.Contains(report.UnclassifiedMaterials, sample =>
                sample.Head == "WEIRD_MATERIAL");

            Assert.NotNull(report.Verification);
            Assert.True(report.Verification.IsClean);
            Assert.Equal(SampleDyeReactionIds.Length, report.Verification.DerivedCutIgnoredReactionCount);
            Assert.Equal(SampleDyeReactionIds.Length, report.Verification.ExecutableVanillaReactionCount);
            Assert.Equal(0, report.DriftCount);
        }

        private static string GetDwarfFortressExecutablePath(string dfFolder)
        {
            // Use the production locator's candidate list so the synthetic install always matches
            // the layout the report runner probes on this platform.
            IReadOnlyList<string> candidates = DwarfFortressExecutableLocator.GetCandidatePaths(dfFolder);
            Assert.NotEmpty(candidates);
            return candidates[0];
        }

        private static void WriteExecutable(string path, IEnumerable<string> tokens)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                _ = Directory.CreateDirectory(directory);

            List<byte> bytes = [];
            foreach (string token in tokens)
            {
                bytes.AddRange(Encoding.ASCII.GetBytes(token));
                bytes.Add(0);
            }

            File.WriteAllBytes(path, [.. bytes]);
        }
    }
}
