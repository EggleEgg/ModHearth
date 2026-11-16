using System.Text.RegularExpressions;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Collects the reaction BUILDING ids vanilla raws reference. Reaction building ids are the one
    /// catalog group DF does not reliably store as standalone executable strings: several short ids
    /// are inlined into comparisons, and graphics use prefixed names such as
    /// <c>WORKSHOP_LOOM</c>. Comparing the catalog with the buildings vanilla reactions actually use
    /// detects a DF update that starts relying on a new hardcoded workshop.
    /// </summary>
    internal static partial class HardcodedVanillaReactionScanner
    {
        [GeneratedRegex(
            @"^\s*\[OBJECT:([^\]]+)\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ObjectHeaderPattern();

        [GeneratedRegex(
            @"^\s*\[BUILDING:([^:\]\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex BuildingPattern();

        public static IReadOnlySet<string> CollectReactionBuildingIds(
            string vanillaPath,
            ICollection<string> warnings)
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(vanillaPath))
                return ids;

            List<string> files = [];
            try
            {
                foreach (string modDirectory in Directory.EnumerateDirectories(vanillaPath))
                {
                    string objectsPath = Path.Combine(modDirectory, "objects");
                    if (!Directory.Exists(objectsPath))
                        continue;

                    files.AddRange(Directory.EnumerateFiles(
                        objectsPath,
                        "*.txt",
                        SearchOption.AllDirectories));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not list vanilla raw files under '{vanillaPath}': {ex.Message}");
                return ids;
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                try
                {
                    bool inReactionObject = false;
                    foreach (string line in File.ReadLines(file))
                    {
                        Match header = ObjectHeaderPattern().Match(line);
                        if (header.Success)
                        {
                            inReactionObject = string.Equals(
                                header.Groups[1].Value.Trim(),
                                "REACTION",
                                StringComparison.OrdinalIgnoreCase);
                            continue;
                        }

                        if (!inReactionObject)
                            continue;

                        Match building = BuildingPattern().Match(line);
                        if (!building.Success)
                            continue;

                        string id = building.Groups[1].Value.Trim();
                        if (id.Length > 0
                            && !string.Equals(id, "NONE", StringComparison.OrdinalIgnoreCase))
                        {
                            _ = ids.Add(id);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not read vanilla raw file '{file}': {ex.Message}");
                }
            }

            return ids;
        }
    }
}
