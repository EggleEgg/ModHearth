using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ModHearth.UI.Theme
{
    /// <summary>
    /// Applies default style colours for any window
    /// </summary>
    public static class WindowThemeManager
    {
        private static readonly List<WeakReference<Window>> registered = [];

        [ThreadStatic]
        private static bool isApplying;

        public static void Register(Window window)
        {
            if (window == null)
                return;

            Cleanup();
            registered.Add(new WeakReference<Window>(window));

            // Hidden until fully styled. Nothing is painted until we explicitly reveal it below, so there's no frame where the wrong colors can be seen.
            window.Opacity = 0;

            window.Opened += OnWindowOpened;
            window.Closed += OnWindowClosed;
            ApplyToWindow(window, Style.instance);
        }

        public static void ApplyToOpenWindows() => ApplyToOpenWindows(Style.instance);

        public static void ApplyToOpenWindows(Style? style)
        {
            if (style == null)
                return;

            Cleanup();
            for (int i = 0; i < registered.Count; i++)
            {
                if (registered[i].TryGetTarget(out Window? window))
                    ApplyToWindow(window, style);
            }
        }

        private static void OnWindowOpened(object? sender, EventArgs e)
        {
            if (sender is not Window window)
                return;

            Style? style = Style.instance;
            if (style == null)
            {
                // No style to apply (shouldn't happen in practice), don't leave the window permanently invisible.
                window.Opacity = 1;
                return;
            }

            // Ensure the visual tree is fully loaded and built before styling
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ApplyToWindow(window, style);
                window.Opacity = 1;
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }

        private static void OnWindowClosed(object? sender, EventArgs e) => Cleanup();

        private static void Cleanup() => _ = registered.RemoveAll(weak => !weak.TryGetTarget(out _));

        public static void ApplyToWindow(Window? window, Style? style)
        {
            if (window == null || style == null || isApplying)
                return;

            Style.instance = style;
            isApplying = true;
            try
            {
                ApplyToVisual(window, style);
            }
            finally
            {
                isApplying = false;
            }
        }

        public static void ApplyToSubtree(Visual? root, Style? style)
        {
            if (root == null || style == null)
                return;

            // Resources and themed view models are already current from the window's own pass.
            ApplyToVisualRecursive(root, style, BuildBrushes(style));
        }

        private static StyleBrushes BuildBrushes(Style style) => new(
            Background: BrushCache.GetBrush(style.BackgroundColor.ToAvaloniaColor()),
            Text: BrushCache.GetBrush(style.TextColor.ToAvaloniaColor()),
            Panel: BrushCache.GetBrush(style.PanelColor.ToAvaloniaColor()),
            PanelClear: BrushCache.GetBrush(style.PanelColorClear.ToAvaloniaColor()),
            StrongPanel: BrushCache.GetBrush(style.StrongPanelColor.ToAvaloniaColor()),
            Button: BrushCache.GetBrush(style.ButtonColor.ToAvaloniaColor()),
            ButtonText: BrushCache.GetBrush(style.ButtonTextColor.ToAvaloniaColor()),
            ButtonOutline: BrushCache.GetBrush(style.ButtonOutlineColor.ToAvaloniaColor()),
            BorderPanel: BrushCache.GetBrush(style.PanelColor.ToAvaloniaColor()),
            DataGrid: BrushCache.GetBrush(style.PanelColor.ToAvaloniaColor()),
            ModRefHighlight: BrushCache.GetBrush(style.ModRefHighlightColor.ToAvaloniaColor()),
            ModRefHighlightDark: BrushCache.GetBrush(style.ModRefHighlightDarkColor.ToAvaloniaColor()),
            InputText: IsDark(style.BackgroundColor) ? Brushes.White : Brushes.Black);

        // Cache brushes to reduce memory usage

        private readonly record struct StyleBrushes(
            IBrush Background,
            IBrush Text,
            IBrush Panel,
            IBrush PanelClear,
            IBrush StrongPanel,
            IBrush Button,
            IBrush ButtonText,
            IBrush ButtonOutline,
            IBrush BorderPanel,
            IBrush DataGrid,
            IBrush ModRefHighlight,
            IBrush ModRefHighlightDark,
            IBrush InputText
        );

        public static void ApplyToVisual(Visual visual, Style style)
        {
            if (visual == null || style == null)
                return;

            Style.instance = style;

            IBrush searchBorderBrush = BrushCache.GetBrush(style.SearchBorderColor.ToAvaloniaColor());
            IBrush searchButtonBrush = BrushCache.GetBrush(style.SearchButtonColor.ToAvaloniaColor());
            IBrush searchButtonHoverBrush = BrushCache.GetBrush(style.SearchButtonHoverColor.ToAvaloniaColor());
            IBrush searchButtonPressedBrush = BrushCache.GetBrush(style.SearchButtonPressedColor.ToAvaloniaColor());
            IBrush buttonTextBrush = BrushCache.GetBrush(style.ButtonTextColor.ToAvaloniaColor());

            bool isDark = IsDark(style.BackgroundColor);
            StyleBrushes brushes = BuildBrushes(style);

            if (visual is Window window)
            {
                window.Background = brushes.Background;
                Avalonia.Styling.ThemeVariant? ownerVariant = window.Owner?.RequestedThemeVariant;
                window.RequestedThemeVariant = ownerVariant ?? (isDark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
            }

            // Set global DynamicResource values once per apply pass
            Application? app = Application.Current;
            if (app != null)
            {
                app.Resources["BorderPanelBrush"] = brushes.BorderPanel;
                app.Resources["BackgroundBrush"] = brushes.Background;
                app.Resources["MainTextBrush"] = brushes.Text;
                app.Resources["PanelBrush"] = brushes.Panel;
                app.Resources["PanelDarkBrush"] = BrushCache.GetBrush(style.PanelColorDark);
                // Use this for generic gridsplitters and linebreakers
                app.Resources["StrongPanelBrush"] = brushes.StrongPanel;
                app.Resources["ButtonBackgroundBrush"] = brushes.Button;
                app.Resources["ButtonForegroundBrush"] = brushes.ButtonText;
                app.Resources["ButtonBorderBrush"] = brushes.ButtonOutline;
                app.Resources["ModRefHighlightBrush"] = brushes.ModRefHighlight;
                app.Resources["ModRefHighlightDarkBrush"] = brushes.ModRefHighlightDark;
                app.Resources["ModRefPanelClearBrush"] = brushes.PanelClear;
                app.Resources["ButtonSelectionBrush"] = BrushCache.GetBrush(style.SelectionColor);
                app.Resources["DockPreviewBrush"] = BrushCache.EditBrushAlpha(style.SelectionColor, 120);
                app.Resources["SelectionPreviewBrush"] = BrushCache.EditBrushAlpha(style.SelectionColor, 30);
                app.Resources["LowAlphaModRefHighlightBrush"] = BrushCache.EditBrushAlpha(brushes.ModRefHighlight, 100);
                app.Resources["SearchBorderBrush"] = searchBorderBrush;
                app.Resources["SearchButtonBrush"] = searchButtonBrush;
                app.Resources["SearchButtonHoverBrush"] = searchButtonHoverBrush;
                app.Resources["SearchButtonPressedBrush"] = searchButtonPressedBrush;
                app.Resources["ButtonTextBrush"] = buttonTextBrush;
                app.Resources["ModRefIssueWarningBrush"] = BrushCache.GetBrush(style.ModRefIssueWarningColor);
            }

            // Single top-down $O(N)$ pass through the visual tree
            ApplyToVisualRecursive(visual, style, brushes);

            ThemedViewModelRegistry.RefreshAll(style);
        }

        private static void ApplyToVisualRecursive(
            Visual visual,
            Style style,
            in StyleBrushes brushes,
            bool inSearchBar = false,
            bool inDockHeader = false,
            bool inButtonOrCombo = false)
        {
            switch (visual)
            {
                case null:
                    return;
                //Use Tag="IgnoreTheme" to skip styling
                case Control control when control.Tag is string ignoreTag && string.Equals(ignoreTag, "IgnoreTheme", StringComparison.OrdinalIgnoreCase):
                    return;
                case IStyleAwareWindow styleAware:
                    styleAware.ApplyCustomStyle(style);
                    break;
            }

            // 1. ModSearchBar styling context
            if (visual is ModSearchBar searchBar)
            {
                searchBar.ApplyStyle(style);
                inSearchBar = true;
            }
            else if (inSearchBar)
            {
                // ModSearchBar (and nested ModColorPicker) manage their own internal styling
                return;
            }

            // 2. Dock chrome / host context calculation
            string typeName = visual.GetType().Name;
            if (visual is Control ctrl && (ctrl.Name == "PART_ContentPresenter" || typeName == "DeferredContentPresenter"))
                inDockHeader = false; // Reset when entering dock body content host
            else if (typeName is "ToolChromeControl" or "ToolDockControl" or "DockControl" || visual is Control { Name: "PART_Grip" })
                inDockHeader = true; // Set when inside dock title/header

            // 4. Element-specific styling
            switch (visual)
            {
                case TextBlock textBlock:
                    {
                        if (!inButtonOrCombo && !inDockHeader && !HasThemedForeground(textBlock))
                        {
                            textBlock.Foreground = brushes.Text;
                        }

                        break;
                    }

                case TextBox textBox:
                    textBox.Background = brushes.Panel;
                    textBox.Foreground = brushes.InputText;
                    break;
                case ComboBox comboBox:
                    comboBox.Background = brushes.Panel;
                    inButtonOrCombo = true;
                    break;
                case ListBox listBox:
                    listBox.Background = brushes.Panel;
                    break;
                case Button button:
                    {
                        // Used for important buttons
                        if (button.Tag is string tag && string.Equals(tag, "Themed", StringComparison.OrdinalIgnoreCase))
                        {
                            button.Background = brushes.Button;
                            button.Foreground = brushes.ButtonText;
                            button.BorderBrush = brushes.ButtonOutline;
                            button.BorderThickness = new Thickness(1);
                        }
                        inButtonOrCombo = true;
                        break;
                    }

                case DataGrid dataGrid:
                    dataGrid.Background = brushes.DataGrid;
                    break;
            }

            // 5. Recurse down to children without heap allocations or ancestor searches
            foreach (Visual child in visual.GetVisualChildren())
            {
                ApplyToVisualRecursive(child, style, brushes, inSearchBar, inDockHeader, inButtonOrCombo);
            }
        }

        private static bool HasThemedForeground(TextBlock textBlock) => textBlock.DataContext is IThemedViewModel;

        private static bool IsDark(SimpleColor color)
        {
            double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
            return luminance < 128;
        }
    }
}