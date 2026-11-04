using System.Text.RegularExpressions;
using ModHearth.Models;

namespace ModHearth.Utilities
{
    /// <summary>
    /// Detects Sound/DFHack/Lua/Native capability evidence via directory structure, file extensions, and
    /// DFHack-specific Lua API usage. Raw and Graphics are NOT detected here -- both signals already come
    /// out of ModRawObjectScanner's existing per-mod scan (RawDatabase.DefinedObjects / HasGraphics), so
    /// callers pass those in rather than this class re-walking objects/ or graphics/ a second time.
    /// </summary>
    internal static partial class ModCapabilityScanner
    {
        // DFHack-specific Lua require()/API usage. A .lua file merely existing under a generic scripts/
        // folder isn't a DFHack signal by itself -- community mods can use that folder for arbitrary
        // purposes -- but actually calling into DFHack's Lua API is conclusive.
        private static readonly Regex DfHackLuaApiRegex = MyRegex();

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
            (bool hasLuaFiles, bool hasDfHackLuaApiUsage) = ScanLuaFiles(modPath);

            if (hasModScriptDirs || hasInitD || hasNativePlugins || hasDfHackLuaApiUsage)
                capabilities |= ModCapabilities.DfHack | ModCapabilities.Lua;

            if (hasInitD)
                capabilities |= ModCapabilities.DfHackStartup;

            if (hasNativePlugins)
                capabilities |= ModCapabilities.Native;

            // Generic scripts/ with .lua files but no DFHack-specific signal elsewhere still counts as
            // Lua (the mod scripts *something*), just not necessarily DFHack.
            if (!capabilities.HasFlag(ModCapabilities.Lua) && hasLuaFiles)
                capabilities |= ModCapabilities.Lua;

            return capabilities;
        }

        private static bool HasSoundAssets(string modPath)
            => AnyFile(Path.Combine(modPath, "sound"), "*.ogg", SearchOption.AllDirectories);

        private static bool HasInitDScripts(string modPath)
            => AnyFile(Path.Combine(modPath, "init.d"), "*.lua", SearchOption.TopDirectoryOnly);

        private static bool HasNativePlugins(string modPath)
        {
            string pluginsDir = Path.Combine(modPath, "plugins");
            return NativePluginExtensions.Any(ext =>
                AnyFile(pluginsDir, $"*{ext}", SearchOption.AllDirectories));
        }

        /// <summary>
        /// Returns whether at least one file matching <paramref name="searchPattern"/> exists under an
        /// existing <paramref name="directory"/>; unreadable folders are treated as empty.
        /// </summary>
        private static bool AnyFile(string directory, string searchPattern, SearchOption searchOption)
        {
            if (!Directory.Exists(directory))
                return false;

            try
            {
                return Directory.EnumerateFiles(directory, searchPattern, searchOption).Any();
            }
            catch
            {
                return false;
            }
        }

        private static (bool HasLua, bool HasDfHackApi) ScanLuaFiles(string modPath)
        {
            bool hasLua = false;
            try
            {
                foreach (string luaFile in Directory.EnumerateFiles(modPath, "*.lua", SearchOption.AllDirectories))
                {
                    hasLua = true;

                    string content;
                    try { content = File.ReadAllText(luaFile); }
                    catch { continue; }

                    if (DfHackLuaApiRegex.IsMatch(content))
                        return (true, true);
                }
            }
            catch
            {
                // Ignore unreadable mod folders.
            }

            return (hasLua, false);
        }

        [GeneratedRegex(@"require\s*\(\s*['""](?:df|dfhack|utils|gui(?:\.\w+)?|plugins)['""]\s*\)|\bdfhack\.\w+|\bdf\.\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex MyRegex();
    }
}
