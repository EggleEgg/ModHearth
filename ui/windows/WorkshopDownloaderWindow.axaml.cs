using System;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Controls;

namespace ModHearth.UI
{
    public partial class WorkshopDownloaderWindow : Window, IStyleAwareWindow
    {
        public static double DefaultWidth => LoadDimension("Width");
        public static double DefaultMinWidth => LoadDimension("MinWidth");
        public static double DefaultMaxWidth => LoadDimension("MaxWidth");

        private static double LoadDimension(string attributeName)
        {
            string path = Path.Combine("ui", "windows", "WorkshopDownloaderWindow.axaml");
            if (!File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "ui", "windows", "WorkshopDownloaderWindow.axaml");
            }
            string content = File.ReadAllText(path);
            var match = Regex.Match(content, $@"{attributeName}\s*=\s*""(?<val>[^""]+)""", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups["val"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                return val;
            }
            throw new InvalidOperationException($"Could not parse {attributeName} from WorkshopDownloaderWindow.axaml");
        }

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
