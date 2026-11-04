using System.Text.RegularExpressions;
using ModHearth.Metadata;
using ModHearth.Utilities;

namespace ModHearth.Models
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
        public string NumericVersion { get; set; }
        public string DisplayedVersion { get; set; }
        public string EarliestCompatibleNumericVersion { get; set; }
        public string EarliestCompatibleDisplayedVersion { get; set; }
        public string Author { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public ModColor AssignedColor { get; set; }

        public string SteamName { get; set; }
        public string SteamDescription { get; set; }
        public string SteamID { get; set; }

        public List<string> RequireBeforeMe { get; set; }
        public List<string> RequireAfterMe { get; set; }
        public List<string> RequireIds { get; set; }
        public List<string> ConflictsWith { get; set; }

        // Path of mod folder, not path to info.
        public string Path { get; set; }

        // Is this modref missing a version (one mod did this, dfhack set version to 1 so this matches it).
        public bool MissingVersion { get; set; }

        // Does this mod have mods it needs loaded before it, mods it needs loaded after it, or conflicts.
        public bool Problematic { get; set; }
        public DateTime? LastModifiedTime { get; set; }
        public bool IsIgnored { get; set; }

        public ModSource Source { get; set; } = ModSource.Local;

        public ModReference()
        {
            ID = string.Empty;
            NumericVersion = string.Empty;
            DisplayedVersion = string.Empty;
            EarliestCompatibleNumericVersion = string.Empty;
            EarliestCompatibleDisplayedVersion = string.Empty;
            Author = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            SteamName = string.Empty;
            SteamDescription = string.Empty;
            SteamID = string.Empty;
            Path = string.Empty;
            Source = ModSource.Local;
            Problematic = false;
            AssignedColor = ModColor.None;
            IsIgnored = false;

            RequireBeforeMe = [];
            RequireAfterMe = [];
            RequireIds = [];
            ConflictsWith = [];
        }

        public ModReference(Dictionary<string, string> modMemoryData)
            : this(modMemoryData, modInfoText: null)
        {
        }

        /// <summary>
        /// Builds a reference from memory/file data. <paramref name="modInfoText"/> lets callers that
        /// already read info.txt pass its contents in, avoiding a second disk read; null falls back to
        /// reading it from <see cref="Path"/>.
        /// </summary>
        public ModReference(Dictionary<string, string> modMemoryData, string? modInfoText)
        {
            Dictionary<string, string> mmd = modMemoryData;
            ID = mmd["id"];
            NumericVersion = mmd["numeric_version"];
            DisplayedVersion = mmd["displayed_version"];
            EarliestCompatibleNumericVersion = mmd["earliest_compatible_numeric_version"];
            EarliestCompatibleDisplayedVersion = mmd["earliest_compatible_displayed_version"];
            Author = mmd["author"];
            Name = mmd["name"];
            Description = mmd["description"];
            Path = System.IO.Path.Combine(mmd["src_dir"]);
            SteamID = mmd["steam_file_id"]; // FIXME: dubious
            SteamName = mmd["steam_title"];
            AssignedColor = ModColorMetadataStore.GetModColor(ID);
            SteamDescription = mmd["steam_description"];

            Source = string.IsNullOrWhiteSpace(SteamID) ? ModSource.Local : ModSource.Steam;
            IsIgnored = false; // Initialize the new property

            RequireBeforeMe = [];
            RequireAfterMe = [];
            RequireIds = [];
            ConflictsWith = [];

            // In theory info file is always present, but handle missing files gracefully.
            string? modInfo = modInfoText;
            if (modInfo == null)
            {
                string? modInfoPath = ResolveInfoPath(Path);
                if (!string.IsNullOrWhiteSpace(modInfoPath))
                    modInfo = File.ReadAllText(modInfoPath);
            }

            if (!string.IsNullOrWhiteSpace(modInfo))
            {
                // Each pattern has exactly one capturing group (empty for a valueless "[TAG]" tag),
                // rather than the old two-alternative pattern that needed both groups concatenated.
                AppendTagValues(RequireBeforeMe, modInfo, @"\[REQUIRES_ID_BEFORE_ME(?::(.*?))?\]");
                AppendTagValues(RequireAfterMe, modInfo, @"\[REQUIRES_ID_AFTER_ME(?::(.*?))?\]");
                AppendTagValues(ConflictsWith, modInfo, @"\[CONFLICTS_WITH_ID(?::(.*?))?\]");
                AppendTagValues(RequireIds, modInfo, @"\[REQUIRES_ID(?::(.*?))?\]");
            }
            else
            {
                string expected = System.IO.Path.Combine(Path, "info.txt");
                Console.WriteLine($"   Warning: info.txt missing for mod '{Name}' at '{expected}'. Skipping dependency parsing.");
            }

            // Set problematic based on if this mod has extra needs.
            Problematic = RequireBeforeMe.Count != 0 || RequireAfterMe.Count != 0 || RequireIds.Count != 0 || ConflictsWith.Count != 0;

        }

        private sealed record DfModSnapshot(string Id, string NumericVersion, DfhMod Mod);
        private volatile DfModSnapshot? dfModSnapshot;

        // Use this mods ID and numvericVersion to create the DFHMod.
        public DfhMod ToDFHMod()
        {
            string id = ID;
            string numeric = NumericVersion;

            DfModSnapshot? snapshot = dfModSnapshot;
            if (snapshot != null
                && string.Equals(snapshot.Id, id, StringComparison.Ordinal)
                && string.Equals(snapshot.NumericVersion, numeric, StringComparison.Ordinal))
            {
                return snapshot.Mod;
            }

            // DFHack does this to version, visible in the JSON file.
            DfhMod mod = new(id, int.Parse(numeric.Replace(".", "")));
            dfModSnapshot = new DfModSnapshot(id, numeric, mod);
            return mod;
        }

        // Functionally get the ToString/HashCode of this mod as a DFHMod. Mainly used for HashMap keys.
        public string DFHackCompatibleString()
        {
            DfhMod temp = ToDFHMod();
            return temp.ToString();
        }

        private static string? ResolveInfoPath(string modPath)
            => PathHelper.ResolveFile(modPath, "info.txt");

        private static void AppendTagValues(List<string> target, string info, string pattern)
        {
            foreach (Match match in Regex.Matches(info, pattern, RegexOptions.IgnoreCase))
            {
                string value = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(value))
                    target.Add(value);
            }
        }
    }
}
