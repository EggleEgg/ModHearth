using ModHearth.Utils.DwarfFortress;
using Xunit;

namespace ModHearth.App.Tests
{
    public class DwarfFortressVersionDetectorTests
    {
        [Theory]
        [InlineData("Release notes for 53.16 (August 5, 2026):", "53.16")]
        [InlineData("Release notes for 0.47.05 (January 28, 2021):", "0.47.05")]
        [InlineData("Release notes for v50.08 (December 19, 2025):", "50.08")]
        public void ParseDwarfFortressVersionFromReleaseNotesReadsCurrentRelease(string text, string expected)
            => Assert.Equal(expected, DwarfFortressVersionDetector.ParseDwarfFortressVersionFromReleaseNotes(text));

        [Fact]
        public void ParseDwarfFortressVersionFromFileChangesReadsCurrentRelease()
            => Assert.Equal(
                "53.16",
                DwarfFortressVersionDetector.ParseDwarfFortressVersionFromFileChanges(
                    "See readme.txt for details.\nAuxiliary file changes for 53.16:\n"));

        [Fact]
        public void ParseDwarfFortressVersionFromVanillaInfoReadsDisplayedVersion()
            => Assert.Equal(
                "53.16",
                DwarfFortressVersionDetector.ParseDwarfFortressVersionFromVanillaInfo(
                    "[ID:vanilla_creatures]\n[NUMERIC_VERSION:5316]\n[DISPLAYED_VERSION:53.16]\n"));

        [Fact]
        public void ParseDFHackVersionFromNewsSkipsTheFutureSection()
        {
            const string news =
                "DFHack future\n" +
                "=============\n\n" +
                "Documentation\n" +
                "-------------\n\n" +
                "DFHack 53.16-r3\n" +
                "===============\n\n" +
                "New Tools\n" +
                "---------\n";

            Assert.Equal("53.16-r3", DwarfFortressVersionDetector.ParseDFHackVersionFromNews(news));
        }

        [Fact]
        public void ParseDFHackVersionFromDocsReadsHtmlHeading()
            => Assert.Equal(
                "53.16-r3",
                DwarfFortressVersionDetector.ParseDFHackVersionFromDocs("<h1 id=\"dfhack-53-16-r3\">DFHack 53.16-r3</h1>"));

        [Theory]
        [InlineData("53.16-r3", "53.16")]
        [InlineData("0.47.05-r5", "0.47.05")]
        [InlineData("garbage", null)]
        [InlineData(null, null)]
        public void DeriveDwarfFortressVersionFromDFHackVersionUsesReleasePrefix(string? dfHackVersion, string? expected)
            => Assert.Equal(expected, DwarfFortressVersionDetector.DeriveDwarfFortressVersionFromDFHackVersion(dfHackVersion));

        [Fact]
        public void DetectReadsSeparateDfAndDFHackInstallations()
        {
            using TempInstallation installation = new();
            installation.WriteToDf("release notes.txt", "Release notes for 53.16 (August 5, 2026):\n");
            installation.WriteToDfHack("news.rst", "DFHack future\n=============\n\nDFHack 53.16-r3\n===============\n");

            DwarfFortressVersionInfo versions =
                DwarfFortressVersionDetector.Detect(installation.DfFolder, installation.DfHackFolder);

            Assert.Equal("53.16", versions.DwarfFortressVersion);
            Assert.Equal("53.16-r3", versions.DFHackVersion);
        }

        [Fact]
        public void DetectFallsBackToVanillaInfoWhenReleaseDocsAreMissing()
        {
            using TempInstallation installation = new();
            installation.WriteToDf("data/vanilla/vanilla_creatures/info.txt", "[DISPLAYED_VERSION:53.16]\n");

            DwarfFortressVersionInfo versions =
                DwarfFortressVersionDetector.Detect(installation.DfFolder, installation.DfHackFolder);

            Assert.Equal("53.16", versions.DwarfFortressVersion);
            Assert.Null(versions.DFHackVersion);
        }

        [Fact]
        public void DetectDerivesDfVersionFromDFHackWhenDfMetadataIsMissing()
        {
            using TempInstallation installation = new();
            installation.WriteToDfHack("news.rst", "DFHack 53.16-r3\n===============\n");

            DwarfFortressVersionInfo versions =
                DwarfFortressVersionDetector.Detect(installation.DfFolder, installation.DfHackFolder);

            Assert.Equal("53.16", versions.DwarfFortressVersion);
            Assert.Equal("53.16-r3", versions.DFHackVersion);
        }

        [Fact]
        public void DetectPrefersDFHackVersionPrefixOverStaleVanillaMetadata()
        {
            using TempInstallation installation = new();
            installation.WriteToDf("data/vanilla/vanilla_creatures/info.txt", "[DISPLAYED_VERSION:53.15]\n");
            installation.WriteToDfHack("news.rst", "DFHack 53.16-r3\n===============\n");

            DwarfFortressVersionInfo versions =
                DwarfFortressVersionDetector.Detect(installation.DfFolder, installation.DfHackFolder);

            Assert.Equal("53.16", versions.DwarfFortressVersion);
        }

        [Fact]
        public void DetectAcceptsDFHackFolderPointingAtHackSubfolder()
        {
            using TempInstallation installation = new();
            installation.WriteToDfHack("news.rst", "DFHack 53.16-r3\n===============\n");

            DwarfFortressVersionInfo versions = DwarfFortressVersionDetector.Detect(
                installation.DfFolder,
                Path.Combine(installation.DfHackFolder, "hack"));

            Assert.Equal("53.16-r3", versions.DFHackVersion);
        }

        [Fact]
        public void DetectReturnsEmptyWhenFoldersDoNotExist()
            => Assert.Equal(
                DwarfFortressVersionInfo.Empty,
                DwarfFortressVersionDetector.Detect(
                    Path.Combine(Path.GetTempPath(), $"missing-df-{Guid.NewGuid():N}"),
                    Path.Combine(Path.GetTempPath(), $"missing-dfhack-{Guid.NewGuid():N}")));

        private sealed class TempInstallation : IDisposable
        {
            private readonly TempFolder temp = new("ModHearthVersionTests");

            public string DfFolder => temp.Combine("Dwarf Fortress");
            public string DfHackFolder => temp.Combine("DFHack");

            public void WriteToDf(string relativePath, string contents)
                => temp.Write(Path.Combine("Dwarf Fortress", relativePath), contents);

            public void WriteToDfHack(string relativePath, string contents)
                => temp.Write(Path.Combine("DFHack", relativePath), contents);

            public void Dispose() => temp.Dispose();
        }
    }
}
