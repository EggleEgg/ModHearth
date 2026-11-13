using ModHearth.Models;
using ModHearth.Utils.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Pins the duplicate-raw heuristic behind the cache-based warning. DF reports collisions
    /// between mods and the official raw folders through the same "Duplicate Object" line, a
    /// CUT of the id suppresses the collision, and a vanilla source mod never collides with the
    /// baseline it defines.
    /// </summary>
    public class ModDuplicateDefinitionAnalyzerTests
    {
        [Fact]
        public void CollisionsBetweenActiveModsAreReported()
        {
            ModRawDependencyInfo modA = Info("mod_a", "PLANT:FLAX");
            ModRawDependencyInfo modB = Info("mod_b", "PLANT:FLAX");

            ModDuplicateDefinitionGroup group = Assert.Single(
                ModDuplicateDefinitionAnalyzer.Analyze([modA, modB], ["mod_a", "mod_b"], VanillaRawBaseline.Empty));

            Assert.Equal("PLANT:FLAX", group.DefinitionKey);
            Assert.Equal(["mod_a", "mod_b"], group.ModIds);
            Assert.False(group.IncludesVanilla);
        }

        [Fact]
        public void VanillaCollisionAddsTheSyntheticOffender()
        {
            ModRawDependencyInfo mod = Info("mod_a", "PLANT:FLAX");
            VanillaRawBaseline baseline = new([new ObjectKey("PLANT", "FLAX")]);

            ModDuplicateDefinitionGroup group = Assert.Single(
                ModDuplicateDefinitionAnalyzer.Analyze([mod], ["mod_a"], baseline));

            Assert.Equal("PLANT:FLAX", group.DefinitionKey);
            Assert.Equal(["mod_a"], group.ModIds);
            Assert.True(group.IncludesVanilla);
        }

        [Fact]
        public void AnActiveCutSuppressesTheCollision()
        {
            ModRawDependencyInfo modA = Info("mod_a", "PLANT:FLAX");
            ModRawDependencyInfo modB = Info("mod_b", "PLANT:FLAX", cuts: ["PLANT:FLAX"]);

            Assert.Empty(ModDuplicateDefinitionAnalyzer.Analyze(
                [modA, modB],
                ["mod_a", "mod_b"],
                VanillaRawBaseline.Empty));
        }

        [Fact]
        public void InactiveModsAndInactiveCutsAreIgnored()
        {
            ModRawDependencyInfo active = Info("mod_a", "PLANT:FLAX");
            ModRawDependencyInfo inactive = Info("mod_b", "PLANT:FLAX", cuts: ["PLANT:FLAX"]);

            // Only one active mod defines the id, so it is not a collision. The inactive mod's
            // definition and CUT must not change that.
            Assert.Empty(ModDuplicateDefinitionAnalyzer.Analyze([active, inactive], ["mod_a"], VanillaRawBaseline.Empty));
        }

        [Fact]
        public void ADuplicateDefinitionInsideOneModIsNotACollision()
        {
            ModRawDependencyInfo mod = Info("mod_a", "PLANT:FLAX", "PLANT:FLAX");

            Assert.Empty(ModDuplicateDefinitionAnalyzer.Analyze([mod], ["mod_a"], VanillaRawBaseline.Empty));
        }

        [Fact]
        public void VanillaSourceModsDoNotCollideWithTheirOwnBaseline()
        {
            ModRawDependencyInfo vanilla = Info("vanilla_entities", "ENTITY:EVIL");
            VanillaRawBaseline baseline = new([new ObjectKey("ENTITY", "EVIL")]);

            // The baseline is loaded from this mod's own folder, so defining EVIL is not a duplicate.
            Assert.Empty(ModDuplicateDefinitionAnalyzer.Analyze(
                [vanilla],
                ["vanilla_entities"],
                baseline,
                vanillaSourceModIds: ["vanilla_entities"]));
        }

        [Fact]
        public void NonVanillaModRedefiningVanillaIsStillReported()
        {
            ModRawDependencyInfo vanilla = Info("vanilla_entities", "ENTITY:EVIL");
            ModRawDependencyInfo mod = Info("playable_races", "ENTITY:EVIL");
            VanillaRawBaseline baseline = new([new ObjectKey("ENTITY", "EVIL")]);

            ModDuplicateDefinitionGroup group = Assert.Single(ModDuplicateDefinitionAnalyzer.Analyze(
                [vanilla, mod],
                ["vanilla_entities", "playable_races"],
                baseline,
                vanillaSourceModIds: ["vanilla_entities"]));

            Assert.Equal(["playable_races", "vanilla_entities"], group.ModIds);
            Assert.True(group.IncludesVanilla);
        }

        [Fact]
        public void DuplicatesBetweenVanillaSourceModsAreStillReported()
        {
            ModRawDependencyInfo first = Info("vanilla_entities", "ENTITY:EVIL");
            ModRawDependencyInfo second = Info("vanilla_entities_copy", "ENTITY:EVIL");
            VanillaRawBaseline baseline = new([new ObjectKey("ENTITY", "EVIL")]);

            ModDuplicateDefinitionGroup group = Assert.Single(ModDuplicateDefinitionAnalyzer.Analyze(
                [first, second],
                ["vanilla_entities", "vanilla_entities_copy"],
                baseline,
                vanillaSourceModIds: ["vanilla_entities", "vanilla_entities_copy"]));

            Assert.Equal(["vanilla_entities", "vanilla_entities_copy"], group.ModIds);
            Assert.False(group.IncludesVanilla);
        }

        private static ModRawDependencyInfo Info(string modId, params string[] definitions) => new()
        {
            ModId = modId,
            DirectDefinitionIds = [.. definitions],
            CutTargetIds = []
        };

        private static ModRawDependencyInfo Info(string modId, string definition, string[] cuts) => new()
        {
            ModId = modId,
            DirectDefinitionIds = [definition],
            CutTargetIds = [.. cuts]
        };
    }
}
