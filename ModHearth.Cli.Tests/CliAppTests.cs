using System.Text;
using ModHearth.Cli;
using Xunit;

namespace ModHearth.Cli.Tests
{
    public class CliAppTests
    {
        [Fact]
        public void VersionFlagPrints()
        {
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            using StringWriter output = new StringWriter(outputBuilder);
            using StringWriter error = new StringWriter(errorBuilder);

            int code = CliApp.Run(new[] { "--version" }, output, error);

            Assert.Equal(0, code);
            Assert.Contains("ModHearth CLI", outputBuilder.ToString());
            Assert.True(string.IsNullOrWhiteSpace(errorBuilder.ToString()));
        }

        [Fact]
        public void ListPacksReadsModManager()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "modhearth-test-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            string modManagerPath = Path.Combine(tempDir, "mod-manager.json");

            string json = "[" +
                          "{\"default\":true,\"modlist\":[{\"id\":\"foo\",\"version\":1}],\"name\":\"Pack A\"}," +
                          "{\"default\":false,\"modlist\":[{\"id\":\"bar\",\"version\":2}],\"name\":\"Pack B\"}" +
                          "]";
            File.WriteAllText(modManagerPath, json);

            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            using StringWriter output = new StringWriter(outputBuilder);
            using StringWriter error = new StringWriter(errorBuilder);

            int code = CliApp.Run(new[] { "list-packs", "--mod-manager", modManagerPath }, output, error);

            Assert.Equal(0, code);
            string outputText = outputBuilder.ToString();
            Assert.Contains("Pack A", outputText);
            Assert.Contains("Pack B", outputText);
            Assert.Contains("*", outputText);
            Assert.True(string.IsNullOrWhiteSpace(errorBuilder.ToString()));
        }

        [Fact]
        public void SetDefaultUpdatesFile()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "modhearth-test-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            string modManagerPath = Path.Combine(tempDir, "mod-manager.json");

            string json = "[" +
                          "{\"default\":true,\"modlist\":[],\"name\":\"Pack A\"}," +
                          "{\"default\":false,\"modlist\":[],\"name\":\"Pack B\"}" +
                          "]";
            File.WriteAllText(modManagerPath, json);

            using StringWriter output = new StringWriter();
            using StringWriter error = new StringWriter();

            int code = CliApp.Run(new[] { "set-default", "--mod-manager", modManagerPath, "--pack", "Pack B" }, output, error);

            Assert.Equal(0, code);
            string updatedJson = File.ReadAllText(modManagerPath);
            Assert.Contains("\"name\": \"Pack B\"", updatedJson);
            Assert.Contains("\"default\": true", updatedJson);
        }
    }
}
