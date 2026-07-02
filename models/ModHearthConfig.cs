namespace ModHearth
{
    /// <summary>
    /// Config class, to extract data from the json.
    /// </summary>
    [Serializable]
    public class ModHearthConfig
    {
        // Path to DF executable (platform-specific).
        public string DFEXEPath { get; set; } = string.Empty;

        // Optional override for the DF base folder (used when a folder is selected instead of an executable).
        public string DFFolderPathOverride { get; set; } = string.Empty;

        // Optional override for the mods folder path (used when filesystem casing differs).
        public string ModsPathOverride { get; set; } = string.Empty;

        public string DFFolderPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DFFolderPathOverride))
                    return DFFolderPathOverride;
                if (string.IsNullOrWhiteSpace(DFEXEPath))
                    return string.Empty;
                return Path.GetDirectoryName(DFEXEPath) ?? string.Empty;
            }
        }

        public string ModsPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ModsPathOverride))
                    return ModsPathOverride;
                if (string.IsNullOrWhiteSpace(DFFolderPath))
                    return string.Empty;
                return Path.Combine(DFFolderPath, "Mods");
            }
        }

        // Path to installed mods cache.
        public string InstalledModsPath { get; set; } = string.Empty;

        // Should this be in lightmode?
        public int theme { get; set; }

        // Auto-reload interval for modlists in seconds. -1 means disabled by checkbox.
        public int AutoReloadIntervalSeconds { get; set; } = -1;

        // Hide console logs on startup.
        public bool showConsole { get; set; } = true;

        // Path to the DFHack folder (e.g., etc/steamapps/common/DFHack).
        public string DFHackFolderPath { get; set; } = string.Empty;
    }
}
