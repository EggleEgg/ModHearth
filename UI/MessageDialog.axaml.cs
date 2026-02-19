using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace ModHearth.UI;

public enum MessageDialogButtons
{
    Ok,
    YesNo
}

public enum MessageDialogResult
{
    Ok,
    Yes,
    No
}

public partial class MessageDialog : Window
{
    private MessageDialogResult result;

    public MessageDialog()
    {
        InitializeComponent();

        OkButton.Click += (_, _) => CloseWithResult(MessageDialogResult.Ok);
        YesButton.Click += (_, _) => CloseWithResult(MessageDialogResult.Yes);
        NoButton.Click += (_, _) => CloseWithResult(MessageDialogResult.No);
    }

    public static async Task<MessageDialogResult> ShowAsync(Window owner, string message, string title, MessageDialogButtons buttons)
    {
        MessageDialog dialog = new MessageDialog
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.MessageText.Text = message;
        dialog.ConfigureButtons(buttons);

        return await dialog.ShowDialog<MessageDialogResult>(owner);
    }

    private void ConfigureButtons(MessageDialogButtons buttons)
    {
        OkButton.IsVisible = buttons == MessageDialogButtons.Ok;
        YesButton.IsVisible = buttons == MessageDialogButtons.YesNo;
        NoButton.IsVisible = buttons == MessageDialogButtons.YesNo;
    }

    private void CloseWithResult(MessageDialogResult value)
    {
        result = value;
        Close(result);
    }
}
