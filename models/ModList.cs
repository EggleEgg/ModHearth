using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModHearth
{
    //#fix# does this need default? just let dfhack handle it?
    internal class ModList
    {
        public string name;
        public List<ModReference> mods;

        public ModList(string name, List<ModReference> mods)
        {
            this.name = name;
            this.mods = mods;
        }

        public ModList(DFHModpack dfhackModlist, Dictionary<string, ModReference> modMap)
        {
            this.name = dfhackModlist.name;
            this.mods = new List<ModReference>();
            foreach(DFHMod dfmod in dfhackModlist.modlist)
            {
                this.mods.Add(modMap[dfmod.id]);
            }
        }
    }
}
