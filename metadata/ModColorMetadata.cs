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
        private static readonly object FileLock = new();
        private static readonly JsonSerializerOptions SerializerOptions = new()

        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static Dictionary<string, ModColor> _modColors = new(StringComparer.OrdinalIgnoreCase);

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
                switch (color)
                {
                    case ModColor.None:
                        _ = _modColors.Remove(modId);
                        break;
                    default:
                        _modColors[modId] = color;
                        break;
                }
                SaveModColors();
            }
        }

        public static void SetModColors(IEnumerable<KeyValuePair<string, ModColor>> updates)
        {
            lock (FileLock)
            {
                bool changed = false;
                foreach (var pair in updates)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;

                    switch (pair.Value)
                    {
                        case ModColor.None:
                            if (_modColors.Remove(pair.Key))
                                changed = true;
                            break;
                        default:
                            if (!_modColors.TryGetValue(pair.Key, out var existing) || existing != pair.Value)
                            {
                                _modColors[pair.Key] = pair.Value;
                                changed = true;
                            }
                            break;
                    }
                }
                if (changed)
                {
                    SaveModColors();
                }
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
                    Dictionary<string, ModColor> loaded = new(StringComparer.OrdinalIgnoreCase);
                    if (data != null)
                    {
                        foreach (var entry in from ModColorMetadata entry in data
                                              where !string.IsNullOrWhiteSpace(entry.ModId)
                                              select entry)
                        {
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
                _ = Directory.CreateDirectory(MetadataDir);

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