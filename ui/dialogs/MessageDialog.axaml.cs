using Avalonia.Controls;

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
        Window owner,
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
        dialog.ConfigureButtons(buttons, okText, yesText, noText, cancelText);

        return await dialog.ShowDialog<MessageDialogResult>(owner);
    }

    private void ConfigureButtons(
        MessageDialogButtons buttons,
        string? okText,
        string? yesText,
        string? noText,
        string? cancelText)
    {
        OkButton.Content = okText ?? "OK";
        YesButton.Content = yesText ?? "Yes";
        NoButton.Content = noText ?? "No";
        CancelButton.Content = cancelText ?? "Cancel";

        OkButton.IsVisible = buttons == MessageDialogButtons.Ok;
        YesButton.IsVisible = buttons == MessageDialogButtons.YesNo || buttons == MessageDialogButtons.YesNoCancel;
        NoButton.IsVisible = buttons == MessageDialogButtons.YesNo || buttons == MessageDialogButtons.YesNoCancel;
        CancelButton.IsVisible = buttons == MessageDialogButtons.YesNoCancel;
    }

    private void CloseWithResult(MessageDialogResult value)
    {
        result = value;
        Close(result);
    }
}
