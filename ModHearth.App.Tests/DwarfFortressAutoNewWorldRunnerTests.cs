using ModHearth.Utils.DwarfFortress;
using Xunit;

namespace ModHearth.App.Tests
{
    public class DwarfFortressAutoNewWorldRunnerTests
    {
        [Theory]
        [InlineData("RESULT=clicked", "clicked")]
        [InlineData("noise\nRESULT=already-open\n", "already-open")]
        [InlineData("RESULT=waiting", "waiting")]
        [InlineData("RESULT=game", "game")]
        [InlineData("RESULT=CLICKED", "clicked")]
        public void ParseResultReadsTheScriptContract(string output, string expected)
        {
            Assert.Equal(expected, DwarfFortressAutoNewWorldRunner.ParseResult(output));
        }

        [Theory]
        [InlineData("")]
        [InlineData("no result line")]
        [InlineData("RESULT=")]
        public void ParseResultRejectsMissingValues(string output)
        {
            Assert.Null(DwarfFortressAutoNewWorldRunner.ParseResult(output));
        }
    }
}
