using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using ModHearth.UI.Theme;

namespace ModHearth.UI.Dialogs
{
    /// <summary>
    /// Generic dialog helper class
    /// </summary>
    public abstract partial class ProgressDialogBase : Window
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

        protected static IProgress<string> CreateProgressReporter(TextBlock statusTextBlock, ProgressBar? progressBar = null) => new Progress<string>(msg =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    statusTextBlock.Text = msg;
                    if (progressBar != null)
                    {
                        double? percentage = ParsePercentage(msg);
                        if (percentage.HasValue)
                        {
                            progressBar.IsIndeterminate = false;
                            progressBar.Minimum = 0;
                            progressBar.Maximum = 100;
                            progressBar.Value = percentage.Value;
                        }
                    }
                });
            });

        private static double? ParsePercentage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return null;

            Match match = PercentageRegex().Match(message);
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return null;
        }

        protected void InitializeCancellation(Button cancelButton) => cancelButton.Click += (_, _) =>
            {
                Cts?.Cancel();
                Close(false);
            };

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Cts?.Dispose();
            Cts = null;
        }

        [GeneratedRegex(@"(\d+(?:\.\d+)?)%")]
        private static partial Regex PercentageRegex();
    }
}
