using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ModHearth.UI.Theme;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Guards the runtime UI scaling wrapper. The app relies on wrapping each window's content once
    /// and then only changing the transform, so repeated theme passes must not nest wrappers or
    /// scale the window bounds twice.
    /// </summary>
    [Collection(AvaloniaUiTestCollection.Name)]
    public class UiScaleManagerTests
    {
        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(10, 0.5)]
        [InlineData(100, 1.0)]
        [InlineData(150, 1.5)]
        [InlineData(250, 2.0)]
        public void GetScaleFactorNormalizesPercent(int percent, double expected)
        {
            Assert.Equal(expected, UiScaleManager.GetScaleFactor(percent), 3);
        }

        [AvaloniaFact]
        public void ApplyToWindowWrapsContentAndScalesWindowBounds()
        {
            Border content = new() { Width = 200, Height = 100 };
            Window window = new()
            {
                Content = content,
                Width = 400,
                Height = 300,
                MinWidth = 200,
                MinHeight = 150,
                MaxWidth = 800,
                MaxHeight = 600
            };

            UiScaleManager.ApplyToWindow(window, 1.5);

            LayoutTransformControl host = Assert.IsType<LayoutTransformControl>(window.Content);
            Assert.Same(content, host.Child);
            ScaleTransform transform = Assert.IsType<ScaleTransform>(host.LayoutTransform);
            Assert.Equal(1.5, transform.ScaleX, 3);
            Assert.Equal(1.5, transform.ScaleY, 3);
            Assert.Equal(600, window.Width, 3);
            Assert.Equal(450, window.Height, 3);
            Assert.Equal(300, window.MinWidth, 3);
            Assert.Equal(225, window.MinHeight, 3);
            Assert.Equal(1200, window.MaxWidth, 3);
            Assert.Equal(900, window.MaxHeight, 3);
        }

        [AvaloniaFact]
        public void RepeatedApplicationDoesNotDoubleScaleOrRewrap()
        {
            Window window = new() { Content = new Border(), Width = 400, Height = 300 };

            UiScaleManager.ApplyToWindow(window, 1.5);
            LayoutTransformControl host = Assert.IsType<LayoutTransformControl>(window.Content);

            UiScaleManager.ApplyToWindow(window, 1.5);

            Assert.Same(host, window.Content);
            Assert.Equal(600, window.Width, 3);
            Assert.Equal(450, window.Height, 3);
        }

        [AvaloniaFact]
        public void ChangedScaleRescalesFromThePreviouslyAppliedFactor()
        {
            Window window = new() { Content = new Border(), Width = 400, Height = 300 };

            UiScaleManager.ApplyToWindow(window, 1.5);
            UiScaleManager.ApplyToWindow(window, 0.5);

            LayoutTransformControl host = Assert.IsType<LayoutTransformControl>(window.Content);
            ScaleTransform transform = Assert.IsType<ScaleTransform>(host.LayoutTransform);
            Assert.Equal(0.5, transform.ScaleX, 3);
            Assert.Equal(0.5, transform.ScaleY, 3);
            Assert.Equal(400.0 * 0.5, window.Width, 3);
            Assert.Equal(300.0 * 0.5, window.Height, 3);
        }

        [AvaloniaFact]
        public void ContentReplacementIsRewrappedWithoutDoubleScaling()
        {
            Window window = new() { Content = new Border(), Width = 400, Height = 300 };
            UiScaleManager.ApplyToWindow(window, 1.5);

            Border replacement = new();
            window.Content = replacement;
            UiScaleManager.ApplyToWindow(window, 1.5);

            LayoutTransformControl host = Assert.IsType<LayoutTransformControl>(window.Content);
            Assert.Same(replacement, host.Child);
            Assert.Equal(600, window.Width, 3);
            Assert.Equal(450, window.Height, 3);
        }

        [AvaloniaFact]
        public void DimensionsAssignedAfterTheFirstScalePassAreScaledBeforeShowing()
        {
            // ProgressDialogBase registers first and then assigns Width.
            Window window = new() { Content = new Border() };
            UiScaleManager.ApplyToWindow(window, 1.5);

            window.Width = 480;
            UiScaleManager.ApplyToWindow(window, 1.5);

            Assert.Equal(720, window.Width, 3);
        }

        [AvaloniaFact]
        public void UserResizeAfterTheOpeningPassIsPreserved()
        {
            Window window = new() { Content = new Border(), Width = 400, Height = 300 };
            UiScaleManager.ApplyToWindow(window, 1.5);
            window.Show();
            try
            {
                // Matches the opening pass performed by WindowThemeManager.
                UiScaleManager.ApplyToWindow(window, 1.5);

                window.Width = 700;
                UiScaleManager.ApplyToWindow(window, 1.5);

                Assert.Equal(700, window.Width, 3);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void ReplacingContentReleasesThePreviouslyWrappedControl()
        {
            Border content = new();
            Window window = new() { Content = content, Width = 400, Height = 300 };
            UiScaleManager.ApplyToWindow(window, 1.5);
            Assert.NotNull(content.Parent);

            window.Content = null;

            // The docking manager clears a floating window's content before reusing its shared
            // control in another window. The wrapper must not keep that control parented.
            Assert.Null(content.Parent);

            Border replacement = new();
            window.Content = replacement;
            UiScaleManager.ApplyToWindow(window, 1.5);

            LayoutTransformControl host = Assert.IsType<LayoutTransformControl>(window.Content);
            Assert.Same(replacement, host.Child);
            Assert.Equal(600, window.Width, 3);
        }

        [AvaloniaFact]
        public void FlyoutInheritsTheScaleOfItsPlacementTarget()
        {
            StackPanel content = new();
            Window window = new() { Content = content, Width = 400, Height = 300 };
            UiScaleManager.ApplyToWindow(window, 1.5);

            Button button = new() { Content = "Open" };
            content.Children.Add(button);
            window.Show();

            try
            {
                window.UpdateLayout();

                Flyout flyout = new() { Content = new TextBlock { Text = "Menu" } };
                flyout.ShowAt(button);
                try
                {
                    Assert.True(flyout.Popup.InheritsTransform);

                    Control presenter = Assert.IsType<FlyoutPresenter>(flyout.Popup.Child);
                    OverlayPopupHost? overlayHost = presenter.FindAncestorOfType<OverlayPopupHost>();
                    Transform? transform = overlayHost?.Transform;
                    if (transform == null && TopLevel.GetTopLevel(presenter) is PopupRoot popupRoot)
                        transform = popupRoot.Transform;

                    ScaleTransform scale = Assert.IsType<ScaleTransform>(transform);
                    Assert.Equal(1.5, scale.ScaleX, 3);
                    Assert.Equal(1.5, scale.ScaleY, 3);
                }
                finally
                {
                    flyout.Hide();
                }
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void ContextMenuInheritsTheScaleOfItsPlacementTarget()
        {
            StackPanel content = new();
            Window window = new() { Content = content, Width = 400, Height = 300 };
            UiScaleManager.ApplyToWindow(window, 1.5);

            ContextMenu menu = new();
            menu.Items.Add(new MenuItem { Header = "Item" });
            Button button = new() { Content = "Menu", ContextMenu = menu };
            content.Children.Add(button);
            window.Show();

            try
            {
                window.UpdateLayout();
                menu.Open(button);

                OverlayPopupHost? overlayHost = menu.FindAncestorOfType<OverlayPopupHost>();
                Transform? transform = overlayHost?.Transform;
                if (transform == null && TopLevel.GetTopLevel(menu) is PopupRoot popupRoot)
                    transform = popupRoot.Transform;

                ScaleTransform scale = Assert.IsType<ScaleTransform>(transform);
                Assert.Equal(1.5, scale.ScaleX, 3);
                Assert.Equal(1.5, scale.ScaleY, 3);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void NonPositiveScaleFallsBackToUnscaled()
        {
            Window window = new() { Content = new Border(), Width = 400, Height = 300 };

            UiScaleManager.ApplyToWindow(window, 0);

            LayoutTransformControl host = Assert.IsType<LayoutTransformControl>(window.Content);
            ScaleTransform transform = Assert.IsType<ScaleTransform>(host.LayoutTransform);
            Assert.Equal(1.0, transform.ScaleX, 3);
            Assert.Equal(1.0, transform.ScaleY, 3);
            Assert.Equal(400, window.Width, 3);
            Assert.Equal(300, window.Height, 3);
        }
    }
}
