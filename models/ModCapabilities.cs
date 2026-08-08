namespace ModHearth;

/// <summary>
/// Capability flags for a mod's package contents (raw definitions, graphics, sound, Lua scripting,
/// DFHack integration, native plugins). DF mods routinely combine several of these in one package
/// (e.g. a total conversion with raws + graphics + DFHack Lua), so this is a flags set rather than a
/// single mutually-exclusive "mod type" enum. Persisted as a plain integer in
/// mod_raw_dependency_cache.json (no JsonStringEnumConverter is registered for it), so:
/// Always append new values at the end to avoid corrupting cached data, same rule as ModColor.
/// </summary>
[Flags]
public enum ModCapabilities
{
    None = 0,
    Raw = 1 << 0,
    Graphics = 1 << 1,
    Sound = 1 << 2,
    Lua = 1 << 3,
    DfHack = 1 << 4,
    Native = 1 << 5,

    //Pretty useless. Users either use DfHack or they dont
    DfHackStartup = 1 << 6
}