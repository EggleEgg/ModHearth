using ModHearth.Models;
using ModHearth.Utils.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Pins the static raw analysis that predicts Dwarf Fortress errorlog problems without
    /// launching the game. The analyzer only reasons about active mods plus the vanilla baseline.
    /// </summary>
    public class ModPossibleIssueAnalyzerTests
    {
        [Fact]
        public void MissingReferencesAreReportedOncePerTarget()
        {
            ModRawDependencyInfo mod = Info(
                "mod_a",
                definitions: ["CREATURE:TIGER"],
                selects: ["CREATURE:MISSING", "PLANT:MISSING_PLANT"],
                cuts: ["CREATURE:MISSING"],
                copyTagsFrom: ["CREATURE:MISSING_SOURCE"]);

            // CREATURE is known from the mod's own definition; PLANT is known because a vanilla
            // plant exists. Both types are analyzed, so the missing ids are reported.
            VanillaRawBaseline baseline = new([new ObjectKey("PLANT", "GRASS")]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], baseline);

            Assert.Equal(3, issues.Count);
            Assert.Contains(issues, issue => issue is { TargetKey: "CREATURE:MISSING", Kind: ModPossibleIssueKind.MissingSelectTarget });
            Assert.Contains(issues, issue => issue is { TargetKey: "PLANT:MISSING_PLANT", Kind: ModPossibleIssueKind.MissingSelectTarget });
            Assert.Contains(issues, issue => issue is { TargetKey: "CREATURE:MISSING_SOURCE", Kind: ModPossibleIssueKind.MissingCopyTagsFromSource });

            // The CUT of CREATURE:MISSING must not produce a second entry for the same target.
            Assert.Single(issues, issue => string.Equals(issue.TargetKey, "CREATURE:MISSING", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ReferencesResolvedByAnotherActiveModAreSilent()
        {
            ModRawDependencyInfo consumer = Info("mod_a", selects: ["CREATURE:SHARED"]);
            ModRawDependencyInfo provider = Info("mod_b", definitions: ["CREATURE:SHARED"]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                [consumer, provider],
                ["mod_a", "mod_b"],
                VanillaRawBaseline.Empty);

            Assert.Empty(issues);
        }

        [Fact]
        public void CutAfterDefinitionMakesPermittedReferenceMissing()
        {
            // CUT removes earlier definitions. For the reference kinds errorlog.txt can verify,
            // the analyzer follows the final merged raw set instead of accepting any definition.
            ModRawDependencyInfo consumer = Info("consumer", permittedReactions: ["SHARED_REACTION"]);
            ModRawDependencyInfo provider = Info("provider", definitions: ["REACTION:SHARED_REACTION"]);
            ModRawDependencyInfo cutter = Info("cutter", cuts: ["REACTION:SHARED_REACTION"]);

            ModPossibleIssue issue = Assert.Single(ModPossibleIssueAnalyzer.Analyze(
                [consumer, provider, cutter],
                ["consumer", "provider", "cutter"],
                VanillaRawBaseline.Empty));

            Assert.Equal("consumer", issue.ModId);
            Assert.Equal("REACTION:SHARED_REACTION", issue.TargetKey);
            Assert.Equal(ModPossibleIssueKind.MissingPermittedReaction, issue.Kind);
        }

        [Fact]
        public void CutBeforeDefinitionLeavesLaterDefinitionActive()
        {
            ModRawDependencyInfo consumer = Info("consumer", permittedReactions: ["SHARED_REACTION"]);
            ModRawDependencyInfo cutter = Info("cutter", cuts: ["REACTION:SHARED_REACTION"]);
            ModRawDependencyInfo provider = Info("provider", definitions: ["REACTION:SHARED_REACTION"]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                [consumer, cutter, provider],
                ["consumer", "cutter", "provider"],
                VanillaRawBaseline.Empty);

            Assert.Empty(issues);
        }

        [Fact]
        public void CutAndRedefinitionInTheSameModKeepsTheReplacement()
        {
            // The cache stores an unordered set per mod. Applying cuts before definitions lets a
            // replacer that cuts a vanilla object and defines its own replacement stay active.
            ModRawDependencyInfo consumer = Info("consumer", permittedReactions: ["SHARED_REACTION"]);
            ModRawDependencyInfo replacer = Info(
                "replacer",
                definitions: ["REACTION:SHARED_REACTION"],
                cuts: ["REACTION:SHARED_REACTION"]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                [consumer, replacer],
                ["consumer", "replacer"],
                VanillaRawBaseline.Empty);

            Assert.Empty(issues);
        }

        [Fact]
        public void HardcodedVanillaDyeCutsAreIgnored()
        {
            // DF silently ignores CUT_REACTION for the vanilla dye reactions. The baseline supplies
            // the raw-derived set, and the analyzer mirrors that exception so these cuts do not
            // become false positives.
            ModRawDependencyInfo consumer = Info(
                "consumer",
                permittedReactions: ["ALDER_CONE_DYE"]);
            ModRawDependencyInfo cutter = Info(
                "cutter",
                cuts: ["REACTION:ALDER_CONE_DYE"]);

            VanillaRawBaseline baseline = new(
                [new ObjectKey("REACTION", "ALDER_CONE_DYE")],
                ["ALDER_CONE_DYE"]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                [consumer, cutter],
                ["consumer", "cutter"],
                baseline);

            Assert.Empty(issues);
        }

        [Fact]
        public void ModAddedDyeCutsAreHonored()
        {
            // The exception is the hardcoded vanilla set, not every reaction whose id ends in _DYE.
            ModRawDependencyInfo consumer = Info(
                "consumer",
                permittedReactions: ["DCCF_DEEP_OAK_BARK_DYE"]);
            ModRawDependencyInfo provider = Info(
                "provider",
                definitions: ["REACTION:DCCF_DEEP_OAK_BARK_DYE"]);
            ModRawDependencyInfo cutter = Info(
                "cutter",
                cuts: ["REACTION:DCCF_DEEP_OAK_BARK_DYE"]);

            ModPossibleIssue issue = Assert.Single(ModPossibleIssueAnalyzer.Analyze(
                [consumer, provider, cutter],
                ["consumer", "provider", "cutter"],
                new VanillaRawBaseline([new ObjectKey("REACTION", "VANILLA_REACTION")])));

            Assert.Equal("REACTION:DCCF_DEEP_OAK_BARK_DYE", issue.TargetKey);
            Assert.Equal(ModPossibleIssueKind.MissingPermittedReaction, issue.Kind);
        }

        [Fact]
        public void CutVanillaReactionIsMissingUnlessAReloaderRestoresIt()
        {
            VanillaRawBaseline baseline = new([new ObjectKey("REACTION", "GLAZE_CRAFT")]);
            ModRawDependencyInfo consumer = Info("consumer", permittedReactions: ["GLAZE_CRAFT"]);
            ModRawDependencyInfo cutter = Info("cutter", cuts: ["REACTION:GLAZE_CRAFT"]);
            ModRawDependencyInfo replacer = Info(
                "replacer",
                definitions: ["REACTION:GLAZE_CRAFT"]);

            ModPossibleIssue issue = Assert.Single(ModPossibleIssueAnalyzer.Analyze(
                [consumer, cutter],
                ["consumer", "cutter"],
                baseline));
            Assert.Equal("REACTION:GLAZE_CRAFT", issue.TargetKey);

            Assert.Empty(ModPossibleIssueAnalyzer.Analyze(
                [consumer, cutter, replacer],
                ["consumer", "cutter", "replacer"],
                baseline));
        }

        [Fact]
        public void ExplicitLoadOrderOverridesInputOrderForCuts()
        {
            ModRawDependencyInfo consumer = Info("consumer", permittedReactions: ["SHARED_REACTION"]);
            ModRawDependencyInfo definition = Info("definition", definitions: ["REACTION:SHARED_REACTION"]);
            ModRawDependencyInfo cutter = Info("cutter", cuts: ["REACTION:SHARED_REACTION"]);

            // The input lists the definition last, but DF loads the cutter after it.
            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                [consumer, cutter, definition],
                ["consumer", "definition", "cutter"],
                VanillaRawBaseline.Empty,
                ["consumer", "definition", "cutter"]);

            Assert.Single(issues);
        }

        [Fact]
        public void ReferencesResolvedByVanillaAreSilent()
        {
            ModRawDependencyInfo mod = Info(
                "mod_a",
                selects: ["PLANT:VANILLA_PLANT"],
                copyTagsFrom: ["CREATURE:VANILLA_CREATURE"]);

            VanillaRawBaseline baseline = new(
            [
                new ObjectKey("PLANT", "VANILLA_PLANT"),
                new ObjectKey("CREATURE", "VANILLA_CREATURE")
            ]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], baseline);

            Assert.Empty(issues);
        }

        [Fact]
        public void DefinitionsFromInactiveModsDoNotResolveReferences()
        {
            ModRawDependencyInfo consumer = Info("mod_a", copyTagsFrom: ["CREATURE:SHARED"]);
            ModRawDependencyInfo inactiveProvider = Info("mod_b", definitions: ["CREATURE:SHARED"]);

            // Vanilla defines CREATURE, so the reference type is analyzed even though the only mod
            // definition that could satisfy it belongs to an inactive mod.
            VanillaRawBaseline baseline = new([new ObjectKey("CREATURE", "DWARF")]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                [consumer, inactiveProvider],
                ["mod_a"],
                baseline);

            ModPossibleIssue issue = Assert.Single(issues);
            Assert.Equal("mod_a", issue.ModId);
            Assert.Equal("CREATURE:SHARED", issue.TargetKey);
            Assert.Equal(ModPossibleIssueKind.MissingCopyTagsFromSource, issue.Kind);
        }

        [Fact]
        public void NullActiveFilterConsidersEveryScannedMod()
        {
            ModRawDependencyInfo consumer = Info("mod_a", selects: ["CREATURE:SHARED"]);
            ModRawDependencyInfo provider = Info("mod_b", definitions: ["CREATURE:SHARED"]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                [consumer, provider],
                activeModIds: null,
                VanillaRawBaseline.Empty);

            Assert.Empty(issues);
        }

        [Fact]
        public void VanillaBaselineCompositeLookupMatchesObjectKeys()
        {
            VanillaRawBaseline baseline = new([new ObjectKey("CREATURE", "DWARF")]);

            Assert.True(baseline.Contains("CREATURE:DWARF"));
            Assert.True(baseline.Contains("creature:dwarf"));
            Assert.False(baseline.Contains("CREATURE:ELF"));
            Assert.False(baseline.Contains("DWARF"));
            Assert.False(baseline.Contains(":DWARF"));

            Assert.True(ObjectKey.TryParse("CREATURE:DWARF", out string objectType, out string id));
            Assert.Equal("CREATURE", objectType);
            Assert.Equal("DWARF", id);
            Assert.False(ObjectKey.TryParse("DWARF", out _, out _));
            Assert.False(ObjectKey.TryParse(null, out _, out _));
        }

        [Fact]
        public void NestedSelectorTypesAreIgnored()
        {
            // SELECT_CASTE/SELECT_MATERIAL/SELECT_TISSUE_LAYER/SYMBOL name tokens inside an object,
            // not top-level objects. Since no active mod or vanilla raw defines those types, they
            // must not be reported as missing objects.
            ModRawDependencyInfo mod = Info(
                "mod_a",
                selects: ["CASTE:ALL", "MATERIAL:PARCHMENT", "TISSUE_LAYER:HEART:BY_CATEGORY:ALL", "SYMBOL:TREE"],
                cuts: ["TISSUE:HAIR"]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], VanillaRawBaseline.Empty);

            Assert.Empty(issues);
        }

        [Fact]
        public void ReferencesOfATypeDefinedByVanillaAreStillChecked()
        {
            // The gate only skips reference types nothing defines; a known type with an unknown id
            // is still a missing object.
            ModRawDependencyInfo mod = Info("mod_a", cuts: ["CREATURE:MISSING_BEAST"]);
            VanillaRawBaseline baseline = new([new ObjectKey("CREATURE", "DWARF")]);

            ModPossibleIssue issue = Assert.Single(ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], baseline));

            Assert.Equal("CREATURE:MISSING_BEAST", issue.TargetKey);
            Assert.Equal(ModPossibleIssueKind.MissingCutTarget, issue.Kind);
        }

        [Fact]
        public void UnresolvedPermittedAndReactionBuildingTargetsAreReported()
        {
            ModRawDependencyInfo mod = Info(
                "mod_a",
                permittedReactions: ["MISSING_REACTION"],
                permittedBuildings: ["MISSING_WORKSHOP"],
                reactionBuildings: ["MISSING_REACTION_WORKSHOP"]);

            // The baseline defines each reference type, so the checks run even though the ids are
            // unknown. Without a known type the analyzer deliberately stays quiet.
            VanillaRawBaseline baseline = new(
            [
                new ObjectKey("REACTION", "VANILLA_REACTION"),
                new ObjectKey("BUILDING", "VANILLA_WORKSHOP")
            ]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], baseline);

            Assert.Equal(3, issues.Count);
            Assert.Contains(issues, issue => issue is { TargetKey: "REACTION:MISSING_REACTION", Kind: ModPossibleIssueKind.MissingPermittedReaction });
            Assert.Contains(issues, issue => issue is { TargetKey: "BUILDING:MISSING_WORKSHOP", Kind: ModPossibleIssueKind.MissingPermittedBuilding });
            Assert.Contains(issues, issue => issue is { TargetKey: "BUILDING:MISSING_REACTION_WORKSHOP", Kind: ModPossibleIssueKind.MissingReactionBuilding });
        }

        [Fact]
        public void PermittedReferencesResolvedByActiveModsOrVanillaAreSilent()
        {
            ModRawDependencyInfo consumer = Info(
                "mod_a",
                permittedReactions: ["SHARED_REACTION", "VANILLA_REACTION"],
                permittedBuildings: ["SHARED_WORKSHOP", "VANILLA_WORKSHOP"],
                reactionBuildings: ["VANILLA_WORKSHOP"]);
            ModRawDependencyInfo provider = Info(
                "mod_b",
                definitions: ["REACTION:SHARED_REACTION", "BUILDING:SHARED_WORKSHOP"]);

            VanillaRawBaseline baseline = new(
            [
                new ObjectKey("REACTION", "VANILLA_REACTION"),
                new ObjectKey("BUILDING", "VANILLA_WORKSHOP")
            ]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze(
                [consumer, provider],
                ["mod_a", "mod_b"],
                baseline);

            Assert.Empty(issues);
        }

        [Fact]
        public void PermittedReferencesAreIgnoredWhenNothingDefinesTheirObjectType()
        {
            ModRawDependencyInfo mod = Info(
                "mod_a",
                permittedReactions: ["MISSING_REACTION"],
                permittedBuildings: ["MISSING_WORKSHOP"],
                reactionBuildings: ["MISSING_REACTION_WORKSHOP"]);

            List<ModPossibleIssue> issues = ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], VanillaRawBaseline.Empty);

            Assert.Empty(issues);
        }

        [Fact]
        public void BuiltInWorkshopAndFurnaceIdsAreValidReactionBuildings()
        {
            // Vanilla reactions reference DF's hardcoded workshops and furnaces with
            // [BUILDING:CARPENTER] and friends. No raw file defines those ids, but DF resolves
            // them against its built-in building types instead of reporting "Unrecognized
            // Building Token". These are exactly the ids vanilla_reactions false-positived on.
            ModRawDependencyInfo mod = Info(
                "mod_a",
                reactionBuildings:
                [
                    "ASHERY", "CARPENTER", "CRAFTSMAN", "DYER", "FARMER", "KILN",
                    "KITCHEN", "MILLSTONE", "QUERN", "SMELTER", "STILL", "TANNER",

                    // furnace_type ids that vanilla does not currently reference.
                    "WOOD", "GLASS", "SMELTER_LAVA", "GLASS_LAVA", "KILN_LAVA"
                ]);

            VanillaRawBaseline baseline = new([new ObjectKey("BUILDING", "SOAP_MAKER")]);

            Assert.Empty(ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], baseline));
        }

        [Fact]
        public void UnknownReactionBuildingIsStillReported()
        {
            ModRawDependencyInfo mod = Info("mod_a", reactionBuildings: ["MAGMA_DREDGER"]);
            VanillaRawBaseline baseline = new([new ObjectKey("BUILDING", "SOAP_MAKER")]);

            ModPossibleIssue issue = Assert.Single(ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], baseline));

            Assert.Equal("BUILDING:MAGMA_DREDGER", issue.TargetKey);
            Assert.Equal(ModPossibleIssueKind.MissingReactionBuilding, issue.Kind);
        }

        [Fact]
        public void BuiltInWorkshopIdsAreNotValidPermittedBuildings()
        {
            // PERMITTED_BUILDING names a mod-defined custom building, so DF's hardcoded
            // workshop ids are not accepted there even though [BUILDING:CARPENTER] is.
            ModRawDependencyInfo mod = Info("mod_a", permittedBuildings: ["CARPENTER"]);
            VanillaRawBaseline baseline = new([new ObjectKey("BUILDING", "SOAP_MAKER")]);

            ModPossibleIssue issue = Assert.Single(ModPossibleIssueAnalyzer.Analyze([mod], ["mod_a"], baseline));

            Assert.Equal("BUILDING:CARPENTER", issue.TargetKey);
            Assert.Equal(ModPossibleIssueKind.MissingPermittedBuilding, issue.Kind);
        }

        private static ModRawDependencyInfo Info(
            string modId,
            string[]? definitions = null,
            string[]? selects = null,
            string[]? cuts = null,
            string[]? copyTagsFrom = null,
            string[]? permittedReactions = null,
            string[]? permittedBuildings = null,
            string[]? reactionBuildings = null) => new()
            {
                ModId = modId,
                DirectDefinitionIds = [.. definitions ?? []],
                SelectTargetIds = [.. selects ?? []],
                CutTargetIds = [.. cuts ?? []],
                CopyTagsFromSourceIds = [.. copyTagsFrom ?? []],
                PermittedReactionIds = [.. permittedReactions ?? []],
                PermittedBuildingIds = [.. permittedBuildings ?? []],
                ReactionBuildingIds = [.. reactionBuildings ?? []]
            };
    }
}
