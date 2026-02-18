using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ModHearth
{
    /// <summary>
    /// Object matching how dfhack handles mods internally. 
    /// Only stores ID and Version. 
    /// Acts like a value type.
    /// </summary>
    public class DFHMod
    {
        public string id { get; set; }
        public int version { get; set; }

        // For display and hash function.
        public override string ToString()
        {
            return id + "|" + version;
        }

        // Simple check if they represent the same mod or not.
        public static bool operator ==(DFHMod lhs, DFHMod rhs)
        {
            if (ReferenceEquals(lhs, rhs)) return true;
            if (ReferenceEquals(lhs, null)) return false;
            if (ReferenceEquals(rhs, null)) return false;
            return lhs.ToString() == rhs.ToString();
        }
        public static bool operator !=(DFHMod lhs, DFHMod rhs)
        {
            return !(lhs == rhs);
        }

        // Simple hash code generation using tostring.
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        // Just use ==.
        public override bool Equals(Object other)
        {
            if(other is DFHMod dfother)
                return this == dfother;
            return false;
        }

        public DFHMod(string id, int version)
        {
            this.id = id;
            this.version = version;
        }

    }

    /// <summary>
    /// Object matching how dfhack handles modpacks internally.
    /// Only stores if it's the default modpack, a list of mods, and a name.
    /// Ordering is strange but matches the dfhack json file.
    /// </summary>
    public class DFHModpack
    {
        public bool @default { get; set; }
        public List<DFHMod> modlist { get; set; }
        public string name { get; set; }

        public DFHModpack(bool @default, List<DFHMod> modlist, string name)
        {
            this.@default = @default;
            this.modlist = modlist;
            this.name = name;
        }
    }
}
