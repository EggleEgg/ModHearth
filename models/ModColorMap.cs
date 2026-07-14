using Avalonia.Media;

namespace ModHearth;

public static class ModColorMap
{
    public static IReadOnlyDictionary<ModColor, Color> Colors { get; }
    public static IReadOnlyDictionary<ModColor, string> ColorNames { get; }

    static ModColorMap()
    {
        Colors = new Dictionary<ModColor, Color>
        {
            { ModColor.None, Color.FromArgb(0, 0, 0, 0) }, // Transparent
            { ModColor.Red, Color.Parse("#FF0000") },
            { ModColor.Crimson, Color.Parse("#DC143C") },
            { ModColor.DarkRed, Color.Parse("#8B0000") },
            { ModColor.Maroon, Color.Parse("#800000") },
            { ModColor.Coral, Color.Parse("#FF7F50") },
            { ModColor.Orange, Color.Parse("#FFA500") },
            { ModColor.Brown, Color.Parse("#A52A2A") },
            { ModColor.Wheat, Color.Parse("#F5DEB3") },
            { ModColor.Gold, Color.Parse("#FFD700") },
            { ModColor.Beige, Color.Parse("#F5F5DC") },
            { ModColor.Yellow, Color.Parse("#FFFF00") },
            { ModColor.Olive, Color.Parse("#808000") },
            { ModColor.Lime, Color.Parse("#BFFF00") },
            { ModColor.Green, Color.Parse("#00FF00") },
            { ModColor.ForestGreen, Color.Parse("#228B22") },
            { ModColor.DarkGreen, Color.Parse("#006400") },
            { ModColor.Turquoise, Color.Parse("#40E0D0") },
            { ModColor.Teal, Color.Parse("#008080") },
            { ModColor.Cyan, Color.Parse("#00FFFF") },
            { ModColor.Aqua, Color.Parse("#7FDBFF") },
            { ModColor.SkyBlue, Color.Parse("#87CEEB") },
            { ModColor.SteelBlue, Color.Parse("#4682B4") },
            { ModColor.Blue, Color.Parse("#0000FF") },
            { ModColor.DarkBlue, Color.Parse("#00008B") },
            { ModColor.Navy, Color.Parse("#000080") },
            { ModColor.Lavender, Color.Parse("#E6E6FA") },
            { ModColor.SlateBlue, Color.Parse("#6A5ACD") },
            { ModColor.Indigo, Color.Parse("#4B0082") },
            { ModColor.Purple, Color.Parse("#800080") },
            { ModColor.Violet, Color.Parse("#EE82EE") },
            { ModColor.Magenta, Color.Parse("#FF00FF") },
            { ModColor.Pink, Color.Parse("#FFC0CB") },
            { ModColor.Silver, Color.Parse("#C0C0C0") },
            { ModColor.DarkGray, Color.Parse("#616161") }

        };

        ColorNames = Colors.ToDictionary(kvp => kvp.Key, kvp => kvp.Key.ToString());
    }

    public static Color GetColor(ModColor modColor, byte alpha = 255)
    {
        Color color = Colors[modColor];
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}