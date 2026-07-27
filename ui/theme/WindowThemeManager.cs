using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Controls.Generators;

namespace ModHearth.UI;

/// <summary>
/// Applies default style colours for any window
/// </summary>
public static class WindowThemeManager
{
    private static readonly List<WeakReference<Window>> registered = new();

    [ThreadStatic]
    private static bool isApplying;

    public static void Register(Window window)
    {
        if (window == null)
            return;

        Cleanup();
        registered.Add(new WeakReference<Window>(window));

        window.Opened += OnWindowOpened;
        window.Closed += OnWindowClosed;
    }

    public static void ApplyToOpenWindows()
    {
        ApplyToOpenWindows(Style.instance);
    }

    public static void ApplyToOpenWindows(Style? style)
    {
        if (style == null)
            return;

        Cleanup();
        foreach (WeakReference<Window> weak in registered.ToList())
        {
            if (weak.TryGetTarget(out Window? window))
                ApplyToWindow(window, style);
        }
    }

    private static void OnWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        Style? style = Style.instance;
        if (style == null)
            return;

        ApplyToWindow(window, style);
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        Cleanup();
    }

    private static void Cleanup()
    {
        registered.RemoveAll(weak => !weak.TryGetTarget(out _));
    }

    public static void ApplyToWindow(Window window, Style style)
    {
        if (window == null || isApplying)
            return;

        isApplying = true;
        try
        {
            foreach (ModSearchBar searchBar in window.GetVisualDescendants().OfType<ModSearchBar>())
                searchBar.ApplyStyle(style);

            IBrush formBrush = BrushCache.GetBrush(style.formColor.ToAvaloniaColor());
            IBrush textBrush = BrushCache.GetBrush(style.textColor.ToAvaloniaColor());
            IBrush panelBrush = BrushCache.GetBrush(style.modRefPanelColor.ToAvaloniaColor());
            IBrush strongPanelBrush = BrushCache.GetBrush(style.strongPanelColor.ToAvaloniaColor());
            IBrush buttonBrush = BrushCache.GetBrush(style.buttonColor.ToAvaloniaColor());
            IBrush buttonTextBrush = BrushCache.GetBrush(style.buttonTextColor.ToAvaloniaColor());
            IBrush buttonOutlineBrush = BrushCache.GetBrush(style.buttonOutlineColor.ToAvaloniaColor());
            IBrush borderPanelBrush = BrushCache.GetBrush(style.backgroundColor.ToAvaloniaColor());
            IBrush dataGridBrush = BrushCache.GetBrush(style.backgroundColor.ToAvaloniaColor());
            IBrush modRefHighlightBrush = BrushCache.GetBrush(style.modRefHighlightColor.ToAvaloniaColor());
            IBrush modRefHighlightDarkBrush = BrushCache.GetBrush(style.modRefHighlightDarkColor.ToAvaloniaColor());

            window.Background = formBrush;
            IBrush inputTextBrush = IsDark(style.formColor) ? Brushes.White : Brushes.Black;

            ThemeVariant? ownerVariant = window.Owner?.RequestedThemeVariant;
            window.RequestedThemeVariant = ownerVariant ?? (IsDark(style.formColor) ? ThemeVariant.Dark : ThemeVariant.Light);

            //Avalonia DynamicResource
            Application? app = Application.Current;
            if (app != null)
            {
                app.Resources["BorderPanelBrush"] = borderPanelBrush;
                app.Resources["FormBackgroundBrush"] = formBrush;
                app.Resources["MainTextBrush"] = textBrush;
                app.Resources["PanelBackgroundBrush"] = panelBrush;
                app.Resources["StrongPanelBackgroundBrush"] = strongPanelBrush;
                app.Resources["ButtonBackgroundBrush"] = buttonBrush;
                app.Resources["ButtonForegroundBrush"] = buttonTextBrush;
                app.Resources["ButtonBorderBrush"] = buttonOutlineBrush;
                app.Resources["ModRefHighlightBrush"] = modRefHighlightBrush;
                app.Resources["ModRefHighlightDarkBrush"] = modRefHighlightDarkBrush;
            }

            foreach (Visual visual in window.GetVisualDescendants())
            {
                // ModSearchBar (and nested ModColorPicker) fully style themselves via ApplyStyle()
                if (visual is Control control && control.FindAncestorOfType<ModSearchBar>() != null)
                    continue;

                if (visual is TextBlock textBlock &&
                    visual.FindAncestorOfType<Button>() == null && visual.FindAncestorOfType<ComboBox>() == null)
                {
                    textBlock.Foreground = textBrush;
                    continue;
                }

                if (visual is TextBox textBox)
                {
                    textBox.Background = panelBrush;
                    textBox.Foreground = inputTextBrush;
                    continue;
                }

                if (visual is ComboBox comboBox)
                {
                    comboBox.Background = panelBrush;
                    comboBox.Foreground = inputTextBrush;
                    continue;
                }

                if (visual is ListBox listBox)
                {
                    listBox.Background = panelBrush;
                    continue;
                }

                if (visual is DataGrid dataGrid)
                {
                    dataGrid.Background = dataGridBrush;
                    continue;
                }
            }

            if (window is IStyleAwareWindow styleAware)
                styleAware.ApplyCustomStyle(style);
        }
        finally
        {
            isApplying = false;
        }
    }

    private static bool IsDark(SimpleColor color)
    {
        double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return luminance < 128;
    }
}
