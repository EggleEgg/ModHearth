using ModHearth.Models;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// The issue-search toggles are persisted with the rest of the config and both searches are
    /// opt-out: existing installs keep analyzing errorlogs and start using the static raw scan.
    /// </summary>
    public class IssueSearchConfigTests
    {
        [Fact]
        public void IssueSearchesAreEnabledByDefault()
        {
            ModHearthConfig config = new();

            Assert.True(config.SearchPossibleIssues);
            Assert.True(config.SearchErrorLogIssues);
        }
    }
}
