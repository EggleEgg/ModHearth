namespace ModHearth.App.Tests
{
    internal enum UiActionMode
    {
        Invoke,
        Skip
    }

    internal sealed record UiActionPlan(UiActionMode Mode, string Reason)
    {
        public static UiActionPlan Invoke(string reason) => new(UiActionMode.Invoke, reason);

        public static UiActionPlan Skip(string reason) => new(UiActionMode.Skip, reason);
    }

    /// <summary>
    /// Decides which discovered UI actions the smoke test may execute. Every click has to be
    /// classified explicitly; a new button that is not listed here fails the test so nobody has to
    /// remember to extend a hardcoded control list.
    /// </summary>
    internal static class UiActionPolicy
    {
        private const string InputEventReason =
            "Requires synthesized pointer, keyboard, or drag state that this smoke test does not " +
            "simulate. The subscription is still inventoried so it cannot disappear silently.";

        private const string LifecycleEventReason =
            "Lifecycle event; it fires naturally when the view is shown, so the smoke test does not " +
            "raise it manually.";

        private const string SearchUtilityReason =
            "Shared search-bar toggle/clear button; only mutates in-memory view state.";

        /// <summary>
        /// Routed events that are inventoried but never raised. These handlers need real input or
        /// layout state; raising them with empty arguments would test the harness, not the app.
        /// </summary>
        private static readonly HashSet<string> InputOnlyEvents = new(StringComparer.Ordinal)
        {
            "KeyDown",
            "KeyUp",
            "TextInput",
            "TextChanged",
            "SelectionChanged",
            "SizeChanged",
            "PointerPressed",
            "PointerReleased",
            "PointerMoved",
            "PointerEntered",
            "PointerExited",
            "PointerWheelChanged",
            "PointerCaptureLost",
            "Tapped",
            "DoubleTapped",
            "DragOver",
            "DragLeave",
            "Drop"
        };

        private static readonly Dictionary<UiActionKey, UiActionPlan> Plans = BuildPlans();

        /// <summary>
        /// Returns <see langword="null"/> for a click that has never been reviewed. Callers must
        /// treat that as a test failure rather than defaulting to "skip".
        /// </summary>
        public static UiActionPlan? Classify(UiActionKey key)
        {
            if (Plans.TryGetValue(key, out UiActionPlan? plan))
                return plan;

            if (key.EventName == "Loaded")
                return UiActionPlan.Skip(LifecycleEventReason);

            if (InputOnlyEvents.Contains(key.EventName))
                return UiActionPlan.Skip(InputEventReason);

            if (key.EventName == "Click" && key.ControlName is "ToggleButton" or "ClearButton")
                return UiActionPlan.Invoke(SearchUtilityReason);

            return null;
        }

        private static Dictionary<UiActionKey, UiActionPlan> BuildPlans()
        {
            Dictionary<UiActionKey, UiActionPlan> plans = [];

            void Invoke(string root, string control, string reason)
            {
                plans[Key(root, control)] = UiActionPlan.Invoke(reason);
            }

            void Skip(string root, string control, string reason)
            {
                plans[Key(root, control)] = UiActionPlan.Skip(reason);
            }

            // CollectionChecklistDialog: checkbox shortcuts and close buttons touch only the
            // dialog's own in-memory item list.
            Invoke("CollectionChecklistDialog", "BtnDownloadAll", "Checks every in-memory checklist row.");
            Invoke("CollectionChecklistDialog", "BtnClearAll", "Unchecks every in-memory checklist row.");
            Invoke("CollectionChecklistDialog", "BtnDownloadMissing", "Checks missing in-memory rows.");
            Invoke("CollectionChecklistDialog", "BtnUpdateExisting", "Checks updatable in-memory rows.");
            Invoke("CollectionChecklistDialog", "BtnConfirm", "Closes the dialog with the checked rows.");
            Invoke("CollectionChecklistDialog", "BtnCancel", "Closes the dialog without a result.");

            // InputDialog: closes with the current textbox content.
            Invoke("InputDialog", "OkButton", "Closes the dialog with the current text.");
            Invoke("InputDialog", "CancelButton", "Closes the dialog without a result.");

            Invoke("ReleaseDownloadDialog", "BtnCancel", "Cancels an in-memory download and closes the dialog.");

            // MainWindow: only view-local actions run here. Anything that touches the file system,
            // starts processes, opens modal dialogs, or talks to the network is skipped below.
            Invoke("MainWindow", "Button", "Dismisses an in-memory notification toast.");
            Invoke("MainWindow", "notificationManagerButton", "Toggles the notification drawer.");
            Invoke("MainWindow", "errorIssueCountButton", "Jumps to the next in-memory error issue.");
            Invoke("MainWindow", "warningIssueCountButton", "Jumps to the next in-memory warning issue.");
            Invoke("MainWindow", "infoIssueCountButton", "Jumps to the next in-memory info issue.");
            Skip("MainWindow", "warningIssuesButton", "Opens the issue-search flyout that needs a visual root.");
            Invoke("MainWindow", "leftDockSplitterCloseButton", "Closes an optional left dock; safe when empty.");
            Invoke("MainWindow", "rightDockSplitterCloseButton", "Closes an optional right dock; safe when empty.");
            Invoke("MainWindow", "bottomDockSplitterCloseButton", "Closes an optional bottom dock; safe when empty.");

            Skip("MainWindow", "saveButton", "Writes mod-manager files and may show confirmation dialogs.");
            Skip("MainWindow", "runDwarfFortressButton", "Launches the Dwarf Fortress process.");
            Skip("MainWindow", "undoChangesButton", "Reloads mod state and may prompt about unsaved changes.");
            Skip("MainWindow", "reloadButton", "Reloads mod state from disk and may prompt.");
            Skip("MainWindow", "newListButton", "Opens the create-modpack input dialog.");
            Skip("MainWindow", "renameListButton", "Opens the rename-modpack input dialog.");
            Skip("MainWindow", "deleteListButton", "Opens a destructive confirmation dialog.");
            Skip("MainWindow", "clearModlistButton", "Opens a destructive confirmation dialog.");
            Skip("MainWindow", "importButton", "Opens a file picker and reads a modpack from disk.");
            Skip("MainWindow", "exportButton", "Opens a save-file picker and writes a modpack to disk.");
            Skip("MainWindow", "sortButton", "Runs the full mod sort and may touch DFHack state.");
            Skip("MainWindow", "sortRulesButton", "Opens the sort-rules window.");
            Skip("MainWindow", "workshopDownloaderButton", "Opens the Workshop downloader window and Steam state.");
            Skip("MainWindow", "updateLogButton", "Opens the update-log window.");
            Skip("MainWindow", "installedModsMenuButton", "Opens the installed-mods flyout.");
            Skip("MainWindow", "settingsButton", "Opens a modal Settings dialog that waits for input.");
            Skip("MainWindow", "updateButton", "Checks the network for new releases.");
            Skip("MainWindow", "OpenColorPickerButton", "Opens a colour-picker flyout that needs a visual root.");
            Skip("MainWindow", "SearchModeButton", "Opens the search-mode flyout that needs a visual root.");

            // MessageDialog: every button just closes the dialog with a result.
            Invoke("MessageDialog", "OkButton", "Closes the dialog with OK.");
            Invoke("MessageDialog", "YesButton", "Closes the dialog with Yes.");
            Invoke("MessageDialog", "NoButton", "Closes the dialog with No.");
            Invoke("MessageDialog", "CancelButton", "Closes the dialog with Cancel.");

            Skip("ModColorPicker", "OpenColorPickerButton", "Opens a colour-picker flyout that needs a visual root.");

            // ModIssuesOverlay: raises its JumpRequested event; hosts decide what to do.
            Invoke("ModIssuesOverlay", "errorIssueCountButton", "Raises JumpRequested for the error tier.");
            Invoke("ModIssuesOverlay", "warningIssueCountButton", "Raises JumpRequested for the warning tier.");
            Invoke("ModIssuesOverlay", "infoIssueCountButton", "Raises JumpRequested for the info tier.");

            Skip("ModSearchBar", "OpenColorPickerButton", "Opens a colour-picker flyout that needs a visual root.");
            Skip("ModSearchBar", "SearchModeButton", "Opens the search-mode flyout that needs a visual root.");

            Skip("ModUpdateLogControl", "OpenColorPickerButton", "Opens a colour-picker flyout that needs a visual root.");
            Skip("ModUpdateLogControl", "SearchModeButton", "Opens the search-mode flyout that needs a visual root.");
            Skip("ModUpdateLogControl", "clearLogsButton", "Prompts for confirmation and deletes persisted update logs.");

            Skip("ModUpdateLogWindow", "OpenColorPickerButton", "Opens a colour-picker flyout that needs a visual root.");
            Skip("ModUpdateLogWindow", "SearchModeButton", "Opens the search-mode flyout that needs a visual root.");
            Skip("ModUpdateLogWindow", "clearLogsButton", "Prompts for confirmation and deletes persisted update logs.");

            Skip("RelationshipPickerWindow", "OpenColorPickerButton", "Opens a colour-picker flyout that needs a visual root.");
            Skip("RelationshipPickerWindow", "SearchModeButton", "Opens the search-mode flyout that needs a visual root.");

            Skip("SettingsDialog", "restoreBackupButton", "Prompts, overwrites data files, and restarts the app.");
            Skip("SettingsDialog", "backupNowButton", "Writes a backup archive to disk and may prompt.");
            Skip("SettingsDialog", "redoConfigButton", "Restarts the application.");

            Skip("SortRulesControl", "OpenColorPickerButton", "Opens a colour-picker flyout that needs a visual root.");
            Skip("SortRulesControl", "SearchModeButton", "Opens the search-mode flyout that needs a visual root.");
            Skip("SortRulesControl", "fixConflictsButton", "Rewrites sort rules and may prompt.");
            Skip("SortRulesControl", "clearAllRelationshipsButton", "Clears saved relationship rules and may prompt.");

            Skip("SortRulesWindow", "OpenColorPickerButton", "Opens a colour-picker flyout that needs a visual root.");
            Skip("SortRulesWindow", "SearchModeButton", "Opens the search-mode flyout that needs a visual root.");
            Skip("SortRulesWindow", "fixConflictsButton", "Rewrites sort rules and may prompt.");
            Skip("SortRulesWindow", "clearAllRelationshipsButton", "Clears saved relationship rules and may prompt.");

            Invoke("SteamCmdSetupDialog", "BtnCancel", "Cancels the optional setup and closes the dialog.");
            Skip("SteamCmdSetupDialog", "BtnBrowse", "Opens a native file picker.");
            Skip("SteamCmdSetupDialog", "BtnInstall", "Downloads SteamCMD and touches the file system.");

            Skip("UpdateDialog", "ChannelSwitchButton", "Fetches the release list from the network.");

            Invoke("WorkshopDownloaderControl", "BtnClearText", "Clears the in-memory URL textbox.");
            Invoke("WorkshopDownloaderControl", "BtnClearCompleted", "Clears completed items from the in-memory queue.");
            Invoke("WorkshopDownloaderControl", "BtnCancelAll", "Cancels in-memory queue items; safe when empty.");
            Skip("WorkshopDownloaderControl", "BtnRetryAll", "Requeues downloads and can start network work.");
            Skip("WorkshopDownloaderControl", "BtnResolveAndQueue", "Resolves Workshop URLs and starts network work.");

            Invoke("WorkshopDownloaderWindow", "BtnClearText", "Clears the in-memory URL textbox.");
            Invoke("WorkshopDownloaderWindow", "BtnClearCompleted", "Clears completed items from the in-memory queue.");
            Invoke("WorkshopDownloaderWindow", "BtnCancelAll", "Cancels in-memory queue items; safe when empty.");
            Skip("WorkshopDownloaderWindow", "BtnRetryAll", "Requeues downloads and can start network work.");
            Skip("WorkshopDownloaderWindow", "BtnResolveAndQueue", "Resolves Workshop URLs and starts network work.");

            return plans;
        }

        private static UiActionKey Key(string root, string control) => new(root, control, "Click");
    }
}
