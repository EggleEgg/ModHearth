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

        // Automatically save modlist changes.
        public bool IsAutoSaveEnabled { get; set; } = false;

        // Automatically sort modlist changes.
        public bool IsAutoSortEnabled { get; set; } = false;

        // Automatically resolve and queue workshop URLs when input changes.
        public bool IsAutoResolveAndQueueEnabled { get; set; } = false;

        // Automatically retry failed or cancelled workshop downloads.
        public bool IsAutoRetryAllEnabled { get; set; } = false;

        // Optional GitHub repository URL from which to load community modsort_rules.json.
        public string CommunitySortRulesUrl { get; set; } = string.Empty;

        // Proportion (0-1) of the mod info dock taken by the mod data panel.
        public double ModDataPanelProportion { get; set; } = 0.35;

        // Split orientation of the mod info dock. 0 = vertical, 1 = horizontal.
        public int ModDataPanelOrientation { get; set; } = 0;

        // Whether the mod data panel is placed before (top/left of) the description.
        public bool ModDataPanelFirst { get; set; } = true;

        // Proportion (0-1) of the mod info dock taken by the description panel.
        public double ModDescriptionPanelProportion { get; set; } = 0.65;

        // Proportion (0-1) of the mod info dock taken by the data/description area (vs preview).
        public double ModInfoPanelProportion { get; set; } = 0.55;

        // Proportion (0-1) of the mod info dock taken by the preview image panel.
        public double ModPreviewPanelProportion { get; set; } = 0.45;

        // Split orientation between the preview panel and the rest. 0 = vertical, 1 = horizontal.
        public int ModPreviewPanelOrientation { get; set; } = 0;

        // Whether the preview panel is placed before (top/left of) the data/description panels.
        public bool ModPreviewPanelFirst { get; set; } = true;

        // Option to open Steam Workshop pages in the Steam client instead of a browser.
        public bool OpenSteamInClient { get; set; } = false;

        // Option to copy Steam File ID instead of Mod ID from context menu.
        public bool CopySteamFileId { get; set; } = false;

        // Search bar filter and sort state for the left modlist.
        public string LeftSearchBarState { get; set; } = string.Empty;

        // Search bar filter and sort state for the right modlist.
        public string RightSearchBarState { get; set; } = string.Empty;

        // Normalized ratio representing the proportional split between resizable columns for MainWindow GridSplitter.
        public double MainWindowGridSplitterRatio { get; set; } = 0.47619;

        // Normalized ratio representing the proportional split between resizable columns for SortRulesWindow GridSplitter.
        public double SortRulesWindowGridSplitterRatio { get; set; } = 0.4444;

        // Default Steam Workshop download provider name. Defaults to SteamWorkerDownloadProvider.
        public string DefaultWorkshopProvider { get; set; } = "SteamWorkerDownloadProvider";
    }
}
