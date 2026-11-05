using System.Text.Json;
using Avalonia.Platform.Storage;
using ModHearth.Models;
using ModHearth.UI.Dialogs;
using ModHearth.Utilities;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        private async Task SaveCurrentModpackAsync()
        {
            ModHearthManager.ModpackSaveResult result = await Task.Run(manager.SaveCurrentModpack);
            _ = await SetAndMarkChangesAsync(false);
            ShowModpackSaveNotice(result);
        }

        private void ShowModpackSaveNotice(ModHearthManager.ModpackSaveResult result)
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
            bool confirm = await DialogService.ShowConfirmAsync(this, "Are you sure you want to reset modlist changes?", "Undo changes");
            if (!confirm)
                return;

            await UndoListChangesAsync();
        }

        private async Task UndoListChangesAsync()
        {
            redoMods = [.. manager.enabledMods];
            redoAvailable = true;

            manager.SetSelectedModpack(lastIndex);
            RefreshModlistPanels();
            _ = await SetAndMarkChangesAsync(false);
        }

        private async Task RedoListChangesAsync()
        {
            if (!redoAvailable || redoMods.Count == 0)
                return;

            isRedoing = true;
            try
            {
                manager.SetActiveMods([.. redoMods]);
                RefreshModlistPanels();
                _ = await SetAndMarkChangesAsync(true);
            }
            finally
            {
                // Always reset, even if the sort/save pass above throws. SetAndMarkChanges's own ClearRedo() guard depends on isRedoing
                // being accurate, and a stale "true" here would defeat it on the next unrelated change.
                isRedoing = false;
            }

            redoAvailable = false;
            redoMods.Clear();
        }

        private void ClearRedo()
        {
            redoAvailable = false;
            redoMods.Clear();
        }

        private void SetAndRefreshModpack(int index)
        {
            manager.SetSelectedModpack(index);
            RefreshModlistPanels();
        }

        /// <summary>
        /// Replaces the modpack combo's items with the manager's current names and selects
        /// <paramref name="index"/>, suppressing SelectionChanged side effects while doing so.
        /// </summary>
        private void RefreshModpackCombo(int index)
            => RefreshModpackCombo([.. manager.modpacks.Select(m => m.Name)], index);

        private void RefreshModpackCombo(IReadOnlyList<string> names, int index)
        {
            modifyingComboBox = true;
            try
            {
                modpackComboBox.ItemsSource = names;
                modpackComboBox.SelectedIndex = index;
            }
            finally
            {
                modifyingComboBox = false;
            }
        }

        /// <summary>
        /// Selects an existing combo entry without triggering the modpack switch handler.
        /// </summary>
        private void SetModpackComboIndex(int index)
        {
            modifyingComboBox = true;
            try
            {
                modpackComboBox.SelectedIndex = index;
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
            undoChangesButton.IsEnabled = made;
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
            if (made && !isRedoing)
                ClearRedo();

            if (!made || (!ConfigManager.IsAutoSortEnabled() && !ConfigManager.IsAutoSaveEnabled()))
            {
                FinishSetAndMarkChanges(made, autoSaved: false);
                return false;
            }

            if (!await autoActionGate.WaitAsync(0))
            {
                autoActionRerunRequested = true;
                return false;
            }

            try
            {
                bool autoSaved = false;
                bool sorted = false;
                do
                {
                    autoActionRerunRequested = false;
                    (bool s, bool a) = await RunAutoSortAndAutoSaveAsync(skipSort);
                    if (s) sorted = true;
                    autoSaved = a;
                }
                while (autoActionRerunRequested);

                FinishSetAndMarkChanges(made, autoSaved);
                return sorted;
            }
            finally
            {
                _ = autoActionGate.Release();
            }
        }

        private async Task<(bool sorted, bool autoSaved)> RunAutoSortAndAutoSaveAsync(bool skipSort)
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

            bool autoSaved = false;
            if (ConfigManager.IsAutoSaveEnabled())
            {
                ModHearthManager.ModpackSaveResult result = await Task.Run(manager.SaveCurrentModpack);
                ShowModpackSaveNotice(result);
                autoSaved = true;
            }

            return (sorted, autoSaved);
        }

        private void FinishSetAndMarkChanges(bool made, bool autoSaved)
        {
            SetChangesMade(!autoSaved && made);

            if (made && !autoSaved)
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
            ModHearthManager.ModpackSaveResult saveResult = manager.SaveAllModpacks();
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

            ModHearthManager.ModpackSaveResult saveResult = manager.SaveCurrentModpack();
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
            ModHearthManager.ModpackSaveResult saveResult = manager.SaveAllModpacks();
            ShowModpackSaveNotice(saveResult);

            RefreshModpackCombo(0);
            manager.SetSelectedModpack(0);
            lastIndex = 0;
            RefreshModlistPanels();
        }

        private async Task ImportModpackAsync()
        {
            string? filePath = await DialogService.PickFileAsync(this,
                "Select a Modpack JSON File",
                [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]);

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            try
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
                        manager.SetActiveMods(importedList.Modlist);
                        RefreshModlistPanels();

                        SetChangesMade(true);
                        MarkChanges(i);
                        return;
                    }
                }

                await RegisterNewModpackAsync(importedList);
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageAsync(this, "Error: " + ex.Message, "Error");
            }
        }

        private async Task ExportModpackAsync()
        {
            string? filePath = await DialogService.PickSaveFileAsync(this,
                "Save Modpack JSON File",
                "modpack.json",
                [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]);

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            try
            {
                string exportString = JsonFileStore.Serialize(manager.SelectedModlist);
                await File.WriteAllTextAsync(filePath, exportString);
                await DialogService.ShowMessageAsync(this, "File saved successfully.", "Success");
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageAsync(this, "Error: " + ex.Message, "Error");
            }
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