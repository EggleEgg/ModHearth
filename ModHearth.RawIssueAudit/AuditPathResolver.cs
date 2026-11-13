using System.Text.Json;

namespace ModHearth.RawIssueAudit
{
    internal sealed record AuditPathSet(
        string ErrorLogPath,
        string CachePath,
        string VanillaPath,
        string? ModListPath,
        string? ConfigPath,
        string? DwarfFortressPath,
        string? DfHackPath);

    internal sealed class AuditInputException(string message) : Exception(message);

    /// <summary>
    /// Resolves the three inputs the audit needs (errorlog, raw cache, vanilla raws) plus the
    /// active modlist. Every path can be supplied explicitly; otherwise the local ModHearth config
    /// and build output folders are probed. The tool deliberately never writes config or cache data.
    /// </summary>
    internal static class AuditPathResolver
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static StringComparer PathComparer
            => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public static AuditPathSet Resolve(
            AuditOptions options,
            ICollection<string> warnings,
            bool allowMissingErrorLog = false)
        {
            string? explicitConfig = NormalizePath(options.ConfigPath);
            if (options.ConfigPath != null && explicitConfig == null)
                throw new AuditInputException("--config expects a non-empty path.");

            string? explicitCache = NormalizePath(options.CachePath);
            if (options.CachePath != null && explicitCache == null)
                throw new AuditInputException("--cache expects a non-empty path.");

            string? explicitDfHack = NormalizePath(options.DfHackPath);
            if (options.DfHackPath != null && explicitDfHack == null)
                throw new AuditInputException("--dfhack expects a non-empty path.");

            string? configPath;
            string? configDfPath;
            string? configDfHackPath;
            if (explicitConfig != null)
            {
                if (!File.Exists(explicitConfig))
                    throw new AuditInputException($"Config file not found: {explicitConfig}");

                configPath = explicitConfig;
                (configDfPath, configDfHackPath) = TryReadConfigPaths(explicitConfig, warnings);
            }
            else
            {
                (configPath, configDfPath, configDfHackPath) = ChooseConfig(explicitCache, warnings);
            }

            string cachePath = explicitCache ?? ChooseCache(configPath);
            if (!File.Exists(cachePath))
            {
                throw new AuditInputException(
                    $"Raw dependency cache not found: {cachePath}. Run ModHearth once so it scans the installed mods, " +
                    "or pass --cache <path>.");
            }

            string? dfPath = NormalizePath(options.DwarfFortressPath);
            if (dfPath != null && !Directory.Exists(dfPath))
                warnings.Add($"Dwarf Fortress folder does not exist: {dfPath}");

            dfPath ??= configDfPath;

            if (dfPath == null && options.ErrorLogPath != null)
            {
                string? logDirectory = Path.GetDirectoryName(NormalizePath(options.ErrorLogPath));
                if (!string.IsNullOrWhiteSpace(logDirectory))
                    dfPath = logDirectory;
            }

            string? errorLogPath = NormalizePath(options.ErrorLogPath);
            if (errorLogPath == null && dfPath != null)
                errorLogPath = Path.Combine(dfPath, "errorlog.txt");

            if (errorLogPath == null)
            {
                throw new AuditInputException(
                    "Could not locate Dwarf Fortress. Pass --df <Dwarf Fortress folder> or --errorlog <path>.");
            }

            if (Directory.Exists(errorLogPath))
                throw new AuditInputException($"Dwarf Fortress errorlog path is a directory: {errorLogPath}");

            // Automation deletes the log before driving worldgen, so a missing file is expected.
            // Every other mode still fails fast instead of silently auditing an empty result.
            if (!File.Exists(errorLogPath) && !allowMissingErrorLog)
                throw new AuditInputException($"Dwarf Fortress errorlog not found: {errorLogPath}");

            string? dfHackPath = explicitDfHack ?? configDfHackPath;
            if (dfHackPath != null && !Directory.Exists(dfHackPath))
                warnings.Add($"DFHack folder does not exist: {dfHackPath}");

            string? vanillaPath = NormalizePath(options.VanillaPath);
            if (vanillaPath == null && dfPath != null)
                vanillaPath = Path.Combine(dfPath, "data", "vanilla");

            if (vanillaPath == null)
            {
                throw new AuditInputException(
                    "Could not locate the vanilla raws. Pass --vanilla <Dwarf Fortress/data/vanilla> or --df <folder>.");
            }

            if (!Directory.Exists(vanillaPath))
                throw new AuditInputException($"Vanilla raw folder not found: {vanillaPath}");

            string? modListPath = NormalizePath(options.ModListPath);
            if (modListPath != null && !File.Exists(modListPath))
                throw new AuditInputException($"Mod list not found: {modListPath}");

            if (modListPath == null && dfPath != null)
            {
                string autoModList = Path.Combine(dfPath, "dfhack-config", "mod-manager.json");
                if (File.Exists(autoModList))
                {
                    modListPath = autoModList;
                }
                else if (!options.AllMods)
                {
                    warnings.Add(
                        $"Mod list not found at '{autoModList}'; treating every cached mod as active. " +
                        "Pass --modlist <path> or --all-mods to make this explicit.");
                }
            }

            return new AuditPathSet(
                errorLogPath,
                cachePath,
                vanillaPath,
                modListPath,
                configPath,
                dfPath,
                dfHackPath);
        }

        private static (string? ConfigPath, string? DfPath, string? DfHackPath) ChooseConfig(
            string? explicitCache,
            ICollection<string> warnings)
        {
            List<string> candidates = [.. GetAppDirectories()
                .Select(directory => Path.Combine(directory, "config.json"))
                .Where(File.Exists)
                .Distinct(PathComparer)];

            if (candidates.Count == 0)
                return (null, null, null);

            if (explicitCache != null)
            {
                string? preferredDirectory = GetAppDirectoryForCache(explicitCache);
                if (preferredDirectory != null)
                {
                    string preferred = Path.Combine(preferredDirectory, "config.json");
                    if (File.Exists(preferred))
                    {
                        _ = candidates.RemoveAll(path => PathComparer.Equals(path, preferred));
                        candidates.Insert(0, preferred);
                    }
                }
            }

            // Prefer a config that actually names a Dwarf Fortress folder; a repository template or
            // a half-initialized config must not hide the real app config in another build folder.
            foreach (string candidate in candidates)
            {
                (string? dfPath, string? dfHackPath) = TryReadConfigPaths(candidate, warnings);
                if (dfPath is { Length: > 0 })
                    return (candidate, dfPath, dfHackPath);
            }

            (string? fallbackDfPath, string? fallbackDfHackPath) =
                TryReadConfigPaths(candidates[0], warnings);
            return (candidates[0], fallbackDfPath, fallbackDfHackPath);
        }

        private static string ChooseCache(string? configPath)
        {
            string? configDirectory = configPath == null ? null : Path.GetDirectoryName(configPath);
            if (configDirectory != null)
            {
                string preferred = Path.Combine(configDirectory, "metadata", "mod_raw_dependency_cache.json");
                if (File.Exists(preferred))
                    return preferred;
            }

            List<string> candidates = [.. GetAppDirectories()
                .Select(directory => Path.Combine(directory, "metadata", "mod_raw_dependency_cache.json"))];

            return candidates
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
                ?? candidates[0];
        }

        private static string? GetAppDirectoryForCache(string cachePath)
        {
            string? metadataDirectory = Path.GetDirectoryName(cachePath);
            if (metadataDirectory == null)
                return null;

            return string.Equals(Path.GetFileName(metadataDirectory), "metadata", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(metadataDirectory)
                : metadataDirectory;
        }

        private static List<string> GetAppDirectories()
        {
            HashSet<string> seen = new(PathComparer);
            List<string> directories = [];

            void Add(string? directory)
            {
                if (string.IsNullOrWhiteSpace(directory))
                    return;

                string normalized = Path.GetFullPath(directory);
                if (seen.Add(normalized))
                    directories.Add(normalized);
            }

            Add(Environment.CurrentDirectory);
            Add(AppContext.BaseDirectory);

            string? repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot != null)
            {
                string netVersion = "net10.0";
                Add(repositoryRoot);
                Add(Path.Combine(repositoryRoot, "bin", "Debug", netVersion));
                Add(Path.Combine(repositoryRoot, "bin", "Release", netVersion));
                Add(Path.Combine(repositoryRoot, "bin", "Debug", netVersion, "win-x64"));
                Add(Path.Combine(repositoryRoot, "bin", "Release", netVersion, "win-x64"));
            }

            return directories;
        }

        private static string? FindRepositoryRoot()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                DirectoryInfo? directory = new(start);
                while (directory != null)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "ModHearth.sln"))
                        || File.Exists(Path.Combine(directory.FullName, "ModHearth.csproj")))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            return null;
        }

        private static (string? DfPath, string? DfHackPath) TryReadConfigPaths(
            string configPath,
            ICollection<string> warnings)
        {
            try
            {
                ConfigFile? config = JsonSerializer.Deserialize<ConfigFile>(
                    File.ReadAllText(configPath),
                    JsonOptions);

                string? dfPath = NormalizePath(
                    string.IsNullOrWhiteSpace(config?.DFFolderPathOverride)
                        ? config?.DFFolderPath
                        : config.DFFolderPathOverride);

                return (dfPath, NormalizePath(config?.DFHackFolderPath));
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not read config '{configPath}': {ex.Message}");
                return (null, null);
            }
        }

        private static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string trimmed = path.Trim().Trim('"');
            if (trimmed.Length == 0)
                return null;

            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch
            {
                return trimmed;
            }
        }

        private sealed class ConfigFile
        {
            public string DFFolderPath { get; set; } = string.Empty;
            public string DFFolderPathOverride { get; set; } = string.Empty;
            public string DFHackFolderPath { get; set; } = string.Empty;
        }
    }
}
