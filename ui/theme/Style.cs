using System.Reflection;
using Avalonia.Media;

namespace ModHearth.UI.Theme
{
    /// <summary>
    /// A simpler representation of color for easy serialization.
    /// </summary>
    [Serializable]
    public class SimpleColor
    {
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        public int A { get; set; }

        // Empty constructor for json serialization.
        public SimpleColor()
        {
        }

        // Create new simpleColor from rgba.
        public SimpleColor(int r, int g, int b, int a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public Color ToAvaloniaColor() => Color.FromArgb((byte)A, (byte)R, (byte)G, (byte)B);

        public SimpleColor(Color color)
        {
            R = color.R;
            G = color.G;
            B = color.B;
            A = color.A;
        }

        /// <summary>Converts a SimpleColor to a CSS hex string (#RRGGBB).</summary>
        public static string ToHex(SimpleColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static implicit operator Color(SimpleColor color)
        {
            return color.ToAvaloniaColor();
        }

        public static implicit operator SimpleColor(Color color)
        {
            return new SimpleColor(color);
        }
    }

    /// <summary>
    /// Central style stuff from the json styles
    /// </summary>
    public class Style
    {
        // Only ever set explicitly, from ConfigManager.LoadStyle, once the object is fully populated and validated via IsComplete(). volatile ensures a
        // thread reading this after a theme swap on another thread sees the new reference promptly rather than a cached stale one.
        public static volatile Style? instance;

        private static readonly PropertyInfo[] RequiredColorProperties = [.. typeof(Style)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(SimpleColor))];

        // Colors.
        public SimpleColor BackgroundColor { get; set; } = null!;
        public SimpleColor PanelColor { get; set; } = null!;
        public SimpleColor PanelColorClear { get; set; } = null!;
        public SimpleColor PanelColorDark { get; set; } = null!;
        public SimpleColor StrongPanelColor { get; set; } = null!;
        public SimpleColor TextColor { get; set; } = null!;
        public SimpleColor ButtonColor { get; set; } = null!;
        public SimpleColor ButtonTextColor { get; set; } = null!;
        public SimpleColor ButtonOutlineColor { get; set; } = null!;
        public SimpleColor ModRefHighlightColor { get; set; } = null!;
        public SimpleColor ModRefHighlightDarkColor { get; set; } = null!;
        public SimpleColor ModRefJumpHighlightColor { get; set; } = null!;
        public SimpleColor ModRefCacheBarColor { get; set; } = null!;
        public SimpleColor ModRefTextBadColor { get; set; } = null!;
        public SimpleColor ModRefTextWarningColor { get; set; } = null!;
        public SimpleColor ModRefTextFilteredColor { get; set; } = null!;
        public SimpleColor SearchBorderColor { get; set; } = null!;
        public SimpleColor SearchButtonColor { get; set; } = null!;
        public SimpleColor SearchButtonHoverColor { get; set; } = null!;
        public SimpleColor SearchButtonPressedColor { get; set; } = null!;
        public SimpleColor SelectionColor { get; set; } = null!;

        // Default style.
        public Style()
        {
        }

        public bool IsComplete() => RequiredColorProperties.All(property => property.GetValue(this) is SimpleColor);
    }
}
