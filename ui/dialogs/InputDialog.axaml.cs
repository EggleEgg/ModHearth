using Avalonia.Controls;
using ModHearth.UI.Theme;

namespace ModHearth.UI
{
    public partial class InputDialog : Window
    {
        public InputDialog()
        {
            InitializeComponent();
            WindowThemeManager.Register(this);

            OkButton.Click += (_, _) => Close(InputBox.Text);
            CancelButton.Click += (_, _) => Close(null);

            Opened += (_, _) =>
            {
                _ = InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        public static async Task<string?> ShowAsync(Window owner, string prompt, string title, string defaultValue)
        {
            InputDialog dialog = new()
            {
                Title = title,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.PromptText.Text = prompt;
            dialog.InputBox.Text = defaultValue ?? string.Empty;
            _ = dialog.InputBox.Focus();
            dialog.InputBox.SelectAll();

            return await dialog.ShowDialog<string?>(owner);
        }
    }
}
