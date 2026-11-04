using Avalonia.Controls;
using Avalonia.Threading;

namespace ModHearth.UI.Dialogs
{
    public partial class ReleaseDownloadDialog : ProgressDialogBase
    {
        public ReleaseDownloadDialog()
        {
            InitializeComponent();
            InitializeCancellation(BtnCancel);
        }

        public static async Task<bool> ShowAndDownloadAsync(
            Window owner,
            string releaseTitle,
            Func<IProgress<string>, CancellationToken, Task<bool>> downloadAction)
        {
            ReleaseDownloadDialog dialog = new();
            if (!string.IsNullOrEmpty(releaseTitle))
            {
                dialog.HeaderText.Text = $"Downloading {releaseTitle}...";
            }

            bool result = false;
            _ = Task.Run(async () =>
            {
                dialog.Cts = new CancellationTokenSource();
                IProgress<string> progress = CreateProgressReporter(dialog.StatusTextBlock);
                try
                {
                    bool success = await downloadAction(progress, dialog.Cts.Token);
                    Dispatcher.UIThread.Post(() =>
                    {
                        result = success;
                        dialog.Close(success);
                    });
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        dialog.StatusTextBlock.Text = "Download cancelled.";
                        dialog.Close(false);
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (dialog.IsLoaded)
                        {
                            await DialogService.ShowMessageAsync(dialog, $"Download failed: {ex.Message}", "Error");
                        }
                        dialog.Close(false);
                    });
                }
            });

            bool? dialogResult = await dialog.ShowDialog<bool?>(owner);
            return dialogResult == true || result;
        }
    }
}
