using ModHearth.Models;
using ModHearth.Utilities.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Pins the duplicate-rawware heuristic behind the cache-based warning. DF reports collisions
    /// between mods and the official raw folders through the same "Duplicate Object" line, and a
    /// CUT of the id suppresses the collision.
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
