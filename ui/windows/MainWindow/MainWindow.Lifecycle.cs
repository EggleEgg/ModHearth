using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModHearth.Config;
using ModHearth.Manager;
using ModHearth.UI.Dialogs;
using ModHearth.UI.ModList;
using ModHearth.UI.ViewModels;
using ModHearth.Utils.Logging;

namespace ModHearth.UI
{
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
                        if (!await ConfirmUnsavedChangesAsync("change modpacks"))
                        {
                            SetModpackComboIndex(lastIndex);
                            return;
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
                if (!await ConfirmUnsavedChangesAsync("exit", () => SetAndMarkChangesAsync(false)))
                    return;

                bypassUnsavedClosePrompt = true;
                Close();
            }
            catch (Exception ex)
            {
                // async void closing handler: keep the window usable and record the failure.
                Loggers.Diag.LogError($"Failed while prompting on close: {ex.Message}");
            }
            finally
            {
                bypassUnsavedClosePrompt = false;
                unsavedClosePromptInFlight = false;
            }
        }

        /// <summary>
        /// Prompts for unsaved changes and resolves the user's choice. Returns false only when the
        /// caller must abort (Cancel). "Save" persists the active modpack; "discard" runs
        /// <paramref name="onDiscard"/> (defaults to clearing the dirty flag) and proceeds.
        /// </summary>
        private async Task<bool> ConfirmUnsavedChangesAsync(string actionName, Func<Task>? onDiscard = null)
        {
            if (!changesMade)
                return true;

            UnsavedChangesChoice choice = await DialogService.ShowUnsavedChangesPromptAsync(
                this,
                manager.SelectedModlist?.Name,
                actionName);

            switch (choice)
            {
                case UnsavedChangesChoice.Cancel:
                    return false;

                case UnsavedChangesChoice.Save:
                    await SaveCurrentModpackAsync();
                    return true;

                default:
                    if (onDiscard != null)
                    {
                        await onDiscard();
                    }
                    else
                    {
                        SetChangesMade(false);
                    }

                    return true;
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
                {
                    return;
                }

                ListBoxItem? item = control.FindAncestorOfType<ListBoxItem>();
                if (item?.DataContext is ModRefViewModel vm && vm.IsJumpHighlighted)
                    return;
            }

            ClearJumpHighlights();
        }
    }
}
