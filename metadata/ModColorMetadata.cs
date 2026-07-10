using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

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

        private static Dictionary<string, ModColor> _modColors = new Dictionary<string, ModColor>();

        static ModColorMetadataStore()
        {
            LoadModColors();
        }

        public static ModColor GetModColor(string modId)
        {
            lock (FileLock)
            {
                if (_modColors.TryGetValue(modId, out ModColor color))
                {
                    return color;
                }
                return ModColor.None;
            }
        }

        public static void SetModColor(string modId, ModColor color)
        {
            lock (FileLock)
            {
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
                    _modColors = new Dictionary<string, ModColor>();
                    return;
                }

                try
                {
                    string json = File.ReadAllText(MetadataPath);
                    var data = JsonSerializer.Deserialize<List<ModColorMetadata>>(json);
                    _modColors = new Dictionary<string, ModColor>();
                    if (data != null)
                    {
                        foreach (var entry in data)
                        {
                            _modColors[entry.ModId] = entry.Color;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log the error and continue with an empty dictionary
                    Console.WriteLine($"Error loading mod colors: {ex.Message}");
                    _modColors = new Dictionary<string, ModColor>();
                }
            }
        }

        private static void SaveModColors()
        {
            lock (FileLock)
            {
                if (!Directory.Exists(MetadataDir))
                {
                    Directory.CreateDirectory(MetadataDir);
                }

                var data = new List<ModColorMetadata>();
                foreach (var entry in _modColors)
                {
                    data.Add(new ModColorMetadata { ModId = entry.Key, Color = entry.Value });
                }

                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(data, options);
                    File.WriteAllText(MetadataPath, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving mod colors: {ex.Message}");
                }
            }
        }
    }
}