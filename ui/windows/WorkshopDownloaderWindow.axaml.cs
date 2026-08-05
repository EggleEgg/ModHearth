using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;

namespace ModHearth.UI
{
    public partial class WorkshopDownloaderWindow : Window
    {
        public static double DefaultWidth => LoadDimension("Width", 550);
        public static double DefaultMinWidth => LoadDimension("MinWidth", 400);
        public static double DefaultMaxWidth => LoadDimension("MaxWidth", 600);
        public static double DefaultHeight => LoadDimension("Height", 750);
        public static double DefaultMinHeight => LoadDimension("MinHeight", 400);

        private static double LoadDimension(string attributeName, double fallback)
        {
            try
            {
                string path = Path.Combine("ui", "windows", "WorkshopDownloaderWindow.axaml");
                if (!File.Exists(path))
                {
                    path = Path.Combine(AppContext.BaseDirectory, "ui", "windows", "WorkshopDownloaderWindow.axaml");
                }
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    var match = Regex.Match(content, $@"{attributeName}\s*=\s*""(?<val>[^""]+)""", RegexOptions.IgnoreCase);
                    if (match.Success && double.TryParse(match.Groups["val"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        return val;
                    }
                }
            }
            catch
            {
                // Fallback
            }
            return fallback;
        }

        public WorkshopDownloaderWindow() : this(null!) { }

        public WorkshopDownloaderWindow(ModHearthManager manager, WorkshopDownloaderControl? control = null)
        {
            InitializeComponent();
            WindowThemeManager.Register(this);
            Content = control ?? new WorkshopDownloaderControl(manager);
        }


    }
}
