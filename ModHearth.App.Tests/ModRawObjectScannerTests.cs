using ModHearth.Models;
using ModHearth.Utilities.ModRaws;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Pins the extra raw tokens the scanner must capture for the static issue search: building
    /// definitions live below <c>[OBJECT:BUILDING]</c>, and permitted/building references are bare
    /// ids rather than "ObjectType:Id" keys.
    /// </summary>
    public class ModRawObjectScannerTests
    {
        [Fact]
        public void CapturesBuildingDefinitionsAndBareReferences()
        {
            string tempRoot = Directory.CreateTempSubdirectory("modhearth-scanner-").FullName;
            try
            {
                string objectsDir = Directory.CreateDirectory(Path.Combine(tempRoot, "objects")).FullName;
                File.WriteAllText(Path.Combine(objectsDir, "objects.txt"), """
                    [OBJECT:BUILDING]
                    [BUILDING_WORKSHOP:MY_WORKSHOP]
                    [NAME:My workshop]

                    [OBJECT:BUILDING]
                    [BUILDING_FURNACE:MY_FURNACE]
                    [NAME:My furnace]

                    [OBJECT:REACTION]
                    [REACTION:MAKE_THING]
                    [BUILDING:MY_WORKSHOP:NONE]

                    [OBJECT:ITEM]
                    [ITEM_WEAPON:ITEM_WEAPON_TEST_SPEAR]

                    [OBJECT:ENTITY]
                    [ENTITY:MY_ENTITY]
                    [PERMITTED_REACTION:MAKE_THING]
                    [PERMITTED_REACTION:MISSING_REACTION]
                    [PERMITTED_BUILDING:MY_WORKSHOP]
                    """);

                RawDatabase database = ModRawObjectScanner.Scan(tempRoot, "test_mod");
                ModRawDependencyInfo info = database.ToDependencyInfo("test_mod", "1", 0, null);

                Assert.Contains("BUILDING:MY_WORKSHOP", info.DirectDefinitionIds);
                Assert.Contains("BUILDING:MY_FURNACE", info.DirectDefinitionIds);
                Assert.Contains("REACTION:MAKE_THING", info.DirectDefinitionIds);
                Assert.Contains("ITEM:ITEM_WEAPON_TEST_SPEAR", info.DirectDefinitionIds);
                Assert.Contains("ENTITY:MY_ENTITY", info.DirectDefinitionIds);

                Assert.Contains("MY_WORKSHOP", info.ReactionBuildingIds);
                Assert.Contains("MY_WORKSHOP", info.PermittedBuildingIds);
                Assert.Contains("MAKE_THING", info.PermittedReactionIds);
                Assert.Contains("MISSING_REACTION", info.PermittedReactionIds);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public void ReactionBuildingDestroyerTokenIsNotTreatedAsABuildingReference()
        {
            string tempRoot = Directory.CreateTempSubdirectory("modhearth-scanner-").FullName;
            try
            {
                string objectsDir = Directory.CreateDirectory(Path.Combine(tempRoot, "objects")).FullName;
                File.WriteAllText(Path.Combine(objectsDir, "creature.txt"), """
                    [OBJECT:CREATURE]
                    [CREATURE:MY_BEAST]
                    [BUILDINGDESTROYER:1]
                    """);

                ModRawDependencyInfo info = ModRawObjectScanner
                    .Scan(tempRoot, "test_mod")
                    .ToDependencyInfo("test_mod", "1", 0, null);

                Assert.Empty(info.ReactionBuildingIds);
                Assert.Empty(info.PermittedBuildingIds);
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
