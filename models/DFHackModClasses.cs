using System.Text.Json.Serialization;

namespace ModHearth.Models
{
    /// <summary>
    /// Object matching how dfhack handles mods internally. 
    /// Only stores ID and Version. 
    /// Acts like a value type.
    /// </summary>
    public class DfhMod(string id, int version)
    {
        // DFHack's mod-manager.json uses lowercase keys; pins the on-disk shape regardless of
        // the CLR property names.
        [JsonPropertyName("id")]
        public string Id { get; init; } = id;

        [JsonPropertyName("version")]
        public int Version { get; init; } = version;
        private string? cachedKey;
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

        // Immutable, so the key is built once. ToString() is the lookup key for modrefMap/modViewMap.
        // Racing threads compute the same string, so the unsynchronised ??= is benign.
        public override string ToString() => cachedKey ??= Id + "|" + Version;

        // Simple check if they represent the same mod or not.

        public static bool operator ==(DfhMod lhs, DfhMod rhs)
        {
            if (ReferenceEquals(lhs, rhs)) return true;
            if (lhs is null) return false;
            if (rhs is null) return false;
            return lhs.Version == rhs.Version &&
                   IdComparer.Equals(lhs.Id, rhs.Id);
        }
        public static bool operator !=(DfhMod lhs, DfhMod rhs)
        {
            return !(lhs == rhs);
        }

        // Simple hash code generation using tostring.
        public override int GetHashCode() => HashCode.Combine(IdComparer.GetHashCode(Id ?? string.Empty), Version);

        // Just use ==.

        public override bool Equals(object? obj) => obj is DfhMod other && this == other;
    }

    /// <summary>
    /// Object matching how dfhack handles modpacks internally.
    /// Only stores if it's the default modpack, a list of mods, and a name.
    /// Ordering is strange but matches the dfhack json file.
    /// </summary>
    public class DfhModpack(bool @default, List<DfhMod> modlist, string name)
    {
        [JsonPropertyName("default")]
        public bool Default { get; set; } = @default;

        [JsonPropertyName("modlist")]
        public List<DfhMod> Modlist { get; set; } = modlist;

        [JsonPropertyName("name")]
        public string Name { get; set; } = name;
    }
}
