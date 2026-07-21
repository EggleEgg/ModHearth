using Avalonia.Platform.Storage;
using System.Text.Json;
using System.Threading;

namespace ModHearth.UI;

public partial class MainWindow
{
    private async Task SaveCurrentModpackAsync()
    {
        ModHearthManager.ModpackSaveResult result = await Task.Run(() => manager.SaveCurrentModpack());
        await SetAndMarkChangesAsync(false);
        ShowModpackSaveNotice(result);
    }

    private void ShowModpackSaveNotice(ModHearthManager.ModpackSaveResult result)
    {
        ResetModManagerWatcher();

        if (ConfigManager.IsAutoSaveEnabled())
        {
            // Confirms the local save succeeded, independent of DFHack
            ShowNotification("Modlist Autosaved", "saveWhiteIcon.svg", 1000);
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
        redoMods = new List<DFHMod>(manager.enabledMods);
        redoAvailable = true;

        manager.SetSelectedModpack(lastIndex);
        RefreshModlistPanels();
        await SetAndMarkChangesAsync(false);
    }

    private async Task RedoListChangesAsync()
    {
        if (!redoAvailable || redoMods.Count == 0)
            return;

        isRedoing = true;
        try
        {
            manager.SetActiveMods(new List<DFHMod>(redoMods));
            RefreshModlistPanels();
            await SetAndMarkChangesAsync(true);
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

    private void MarkChanges(int index)
    {
        if (changesMarked)
            return;
        if (index < 0 || index >= manager.modpacks.Count)
            return;

        List<string> names = manager.modpacks.Select(m => m.name).ToList();
        names[index] = names[index] + "*";
        modifyingComboBox = true;
        modpackComboBox.ItemsSource = names;
        modpackComboBox.SelectedIndex = index;
        modifyingComboBox = false;
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

        List<string> names = manager.modpacks.Select(m => m.name).ToList();
        if (names[index].EndsWith("*", StringComparison.Ordinal))
            names[index] = names[index][..^1];

        modifyingComboBox = true;
        modpackComboBox.ItemsSource = names;
        modpackComboBox.SelectedIndex = index;
        modifyingComboBox = false;
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
    private async Task SetAndMarkChangesAsync(bool made)
    {
        if (made && !isRedoing)
            ClearRedo();

        if (!made || (!ConfigManager.IsAutoSortEnabled() && !ConfigManager.IsAutoSaveEnabled()))
        {
            FinishSetAndMarkChanges(made, autoSaved: false);
            return;
        }

        if (!await autoActionGate.WaitAsync(0))
        {
            autoActionRerunRequested = true;
            return;
        }

        try
        {
            bool autoSaved;
            do
            {
                autoActionRerunRequested = false;
                autoSaved = await RunAutoSortAndAutoSaveAsync();
            }
            while (autoActionRerunRequested);

            FinishSetAndMarkChanges(made, autoSaved);
        }
        finally
        {
            autoActionGate.Release();
        }
    }

    private async Task<bool> RunAutoSortAndAutoSaveAsync()
    {
        if (ConfigManager.IsAutoSortEnabled())
        {
            bool sorted = await Task.Run(() => manager.ModSortEnabledMods());
            if (sorted)
                ShowNotification("Modlist Autosorted", "sortBorderIcon.svg", 1000);
            RefreshModlistPanels();
        }

        bool autoSaved = false;
        if (ConfigManager.IsAutoSaveEnabled())
        {
            ModHearthManager.ModpackSaveResult result = await Task.Run(() => manager.SaveCurrentModpack());
            ShowModpackSaveNotice(result);
            autoSaved = true;
        }

        return autoSaved;
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

        DFHModpack newPack = new DFHModpack(false, manager.GenerateVanillaModlist(), newName);
        await RegisterNewModpackAsync(newPack);
    }

    private async Task RegisterNewModpackAsync(DFHModpack newList)
    {
        modifyingComboBox = true;

        manager.modpacks.Add(newList);
        ModHearthManager.ModpackSaveResult saveResult = manager.SaveAllModpacks();
        ShowModpackSaveNotice(saveResult);

        modpackComboBox.ItemsSource = manager.modpacks.Select(m => m.name).ToList();
        modpackComboBox.SelectedIndex = manager.modpacks.Count - 1;

        manager.SetSelectedModpack(modpackComboBox.SelectedIndex);
        RefreshModlistPanels();
        await SetAndMarkChangesAsync(false);

        modifyingComboBox = false;
    }

    private async Task RenameModpackAsync()
    {
        string? newName = await DialogService.ShowInputAsync(this,
            "Please enter a new name for the modpack",
            "New Modpack Name",
            manager.SelectedModlist.name);

        if (string.IsNullOrWhiteSpace(newName))
            return;

        modifyingComboBox = true;

        manager.SelectedModlist.name = newName;
        modpackComboBox.ItemsSource = manager.modpacks.Select(m => m.name).ToList();
        modpackComboBox.SelectedIndex = manager.selectedModlistIndex;

        ModHearthManager.ModpackSaveResult saveResult = manager.SaveCurrentModpack();
        ShowModpackSaveNotice(saveResult);
        await SetAndMarkChangesAsync(false);

        modifyingComboBox = false;
    }

    private async Task DeleteModpackAsync()
    {
        bool confirm = await DialogService.ShowConfirmAsync(this,
            $"Are you sure you want to delete {manager.SelectedModlist.name}? This is final.",
            "Delete modlist");
        if (!confirm)
            return;

        await SetAndMarkChangesAsync(false);

        if (manager.modpacks.Count == 1)
        {
            await DialogService.ShowMessageAsync(this, "You cannot delete the last modlist.", "Failed");
            return;
        }

        modifyingComboBox = true;

        int removeIndex = manager.selectedModlistIndex;
        manager.modpacks.RemoveAt(removeIndex);
        ModHearthManager.ModpackSaveResult saveResult = manager.SaveAllModpacks();
        ShowModpackSaveNotice(saveResult);

        modpackComboBox.ItemsSource = manager.modpacks.Select(m => m.name).ToList();
        manager.SetSelectedModpack(0);
        modpackComboBox.SelectedIndex = 0;
        lastIndex = 0;
        RefreshModlistPanels();

        modifyingComboBox = false;
    }

    private async Task ImportModpackAsync()
    {
        string? filePath = await DialogService.PickFileAsync(this,
            "Select a Modpack JSON File",
            new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } });

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            string importedString = await File.ReadAllTextAsync(filePath);
            DFHModpack? importedList = JsonSerializer.Deserialize<DFHModpack>(importedString);
            if (importedList == null)
                throw new InvalidOperationException("Invalid modpack file.");

            for (int i = 0; i < manager.modpacks.Count; i++)
            {
                DFHModpack otherModlist = manager.modpacks[i];
                if (otherModlist.name == importedList.name)
                {
                    bool overwrite = await DialogService.ShowConfirmAsync(this,
                        $"A modpack with the name {otherModlist.name} is already present. Would you like to overwrite it?",
                        "Modlist Already Present");
                    if (!overwrite)
                        return;

                    modifyingComboBox = true;
                    modpackComboBox.SelectedIndex = i;
                    lastIndex = i;
                    modifyingComboBox = false;

                    manager.SetSelectedModpack(i);
                    manager.SetActiveMods(importedList.modlist);
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
            new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } });

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
            string exportString = JsonSerializer.Serialize(manager.SelectedModlist, options);
            await File.WriteAllTextAsync(filePath, exportString);
            await DialogService.ShowMessageAsync(this, "File saved successfully.", "Success");
        }
        catch (Exception ex)
        {
            await DialogService.ShowMessageAsync(this, "Error: " + ex.Message, "Error");
        }
    }
}