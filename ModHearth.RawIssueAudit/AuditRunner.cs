using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModHearth.Models;
using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Loads the same inputs ModHearth uses at runtime, runs the production analyzers, and hands
    /// their output to <see cref="ReferenceAuditComparer"/>. The runner is read-only: it never
    /// writes config.json, the raw cache, or any metadata file.
    /// </summary>
    internal static class AuditRunner
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static AuditReport Run(AuditOptions options)
        {
            List<string> warnings = [];
            AuditPathSet paths = AuditPathResolver.Resolve(
                options,
                warnings,
                allowMissingErrorLog: options.AutomateWorldgen);

            if (!ModRawDependencyCacheStore.TryLoadEntries(
                paths.CachePath,
                out List<ModRawDependencyInfo>? cacheEntries,
                out int schemaVersion))
            {
                string message = schemaVersion == 0
                    ? $"Raw dependency cache is unreadable: {paths.CachePath}"
                    : $"Raw dependency cache schema {schemaVersion} is not supported " +
                        $"(expected {ModRawDependencyCacheStore.CurrentSchemaVersion}). Run ModHearth once to rebuild it.";
                throw new AuditInputException(message);
            }

            if (cacheEntries.Count == 0)
                throw new AuditInputException($"Raw dependency cache has no mod entries: {paths.CachePath}");

            // When automation runs, DF may rewrite an empty mod-manager.json with the preset it
            // actually loads. Validate the file now, but defer selection until after worldgen so
            // the audit and the fresh errorlog describe the same mod set.
            ActiveModSelection? selection = null;
            if (options.AutomateWorldgen)
            {
                ValidateModListForAutomation(paths.ModListPath, warnings);
            }
            else
            {
                selection = SelectActiveMods(options, paths.ModListPath, cacheEntries, warnings);
            }

            VanillaRawBaseline vanillaBaseline = VanillaRawBaseline.Load(paths.VanillaPath);
            if (vanillaBaseline.IsEmpty)
            {
                throw new AuditInputException(
                    $"No vanilla object definitions were found under '{paths.VanillaPath}'. " +
                    "Pass --vanilla <Dwarf Fortress/data/vanilla> or --df <Dwarf Fortress folder>.");
            }

            // Automation is deliberately late: every non-log input is validated first, so an
            // invalid cache or modlist cannot clear errorlog.txt or launch Dwarf Fortress.
            if (options.AutomateWorldgen)
            {
                WorldgenAutomationRunner.Run(options, paths, warnings);
                selection = SelectActiveMods(options, paths.ModListPath, cacheEntries, warnings);
            }

            if (selection == null || selection.Entries.Count == 0)
                throw new AuditInputException("No active mods could be resolved from the raw cache.");

            HashSet<string> activeModIds = new(
                selection.Entries.Select(entry => entry.ModId),
                StringComparer.OrdinalIgnoreCase);

            List<ModPossibleIssue> predictions = ModPossibleIssueAnalyzer.Analyze(
                selection.Entries,
                activeModIds,
                vanillaBaseline);

            RawReferenceIndex rawReferences = RawReferenceIndex.Build(cacheEntries, activeModIds);

            Dictionary<string, SnapshotEntry> snapshot = LoadSnapshot(paths.CachePath, warnings);
            List<ModReference> modReferences = [.. selection.Entries.Select(info =>
            {
                _ = snapshot.TryGetValue(info.ModId, out SnapshotEntry? entry);
                return new ModReference
                {
                    ID = info.ModId,
                    Name = string.IsNullOrWhiteSpace(entry?.ModName) ? info.ModId : entry.ModName,
                    Path = entry?.Path ?? string.Empty,
                    NumericVersion = info.NumericVersion
                };
            })];

            ModErrorLogAnalysis errorLog = ModErrorLogAnalyzer.Analyze(
                paths.ErrorLogPath,
                modReferences,
                selection.Entries);

            AuditComparison comparison = ReferenceAuditComparer.Compare(
                predictions,
                errorLog,
                rawReferences,
                vanillaBaseline,
                warnings);

            if (errorLog.ConsideredLineCount == 0 && predictions.Count > 0)
            {
                warnings.Add(options.AutomateWorldgen
                    ? "errorlog.txt contained no parseable error lines after automation. The active " +
                        "mod set may produce no diagnostics, or Dwarf Fortress wrote them before the log was cleared."
                    : "errorlog.txt contained no parseable error lines. Delete it, launch Dwarf Fortress once " +
                        "with the active modlist, then run the audit again.");
            }

            AuditInputSummary inputs = new(
                paths.ErrorLogPath,
                errorLog.LastWriteUtc,
                paths.CachePath,
                File.GetLastWriteTimeUtc(paths.CachePath),
                schemaVersion,
                cacheEntries.Count,
                cacheEntries
                    .Select(entry => entry.ModId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                paths.VanillaPath,
                vanillaBaseline.Count,
                paths.ModListPath,
                selection.ModListName,
                selection.Source,
                selection.Entries.Count,
                paths.ConfigPath,
                paths.DwarfFortressPath);

            return new AuditReport(
                inputs,
                comparison.Summary,
                comparison.Mismatches,
                comparison.Matches,
                comparison.UnattributedReferences,
                comparison.UnverifiablePredictions,
                comparison.UnmappedErrorLogCategories,
                warnings);
        }

        /// <summary>
        /// Parses the DFHack mod list without resolving mods. Automation uses this before it
        /// touches the errorlog or launches DF: an unreadable file is fatal, but an empty array is
        /// allowed because DF falls back to its built-in preset and the audit re-reads the file
        /// once worldgen has finished.
        /// </summary>
        private static void ValidateModListForAutomation(string? modListPath, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(modListPath))
            {
                throw new AuditInputException(
                    "--automate requires DFHack's mod-manager.json so the audit can compare the same " +
                    "mod set Dwarf Fortress loads. Pass --modlist <path>, or run Dwarf Fortress once " +
                    "with DFHack installed.");
            }

            List<ModPackFile> packs = ReadModPacks(modListPath);
            if (packs.Count == 0)
            {
                warnings.Add(
                    $"Mod list '{modListPath}' contains no mod packs; Dwarf Fortress will fall back " +
                    "to its default preset. The audit re-reads this file after worldgen.");
                return;
            }

            if (packs.All(pack => pack.Modlist is not { Count: > 0 }))
            {
                throw new AuditInputException(
                    $"Mod list '{modListPath}' does not contain a usable modpack. " +
                    "Select a modpack in ModHearth before running --automate.");
            }
        }

        private static List<ModPackFile> ReadModPacks(string modListPath)
        {
            try
            {
                return JsonSerializer.Deserialize<List<ModPackFile>>(
                    File.ReadAllText(modListPath),
                    JsonOptions) ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                throw new AuditInputException($"Could not read mod list '{modListPath}': {ex.Message}");
            }
        }

        private static ActiveModSelection SelectActiveMods(
            AuditOptions options,
            string? modListPath,
            List<ModRawDependencyInfo> cacheEntries,
            List<string> warnings)
        {
            if (options.AllMods || string.IsNullOrWhiteSpace(modListPath))
            {
                string source = options.AllMods
                    ? "all cached mods (--all-mods)"
                    : "all cached mods (mod list unavailable)";
                List<ModRawDependencyInfo> allEntries = SelectOnePerMod(cacheEntries, warnings);
                return new ActiveModSelection(allEntries, source, null);
            }

            List<ModPackFile> packs = ReadModPacks(modListPath);

            if (packs.Count == 0)
            {
                // DFHack writes an empty array when no preset is selected. DF then falls back to
                // its built-in vanilla preset, so assuming "all cached mods" would compare the
                // wrong set against the log. The caller can still pass --all-mods explicitly.
                throw new AuditInputException(options.AutomateWorldgen
                    ? $"Mod list '{modListPath}' still contains no mod packs after worldgen. " +
                        "Select a modpack in ModHearth and retry."
                    : $"Mod list '{modListPath}' contains no mod packs. Select a modpack in ModHearth, " +
                        "or pass --all-mods to audit every cached mod.");
            }

            ModPackFile? selectedPack = packs.FirstOrDefault(pack => pack.Default)
                ?? packs.FirstOrDefault(pack => pack.Modlist is { Count: > 0 })
                ?? packs.FirstOrDefault();

            if (selectedPack?.Modlist == null || selectedPack.Modlist.Count == 0)
            {
                throw new AuditInputException(
                    $"Mod list '{modListPath}' does not contain a usable default modpack. " +
                    "Select a modpack in ModHearth, or pass --all-mods to audit every cached mod.");
            }

            Dictionary<string, List<ModRawDependencyInfo>> cacheById = cacheEntries
                .GroupBy(entry => entry.ModId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.OrdinalIgnoreCase);

            List<ModRawDependencyInfo> selectedEntries = [];
            HashSet<string> seenModIds = new(StringComparer.OrdinalIgnoreCase);

            foreach (ModListEntry mod in selectedPack.Modlist)
            {
                string modId = mod.Id?.Trim() ?? string.Empty;
                if (modId.Length == 0 || !seenModIds.Add(modId))
                    continue;

                if (!cacheById.TryGetValue(modId, out List<ModRawDependencyInfo>? candidates)
                    || candidates.Count == 0)
                {
                    warnings.Add(
                        $"Active mod '{modId}' is missing from the raw cache; run ModHearth once to rescan it.");
                    continue;
                }

                string activeVersion = NormalizeVersion(
                    mod.Version.ToString(CultureInfo.InvariantCulture));
                ModRawDependencyInfo? exact = candidates.FirstOrDefault(candidate =>
                    string.Equals(
                        NormalizeVersion(candidate.NumericVersion),
                        activeVersion,
                        StringComparison.OrdinalIgnoreCase));

                if (exact != null)
                {
                    selectedEntries.Add(exact);
                    if (candidates.Count > 1)
                    {
                        warnings.Add(
                            $"Raw cache contains {candidates.Count} versions of '{modId}'; " +
                            $"using the active version ({mod.Version}).");
                    }

                    continue;
                }

                ModRawDependencyInfo fallback = SelectLatest(candidates, warnings);
                warnings.Add(
                    $"Active mod '{modId}' version {mod.Version} is not in the raw cache " +
                    $"(cached: {FormatVersions(candidates)}); using cached version '{fallback.NumericVersion}'.");
                selectedEntries.Add(fallback);
            }

            string packName = string.IsNullOrWhiteSpace(selectedPack.Name) ? "default" : selectedPack.Name;
            return new ActiveModSelection(selectedEntries, $"mod list '{packName}'", packName);
        }

        private static List<ModRawDependencyInfo> SelectOnePerMod(
            IEnumerable<ModRawDependencyInfo> cacheEntries,
            List<string> warnings)
        {
            List<ModRawDependencyInfo> selected = [];
            foreach (IGrouping<string, ModRawDependencyInfo> group in cacheEntries
                .GroupBy(entry => entry.ModId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                selected.Add(SelectLatest([.. group], warnings));
            }

            return selected;
        }

        private static ModRawDependencyInfo SelectLatest(
            List<ModRawDependencyInfo> candidates,
            List<string> warnings)
        {
            if (candidates.Count == 1)
                return candidates[0];

            string modId = candidates[0].ModId;
            warnings.Add(
                $"Raw cache contains {candidates.Count} versions of '{modId}'; " +
                $"using the newest cached version ({FormatVersions(candidates)}).");

            return candidates
                .OrderByDescending(candidate => VersionNumber(candidate.NumericVersion))
                .ThenByDescending(candidate => candidate.NumericVersion, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(candidate => candidate.ObjectsFolderStampTicks)
                .First();
        }

        private static string FormatVersions(IEnumerable<ModRawDependencyInfo> candidates)
            => string.Join(
                ", ",
                candidates
                    .Select(candidate => string.IsNullOrWhiteSpace(candidate.NumericVersion)
                        ? "(none)"
                        : candidate.NumericVersion)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(version => version, StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// DFHack's mod-manager stores an integer version, while info.txt may expose a dotted or
        /// zero-padded value ("0.96", "0579"). Comparing the digit runs numerically keeps the
        /// cache row and the active modlist entry aligned without silently accepting an unrelated
        /// version.
        /// </summary>
        private static string NormalizeVersion(string? version)
        {
            string trimmed = (version ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            string digits = new([.. trimmed.Where(char.IsDigit)]);
            if (digits.Length > 0
                && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long numeric))
            {
                return numeric.ToString(CultureInfo.InvariantCulture);
            }

            return trimmed.ToUpperInvariant();
        }

        private static long VersionNumber(string? version)
        {
            string normalized = NormalizeVersion(version);
            return long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out long numeric)
                ? numeric
                : -1;
        }

        private static Dictionary<string, SnapshotEntry> LoadSnapshot(
            string cachePath,
            List<string> warnings)
        {
            string? cacheDirectory = Path.GetDirectoryName(cachePath);
            if (string.IsNullOrWhiteSpace(cacheDirectory))
                return new Dictionary<string, SnapshotEntry>(StringComparer.OrdinalIgnoreCase);

            string snapshotPath = Path.Combine(cacheDirectory, "mod_folder_snapshot.json");
            if (!File.Exists(snapshotPath))
                return new Dictionary<string, SnapshotEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                List<SnapshotEntry>? entries = JsonSerializer.Deserialize<List<SnapshotEntry>>(
                    File.ReadAllText(snapshotPath),
                    JsonOptions);

                Dictionary<string, SnapshotEntry> snapshot = new(StringComparer.OrdinalIgnoreCase);
                foreach (SnapshotEntry entry in entries ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(entry.ModId))
                        _ = snapshot.TryAdd(entry.ModId.Trim(), entry);
                }

                return snapshot;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not read mod folder snapshot '{snapshotPath}': {ex.Message}");
                return new Dictionary<string, SnapshotEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private sealed record ActiveModSelection(
            IReadOnlyList<ModRawDependencyInfo> Entries,
            string Source,
            string? ModListName);

        private sealed class ModPackFile
        {
            [JsonPropertyName("default")]
            public bool Default { get; set; }

            [JsonPropertyName("modlist")]
            public List<ModListEntry>? Modlist { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }

        private sealed class ModListEntry
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("version")]
            public int Version { get; set; }
        }

        private sealed class SnapshotEntry
        {
            public string ModId { get; set; } = string.Empty;
            public string ModName { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
        }
    }
}
