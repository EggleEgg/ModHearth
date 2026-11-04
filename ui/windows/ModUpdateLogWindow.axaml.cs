using Avalonia.Controls;
using ModHearth.UI.Theme;

namespace ModHearth.UI
{
    public partial class ModUpdateLogWindow : Window
    {
        public static double DefaultWidth => 980;
        public static double DefaultMinWidth => 820;
        public static double DefaultMaxWidth => 1200;
        public static double DefaultHeight => 560;
        public static double DefaultMinHeight => 200;
        public static double DefaultMaxHeight => 600;

        public ModUpdateLogWindow() : this(null, null) { }

        public ModUpdateLogWindow(ModHearthManager? manager, ModUpdateLogControl? control = null)
        {
            InitializeComponent();
            WindowThemeManager.Register(this);
            Content = control ?? new ModUpdateLogControl(manager);
        }

    }
}
