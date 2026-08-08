using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ModHearth.Models;

namespace ModHearth.UI;

/// <summary>
/// Helper for creating standardized color swatch buttons across code-behind menus and search bars.
/// </summary>
public static class ColorSwatchHelper
{
    public static Button CreateColorSwatchButton(ModColorInfo colorInfo, Action<ModColor> onSelected)
    {
        Border swatch = new()
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(3),
            Background = (colorInfo.ModColor == ModColor.None) ? Brushes.Transparent : BrushCache.GetBrush(colorInfo.Color),
            BorderBrush = colorInfo.IsSelected && Style.instance != null ? BrushCache.GetBrush(Style.instance.selectionColor.ToAvaloniaColor()) : Brushes.Gray,
            BorderThickness = new Thickness(colorInfo.IsSelected ? 4 : 1)
        };

        if (colorInfo.ModColor == ModColor.None)
        {
            swatch.Child = new TextBlock
            {
                Text = "\u2715",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };
        }

        Button button = new()
        {
            Content = swatch,
            Padding = new Thickness(0),
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };

        ToolTip.SetTip(button, colorInfo.Name);
        button.Click += (_, _) => onSelected(colorInfo.ModColor);

        return button;
    }
}
