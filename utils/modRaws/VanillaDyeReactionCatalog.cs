using System.Text.RegularExpressions;

namespace ModHearth.Utils.ModRaws
{
    /// <summary>
    /// Discovers the vanilla dye reactions whose <c>CUT_REACTION</c> Dwarf Fortress ignores. The ids
    /// are not stored in the catalog: <c>reaction_dyes.txt</c> defines them and
    /// <c>entity_default.txt</c> permits them, so intersecting the two files follows vanilla raw
    /// updates automatically. The hardcoded report's executable check confirms the derived ids are
    /// still hardcoded in the installed Dwarf Fortress build.
    /// </summary>
    internal static partial class VanillaDyeReactionCatalog
    {
        private const string DefinitionFileName = "reaction_dyes.txt";
        private const string PermissionFileName = "entity_default.txt";

        [GeneratedRegex(
            @"^\s*\[REACTION:([^\]\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ReactionPattern();

        [GeneratedRegex(
            @"^\s*\[PERMITTED_REACTION:([^\]\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex PermittedReactionPattern();

        public static IReadOnlySet<string> Load(
            string vanillaModsPath,
            ICollection<string>? warnings = null)
        {
            HashSet<string> dyeReactions = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(vanillaModsPath) || !Directory.Exists(vanillaModsPath))
                return dyeReactions;

            List<string> definitionFiles = [];
            List<string> permissionFiles = [];

            try
            {
                foreach (string modDirectory in Directory.EnumerateDirectories(vanillaModsPath))
                {
                    string objectsPath = Path.Combine(modDirectory, "objects");
                    if (!Directory.Exists(objectsPath))
                        continue;

                    foreach (string file in Directory.EnumerateFiles(
                        objectsPath,
                        "*",
                        SearchOption.AllDirectories))
                    {
                        string fileName = Path.GetFileName(file);
                        if (string.Equals(fileName, DefinitionFileName, StringComparison.OrdinalIgnoreCase))
                            definitionFiles.Add(file);
                        else if (string.Equals(fileName, PermissionFileName, StringComparison.OrdinalIgnoreCase))
                            permissionFiles.Add(file);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings?.Add($"Could not list vanilla raws under '{vanillaModsPath}': {ex.Message}");
                return dyeReactions;
            }

            HashSet<string> definitions = ReadIds(
                definitionFiles,
                ReactionPattern(),
                warnings);
            if (definitions.Count == 0)
            {
                warnings?.Add(
                    $"No vanilla dye reaction definitions were found in '{DefinitionFileName}' " +
                    $"under '{vanillaModsPath}'.");
                return dyeReactions;
            }

            HashSet<string> permissions = ReadIds(
                permissionFiles,
                PermittedReactionPattern(),
                warnings);
            if (permissions.Count == 0)
            {
                warnings?.Add(
                    $"No vanilla permitted reactions were found in '{PermissionFileName}' " +
                    $"under '{vanillaModsPath}'.");
                return dyeReactions;
            }

            foreach (string id in definitions)
            {
                if (permissions.Contains(id))
                    _ = dyeReactions.Add(id);
            }

            if (dyeReactions.Count == 0)
            {
                warnings?.Add(
                    $"No vanilla dye reactions were found by intersecting '{DefinitionFileName}' with " +
                    $"'{PermissionFileName}' under '{vanillaModsPath}'.");
            }

            return dyeReactions;
        }

        private static HashSet<string> ReadIds(
            IEnumerable<string> files,
            Regex pattern,
            ICollection<string>? warnings)
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                try
                {
                    foreach (string line in File.ReadLines(file))
                    {
                        Match match = pattern.Match(line);
                        if (!match.Success)
                            continue;

                        string id = match.Groups[1].Value.Trim();
                        if (id.Length > 0)
                            _ = ids.Add(id);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings?.Add($"Could not read vanilla raw file '{file}': {ex.Message}");
                }
            }

            return ids;
        }
    }
}
