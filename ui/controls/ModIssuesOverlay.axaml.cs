using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ModHearth.Models;

namespace ModHearth.UI
{
    /// <summary>
    /// Bottom-right issue counter shown over a mod list. Hosts the three severity buttons and
    /// own their click-to-jump behaviour so every list uses identical markup and wiring.
    /// </summary>
    public partial class ModIssuesOverlay : UserControl
    {
        public ModIssuesOverlay()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Raised when the user clicks one of the severity counters. The host decides which mod
        /// list and jump cursor the request maps to.
        /// </summary>
        public event EventHandler<ModIssueTier>? JumpRequested;

        /// <summary>
        /// Applies the per-tier mod counts and hides the overlay when all tiers are empty.
        /// </summary>
        public void UpdateCounts(int errorCount, int warningCount, int infoCount)
        {
            errorIssueCountText.Text = errorCount.ToString(CultureInfo.InvariantCulture);
            warningIssueCountText.Text = warningCount.ToString(CultureInfo.InvariantCulture);
            infoIssueCountText.Text = infoCount.ToString(CultureInfo.InvariantCulture);

            errorIssueCountButton.IsVisible = errorCount > 0;
            warningIssueCountButton.IsVisible = warningCount > 0;
            infoIssueCountButton.IsVisible = infoCount > 0;

            IsVisible = errorCount + warningCount + infoCount > 0;
        }

        private void OnErrorIssueClick(object? sender, RoutedEventArgs e) => RaiseJump(ModIssueTier.Error);

        private void OnWarningIssueClick(object? sender, RoutedEventArgs e) => RaiseJump(ModIssueTier.Warning);

        private void OnInfoIssueClick(object? sender, RoutedEventArgs e) => RaiseJump(ModIssueTier.Info);

        private void RaiseJump(ModIssueTier tier) => JumpRequested?.Invoke(this, tier);
    }
}
