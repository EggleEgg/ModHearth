using Avalonia.Controls;
using ModHearth.UI.Theme;

namespace ModHearth.UI
{
    public partial class WorkshopDownloaderWindow : Window
    {
        public static double DefaultWidth => 550;
        public static double DefaultMinWidth => 400;
        public static double DefaultMaxWidth => 600;
        public static double DefaultHeight => 750;
        public static double DefaultMinHeight => 400;

        public WorkshopDownloaderWindow() : this(null!) { }

        public WorkshopDownloaderWindow(ModHearthManager manager, WorkshopDownloaderControl? control = null)
        {
            InitializeComponent();
            WindowThemeManager.Register(this);
            Content = control ?? new WorkshopDownloaderControl(manager);
        }

    }
}
