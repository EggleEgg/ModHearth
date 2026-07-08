namespace ModHearth.Utilities;

/// <summary>
/// A composite key that uniquely identifies a Dwarf Fortress raw object.
/// Equality is case-insensitive so that ENTITY:MOUNTAIN and entity:mountain
/// resolve to the same object.
/// </summary>
public sealed class ObjectKey : IEquatable<ObjectKey>
{
    public string ObjectType { get; }
    public string Id { get; }

    public ObjectKey(string objectType, string id)
    {
        ObjectType = objectType;
        Id = id;
    }

    public bool Equals(ObjectKey? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return string.Equals(ObjectType, other.ObjectType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
        => obj is ObjectKey other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(ObjectType),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Id));
    }

    public override string ToString()
        => $"{ObjectType}:{Id}";
}
