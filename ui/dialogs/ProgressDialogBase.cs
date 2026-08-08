using Avalonia.Controls;
using Avalonia.Threading;

namespace ModHearth.UI
{
    /// <summary>
    /// Generic dialog helper class
    /// </summary>
    public abstract class ProgressDialogBase : Window
    {
        protected CancellationTokenSource? Cts;

        protected ProgressDialogBase()
        {
            WindowThemeManager.Register(this);
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;
        }

        protected IProgress<string> CreateProgressReporter(TextBlock statusTextBlock)
        {
            return new Progress<string>(msg =>
            {
                Dispatcher.UIThread.Post(() => statusTextBlock.Text = msg);
            });
        }

        protected void InitializeCancellation(Button cancelButton)
        {
            cancelButton.Click += (_, _) =>
            {
                Cts?.Cancel();
                Close(false);
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Cts?.Dispose();
            Cts = null;
        }
    }
}
