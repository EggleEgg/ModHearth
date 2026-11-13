using System.Text.Json;
using Avalonia.Platform.Storage;
using ModHearth.Config;
using ModHearth.Models;
using ModHearth.UI.Dialogs;
using ModHearth.Utils.IO;

using static ModHearth.Manager.ModHearthManager;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private async Task SaveCurrentModpackAsync()
        {
            ModpackSaveResult result = await Task.Run(manager.SaveCurrentModpack);
            _ = await SetAndMarkChangesAsync(false);
            ShowModpackSaveNotice(result);
        }

        private void ShowModpackSaveNotice(ModpackSaveResult result)
        {
            ResetModManagerWatcher();

            if (ConfigManager.IsAutoSaveEnabled())
            {
                // Confirms the local save succeeded, independent of DFHack
                ShowNotification("Modlist Autosaved", "saveUpdateIcon.svg", notificationDelay: 1000);
            }

            if (string.IsNullOrWhiteSpace(result.LiveReloadMessage))
                return;

            if (result.UsesFallbackStorage || !result.LiveReloadApplied)
                ShowTransientStatusNotice(result.LiveReloadMessage);
        }

        private async Task UndoChangesAsync()
        {
            EnsureModlistHistoryCurrent();
            IReadOnlyList<DfhMod>? snapshot = modlistHistory.Undo();
            if (snapshot == null)
                return;

            await ApplyModlistSnapshotAsync(snapshot);
        }

        private async Task RedoListChangesAsync()
        {
            EnsureModlistHistoryCurrent();
            IReadOnlyList<DfhMod>? snapshot = modlistHistory.Redo();
            if (snapshot == null)
                return;

            await ApplyModlistSnapshotAsync(snapshot);
        }

        private async Task ApplyModlistSnapshotAsync(IReadOnlyList<DfhMod> snapshot)
        {
            manager.SetActiveMods([.. snapshot]);
            RefreshModlistPanels();

            // History navigation is not a new edit: skip auto-sort/auto-save so the restored state
            // stays exactly as recorded, and derive the dirty flag from the saved modpack instead.
            _ = await SetAndMarkChangesAsync(false);
        }

        private void SetAndRefreshModpack(int index)
        {
            manager.SetSelectedModpack(index);
            ResetModlistHistory();
            RefreshModlistPanels();
        }

        /// <summary>
        /// Keeps the undo history attached to the modpack it was recorded for. Switching packs (or
        /// reloading them from disk) replaces the manager's modpack instances, which starts a fresh
        /// history instead of applying another pack's snapshot to the current one.
        /// </summary>
        private void EnsureModlistHistoryCurrent()
        {
            if (manager.modpacks.Count == 0)
            {
                if (modlistHistoryOwner != null)
                {
                    modlistHistoryOwner = null;
                    modlistHistory.Clear();
                }

                return;
            }

            int index = manager.selectedModlistIndex;
            if (index < 0 || index >= manager.modpacks.Count)
                return;

            DfhModpack selected = manager.modpacks[index];
            if (ReferenceEquals(modlistHistoryOwner, selected))
                return;

            modlistHistoryOwner = selected;
            modlistHistory.Reset(manager.enabledMods);
        }

        private void ResetModlistHistory()
        {
            modlistHistoryOwner = null;
            modlistHistory.Clear();
            EnsureModlistHistoryCurrent();
        }

        private void RecordModlistHistory()
        {
            EnsureModlistHistoryCurrent();
            if (modlistHistoryOwner != null)
                _ = modlistHistory.Record(manager.enabledMods);
        }

        private bool CanUndoModlistChanges()
        {
            EnsureModlistHistoryCurrent();
            return modlistHistory.CanUndo;
        }

        private bool CanRedoModlistChanges()
        {
            EnsureModlistHistoryCurrent();
            return modlistHistory.CanRedo;
        }

        /// <summary>
        /// Replaces the modpack combo's items with the manager's current names and selects
        /// <paramref name="index"/>, suppressing SelectionChanged side effects while doing so.
        /// </summary>
        private void RefreshModpackCombo(int index)
            => RefreshModpackCombo([.. manager.modpacks.Select(m => m.Name)], index);

        private void RefreshModpackCombo(IReadOnlyList<string> names, int index)
        {
            WithComboGuard(() =>
            {
                modpackComboBox.ItemsSource = names;
                modpackComboBox.SelectedIndex = index;
            });
        }

        /// <summary>
        /// Selects an existing combo entry without triggering the modpack switch handler.
        /// </summary>
        private void SetModpackComboIndex(int index)
        {
            WithComboGuard(() => modpackComboBox.SelectedIndex = index);
        }

        /// <summary>
        /// Runs a combo mutation with SelectionChanged side effects suppressed.
        /// </summary>
        private void WithComboGuard(Action mutate)
        {
            modifyingComboBox = true;
            try
            {
                mutate();
            }
            finally
            {
                modifyingComboBox = false;
            }
        }

        private void MarkChanges(int index)
        {
            if (changesMarked)
                return;
            if (index < 0 || index >= manager.modpacks.Count)
                return;

            List<string> names = [.. manager.modpacks.Select(m => m.Name)];
            names[index] = names[index] + "*";
            RefreshModpackCombo(names, index);
            changesMarked = true;
        }

        private void UnmarkChanges(int index)
        {
            if (!changesMarked)
                return;
            if (index < 0 || index >= manager.modpacks.Count)
            {
                changesMarked = false;
                return;
            }

            List<string> names = [.. manager.modpacks.Select(m => m.Name)];
            if (names[index].EndsWith('*'))
                names[index] = names[index][..^1];

            RefreshModpackCombo(names, index);
            changesMarked = false;
        }

        private void SetChangesMade(bool made)
        {
            changesMade = made;
            undoChangesButton.IsEnabled = CanUndoModlistChanges();
            renameListButton.IsEnabled = !made;
            importButton.IsEnabled = !made;
            exportButton.IsEnabled = !made;
            newListButton.IsEnabled = !made;
        }

        // At most one autosort+autosave pass runs at a time. If a trigger arrives while a pass is already running, we don't start a second overlapping one (which could finish out of order
        // and silently clobber the more recent state with a stale sort/save),
        // we just flag that the in-flight pass should run once more after it finishes, reflecting whatever the latest state is by then.
        // Made=false calls skip this gate entirely, matching original behavior where they never triggered sort/save in the first place.
        private async Task<bool> SetAndMarkChangesAsync(bool made, bool skipSort = false)
        {
            if (!made || (!ConfigManager.IsAutoSortEnabled() && !ConfigManager.IsAutoSaveEnabled()))
            {
                FinishSetAndMarkChanges(made);
                return false;
            }

            if (!await autoActionGate.WaitAsync(0))
            {
                autoActionRerunRequested = true;
                return false;
            }

            try
            {
                bool sorted = false;
                do
                {
                    autoActionRerunRequested = false;
                    sorted |= await RunAutoSortAndAutoSaveAsync(skipSort);
                }
                while (autoActionRerunRequested);

                FinishSetAndMarkChanges(made);
                return sorted;
            }
            finally
            {
                _ = autoActionGate.Release();
            }
        }

        private async Task<bool> RunAutoSortAndAutoSaveAsync(bool skipSort)
        {
            bool sorted = false;
            if (ConfigManager.IsAutoSortEnabled() && !skipSort)
            {
                sorted = await Task.Run(manager.ModSortEnabledMods);
                if (sorted)
                {
                    ShowNotification("Modlist Autosorted", "sortBorderUpdateIcon.svg", notificationDelay: 1000);
                    RefreshModlistPanels(updateCache: false, updateColors: false);
                }
            }

            if (ConfigManager.IsAutoSaveEnabled())
            {
                ModpackSaveResult result = await Task.Run(manager.SaveCurrentModpack);
                ShowModpackSaveNotice(result);
            }

            return sorted;
        }

        private void FinishSetAndMarkChanges(bool made)
        {
            if (made)
                RecordModlistHistory();

            // Saving updates the selected modpack's stored list, so comparing against it keeps the
            // dirty flag correct after save, undo, redo, and modpack switches without a separate flag.
            bool hasChanges = !manager.enabledMods.SequenceEqual(manager.SelectedModlist.Modlist);
            SetChangesMade(hasChanges);

            if (hasChanges)
                MarkChanges(lastIndex);
            else
                UnmarkChanges(lastIndex);
        }

        private async Task CreateNewModpackAsync()
        {
            string? newName = await DialogService.ShowInputAsync(this,
                "Please enter a name for the new modpack",
                "New Modpack Name",
                string.Empty);

            if (string.IsNullOrWhiteSpace(newName))
                return;

            DfhModpack newPack = new(false, manager.GenerateVanillaModlist(), newName);
            await RegisterNewModpackAsync(newPack);
        }

        private async Task RegisterNewModpackAsync(DfhModpack newList)
        {
            manager.modpacks.Add(newList);
            ModpackSaveResult saveResult = manager.SaveAllModpacks();
            ShowModpackSaveNotice(saveResult);

            int index = manager.modpacks.Count - 1;
            RefreshModpackCombo(index);

            manager.SetSelectedModpack(index);
            RefreshModlistPanels();
            _ = await SetAndMarkChangesAsync(false);
        }

        private async Task RenameModpackAsync()
        {
            string? newName = await DialogService.ShowInputAsync(this,
                "Please enter a new name for the modpack",
                "New Modpack Name",
                manager.SelectedModlist.Name);

            if (string.IsNullOrWhiteSpace(newName))
                return;

            manager.SelectedModlist.Name = newName;
            RefreshModpackCombo(manager.selectedModlistIndex);

            ModpackSaveResult saveResult = manager.SaveCurrentModpack();
            ShowModpackSaveNotice(saveResult);
            _ = await SetAndMarkChangesAsync(false);
        }

        private async Task DeleteModpackAsync()
        {
            bool confirm = await DialogService.ShowConfirmAsync(this,
                $"Are you sure you want to delete the modlist '{manager.SelectedModlist.Name}'? This is final.",
                "Delete modlist");
            if (!confirm)
                return;

            _ = await SetAndMarkChangesAsync(false);

            if (manager.modpacks.Count == 1)
            {
                await DialogService.ShowMessageAsync(this, "You cannot delete the last modlist.", "Failed");
                return;
            }

            int removeIndex = manager.selectedModlistIndex;
            manager.modpacks.RemoveAt(removeIndex);
            ModpackSaveResult saveResult = manager.SaveAllModpacks();
            ShowModpackSaveNotice(saveResult);

            RefreshModpackCombo(0);
            manager.SetSelectedModpack(0);
            lastIndex = 0;
            ResetModlistHistory();
            RefreshModlistPanels();
            SetChangesMade(false);
        }

        private async Task ImportModpackAsync()
        {
            string? filePath = await DialogService.PickFileAsync(this,
                "Select a Modpack JSON File",
                [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]);

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            _ = await DialogService.TryRunFileActionAsync(this, async () =>
            {
                string importedString = await File.ReadAllTextAsync(filePath);
                DfhModpack? importedList = JsonSerializer.Deserialize<DfhModpack>(importedString, JsonFileStore.CaseInsensitive) ?? throw new InvalidOperationException("Invalid modpack file.");
                for (int i = 0; i < manager.modpacks.Count; i++)
                {
                    DfhModpack otherModlist = manager.modpacks[i];
                    if (otherModlist.Name == importedList.Name)
                    {
                        bool overwrite = await DialogService.ShowConfirmAsync(this,
                            $"A modpack with the name {otherModlist.Name} is already present. Would you like to overwrite it?",
                            "Modlist Already Present");
                        if (!overwrite)
                            return;

                        SetModpackComboIndex(i);
                        lastIndex = i;

                        manager.SetSelectedModpack(i);
                        ResetModlistHistory();
                        manager.SetActiveMods(importedList.Modlist);
                        RefreshModlistPanels();

                        FinishSetAndMarkChanges(made: true);
                        return;
                    }
                }

                await RegisterNewModpackAsync(importedList);
            });
        }

        private async Task ExportModpackAsync()
        {
            string? filePath = await DialogService.PickSaveFileAsync(this,
                "Save Modpack JSON File",
                "modpack.json",
                [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]);

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            _ = await DialogService.TryRunFileActionAsync(this, async () =>
            {
                string exportString = JsonFileStore.Serialize(manager.SelectedModlist);
                await File.WriteAllTextAsync(filePath, exportString);
            }, successMessage: "File saved successfully.");
        }

        private async Task ClearModlistAsync()
        {
            if (manager.enabledMods.Count == 0)
                return;

            manager.SetActiveMods([]);
            RefreshModlistPanels();
            _ = await SetAndMarkChangesAsync(true);
        }
    }
}