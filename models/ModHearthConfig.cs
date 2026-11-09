namespace ModHearth.Models
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

        // Path to installed_mods cache.
        public string InstalledModsPath { get; set; } = string.Empty;

        // Should this be in lightmode?
        public int theme { get; set; } //TODO Fix naming rule

        // Auto-reload interval for modlists in seconds. -1 means disabled by checkbox.
        public int AutoReloadIntervalSeconds { get; set; } = -1;

        // Hide console logs on startup. This setting does nothing on Linux
        public bool showConsole { get; set; } = true; //TODO Fix naming rule

        // Path to the DFHack folder (e.g., etc/steamapps/common/DFHack).
        public string DFHackFolderPath { get; set; } = string.Empty;

        // Automatically save modlist changes.
        public bool IsAutoSaveEnabled { get; set; } = true;

        // Automatically sort modlist changes.
        public bool IsAutoSortEnabled { get; set; }

        // Automatically resolve and queue workshop URLs when input changes.

        public bool IsAutoResolveAndQueueEnabled { get; set; }

        // Automatically retry failed or cancelled workshop downloads.

        public bool IsAutoRetryAllEnabled { get; set; }

        // Optional GitHub repository URL from which to load community modsort_rules.json.

        public string CommunitySortRulesUrl { get; set; } = string.Empty;

        // Proportion (0-1) of the mod info dock taken by the mod data panel.
        public double ModDataPanelProportion { get; set; } = 0.35;

        // Split orientation of the mod info dock. 0 = vertical, 1 = horizontal.
        public int ModDataPanelOrientation { get; set; }

        // Whether the mod data panel is placed before (top/left of) the description.

        public bool ModDataPanelFirst { get; set; } = true;

        // Proportion (0-1) of the mod info dock taken by the description panel.
        public double ModDescriptionPanelProportion { get; set; } = 0.65;

        // Proportion (0-1) of the mod info dock taken by the data/description area (vs preview).
        public double ModInfoPanelProportion { get; set; } = 0.55;

        // Proportion (0-1) of the mod info dock taken by the preview image panel.
        public double ModPreviewPanelProportion { get; set; } = 0.45;

        // Split orientation between the preview panel and the rest. 0 = vertical, 1 = horizontal.
        public int ModPreviewPanelOrientation { get; set; }

        // Whether the preview panel is placed before (top/left of) the data/description panels.

        public bool ModPreviewPanelFirst { get; set; } = true;

        // Option to open Steam Workshop pages in the Steam client instead of a browser.
        public bool OpenSteamInClient { get; set; }

        // Option to open Steam folder instead of local folder for SteamFolderMod mods.

        public bool OpenSteamFolder { get; set; }

        // Option to copy Steam File ID instead of Mod ID from context menu.

        public bool CopySteamFileId { get; set; }

        // Search bar filter and sort state for the left modlist.

        public string LeftSearchBarState { get; set; } = string.Empty;

        // Search bar filter and sort state for the right modlist.
        public string RightSearchBarState { get; set; } = string.Empty;

        // Normalized ratio representing the proportional split between resizable columns for MainWindow GridSplitter.
        public double MainWindowGridSplitterRatio { get; set; } = 0.47619;

        // Normalized ratio representing the proportional split between resizable columns for SortRulesWindow GridSplitter.
        public double SortRulesWindowGridSplitterRatio { get; set; } = 0.4444;

        // Default Steam Workshop download provider name.
        public string DefaultWorkshopProvider { get; set; } = "SteamCmdDownloadProvider";

        // Path to SteamCMD executable.
        public string SteamCmdPath { get; set; } = string.Empty;

        // Whether workshop downloader opens in docked mode by default.
        public bool IsWorkshopDownloaderDocked { get; set; }

        // Whether mod update log opens in docked mode by default.

        public bool IsModUpdateLogDocked { get; set; }

        // Whether sort rules opens in docked mode by default.

        public bool IsSortRulesDocked { get; set; }

        // Proportions of each docked splitter control mapped by key.

        public Dictionary<string, double> DockSplitterProportions { get; set; } = [];

        // Dock side (DockSide enum int) mapped by key.
        public Dictionary<string, int> DockSides { get; set; } = [];

        // Ignored update version.
        public string IgnoredUpdateVersion { get; set; } = string.Empty;

        // Whether to automatically check for releases on program startup.
        public bool AutoCheckForReleasesOnStartup { get; set; } = true;

        // Whether to backup ModHearth data (metadata/, modrules.json, config.json) before installing a new release.
        public bool BackupDataBeforeUpdate { get; set; } = true;
    }
}
