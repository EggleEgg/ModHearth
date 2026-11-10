using System.Text.RegularExpressions;
using ModHearth.Models;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Locates the repository's bundled <c>example_mod</c> and turns it into a
    /// <see cref="ModReference"/> for views that need real mod data.
    /// </summary>
    internal static class ExampleModTestData
    {
        public static string FindPath()
        {
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                foreach (string subPath in new[] { "ModHearth.App.Tests/example_mod", "example_mod" })
                {
                    string candidate = Path.Combine(dir.FullName, subPath);
                    if (Directory.Exists(candidate))
                        return candidate;
                }
            }

            return string.Empty;
        }

        public static ModReference? LoadModReference(string modDir)
        {
            if (string.IsNullOrEmpty(modDir) || !Directory.Exists(modDir))
                return null;

            string infoPath = Path.Combine(modDir, "info.txt");
            if (!File.Exists(infoPath))
                return null;

            string infoContent = File.ReadAllText(infoPath);

            string GetTag(string tagName)
            {
                Match m = Regex.Match(infoContent, $@"\[{tagName}:([^\]]+)\]", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
            }

            string[] tags =
            [
                "ID", "NUMERIC_VERSION", "DISPLAYED_VERSION",
                "EARLIEST_COMPATIBLE_NUMERIC_VERSION", "EARLIEST_COMPATIBLE_DISPLAYED_VERSION",
                "AUTHOR", "NAME", "DESCRIPTION", "STEAM_FILE_ID", "STEAM_TITLE", "STEAM_DESCRIPTION"
            ];

            Dictionary<string, string> memoryData = new(StringComparer.OrdinalIgnoreCase)
            {
                ["src_dir"] = modDir
            };

            foreach (string tag in tags)
            {
                memoryData[tag.ToLowerInvariant()] = GetTag(tag);
            }

            return new ModReference(memoryData);
        }
    }
}
