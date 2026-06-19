using System;
using System.Linq;
using System.Reflection;
using Avalonia.Media;

namespace ModHearth.UI
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
    /// Central style stuff
    /// </summary>
    public class Style
    {
        // This only ever has one instance
        public static Style? instance;

        private static readonly PropertyInfo[] RequiredColorProperties = typeof(Style)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(SimpleColor))
            .ToArray();

        // Colors.
        public SimpleColor backgroundColor { get; set; } = null!;
        public SimpleColor modRefHighlightColor { get; set; } = null!;
        public SimpleColor modRefJumpHighlightColor { get; set; } = null!;
        public SimpleColor modRefCacheBarColor { get; set; } = null!;
        public SimpleColor modRefPanelColor { get; set; } = null!;
        public SimpleColor modRefTextColor { get; set; } = null!;
        public SimpleColor modRefTextBadColor { get; set; } = null!;
        public SimpleColor modRefTextWarningColor { get; set; } = null!;
        public SimpleColor modRefTextFilteredColor { get; set; } = null!;
        public SimpleColor formColor { get; set; } = null!;
        public SimpleColor textColor { get; set; } = null!;
        public SimpleColor buttonColor { get; set; } = null!;
        public SimpleColor buttonTextColor { get; set; } = null!;
        public SimpleColor buttonOutlineColor { get; set; } = null!;
        public SimpleColor searchBorderColor { get; set; } = null!;
        public SimpleColor searchButtonColor { get; set; } = null!;
        public SimpleColor searchButtonHoverColor { get; set; } = null!;
        public SimpleColor searchButtonPressedColor { get; set; } = null!;

        // Default style.
        public Style()
        {
            instance = this;
        }

        public bool IsComplete()
        {
            return RequiredColorProperties.All(property => property.GetValue(this) is SimpleColor);
        }
    }
}
