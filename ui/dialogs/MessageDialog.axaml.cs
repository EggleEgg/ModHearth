using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace ModHearth.UI;

public enum MessageDialogButtons
{
    Ok,
    YesNo,
    YesNoCancel
}

public enum MessageDialogResult
{
    Ok,
    Yes,
    No,
    Cancel
}

public partial class MessageDialog : Window
{
    private MessageDialogResult result = MessageDialogResult.Cancel;

    public MessageDialog()
    {
        InitializeComponent();
        WindowThemeManager.Register(this);

        OkButton.Click += (_, _) => CloseWithResult(MessageDialogResult.Ok);
        YesButton.Click += (_, _) => CloseWithResult(MessageDialogResult.Yes);
        NoButton.Click += (_, _) => CloseWithResult(MessageDialogResult.No);
        CancelButton.Click += (_, _) => CloseWithResult(MessageDialogResult.Cancel);
    }

    public static async Task<MessageDialogResult> ShowAsync(
        Window? owner,
        string message,
        string title,
        MessageDialogButtons buttons,
        string? okText = null,
        string? yesText = null,
        string? noText = null,
        string? cancelText = null)
    {
        MessageDialog dialog = new MessageDialog
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.MessageText.Text = message;
        ConfigureButtons(dialog, buttons, okText, yesText, noText, cancelText);

        Window? validOwner = owner;
        if ((validOwner == null || !validOwner.IsLoaded) && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null && desktop.MainWindow.IsLoaded)
        {
            validOwner = desktop.MainWindow;
        }

        try
        {
            if (validOwner != null && validOwner.IsLoaded)
            {
                return await dialog.ShowDialog<MessageDialogResult>(validOwner);
            }
        }
        catch (InvalidOperationException)
        {
            // Owner is closed or invalid
        }

        return MessageDialogResult.Cancel;
    }

    private static void ConfigureButtons(MessageDialog dialog, MessageDialogButtons buttons,
        string? okText,
        string? yesText,
        string? noText,
        string? cancelText)
    {
        dialog.OkButton.Content = okText ?? "OK";
        dialog.YesButton.Content = yesText ?? "Yes";
        dialog.NoButton.Content = noText ?? "No";
        dialog.CancelButton.Content = cancelText ?? "Cancel";

        dialog.OkButton.IsVisible = buttons == MessageDialogButtons.Ok;
        dialog.YesButton.IsVisible = buttons == MessageDialogButtons.YesNo || buttons == MessageDialogButtons.YesNoCancel;
        dialog.NoButton.IsVisible = buttons == MessageDialogButtons.YesNo || buttons == MessageDialogButtons.YesNoCancel;
        dialog.CancelButton.IsVisible = buttons == MessageDialogButtons.YesNoCancel;
    }

    private void CloseWithResult(MessageDialogResult value)
    {
        result = value;
        Close(result);
    }
}
