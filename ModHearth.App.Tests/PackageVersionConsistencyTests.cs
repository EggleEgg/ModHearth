using System.Text.RegularExpressions;
using Xunit;

namespace ModHearth.App.Tests;

/// <summary>
/// Prevents SkiaSharp.NativeAssets.* from drifting away from the managed SkiaSharp package version. NuGet does not enforce these stay in lockstep on its own.
/// An explicit version pin on one native asset package without pinning the managed package (or pinning it to a different version) ships
/// a native library whose ABI doesn't match what the managed wrapper expects.
/// </summary>
public class PackageVersionConsistencyTests
{
    private static readonly Regex PackageReferenceRegex = new(
        @"<PackageReference\s+Include=""(?<id>[^""]+)""\s+Version=""(?<version>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void SkiaSharp_Native_Asset_Packages_Match_Managed_SkiaSharp_Version()
    {
        string csprojPath = FindMainCsprojPath();
        string content = File.ReadAllText(csprojPath);

        Dictionary<string, string> skiaPackages = PackageReferenceRegex
            .Matches(content)
            .Select(m => (Id: m.Groups["id"].Value, Version: m.Groups["version"].Value))
            .Where(p => p.Id.Equals("SkiaSharp", StringComparison.OrdinalIgnoreCase) ||
                        p.Id.StartsWith("SkiaSharp.NativeAssets.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Id, p => p.Version, StringComparer.OrdinalIgnoreCase);

        Assert.True(skiaPackages.ContainsKey("SkiaSharp"),
            "Expected an explicit <PackageReference Include=\"SkiaSharp\" .../> in ModHearth.csproj " +
            "so native asset packages have a fixed version to be checked against.");

        string expectedVersion = skiaPackages["SkiaSharp"];

        foreach ((string id, string version) in skiaPackages)
        {
            if (id.Equals("SkiaSharp", StringComparison.OrdinalIgnoreCase))
                continue;

            Assert.True(version == expectedVersion,
                $"{id} is pinned to {version}, but managed SkiaSharp is pinned to {expectedVersion}. " +
                "These must match exactly or the wrong native library ships for that platform.");
        }
    }

    private static string FindMainCsprojPath()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "ModHearth.csproj");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate ModHearth.csproj by walking up from the test output directory.");
    }
}