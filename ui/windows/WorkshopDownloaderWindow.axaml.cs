using Avalonia.Controls;

namespace ModHearth.UI
{
    public partial class WorkshopDownloaderWindow : Window, IStyleAwareWindow
    {
        public WorkshopDownloaderWindow() : this(null!) { }

        public WorkshopDownloaderWindow(ModHearthManager manager, WorkshopDownloaderControl? control = null)
        {
            InitializeComponent();
            WindowThemeManager.Register(this);
            Content = control ?? new WorkshopDownloaderControl(manager);
        }

        public void ApplyCustomStyle(Style style)
        {
            WindowThemeManager.ApplyToWindow(this, style);
        }
    }
}
