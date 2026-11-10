namespace ModHearth.Utilities.ModRaws
{
    /// <summary>
    /// A composite key that uniquely identifies a Dwarf Fortress raw object.
    /// Equality is case-insensitive so that ENTITY:MOUNTAIN and entity:mountain
    /// resolve to the same object.
    /// </summary>
    public sealed class ObjectKey(string objectType, string id) : IEquatable<ObjectKey>
    {
        public string ObjectType { get; } = objectType;
        public string Id { get; } = id;

        public bool Equals(ObjectKey? other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return string.Equals(ObjectType, other.ObjectType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => obj is ObjectKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(ObjectType),
                StringComparer.OrdinalIgnoreCase.GetHashCode(Id));

        public override string ToString() => $"{ObjectType}:{Id}";

        /// <summary>
        /// Splits an "ObjectType:Id" composite key. Returns false for keys without both parts.
        /// </summary>
        public static bool TryParse(string? compositeKey, out string objectType, out string id)
        {
            objectType = string.Empty;
            id = string.Empty;

            if (string.IsNullOrWhiteSpace(compositeKey))
                return false;

            int separatorIndex = compositeKey.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= compositeKey.Length - 1)
                return false;

            objectType = compositeKey[..separatorIndex].Trim();
            id = compositeKey[(separatorIndex + 1)..].Trim();
            return objectType.Length > 0 && id.Length > 0;
        }

        /// <summary>
        /// Turns a "ObjectType:Id" key produced by ToString() into a friendlier "Id [ObjectType]" label
        /// for display. Falls back to the raw key unchanged if it isn't in that format.
        /// </summary>
        public static string FormatForDisplay(string compositeKey)
        {
            if (string.IsNullOrEmpty(compositeKey))
                return compositeKey;

            int separatorIndex = compositeKey.IndexOf(':');
            if (separatorIndex < 0)
                return compositeKey;

            string objectType = compositeKey[..separatorIndex];
            string id = compositeKey[(separatorIndex + 1)..];
            return string.IsNullOrEmpty(objectType) ? id : $"{id} [{objectType}]";
        }
    }
}
