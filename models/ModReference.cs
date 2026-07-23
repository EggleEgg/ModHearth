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
        public string ID { get; set; } = string.Empty;
        public string numericVersion { get; set; } = string.Empty;
        public string displayedVersion { get; set; } = string.Empty;
        public string earliestCompatibleNumericVersion { get; set; } = string.Empty;
        public string earliestCompatibleDisplayedVersion { get; set; } = string.Empty;
        public string author { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;

        public ModColor AssignedColor { get; set; } = ModColor.None;

        public string steamName { get; set; } = string.Empty;
        public string steamDescription { get; set; } = string.Empty;
        public string steamID { get; set; } = string.Empty;

        public List<string> require_before_me { get; set; } = new List<string>();
        public List<string> require_after_me { get; set; } = new List<string>();
        public List<string> require_ids { get; set; } = new List<string>();
        public List<string> conflicts_with { get; set; } = new List<string>();

        // Path of mod folder, not path to info.
        public string path { get; set; } = string.Empty;

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

            require_before_me = new List<string>();
            require_after_me = new List<string>();
            require_ids = new List<string>();
            conflicts_with = new List<string>();
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

            require_before_me = new List<string>();
            require_after_me = new List<string>();
            require_ids = new List<string>();
            conflicts_with = new List<string>();

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
                        require_before_me.Add(value);
                }

                foreach (Match match in requireAfterMatches)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(value))
                        require_after_me.Add(value);
                }

                foreach (Match match in conflictsMatches)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(value))
                        conflicts_with.Add(value);
                }

                foreach (Match match in requiresMatches)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(value))
                        require_ids.Add(value);
                }
            }
            else
            {
                string expected = Path.Combine(path, "info.txt");
                Console.WriteLine($"   Warning: info.txt missing for mod '{name}' at '{expected}'. Skipping dependency parsing.");
            }

            // Set problematic based on if this mod has extra needs.
            problematic = require_before_me.Count != 0 || require_after_me.Count != 0 || require_ids.Count != 0 || conflicts_with.Count != 0;

        }

        // Use this mods ID and numvericVersion to create the DFHMod.
        public DFHMod ToDFHMod()
        {
            // DFHack does this to version, visible in the JSON file.
            int version = int.Parse(numericVersion.Replace(".", ""));
            DFHMod mod = new DFHMod(ID, version);
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
