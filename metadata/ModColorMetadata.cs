using System.Text.Json;
using System.Text.Json.Serialization;
using ModHearth.Models;
using ModHearth.Utilities;

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
                if (color == ModColor.None)
                    _ = _modColors.Remove(modId);
                else
                    _modColors[modId] = color;

                SaveModColors();
            }
        }

        public static void SetModColors(IEnumerable<KeyValuePair<string, ModColor>> updates)
        {
            lock (FileLock)
            {
                bool changed = false;
                foreach (KeyValuePair<string, ModColor> pair in updates)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;

                    if (pair.Value == ModColor.None)
                    {
                        if (_modColors.Remove(pair.Key))
                            changed = true;
                    }
                    else if (!_modColors.TryGetValue(pair.Key, out ModColor existing) || existing != pair.Value)
                    {
                        _modColors[pair.Key] = pair.Value;
                        changed = true;
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
                Dictionary<string, ModColor> loaded = new(StringComparer.OrdinalIgnoreCase);
                if (JsonFileStore.TryLoad(MetadataPath, "ModColors", out List<ModColorMetadata>? data, SerializerOptions))
                {
                    foreach (ModColorMetadata entry in data.Where(e => !string.IsNullOrWhiteSpace(e.ModId)))
                        loaded[entry.ModId] = entry.Color;
                }
                _modColors = loaded;
            }
        }

        private static void SaveModColors()
            => _ = JsonFileStore.TrySave(MetadataPath,
                _modColors.Select(e => new ModColorMetadata { ModId = e.Key, Color = e.Value }).ToList(),
                "ModColors", SerializerOptions);
    }
}