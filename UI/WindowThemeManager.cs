using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModHearth.UI;

public static class WindowThemeManager
{
    private static readonly List<WeakReference<Window>> registered = new();

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

    private static void ApplyToWindow(Window window, Style style)
    {
        if (window == null)
            return;

        foreach (ModSearchBar searchBar in window.GetVisualDescendants().OfType<ModSearchBar>())
            searchBar.ApplyStyle(style);

        IBrush formBrush = new SolidColorBrush(style.formColor.ToAvaloniaColor());
        IBrush textBrush = new SolidColorBrush(style.textColor.ToAvaloniaColor());
        IBrush panelBrush = new SolidColorBrush(style.modRefPanelColor.ToAvaloniaColor());
        IBrush buttonBrush = new SolidColorBrush(style.buttonColor.ToAvaloniaColor());
        IBrush buttonTextBrush = new SolidColorBrush(style.buttonTextColor.ToAvaloniaColor());
        IBrush buttonOutlineBrush = new SolidColorBrush(style.buttonOutlineColor.ToAvaloniaColor());

        window.Background = formBrush;

        ThemeVariant? ownerVariant = window.Owner?.RequestedThemeVariant;
        window.RequestedThemeVariant = ownerVariant ?? (IsDark(style.formColor) ? ThemeVariant.Dark : ThemeVariant.Light);

        IBrush inputTextBrush = IsDark(style.formColor) ? Brushes.White : Brushes.Black;

        foreach (Visual visual in window.GetVisualDescendants())
        {
            if (visual is TextBlock textBlock)
            {
                if (!textBlock.IsSet(TextBlock.ForegroundProperty))
                    textBlock.Foreground = textBrush;
                continue;
            }

            if (visual is TextBox textBox)
            {
                if (textBox.FindAncestorOfType<ModSearchBar>() != null)
                    continue;

                if (!textBox.IsSet(TextBox.BackgroundProperty))
                    textBox.Background = panelBrush;
                if (!textBox.IsSet(TextBox.ForegroundProperty))
                    textBox.Foreground = inputTextBrush;
                continue;
            }

            if (visual is ComboBox comboBox)
            {
                if (!comboBox.IsSet(ComboBox.BackgroundProperty))
                    comboBox.Background = panelBrush;
                if (!comboBox.IsSet(ComboBox.ForegroundProperty))
                    comboBox.Foreground = inputTextBrush;
                continue;
            }

            if (visual is ListBox listBox)
            {
                if (!listBox.IsSet(ListBox.BackgroundProperty))
                    listBox.Background = panelBrush;
                continue;
            }

            if (visual is Button button)
            {
                if (!button.IsSet(Button.BackgroundProperty))
                    button.Background = buttonBrush;
                if (!button.IsSet(Button.ForegroundProperty))
                    button.Foreground = buttonTextBrush;
                if (!button.IsSet(Button.BorderBrushProperty))
                    button.BorderBrush = buttonOutlineBrush;
                if (!button.IsSet(Button.BorderThicknessProperty))
                    button.BorderThickness = new Thickness(1);
            }
        }

        if (window is IStyleAwareWindow styleAware)
            styleAware.ApplyCustomStyle(style);
    }

    private static bool IsDark(SimpleColor color)
    {
        double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return luminance < 128;
    }
}
