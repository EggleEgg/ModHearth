namespace ModHearth
{
    /// <summary>
    /// Object matching how dfhack handles mods internally. 
    /// Only stores ID and Version. 
    /// Acts like a value type.
    /// </summary>
    public class DFHMod(string id, int version)
    {
        public string id { get; set; } = id;
        public int version { get; set; } = version;
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

        // For display and hash function.
        public override string ToString() => id + "|" + version;

        // Simple check if they represent the same mod or not.
        public static bool operator ==(DFHMod lhs, DFHMod rhs)
        {
            if (ReferenceEquals(lhs, rhs)) return true;
            if (ReferenceEquals(lhs, null)) return false;
            if (ReferenceEquals(rhs, null)) return false;
            return lhs.version == rhs.version &&
                   IdComparer.Equals(lhs.id, rhs.id);
        }
        public static bool operator !=(DFHMod lhs, DFHMod rhs)
        {
            return !(lhs == rhs);
        }

        // Simple hash code generation using tostring.
        public override int GetHashCode() => HashCode.Combine(IdComparer.GetHashCode(id ?? string.Empty), version);

        // Just use ==.
        public override bool Equals(Object? other)
        {
            switch (other)
            {
                case DFHMod dfother:
                    return this == dfother;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Object matching how dfhack handles modpacks internally.
    /// Only stores if it's the default modpack, a list of mods, and a name.
    /// Ordering is strange but matches the dfhack json file.
    /// </summary>
    public class DFHModpack(bool @default, List<DFHMod> modlist, string name)
    {
        public bool @default { get; set; } = @default;
        public List<DFHMod> modlist { get; set; } = modlist;
        public string name { get; set; } = name;
    }
}
