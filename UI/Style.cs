using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
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

        // Colors.
        public SimpleColor modRefColor { get; set; } = null!;
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
        public SimpleColor searchButtonColor { get; set; } = null!;

        // Default style.
        public Style()
        {
            instance = this;
        }

        private static Style? fallback;

        public static Style GetFallback()
        {
            if (fallback != null)
                return fallback;

            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream? stream = assembly.GetManifestResourceStream("ModHearth.style.json");
            if (stream == null)
                throw new InvalidOperationException("Embedded style.json not found.");

            using StreamReader reader = new StreamReader(stream);
            string jsonContent = reader.ReadToEnd();
            Style? embedded = JsonSerializer.Deserialize<Style>(jsonContent);
            if (embedded == null)
                throw new InvalidOperationException("Embedded style.json could not be parsed.");

            fallback = embedded;
            return fallback;
        }

        public void ApplyDefaults(Style fallback)
        {
            if (fallback == null)
                throw new ArgumentNullException(nameof(fallback));

            modRefColor ??= fallback.modRefColor;
            modRefHighlightColor ??= fallback.modRefHighlightColor;
            modRefJumpHighlightColor ??= fallback.modRefJumpHighlightColor;
            modRefCacheBarColor ??= fallback.modRefCacheBarColor;
            modRefPanelColor ??= fallback.modRefPanelColor;
            modRefTextColor ??= fallback.modRefTextColor;
            modRefTextBadColor ??= fallback.modRefTextBadColor;
            modRefTextWarningColor ??= fallback.modRefTextWarningColor;
            modRefTextFilteredColor ??= fallback.modRefTextFilteredColor;
            formColor ??= fallback.formColor;
            textColor ??= fallback.textColor;
            buttonColor ??= fallback.buttonColor;
            buttonTextColor ??= fallback.buttonTextColor;
            buttonOutlineColor ??= fallback.buttonOutlineColor ?? buttonTextColor;
            searchButtonColor ??= fallback.searchButtonColor ?? buttonColor;
        }

        public static Style EnsureDefaults(Style style, Style fallback)
        {
            if (style == null)
                throw new ArgumentNullException(nameof(style));

            style.ApplyDefaults(fallback);
            return style;
        }
    }
}
