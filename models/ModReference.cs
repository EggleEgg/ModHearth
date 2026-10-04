using System.Text.RegularExpressions;
using ModHearth.Metadata;

namespace ModHearth
{
    public enum ModSource
    {
        Local,
        Steam
    }

    /// <summary>
    /// Stores all data relevant to a mod from DFHack or FindAllModsFromDisk() (filesearch)
    /// More comprehensive than DFHMod, but not used in the actual creation of modpacks.
    /// </summary>
    public class ModReference
    {
        // Data found in modinfo files.
        public string ID { get; set; }
        public string numericVersion { get; set; }
        public string displayedVersion { get; set; }
        public string earliestCompatibleNumericVersion { get; set; }
        public string earliestCompatibleDisplayedVersion { get; set; }
        public string author { get; set; }
        public string name { get; set; }
        public string description { get; set; }

        public ModColor AssignedColor { get; set; }

        public string steamName { get; set; }
        public string steamDescription { get; set; }
        public string steamID { get; set; }

        public List<string> requireBeforeMe { get; set; }
        public List<string> requireAfterMe { get; set; }
        public List<string> requireIds { get; set; }
        public List<string> conflictsWith { get; set; }

        // Path of mod folder, not path to info.
        public string path { get; set; }

        // Is this modref missing a version (one mod did this, dfhack set version to 1 so this matches it).
        public bool MissingVersion { get; set; } = false;

        // Does this mod have mods it needs loaded before it, mods it needs loaded after it, or conflicts.
        public bool problematic { get; set; } = false;
        public DateTime? LastModifiedTime { get; set; }
        public bool IsIgnored { get; set; } = false;

        public ModSource Source { get; set; } = ModSource.Local;

        public ModReference()
        {
            ID = string.Empty;
            numericVersion = string.Empty;
            displayedVersion = string.Empty;
            earliestCompatibleNumericVersion = string.Empty;
            earliestCompatibleDisplayedVersion = string.Empty;
            author = string.Empty;
            name = string.Empty;
            description = string.Empty;
            steamName = string.Empty;
            steamDescription = string.Empty;
            steamID = string.Empty;
            path = string.Empty;
            Source = ModSource.Local;
            problematic = false;
            AssignedColor = ModColor.None;
            IsIgnored = false;

            requireBeforeMe = [];
            requireAfterMe = [];
            requireIds = [];
            conflictsWith = [];
        }


        public ModReference(Dictionary<string, string> modMemoryData)
        {
            Dictionary<string, string> mmd = modMemoryData;
            ID = mmd["id"];
            numericVersion = mmd["numeric_version"];
            displayedVersion = mmd["displayed_version"];
            earliestCompatibleNumericVersion = mmd["earliest_compatible_numeric_version"];
            earliestCompatibleDisplayedVersion = mmd["earliest_compatible_displayed_version"];
            author = mmd["author"];
            name = mmd["name"];
            description = mmd["description"];
            path = Path.Combine(mmd["src_dir"]);
            steamID = mmd["steam_file_id"]; // FIXME: dubious
            steamName = mmd["steam_title"];
            AssignedColor = ModColorMetadataStore.GetModColor(ID);
            steamDescription = mmd["steam_description"];

            Source = string.IsNullOrWhiteSpace(steamID) ? ModSource.Local : ModSource.Steam;
            IsIgnored = false; // Initialize the new property

            requireBeforeMe = [];
            requireAfterMe = [];
            requireIds = [];
            conflictsWith = [];

            // In theory info file is always present, but handle missing files gracefully.
            string? modInfoPath = ResolveInfoPath(path);
            if (!string.IsNullOrWhiteSpace(modInfoPath))
            {
                string modInfo = File.ReadAllText(modInfoPath);

                MatchCollection requireBeforeMatches = Regex.Matches(modInfo, @"\[REQUIRES_ID_BEFORE_ME(?::(.*?))?\]", RegexOptions.IgnoreCase);
                MatchCollection requireAfterMatches = Regex.Matches(modInfo, @"\[REQUIRES_ID_AFTER_ME(?::(.*?))?\]", RegexOptions.IgnoreCase);
                MatchCollection conflictsMatches = Regex.Matches(modInfo, @"\[CONFLICTS_WITH_ID(?::(.*?))?\]", RegexOptions.IgnoreCase);
                MatchCollection requiresMatches = Regex.Matches(modInfo, @"\[REQUIRES_ID(?::(.*?))?\]", RegexOptions.IgnoreCase);

                // Each pattern now has exactly one capturing group (empty for a valueless "[TAG]" tag), rather than the old two-alternative pattern
                // that needed both groups concatenated together.
                foreach (Match match in requireBeforeMatches)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(value))
                        requireBeforeMe.Add(value);
                }

                foreach (Match match in requireAfterMatches)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(value))
                        requireAfterMe.Add(value);
                }

                foreach (Match match in conflictsMatches)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(value))
                        conflictsWith.Add(value);
                }

                foreach (Match match in requiresMatches)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(value))
                        requireIds.Add(value);
                }
            }
            else
            {
                string expected = Path.Combine(path, "info.txt");
                Console.WriteLine($"   Warning: info.txt missing for mod '{name}' at '{expected}'. Skipping dependency parsing.");
            }

            // Set problematic based on if this mod has extra needs.
            problematic = requireBeforeMe.Count != 0 || requireAfterMe.Count != 0 || requireIds.Count != 0 || conflictsWith.Count != 0;

        }

        // Use this mods ID and numvericVersion to create the DFHMod.
        public DFHMod ToDFHMod()
        {
            // DFHack does this to version, visible in the JSON file.
            int version = int.Parse(numericVersion.Replace(".", ""));
            DFHMod mod = new(ID, version);
            return mod;
        }

        // Functionally get the ToString/HashCode of this mod as a DFHMod. Mainly used for HashMap keys.
        public string DFHackCompatibleString()
        {
            DFHMod temp = ToDFHMod();
            return temp.ToString();
        }

        private static string? ResolveInfoPath(string modPath)
        {
            if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
                return null;

            string infoPath = Path.Combine(modPath, "info.txt");
            if (File.Exists(infoPath))
                return infoPath;

            try
            {
                return Directory.EnumerateFiles(modPath)
                    .FirstOrDefault(file =>
                        string.Equals(Path.GetFileName(file), "info.txt", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }
    }
}
