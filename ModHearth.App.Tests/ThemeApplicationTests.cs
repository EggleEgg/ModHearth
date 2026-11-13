using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ModHearth.UI;
using ModHearth.UI.Theme;
using Xunit;

namespace ModHearth.App.Tests
{
    /// <summary>
    /// Covers the theme-application hook used by the docked tool panels. The theme pass only calls
    /// <see cref="IStyleAwareWindow.ApplyCustomStyle"/> for controls that implement the interface, so
    /// these tests guard both the dispatcher path and the two controls that rely on it.
    /// </summary>
    [Collection(AvaloniaUiTestCollection.Name)]
    public class ThemeApplicationTests(ITestOutputHelper output)
    {
        [AvaloniaFact]
        public void ApplyToVisualCallsCustomStyleOnStyleAwareControl()
        {
            Style style = LoadStyle();
            StyleAwareProbe probe = new();

            WindowThemeManager.ApplyToVisual(probe, style);

            Assert.Same(style, probe.AppliedStyle);
        }

        [AvaloniaFact]
        public void ApplyToVisualReachesNestedStyleAwareControls()
        {
            Style style = LoadStyle();
            StyleAwareProbe first = new();
            StyleAwareProbe second = new();
            StackPanel panel = new();
            panel.Children.Add(first);
            panel.Children.Add(new Border { Child = second });

            WindowThemeManager.ApplyToVisual(panel, style);

            Assert.Same(style, first.AppliedStyle);
            Assert.Same(style, second.AppliedStyle);
        }

        [AvaloniaFact]
        public void ApplyToVisualSkipsIgnoreThemeSubtree()
        {
            Style style = LoadStyle();
            StyleAwareProbe probe = new() { Tag = "IgnoreTheme" };

            WindowThemeManager.ApplyToVisual(probe, style);

            Assert.Null(probe.AppliedStyle);
        }

        [Fact]
        public void DockedToolControlsImplementStyleAwareContract()
        {
            // The custom-style pass is the only caller; a control that drops the interface silently
            // loses its theme updates (the regression this contract prevents).
            Assert.True(typeof(IStyleAwareWindow).IsAssignableFrom(typeof(ModUpdateLogControl)));
            Assert.True(typeof(IStyleAwareWindow).IsAssignableFrom(typeof(WorkshopDownloaderControl)));
        }

        private Style LoadStyle()
        {
            AvaloniaTestEnvironment.EnsureInitialized(output.WriteLine);
            return Style.instance ?? StyleLoader.LoadStyle(createLogs: false);
        }

        private sealed class StyleAwareProbe : Control, IStyleAwareWindow
        {
            public Style? AppliedStyle { get; private set; }

            public void ApplyCustomStyle(Style style) => AppliedStyle = style;
        }
    }
}
