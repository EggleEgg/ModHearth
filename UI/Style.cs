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

        // Explicit conversion to system drawing color.
        public Color ToColor()
        {
            return Color.FromArgb(A, R, G, B);
        }

        // Create new simpleColor from system drawing color.
        public SimpleColor(Color color)
        {
            R = color.R;
            G = color.G;
            B = color.B;
            A = color.A;
        }

        // Implicit conversion from simpleColor to system drawing color.
        public static implicit operator Color(SimpleColor color)
        {
            return color.ToColor();
        }

        // Implicit conversion from system drawing color to simpleColor.
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
        public static Style instance;

        // Colors.
        public SimpleColor modRefColor { get; set; }
        public SimpleColor modRefHighlightColor { get; set; }
        public SimpleColor modRefJumpHighlightColor { get; set; }
        public SimpleColor modRefCacheBarColor { get; set; }
        public SimpleColor modRefPanelColor { get; set; }
        public SimpleColor modRefTextColor { get; set; }
        public SimpleColor modRefTextBadColor { get; set; }
        public SimpleColor modRefTextFilteredColor { get; set; }
        public SimpleColor formColor { get; set; }
        public SimpleColor textColor { get; set; }

        // Default style.
        public Style()
        {
            instance = this;
        }

        // ModReferencePanel style.
        public static BorderStyle modRefBorder = BorderStyle.None;
        public static Font modRefFont = new Font("Arial", 9, FontStyle.Bold);
        public static Font modRefStrikeFont = new Font("Arial", 9, FontStyle.Bold | FontStyle.Regular);
        public static int modRefHeight = 16;
        public static Padding modRefPadding = new Padding(2, 0, 2, 0);

        // Main form style.
        public int largeBorder = 12;
        public int smallBorder = 6;

        // Right panel style.
        public static int rightPanelW = 120;

        // Left panel style.
        // The width and height of a standard steam workshop item image. 
        public static int leftPanelW = 636;
        public static int leftPanelH = 358;

        // Popup style.
        public static int popupMessageBorder = 20;
        public static int popupHeight = 100;
        public static int popupButtonWidth = 80;
        public static int popupButtonHeight = 30;
    }
}
