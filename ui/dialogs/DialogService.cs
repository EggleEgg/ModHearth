using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ModHearth.UI;

/** <summary> Generic "are you sure you want to exit without saving?" dialog </summary>*/
public enum UnsavedChangesChoice
{
    Save,
    ExitWithoutSaving,
    Cancel
}

public static class DialogService
{
    public static async Task ShowMessageAsync(Window owner, string message, string title)
    {
        _ = await MessageDialog.ShowAsync(owner, message, title, MessageDialogButtons.Ok);
    }

    public static async Task<bool> ShowConfirmAsync(Window owner, string message, string title)
    {
        MessageDialogResult result = await MessageDialog.ShowAsync(owner, message, title, MessageDialogButtons.YesNo);
        return result == MessageDialogResult.Yes;
    }

    public static async Task<UnsavedChangesChoice> ShowUnsavedChangesPromptAsync(
        Window owner,
        string? subjectName,
        string actionName)
    {
        string scopedSubject = string.IsNullOrWhiteSpace(subjectName)
            ? "this item"
            : $"'{subjectName}'";
        string message = $"You have unsaved changes in {scopedSubject}. Are you sure you want to {actionName} without saving?";

        MessageDialogResult result = await MessageDialog.ShowAsync(
            owner,
            message,
            "Unsaved changes",
            MessageDialogButtons.YesNoCancel,
            yesText: "Save",
            noText: "Exit without saving",
            cancelText: "Cancel");

        return result switch
        {
            MessageDialogResult.Yes => UnsavedChangesChoice.Save,
            MessageDialogResult.No => UnsavedChangesChoice.ExitWithoutSaving,
            _ => UnsavedChangesChoice.Cancel
        };
    }

    public static Task<string?> ShowInputAsync(Window owner, string prompt, string title, string defaultValue)
    {
        return InputDialog.ShowAsync(owner, prompt, title, defaultValue);
    }

    public static async Task<string?> PickFileAsync(Window owner, string title, IEnumerable<FilePickerFileType> fileTypes)
    {
        FilePickerOpenOptions options = new()
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes?.ToList()
        };

        IReadOnlyList<IStorageFile> result = await owner.StorageProvider.OpenFilePickerAsync(options);
        return result?.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> PickFolderAsync(Window owner, string title)
    {
        FolderPickerOpenOptions options = new()
        {
            Title = title
        };

        IReadOnlyList<IStorageFolder> result = await owner.StorageProvider.OpenFolderPickerAsync(options);
        return result?.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> PickSaveFileAsync(Window owner, string title, string defaultFileName, IEnumerable<FilePickerFileType> fileTypes)
    {
        FilePickerSaveOptions options = new()
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = fileTypes?.ToList()
        };

        IStorageFile? result = await owner.StorageProvider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }

    public static async Task<bool> RunConfirmedActionAsync(
        Window owner,
        string prompt,
        string title,
        Func<List<string>> workFunc)
    {
        bool confirm = await ShowConfirmAsync(owner, prompt, title);
        if (!confirm)
            return false;

        List<string> failures = await Task.Run(workFunc);
        if (failures.Count > 0)
            await ShowMessageAsync(owner, string.Join(Environment.NewLine, failures), title);

        return true;
    }

    public static async Task<bool> RunConfirmedActionAsync(
        Window owner,
        string prompt,
        string title,
        Func<(bool success, string message)> workFunc,
        string successTitle = "Success",
        string failureTitle = "Clear failed")
    {
        bool confirm = await ShowConfirmAsync(owner, prompt, title);
        if (!confirm)
            return false;

        (bool success, string message) = await Task.Run(workFunc);
        await ShowMessageAsync(owner, message, success ? successTitle : failureTitle);

        return success;
    }
}
