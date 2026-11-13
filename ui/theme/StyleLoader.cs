using ModHearth.Config;
using ModHearth.Utils.IO;

namespace ModHearth.UI.Theme
{
    /// <summary>
    /// Loads styles/style.*.json into <see cref="Style"/>. Lives in the UI layer so ConfigManager stays config-only.
    /// </summary>
    public static class StyleLoader
    {
        private static readonly string styleLightPath = Path.Combine(AppContext.BaseDirectory, "styles", "style.light.json");
        private static readonly string styleDarkPath = Path.Combine(AppContext.BaseDirectory, "styles", "style.dark.json");

        public static Style LoadStyle() => LoadStyle(true);

        public static Style LoadStyle(bool createLogs)
        {
            string stylePath = GetStylePathForTheme(ConfigManager.GetTheme());
            Style style;

            try
            {
                if (!File.Exists(stylePath))
                    throw new FileNotFoundException("Style file missing.", stylePath);

                if (createLogs) Console.WriteLine("Style file found.");
                if (!TryLoadStyleFromPath(stylePath, out style))
                    throw new InvalidOperationException($"Style file invalid: {stylePath}");
            }
            catch (Exception ex)
            {
                string message = $"Style load failed: {ex.Message}\nMissing or invalid style file: {stylePath}";
                Console.WriteLine(message);
                throw new InvalidOperationException(message, ex);
            }

            Style.instance = style;
            return style;
        }

        public static string GetStylePathForTheme(int theme) => theme == 0 ? styleLightPath : styleDarkPath;

        private static bool TryLoadStyleFromPath(string stylePath, out Style style)
        {
            style = null!;
            if (!JsonFileStore.TryLoad(stylePath, "Style", out Style? found) || !found.IsComplete())
                return false;
            style = found;
            return true;
        }
    }
}
