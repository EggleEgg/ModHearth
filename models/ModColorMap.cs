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
            { ModColor.Green, Color.Parse("#00FF00") },
            { ModColor.Blue, Color.Parse("#0000FF") },
            { ModColor.Yellow, Color.Parse("#FFFF00") },
            { ModColor.Orange, Color.Parse("#FFA500") },
            { ModColor.Purple, Color.Parse("#800080") },
            { ModColor.Pink, Color.Parse("#FFC0CB") },
            { ModColor.Brown, Color.Parse("#A52A2A") },
            { ModColor.Cyan, Color.Parse("#00FFFF") },
            { ModColor.Magenta, Color.Parse("#FF00FF") },
            { ModColor.Lime, Color.Parse("#BFFF00") },
            { ModColor.Teal, Color.Parse("#008080") },
            { ModColor.Lavender, Color.Parse("#E6E6FA") },
            { ModColor.Beige, Color.Parse("#F5F5DC") },
            { ModColor.Maroon, Color.Parse("#800000") },
            { ModColor.Navy, Color.Parse("#000080") },
            { ModColor.Olive, Color.Parse("#808000") },
            { ModColor.Aqua, Color.Parse("#7FDBFF") },
            { ModColor.Coral, Color.Parse("#FF7F50") },
            { ModColor.Indigo, Color.Parse("#4B0082") },
            { ModColor.Gold, Color.Parse("#FFD700") },
            { ModColor.Silver, Color.Parse("#C0C0C0") },
            { ModColor.Turquoise, Color.Parse("#40E0D0") },
            { ModColor.Violet, Color.Parse("#EE82EE") },
            { ModColor.Wheat, Color.Parse("#F5DEB3") },
            { ModColor.SlateBlue, Color.Parse("#6A5ACD") },
            { ModColor.DarkGreen, Color.Parse("#006400") },
            { ModColor.DarkRed, Color.Parse("#8B0000") },
            { ModColor.DarkBlue, Color.Parse("#00008B") },
            { ModColor.ForestGreen, Color.Parse("#228B22") },
            { ModColor.Crimson, Color.Parse("#DC143C") },
            { ModColor.SteelBlue, Color.Parse("#4682B4") }
        };

        ColorNames = Colors.ToDictionary(kvp => kvp.Key, kvp => kvp.Key.ToString());
    }

    public static Color GetColor(ModColor modColor, byte alpha = 255)
    {
        Color color = Colors[modColor];
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}