using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace ModHearth.Tests;

public class ModIdentityAndInfoTests
{
    [Fact]
    public void DfhMod_Equality_Is_CaseInsensitive_For_Id()
    {
        DFHMod upper = new DFHMod("Example_Mod", 123);
        DFHMod lower = new DFHMod("example_mod", 123);

        Assert.True(upper == lower);
        Assert.Equal(upper, lower);
        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());

        HashSet<DFHMod> pool = new HashSet<DFHMod> { upper };
        Assert.Contains(lower, pool);
    }

    [Fact]
    public void DfhMod_Equality_Still_Requires_Same_Version()
    {
        DFHMod v1 = new DFHMod("example_mod", 1);
        DFHMod v2 = new DFHMod("example_mod", 2);

        Assert.True(v1 != v2);
        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void ModReference_Loads_Info_File_Case_Insensitively()
    {
        string root = Path.Combine(Path.GetTempPath(), "modhearth-tests", Guid.NewGuid().ToString("N"));
        string modPath = Path.Combine(root, "sample_mod");
        Directory.CreateDirectory(modPath);
        try
        {
            File.WriteAllText(
                Path.Combine(modPath, "INFO.TXT"),
                "[REQUIRES_ID:dependency_mod]" + Environment.NewLine);

            Dictionary<string, string> memoryData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "sample_mod",
                ["numeric_version"] = "1",
                ["displayed_version"] = "1",
                ["earliest_compatible_numeric_version"] = "1",
                ["earliest_compatible_displayed_version"] = "1",
                ["author"] = string.Empty,
                ["name"] = "Sample Mod",
                ["description"] = string.Empty,
                ["steam_file_id"] = string.Empty,
                ["steam_title"] = string.Empty,
                ["steam_description"] = string.Empty,
                ["src_dir"] = modPath
            };

            ModReference modRef = new ModReference(memoryData);
            Assert.Contains("dependency_mod", modRef.require_ids);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
