namespace ModHearth
{
    // Per-mod raw-file scan result, persisted to disk so mods whose objects folder hasn't changed since the last scan skip re-scanning entirely.
    // This is the data ModSort's dependency graph is built from, replacing the old name/ID/description-text heuristics.
    public sealed class ModRawDependencyInfo
    {
        public string ModId { get; set; } = string.Empty;
        public string NumericVersion { get; set; } = string.Empty;

        // Cache key component: LastWriteTimeUtc (ticks), walked recursively, of this mod's objects/ folder at scan time. Any add/edit/delete
        // under objects/ changes this, invalidating the cached entry.
        public long ObjectsFolderStampTicks { get; set; }

        // True if this mod contains any CUT_* token anywhere under objects/. Replaces the old name/ID keyword-matching heuristic as the signal
        // for "this is a replacer/overhaul that must load early relative to anything it cuts."
        public bool IsCutter { get; set; }

        // IDs this mod wipes via a SELECT_x block that also contains a CUT token. Anything else that SELECTs the same ID must load after
        // this mod, or its additions get silently erased (the "Better Instruments" scenario from the design notes).
        public List<string> CutTargetIds { get; set; } = new();

        // Every ID this mod patches via SELECT_x, whether or not it also cuts it.
        public List<string> SelectTargetIds { get; set; } = new();

        // Source IDs referenced via [COPY_TAGS_FROM:ID] — a hard dependency; this mod must load after whatever mod directly defines the source.
        public List<string> CopyTagsFromSourceIds { get; set; } = new();

        // IDs this mod defines directly (e.g. [CREATURE:X], [ENTITY:X]) outside of any SELECT_x block. Two different mods both directly
        // defining the same ID with no CUT relationship is the "duplicate raws" silent-breakage case — DF's errorlog.txt won't necessarily catch it.
        public List<string> DirectDefinitionIds { get; set; } = new();

        // Coarse traits used only for tie-breaking mods that have no
        // explicit graph relationship to anything else.
        public bool HasVanillaEntity { get; set; }
        public bool HasNewEntity { get; set; }
        public bool HasReaction { get; set; }
        public bool HasCreature { get; set; }
        public bool HasNewStuff { get; set; }
        public bool HasGraphics { get; set; }
    }
}