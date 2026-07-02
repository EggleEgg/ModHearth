using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void OnModpackChanged()
    {
        if (modifyingComboBox)
            return;

        if (changesMade)
        {
            _ = HandleModpackChangeWithUnsavedAsync();
            return;
        }

        SetAndRefreshModpack(modpackComboBox.SelectedIndex);
        lastIndex = modpackComboBox.SelectedIndex;
    }

    private async Task HandleModpackChangeWithUnsavedAsync()
    {
        UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
            this,
            manager.SelectedModlist.name,
            "switch modpacks");

        if (choice == UnsavedChangesChoice.Save)
            await SaveCurrentModpackAsync();
        else if (choice == UnsavedChangesChoice.ExitWithoutSaving)
            SetAndMarkChanges(false);
        else
        {
            modifyingComboBox = true;
            modpackComboBox.SelectedIndex = lastIndex;
            modifyingComboBox = false;
            return;
        }

        SetAndRefreshModpack(modpackComboBox.SelectedIndex);
        lastIndex = modpackComboBox.SelectedIndex;
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
            if (choice == UnsavedChangesChoice.Cancel)
                return;

            if (choice == UnsavedChangesChoice.Save)
                await SaveCurrentModpackAsync();
            else
                SetAndMarkChanges(false);

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
