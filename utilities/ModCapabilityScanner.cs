using System.Text.RegularExpressions;

namespace ModHearth.Utilities;

/// <summary>
/// Detects Sound/DFHack/Lua/Native capability evidence via directory structure, file extensions, and
/// DFHack-specific Lua API usage. Raw and Graphics are NOT detected here -- both signals already come
/// out of ModRawObjectScanner's existing per-mod scan (RawDatabase.DefinedObjects / HasGraphics), so
/// callers pass those in rather than this class re-walking objects/ or graphics/ a second time.
/// </summary>
internal static class ModCapabilityScanner
{
    // DFHack-specific Lua require()/API usage. A .lua file merely existing under a generic scripts/
    // folder isn't a DFHack signal by itself -- community mods can use that folder for arbitrary
    // purposes -- but actually calling into DFHack's Lua API is conclusive.
    private static readonly Regex DfHackLuaApiRegex = new(
        @"require\s*\(\s*['""](?:df|dfhack|utils|gui(?:\.\w+)?|plugins)['""]\s*\)|\bdfhack\.\w+|\bdf\.\w+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Checked regardless of host OS: a mod package can ship plugin binaries for platforms other than
    // the one ModHearth is currently running on, and that's still meaningful "this is DFHack-native"
    // evidence even if this platform's variant is what would actually load.
    private static readonly string[] NativePluginExtensions = [".dll", ".so", ".dylib"];

    public static ModCapabilities Scan(string modPath, bool hasRawDefinitions, bool hasGraphics)
    {
        if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
            return ModCapabilities.None;

        ModCapabilities capabilities = ModCapabilities.None;

        if (hasRawDefinitions)
            capabilities |= ModCapabilities.Raw;
        if (hasGraphics)
            capabilities |= ModCapabilities.Graphics;

        if (HasSoundAssets(modPath))
            capabilities |= ModCapabilities.Sound;

        // scripts_modactive/ and scripts_modinstalled/ are DFHack's own mod-script directories --
        // DFHack automatically adds these to its script search path for installed/active mods, so
        // their presence alone is conclusive, not weighted evidence.
        bool hasModScriptDirs =
            Directory.Exists(Path.Combine(modPath, "scripts_modactive")) ||
            Directory.Exists(Path.Combine(modPath, "scripts_modinstalled"));

        bool hasInitD = HasInitDScripts(modPath);
        bool hasNativePlugins = HasNativePlugins(modPath);
        bool hasDfHackLuaApiUsage = HasDfHackLuaApiUsage(modPath);

        if (hasModScriptDirs || hasInitD || hasNativePlugins || hasDfHackLuaApiUsage)
            capabilities |= ModCapabilities.DfHack | ModCapabilities.Lua;

        if (hasInitD)
            capabilities |= ModCapabilities.DfHackStartup;

        if (hasNativePlugins)
            capabilities |= ModCapabilities.Native;

        // Generic scripts/ with .lua files but no DFHack-specific signal elsewhere still counts as
        // Lua (the mod scripts *something*), just not necessarily DFHack.
        if (!capabilities.HasFlag(ModCapabilities.Lua) && HasAnyLuaFiles(modPath))
            capabilities |= ModCapabilities.Lua;

        return capabilities;
    }

    private static bool HasSoundAssets(string modPath)
    {
        string soundDir = Path.Combine(modPath, "sound");
        if (!Directory.Exists(soundDir))
            return false;

        try
        {
            return Directory.EnumerateFiles(soundDir, "*.ogg", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool HasInitDScripts(string modPath)
    {
        string initDDir = Path.Combine(modPath, "init.d");
        if (!Directory.Exists(initDDir))
            return false;

        try
        {
            return Directory.EnumerateFiles(initDDir, "*.lua", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool HasNativePlugins(string modPath)
    {
        string pluginsDir = Path.Combine(modPath, "plugins");
        if (!Directory.Exists(pluginsDir))
            return false;

        try
        {
            return NativePluginExtensions.Any(ext =>
                Directory.EnumerateFiles(pluginsDir, $"*{ext}", SearchOption.AllDirectories).Any());
        }
        catch
        {
            return false;
        }
    }

    private static bool HasAnyLuaFiles(string modPath)
    {
        try
        {
            return Directory.EnumerateFiles(modPath, "*.lua", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool HasDfHackLuaApiUsage(string modPath)
    {
        try
        {
            foreach (string luaFile in Directory.EnumerateFiles(modPath, "*.lua", SearchOption.AllDirectories))
            {
                string content;
                try
                {
                    content = File.ReadAllText(luaFile);
                }
                catch
                {
                    continue;
                }

                if (DfHackLuaApiRegex.IsMatch(content))
                    return true;
            }
        }
        catch
        {
            // Ignore unreadable mod folders; treat as no DFHack Lua usage found.
        }

        return false;
    }
}