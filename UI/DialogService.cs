using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModHearth.UI;

public static class DialogService
{
    public static async Task ShowMessageAsync(Window owner, string message, string title)
    {
        await MessageDialog.ShowAsync(owner, message, title, MessageDialogButtons.Ok);
    }

    public static async Task<bool> ShowConfirmAsync(Window owner, string message, string title)
    {
        MessageDialogResult result = await MessageDialog.ShowAsync(owner, message, title, MessageDialogButtons.YesNo);
        return result == MessageDialogResult.Yes;
    }

    public static Task<string?> ShowInputAsync(Window owner, string prompt, string title, string defaultValue)
    {
        return InputDialog.ShowAsync(owner, prompt, title, defaultValue);
    }

    public static async Task<string?> PickFileAsync(Window owner, string title, IEnumerable<FilePickerFileType> fileTypes)
    {
        FilePickerOpenOptions options = new FilePickerOpenOptions
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
        FolderPickerOpenOptions options = new FolderPickerOpenOptions
        {
            Title = title
        };

        IReadOnlyList<IStorageFolder> result = await owner.StorageProvider.OpenFolderPickerAsync(options);
        return result?.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> PickSaveFileAsync(Window owner, string title, string defaultFileName, IEnumerable<FilePickerFileType> fileTypes)
    {
        FilePickerSaveOptions options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = fileTypes?.ToList()
        };

        IStorageFile? result = await owner.StorageProvider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }
}
