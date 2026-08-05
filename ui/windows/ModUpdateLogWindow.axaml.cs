using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;

namespace ModHearth.UI;

public partial class ModUpdateLogWindow : Window
{
    public static double DefaultWidth => LoadDimension("Width", 980);
    public static double DefaultMinWidth => LoadDimension("MinWidth", 820);
    public static double DefaultMaxWidth => LoadDimension("MaxWidth", 1200);
    public static double DefaultHeight => LoadDimension("Height", 320);
    public static double DefaultMinHeight => LoadDimension("MinHeight", 240);
    public static double DefaultMaxHeight => LoadDimension("MaxHeight", 600);

    private static double LoadDimension(string attributeName, double fallback)
    {
        try
        {
            string path = Path.Combine("ui", "windows", "ModUpdateLogWindow.axaml");
            if (!File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "ui", "windows", "ModUpdateLogWindow.axaml");
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

    public ModUpdateLogWindow() : this(null, null) { }

    public ModUpdateLogWindow(ModHearthManager? manager, ModUpdateLogControl? control = null)
    {
        InitializeComponent();
        WindowThemeManager.Register(this);
        Content = control ?? new ModUpdateLogControl(manager);
    }


}
