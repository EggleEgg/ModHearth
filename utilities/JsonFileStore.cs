using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ModHearth.Utilities.Logging;

namespace ModHearth.Utilities
{
    /// <summary>
    /// One cached options instance (CA1869) and one Load/Save shape that logs failures instead of swallowing them.
    /// </summary>
    internal static class JsonFileStore
    {
        public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        /// <summary>
        /// Lenient read options. Older builds (and hand-edited files) may use PascalCase keys where
        /// the on-disk contract is lowercase, so reads accept either casing.
        /// </summary>
        public static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

        public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
            => JsonSerializer.Serialize(value, options ?? Indented);

        /// <summary>False when the file is missing (not an error) or unreadable/invalid (logged).</summary>
        public static bool TryLoad<T>(string path, string label, [NotNullWhen(true)] out T? value, JsonSerializerOptions? options = null)
        {
            value = default;
            if (!File.Exists(path))
                return false;

            try
            {
                value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), options ?? CaseInsensitive);
                return !Equals(value, default(T));
            }
            catch (Exception ex)
            {
                AppLogging.LogError($"[{label}] Failed to read '{path}': {ex.Message}");
                value = default;
                return false;
            }
        }

        public static bool TrySave<T>(string path, T value, string label, JsonSerializerOptions? options = null)
        {
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    _ = Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonSerializer.Serialize(value, options ?? Indented));
                return true;
            }
            catch (Exception ex)
            {
                AppLogging.LogError($"[{label}] Failed to write '{path}': {ex.Message}");
                return false;
            }
        }
    }
}
