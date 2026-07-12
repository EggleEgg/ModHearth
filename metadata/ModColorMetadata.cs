using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModHearth.Metadata
{
    public class ModColorMetadata
    {
        public string ModId { get; set; } = string.Empty;
        public ModColor Color { get; set; }
    }

    public static class ModColorMetadataStore
    {
        private static readonly string MetadataDir = Path.Combine(AppContext.BaseDirectory, "metadata");
        private static readonly string MetadataPath = Path.Combine(MetadataDir, "mod_colors.json");
        private static readonly object FileLock = new object();
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static Dictionary<string, ModColor> _modColors = new Dictionary<string, ModColor>(StringComparer.OrdinalIgnoreCase);

        static ModColorMetadataStore()
        {
            LoadModColors();
        }

        public static ModColor GetModColor(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return ModColor.None;

            lock (FileLock)
            {
                return _modColors.TryGetValue(modId, out ModColor color) ? color : ModColor.None;
            }
        }

        public static void SetModColor(string modId, ModColor color)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return;

            lock (FileLock)
            {
                if (color == ModColor.None)
                    _modColors.Remove(modId);
                else
                    _modColors[modId] = color;
                SaveModColors();
            }
        }

        private static void LoadModColors()
        {
            lock (FileLock)
            {
                if (!File.Exists(MetadataPath))
                {
                    _modColors = new Dictionary<string, ModColor>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                try
                {
                    string json = File.ReadAllText(MetadataPath);
                    List<ModColorMetadata>? data = JsonSerializer.Deserialize<List<ModColorMetadata>>(json, SerializerOptions);
                    Dictionary<string, ModColor> loaded = new Dictionary<string, ModColor>(StringComparer.OrdinalIgnoreCase);
                    if (data != null)
                    {
                        foreach (ModColorMetadata entry in data)
                        {
                            if (!string.IsNullOrWhiteSpace(entry.ModId))
                                loaded[entry.ModId] = entry.Color;
                        }
                    }
                    _modColors = loaded;
                }
                catch (Exception ex)
                {
                    // Log the error and continue with an empty dictionary
                    Console.WriteLine($"Error loading mod colors: {ex.Message}");
                    _modColors = new Dictionary<string, ModColor>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        private static void SaveModColors()
        {
            if (!Directory.Exists(MetadataDir))
                Directory.CreateDirectory(MetadataDir);

            List<ModColorMetadata> data = _modColors
                .Select(entry => new ModColorMetadata { ModId = entry.Key, Color = entry.Value })
                .ToList();

            try
            {
                string json = JsonSerializer.Serialize(data, SerializerOptions);
                File.WriteAllText(MetadataPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving mod colors: {ex.Message}");
            }
        }
    }
}