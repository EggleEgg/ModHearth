using ModHearth.Utils.Steam;
using Xunit;

namespace ModHearth.App.Tests
{
    public class SteamCmdOutputParserTests
    {
        [Theory]
        [InlineData("Update state (0x61) downloading, progress: 85.86 (120 / 140)", 85.86)]
        [InlineData("progress: 0.00 (0 / 0)", 0)]
        [InlineData("PROGRESS: 100.0 (140 / 140)", 100)]
        [InlineData("Update state (0x81) verifying install, progress: 12 (12 / 100)", 12)]
        public void ParsesSteamCmdProgressLines(string line, double expected)
        {
            Assert.True(SteamCmdOutputParser.TryParseProgressPercent(line, out double percent));
            Assert.Equal(expected, percent, precision: 6);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Loading Steam API...OK")]
        [InlineData("progress: waiting for server")]
        public void RejectsLinesWithoutAProgressNumber(string? line)
        {
            Assert.False(SteamCmdOutputParser.TryParseProgressPercent(line, out double percent));
            Assert.Equal(0, percent);
        }

        [Theory]
        [InlineData("[  0%] Checking for available updates...", 0, "Checking for available updates...")]
        [InlineData("[ 42%] Downloading update (15,360 of 36,457 KB)...", 42, "Downloading update (15,360 of 36,457 KB)...")]
        [InlineData("[100%] Download complete.", 100, "Download complete.")]
        public void ParsesBootstrapUpdaterProgressLines(string line, double expectedPercent, string expectedMessage)
        {
            Assert.True(SteamCmdOutputParser.TryParseBracketLine(line, out double? percent, out string message));
            Assert.Equal(expectedPercent, percent);
            Assert.Equal(expectedMessage, message);
        }

        [Fact]
        public void ParsesIndeterminateBootstrapUpdaterStages()
        {
            Assert.True(SteamCmdOutputParser.TryParseBracketLine("[----] Installing update...", out double? percent, out string message));
            Assert.Null(percent);
            Assert.Equal("Installing update...", message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Loading Steam API...OK")]
        [InlineData("[ERR] something went wrong")]
        public void RejectsLinesWithoutABootstrapUpdaterPrefix(string? line)
        {
            Assert.False(SteamCmdOutputParser.TryParseBracketLine(line, out double? percent, out string message));
            Assert.Null(percent);
            Assert.Equal(string.Empty, message);
        }
    }
}
