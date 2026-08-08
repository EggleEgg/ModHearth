using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI;

public static class WindowDimensionLoader
{
    public static double Load(string axamlFileName, string attributeName, double fallback)
    {
        try
        {
            string relative = axamlFileName.Contains("ui") ? axamlFileName : Path.Combine("ui", "windows", axamlFileName);
            string path = relative;
            if (!File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, relative);
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
        catch (Exception ex)
        {
            InfoLogger.Log($"WindowDimensionLoader error loading {attributeName} for {axamlFileName}: {ex.Message}");
        }
        return fallback;
    }
}
