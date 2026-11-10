using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

            errorIssueCountButton.AddHandler(PointerPressedEvent, OnErrorIssuePointerPressed, RoutingStrategies.Tunnel, true);
            warningIssueCountButton.AddHandler(PointerPressedEvent, OnWarningIssuePointerPressed, RoutingStrategies.Tunnel, true);
            infoIssueCountButton.AddHandler(PointerPressedEvent, OnInfoIssuePointerPressed, RoutingStrategies.Tunnel, true);
        }

        /// <summary>
        /// Raised when the user clicks one of the severity counters. The host decides which mod
        /// list and jump cursor the request maps to.
        /// </summary>
        public event EventHandler<ModIssueTier>? JumpRequested;

        /// <summary>
        /// Raised when the user right-clicks one of the severity counters. The host decides which mod
        /// list and removal action the request maps to.
        /// </summary>
        public event EventHandler<ModIssueTier>? RemoveRequested;

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

        private void OnErrorIssuePointerPressed(object? sender, PointerPressedEventArgs e) => RaiseRemove(sender, e, ModIssueTier.Error);

        private void OnWarningIssuePointerPressed(object? sender, PointerPressedEventArgs e) => RaiseRemove(sender, e, ModIssueTier.Warning);

        private void OnInfoIssuePointerPressed(object? sender, PointerPressedEventArgs e) => RaiseRemove(sender, e, ModIssueTier.Info);

        private void RaiseRemove(object? sender, PointerPressedEventArgs e, ModIssueTier tier)
        {
            if (sender is not Visual visual || !e.GetCurrentPoint(visual).Properties.IsRightButtonPressed)
                return;

            e.Handled = true;
            RemoveRequested?.Invoke(this, tier);
        }

        private void RaiseJump(ModIssueTier tier) => JumpRequested?.Invoke(this, tier);
    }
}
