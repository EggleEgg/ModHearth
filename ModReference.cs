using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ModHearth
{
    /// <summary>
    /// Stores all data relevant to a mod.
    /// More comprehensive than DFHMod, but not used in the actual creation of modpacks.
    /// </summary>
    public class ModReference
    {
        // Data found in modinfo files.
        public string ID;
        public string numericVersion;
        public string displayedVersion;
        public string earliestCompatibleNumericVersion;
        public string earliestCompatibleDisplayedVersion;
        public string author;
        public string name;
        public string description;

        public string steamName;
        public string steamDescription;
        public string steamID;

        public List<string> require_before_me;
        public List<string> require_after_me;
        public List<string> require_ids;
        public List<string> conflicts_with;

        // Path of mod folder, not path to info.
        public string path;

        // Is this modref missing a version (one mod did this, dfhack set version to 1 so this matches it).
        public bool MissingVersion = false;

        // Does this mod have mods it needs loaded before it, mods it needs loaded after it, or conflicts.
        public bool problematic;

        // #FIXME: pointless constructor.
        public ModReference(string ID, string numericVersion, string displayedVersion, string earliestCompatibleNumericVersion, string earliestCompatibleDisplayedVersion, string author, string name, string description, string steamName, string steamDescription, string steamID, string path)
        {
            this.ID = ID;
            this.numericVersion = numericVersion;
            this.displayedVersion = displayedVersion;
            this.earliestCompatibleNumericVersion = earliestCompatibleNumericVersion;
            this.earliestCompatibleDisplayedVersion = earliestCompatibleDisplayedVersion;
            this.author = author;
            this.name = name;
            this.description = description;
            this.steamName = steamName;
            this.steamDescription = steamDescription;
            this.steamID = steamID;
            this.path = path;
            problematic = false;

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
            steamDescription = mmd["steam_description"];

            require_before_me = new List<string>();
            require_after_me = new List<string>();
            require_ids = new List<string>();
            conflicts_with = new List<string>();

            // In theory info file is always present, but handle missing files gracefully.
            string modInfoPath = Path.Combine(path, "info.txt");
            if (File.Exists(modInfoPath))
            {
                string modInfo = File.ReadAllText(modInfoPath);

                // FIXME: this should really pull from memory using lua, but dealing with the tables sucks.
                // FIXME: this may not match the game internals.
                MatchCollection requireBeforeMatches = Regex.Matches(modInfo, @"\[REQUIRES_ID_BEFORE_ME\]:*(.*?)\n|\[REQUIRES_ID_BEFORE_ME:*(.*?)\]", RegexOptions.IgnoreCase);
                MatchCollection requireAfterMatches = Regex.Matches(modInfo, @"\[REQUIRES_ID_AFTER_ME\]:*(.*?)\n|\[REQUIRES_ID_AFTER_ME:*(.*?)\]", RegexOptions.IgnoreCase);
                MatchCollection conflictsMatches = Regex.Matches(modInfo, @"\[CONFLICTS_WITH_ID\]:*(.*?)\n|\[CONFLICTS_WITH_ID:*(.*?)\]", RegexOptions.IgnoreCase);
                MatchCollection requiresMatches = Regex.Matches(modInfo, @"\[REQUIRES_ID\]:*(.*?)\n|\[REQUIRES_ID:*(.*?)\]", RegexOptions.IgnoreCase);

                // See if this mod has any extra needs. The groups are added, since one is empty.
                foreach (Match match in requireBeforeMatches)
                {
                    require_before_me.Add(match.Groups[1].Value + match.Groups[2].Value);
                }

                foreach (Match match in requireAfterMatches)
                {
                    require_after_me.Add(match.Groups[1].Value + match.Groups[2].Value);
                }

                foreach (Match match in conflictsMatches)
                {
                    conflicts_with.Add(match.Groups[1].Value + match.Groups[2].Value);
                }

                foreach (Match match in requiresMatches)
                {
                    require_ids.Add(match.Groups[1].Value + match.Groups[2].Value);
                }
            }
            else
            {
                Console.WriteLine($"   Warning: info.txt missing for mod '{name}' at '{modInfoPath}'. Skipping dependency parsing.");
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
    }
}
