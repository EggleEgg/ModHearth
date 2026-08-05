using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void OnModpackChanged()
    {
        if (modifyingComboBox)
            return;

        if (manager.SelectedModlist == null)
            return;

        if (changesMade)
        {
            if (ConfigManager.IsAutoSaveEnabled())
            {
                ModHearthManager.ModpackSaveResult result = manager.SaveCurrentModpack();
                ShowModpackSaveNotice(result);
            }
            else
            {
                // If autosave is not enabled, prompt the user if there are unsaved changes.
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
                        this,
                        manager.SelectedModlist.name,
                        "change modpacks");
                    switch (choice)
                    {
                        case UnsavedChangesChoice.Cancel:
                            modifyingComboBox = true;
                            modpackComboBox.SelectedIndex = lastIndex;
                            modifyingComboBox = false;
                            return;
                        case UnsavedChangesChoice.Save:
                            await SaveCurrentModpackAsync();
                            break;
                    }

                    SetAndRefreshModpack(modpackComboBox.SelectedIndex);
                    lastIndex = modpackComboBox.SelectedIndex;
                    SetChangesMade(false);
                });
                return;
            }
        }

        SetAndRefreshModpack(modpackComboBox.SelectedIndex);
        lastIndex = modpackComboBox.SelectedIndex;
        SetChangesMade(false);
    }

    private async void MainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (bypassUnsavedClosePrompt || !changesMade)
            return;

        e.Cancel = true;
        if (unsavedClosePromptInFlight)
            return;

        unsavedClosePromptInFlight = true;
        try
        {
            UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
                this,
                manager.SelectedModlist.name,
                "exit");
            switch (choice)
            {
                case UnsavedChangesChoice.Cancel:
                    return;
                case UnsavedChangesChoice.Save:
                    await SaveCurrentModpackAsync();
                    break;
                default:
                    await SetAndMarkChangesAsync(false);
                    break;
            }

            bypassUnsavedClosePrompt = true;
            Close();
        }
        finally
        {
            bypassUnsavedClosePrompt = false;
            unsavedClosePromptInFlight = false;
        }
    }

    private void WindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ContextMenuCoordinator.DismissActive();

        if (!HasJumpHighlights())
            return;

        if (e.Source is Control control)
        {
            if (warningIssuesButton != null &&
                (control == warningIssuesButton || control.FindAncestorOfType<Button>() == warningIssuesButton))
                return;

            ListBoxItem? item = control.FindAncestorOfType<ListBoxItem>();
            if (item?.DataContext is ModRefViewModel vm && vm.IsJumpHighlighted)
                return;
        }

        ClearJumpHighlights();
    }
}
