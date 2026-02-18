using System.Text.Json;
using ModHearth;

namespace ModHearth.Cli
{
    public static class CliApp
    {
        private const string CommandListPacks = "list-packs";
        private const string CommandListMods = "list-mods";
        private const string CommandSetDefault = "set-default";
        private const string CommandHelp = "help";

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args == null || args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
            {
                WriteHelp(output);
                return 0;
            }

            if (HasFlag(args, "--version") || HasFlag(args, "-v"))
            {
                output.WriteLine($"ModHearth CLI {VersionInfo.GetVersionString()}");
                return 0;
            }

            string command = args[0];
            switch (command)
            {
                case CommandHelp:
                    WriteHelp(output);
                    return 0;
                case CommandListPacks:
                    return ListPacks(args, output, error);
                case CommandListMods:
                    return ListMods(args, output, error);
                case CommandSetDefault:
                    return SetDefault(args, output, error);
                default:
                    error.WriteLine($"Unknown command: {command}");
                    WriteHelp(error);
                    return 2;
            }
        }

        private static int ListPacks(string[] args, TextWriter output, TextWriter error)
        {
            if (!TryResolveModManagerPath(args, error, out string modManagerPath))
                return 2;

            if (!ModpackFile.TryLoad(modManagerPath, error, out List<DFHModpack> packs))
                return 3;

            foreach (DFHModpack pack in packs)
            {
                string prefix = pack.@default ? "*" : " ";
                int count = pack.modlist?.Count ?? 0;
                output.WriteLine($"{prefix} {pack.name} [{count}]");
            }

            return 0;
        }

        private static int ListMods(string[] args, TextWriter output, TextWriter error)
        {
            if (!TryResolveModManagerPath(args, error, out string modManagerPath))
                return 2;

            string packName = GetOption(args, "--pack");
            if (string.IsNullOrWhiteSpace(packName))
            {
                error.WriteLine("Missing --pack <name>.");
                return 2;
            }

            if (!ModpackFile.TryLoad(modManagerPath, error, out List<DFHModpack> packs))
                return 3;

            DFHModpack pack = FindPack(packs, packName);
            if (pack == null)
            {
                error.WriteLine($"Pack not found: {packName}");
                return 4;
            }

            if (pack.modlist == null || pack.modlist.Count == 0)
            {
                output.WriteLine("(empty)");
                return 0;
            }

            foreach (DFHMod mod in pack.modlist)
            {
                output.WriteLine($"{mod.id}|{mod.version}");
            }

            return 0;
        }

        private static int SetDefault(string[] args, TextWriter output, TextWriter error)
        {
            if (!TryResolveModManagerPath(args, error, out string modManagerPath))
                return 2;

            string packName = GetOption(args, "--pack");
            if (string.IsNullOrWhiteSpace(packName))
            {
                error.WriteLine("Missing --pack <name>.");
                return 2;
            }

            if (!ModpackFile.TryLoad(modManagerPath, error, out List<DFHModpack> packs))
                return 3;

            DFHModpack pack = FindPack(packs, packName);
            if (pack == null)
            {
                error.WriteLine($"Pack not found: {packName}");
                return 4;
            }

            foreach (DFHModpack entry in packs)
                entry.@default = false;

            pack.@default = true;

            if (!ModpackFile.TrySave(modManagerPath, packs, error))
                return 5;

            output.WriteLine($"Default pack set to: {pack.name}");
            return 0;
        }

        private static DFHModpack FindPack(List<DFHModpack> packs, string name)
        {
            return packs.FirstOrDefault(p =>
                p.name != null &&
                p.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static void WriteHelp(TextWriter output)
        {
            output.WriteLine("ModHearth CLI");
            output.WriteLine("Usage:");
            output.WriteLine("  modhearth-cli --version");
            output.WriteLine("  modhearth-cli list-packs --mod-manager <path>");
            output.WriteLine("  modhearth-cli list-packs --df-folder <path>");
            output.WriteLine("  modhearth-cli list-mods --mod-manager <path> --pack <name>");
            output.WriteLine("  modhearth-cli set-default --mod-manager <path> --pack <name>");
            output.WriteLine("  modhearth-cli help");
            output.WriteLine();
            output.WriteLine("Options:");
            output.WriteLine("  --mod-manager <path>  Path to dfhack-config/mod-manager.json");
            output.WriteLine("  --df-folder <path>    Dwarf Fortress install directory");
            output.WriteLine("  --config <path>       Optional ModHearth config.json");
            output.WriteLine("  --pack <name>         Modpack name");
        }

        private static bool TryResolveModManagerPath(string[] args, TextWriter error, out string modManagerPath)
        {
            modManagerPath = GetOption(args, "--mod-manager");
            if (!string.IsNullOrWhiteSpace(modManagerPath))
            {
                modManagerPath = Path.GetFullPath(modManagerPath);
                if (!File.Exists(modManagerPath))
                {
                    error.WriteLine($"mod-manager.json not found: {modManagerPath}");
                    return false;
                }
                return true;
            }

            string dfFolder = GetOption(args, "--df-folder");
            if (string.IsNullOrWhiteSpace(dfFolder))
            {
                string configPath = GetOption(args, "--config");
                if (!string.IsNullOrWhiteSpace(configPath))
                {
                    dfFolder = ConfigLoader.TryGetDfFolder(configPath, error);
                }
            }

            if (string.IsNullOrWhiteSpace(dfFolder))
            {
                error.WriteLine("Missing --mod-manager or --df-folder (or --config).");
                return false;
            }

            modManagerPath = Path.GetFullPath(Path.Combine(dfFolder, "dfhack-config", "mod-manager.json"));
            if (!File.Exists(modManagerPath))
            {
                error.WriteLine($"mod-manager.json not found: {modManagerPath}");
                return false;
            }

            return true;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static class ConfigLoader
        {
            public static string TryGetDfFolder(string configPath, TextWriter error)
            {
                if (string.IsNullOrWhiteSpace(configPath))
                    return null;

                string fullPath = Path.GetFullPath(configPath);
                if (!File.Exists(fullPath))
                {
                    error.WriteLine($"Config not found: {fullPath}");
                    return null;
                }

                try
                {
                    string json = File.ReadAllText(fullPath);
                    ModHearthConfig config = JsonSerializer.Deserialize<ModHearthConfig>(json);
                    if (config == null)
                        return null;

                    if (!string.IsNullOrWhiteSpace(config.DFFolderPath))
                        return config.DFFolderPath;

                    if (!string.IsNullOrWhiteSpace(config.DFEXEPath))
                        return Path.GetDirectoryName(config.DFEXEPath);
                }
                catch (Exception ex)
                {
                    error.WriteLine($"Failed to read config: {ex.Message}");
                }

                return null;
            }
        }

        private sealed class ModHearthConfig
        {
            public string DFEXEPath { get; set; }
            public string DFFolderPath { get; set; }
        }

        private static class ModpackFile
        {
            public static bool TryLoad(string path, TextWriter error, out List<DFHModpack> packs)
            {
                packs = null;
                if (!File.Exists(path))
                {
                    error.WriteLine($"mod-manager.json not found: {path}");
                    return false;
                }

                try
                {
                    string json = File.ReadAllText(path);
                    packs = JsonSerializer.Deserialize<List<DFHModpack>>(json);
                    if (packs == null)
                        packs = new List<DFHModpack>();
                    return true;
                }
                catch (Exception ex)
                {
                    error.WriteLine($"Failed to read mod-manager.json: {ex.Message}");
                    return false;
                }
            }

            public static bool TrySave(string path, List<DFHModpack> packs, TextWriter error)
            {
                try
                {
                    JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(packs, options);
                    File.WriteAllText(path, json);
                    return true;
                }
                catch (Exception ex)
                {
                    error.WriteLine($"Failed to write mod-manager.json: {ex.Message}");
                    return false;
                }
            }
        }

        private static class VersionInfo
        {
            public static string GetVersionString()
            {
                string runNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
                if (!string.IsNullOrWhiteSpace(runNumber))
                    return runNumber;

                string infoVersion = typeof(CliApp).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()
                    ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(infoVersion))
                {
                    int plusIndex = infoVersion.IndexOf('+');
                    if (plusIndex > 0)
                        infoVersion = infoVersion.Substring(0, plusIndex);
                    if (!string.IsNullOrWhiteSpace(infoVersion))
                        return infoVersion;
                }

                return "dev";
            }
        }
    }
}
