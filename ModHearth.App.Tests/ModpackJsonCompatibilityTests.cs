using System.Text.Json;
using ModHearth.Models;
using ModHearth.Utilities;
using Xunit;

namespace ModHearth.App.Tests
{
    public class ModpackJsonCompatibilityTests
    {
        private const string DfHackJson = """
            [{"default": true, "modlist": [{"id": "vanilla", "version": 51}, {"id": "my_mod", "version": 3}], "name": "My pack"}]
            """;

        [Fact]
        public void ReadsDfHackFormat()
        {
            List<DfhModpack> packs = JsonSerializer.Deserialize<List<DfhModpack>>(DfHackJson, JsonFileStore.CaseInsensitive)!;
            Assert.Equal("My pack", packs[0].Name);
            Assert.True(packs[0].Default);
            Assert.Equal(2, packs[0].Modlist.Count);
            Assert.Equal(3, packs[0].Modlist[1].Version);
        }

        [Fact]
        public void ReadsPascalCaseFilesWrittenByTheBrokenBuild()
        {
            string json = """[{"Default": true, "Modlist": [{"Id": "vanilla", "Version": 51}], "Name": "Old"}]""";
            List<DfhModpack> packs = JsonSerializer.Deserialize<List<DfhModpack>>(json, JsonFileStore.CaseInsensitive)!;
            Assert.Equal("Old", packs[0].Name);
            Assert.Single(packs[0].Modlist);
        }

        [Fact]
        public void WritesDfHackFormat()
        {
            string json = JsonFileStore.Serialize(new List<DfhModpack> { new(true, [new DfhMod("vanilla", 51)], "p") });
            Assert.Contains("\"modlist\"", json);
            Assert.DoesNotContain("\"Modlist\"", json);
            Assert.Contains("\"default\"", json);
            Assert.Contains("\"name\"", json);
            Assert.Contains("\"version\"", json);
        }
    }
}
