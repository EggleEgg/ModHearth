using ModHearth.Models;
using ModHearth.Utils.DwarfFortress;
using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Read-only companion to the comparison audit. It inventories the hardcoded-token catalog,
    /// counts how the active mods use those tokens, scans REAGENT/PRODUCT material slots for
    /// references the catalog does not explain, and optionally verifies the catalog against the
    /// installed Dwarf Fortress executable.
    /// </summary>
    internal static class HardcodedReportRunner
    {
        public static HardcodedReport Run(AuditOptions options)
        {
            if (options.AutomateWorldgen)
                throw new AuditInputException("--hardcoded-report cannot be combined with --automate.");

            List<string> warnings = [];
            AuditPathSet paths = AuditPathResolver.Resolve(
                options,
                warnings,
                allowMissingErrorLog: true,
                requireErrorLogPath: false);

            List<ModRawDependencyInfo> cacheEntries = AuditRunner.LoadCache(paths.CachePath, out int schemaVersion);
            AuditRunner.ActiveModSelection selection = AuditRunner.SelectActiveMods(
                options,
                paths.ModListPath,
                cacheEntries,
                warnings);

            if (selection.Entries.Count == 0)
                throw new AuditInputException("No active mods could be resolved from the raw cache.");

            VanillaRawBaseline vanillaBaseline = VanillaRawBaseline.Load(paths.VanillaPath, warnings);
            if (vanillaBaseline.IsEmpty)
            {
                throw new AuditInputException(
                    $"No vanilla object definitions were found under '{paths.VanillaPath}'. " +
                    "Pass --vanilla <Dwarf Fortress/data/vanilla> or --df <Dwarf Fortress folder>.");
            }

            Dictionary<string, AuditRunner.SnapshotEntry> snapshot = AuditRunner.LoadSnapshot(
                paths.CachePath,
                warnings);

            HardcodedMaterialScanResult materialScan = HardcodedMaterialScanner.Scan(
                selection.Entries,
                snapshot,
                vanillaBaseline,
                warnings);

            List<HardcodedTokenUsage> usage = BuildUsage(
                selection.Entries,
                materialScan.Usage,
                vanillaBaseline);
            List<HardcodedInvalidContext> invalidContexts = BuildInvalidContexts(selection.Entries);
            List<HardcodedCatalogCheck> vanillaChecks = BuildVanillaChecks(
                vanillaBaseline,
                paths.VanillaPath,
                warnings);

            HardcodedVerificationResult? verification = null;
            string? executablePath = null;
            if (options.VerifyHardcoded)
            {
                executablePath = ResolveExecutable(paths);
                verification = HardcodedDriftVerifier.Verify(
                    executablePath,
                    vanillaBaseline.IdsOf("REACTION"),
                    vanillaBaseline.CutIgnoredDyeReactions);
            }

            HardcodedReportInputs inputs = new(
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
                paths.DwarfFortressPath,
                executablePath,
                materialScan.ScannedModCount,
                materialScan.ScannedFileCount);

            IReadOnlyList<string> derivedDyeTokens = [.. vanillaBaseline.CutIgnoredDyeReactions
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)];
            List<HardcodedCatalogGroupSummary> catalog = [.. DwarfFortressHardcodedTokens.Groups
                .Select(group => new HardcodedCatalogGroupSummary(
                    group.Id,
                    group.Domain,
                    group.Context,
                    group.Behavior,
                    group.Verification,
                    group.Description,
                    group.Source,
                    DwarfFortressHardcodedTokens.VerifiedAgainst,
                    group.Id == HardcodedTokenGroupId.CutImmuneDyeReactions
                        ? derivedDyeTokens
                        : group.Tokens))];

            return new HardcodedReport(
                inputs,
                catalog,
                usage,
                materialScan.UnresolvedMaterials,
                invalidContexts,
                vanillaChecks,
                materialScan.UnclassifiedMaterials,
                warnings,
                verification);
        }

        private static List<HardcodedTokenUsage> BuildUsage(
            IReadOnlyList<ModRawDependencyInfo> activeEntries,
            IReadOnlyList<HardcodedTokenUsage> materialUsage,
            VanillaRawBaseline vanillaBaseline)
        {
            Dictionary<(HardcodedTokenGroupId GroupId, string Token), UsageAccumulator> usage = [];

            void Add(HardcodedTokenGroupId groupId, string token, string modId, int count)
            {
                if (count <= 0 || string.IsNullOrWhiteSpace(token))
                    return;

                string normalized = token.Trim().ToUpperInvariant();
                if (!usage.TryGetValue((groupId, normalized), out UsageAccumulator? accumulator))
                {
                    accumulator = new UsageAccumulator(groupId, normalized);
                    usage[(groupId, normalized)] = accumulator;
                }

                accumulator.Count += count;
                if (!string.IsNullOrWhiteSpace(modId))
                    _ = accumulator.Mods.Add(modId.Trim());
            }

            foreach (HardcodedTokenUsage material in materialUsage)
            {
                if (material.OccurrenceCount <= 0 || string.IsNullOrWhiteSpace(material.Token))
                    continue;

                string normalized = material.Token.Trim().ToUpperInvariant();
                if (!usage.TryGetValue((material.GroupId, normalized), out UsageAccumulator? accumulator))
                {
                    accumulator = new UsageAccumulator(material.GroupId, normalized);
                    usage[(material.GroupId, normalized)] = accumulator;
                }

                accumulator.Count += material.OccurrenceCount;
                accumulator.Mods.UnionWith(material.Mods);
            }

            foreach (ModRawDependencyInfo info in activeEntries)
            {
                foreach (string buildingId in info.ReactionBuildingIds ?? [])
                {
                    if (DwarfFortressHardcodedTokens.IsReactionBuildingId(buildingId))
                        Add(HardcodedTokenGroupId.ReactionBuildings, buildingId, info.ModId, 1);
                }

                foreach (string cutTarget in info.CutTargetIds ?? [])
                {
                    if (!ObjectKey.TryParse(cutTarget, out string objectType, out string id)
                        || !string.Equals(objectType, "REACTION", StringComparison.OrdinalIgnoreCase)
                        || !vanillaBaseline.IsCutIgnoredDyeReaction(id))
                    {
                        continue;
                    }

                    Add(HardcodedTokenGroupId.CutImmuneDyeReactions, id, info.ModId, 1);
                }
            }

            return [.. usage.Values
                .Select(accumulator => new HardcodedTokenUsage(
                    accumulator.GroupId,
                    accumulator.Token,
                    accumulator.Count,
                    [.. accumulator.Mods.OrderBy(mod => mod, StringComparer.OrdinalIgnoreCase)]))
                .OrderBy(entry => entry.GroupId)
                .ThenBy(entry => entry.Token, StringComparer.OrdinalIgnoreCase)];
        }

        private static List<HardcodedInvalidContext> BuildInvalidContexts(
            IReadOnlyList<ModRawDependencyInfo> activeEntries)
        {
            Dictionary<string, HardcodedInvalidContext> contexts = new(StringComparer.OrdinalIgnoreCase);

            foreach (ModRawDependencyInfo info in activeEntries)
            {
                foreach (string buildingId in info.PermittedBuildingIds ?? [])
                {
                    if (!DwarfFortressHardcodedTokens.IsReactionBuildingId(buildingId))
                        continue;

                    string token = buildingId.Trim();
                    string key = info.ModId.ToUpperInvariant() + "\u001f" + token.ToUpperInvariant();
                    _ = contexts.TryAdd(
                        key,
                        new HardcodedInvalidContext(
                            info.ModId,
                            token,
                            "PERMITTED_BUILDING only accepts raw-defined custom buildings; " +
                            "this id is a hardcoded workshop/furnace type."));
                }
            }

            return [.. contexts.Values
                .OrderBy(entry => entry.ModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Token, StringComparer.OrdinalIgnoreCase)];
        }

        internal static List<HardcodedCatalogCheck> BuildVanillaChecks(
            VanillaRawBaseline vanillaBaseline,
            string vanillaPath,
            ICollection<string> warnings)
        {
            List<HardcodedCatalogCheck> checks = [];

            foreach (string token in HardcodedVanillaReactionScanner.CollectReactionBuildingIds(
                vanillaPath,
                warnings))
            {
                if (vanillaBaseline.Contains("BUILDING", token)
                    || DwarfFortressHardcodedTokens.IsReactionBuildingId(token))
                {
                    continue;
                }

                checks.Add(new HardcodedCatalogCheck(
                    HardcodedTokenGroupId.ReactionBuildings,
                    token,
                    HardcodedCatalogCheckStatus.CatalogMissingReactionBuildingUsedByVanilla,
                    "A vanilla reaction uses this BUILDING id, no vanilla raw defines it, and it " +
                    "is not in the built-in catalog. A DF update may have added a hardcoded " +
                    "workshop or furnace; confirm it and add it to the catalog."));
            }

            foreach (string token in DwarfFortressHardcodedTokens.TokensOf(
                HardcodedTokenGroupId.ReactionBuildings))
            {
                if (!vanillaBaseline.Contains("BUILDING", token))
                    continue;

                checks.Add(new HardcodedCatalogCheck(
                    HardcodedTokenGroupId.ReactionBuildings,
                    token,
                    HardcodedCatalogCheckStatus.UnexpectedVanillaDefinition,
                    "A vanilla raw now defines this built-in workshop/furnace id. Check whether DF " +
                    "changed how reaction BUILDING tokens resolve."));
            }

            foreach (string token in DwarfFortressHardcodedTokens.TokensOf(
                HardcodedTokenGroupId.ConcreteBuiltInMaterials))
            {
                if (!vanillaBaseline.Contains("INORGANIC", token))
                    continue;

                checks.Add(new HardcodedCatalogCheck(
                    HardcodedTokenGroupId.ConcreteBuiltInMaterials,
                    token,
                    HardcodedCatalogCheckStatus.UnexpectedVanillaDefinition,
                    "The vanilla raws now define an inorganic with the same id as this built-in " +
                    "material. Confirm the material token still resolves to the built-in."));
            }

            checks.Sort((left, right) =>
            {
                int statusComparison = left.Status.CompareTo(right.Status);
                if (statusComparison != 0)
                    return statusComparison;

                int groupComparison = Nullable.Compare(left.GroupId, right.GroupId);
                return groupComparison != 0
                    ? groupComparison
                    : string.Compare(left.Token, right.Token, StringComparison.OrdinalIgnoreCase);
            });

            return checks;
        }

        private static string ResolveExecutable(AuditPathSet paths)
        {
            if (string.IsNullOrWhiteSpace(paths.DwarfFortressPath))
            {
                throw new AuditInputException(
                    "--verify-hardcoded requires a Dwarf Fortress folder. Pass --df <folder> or " +
                    "run with ModHearth's config.json available.");
            }

            if (!DwarfFortressExecutableLocator.TryResolvePath(paths.DwarfFortressPath, out string executablePath))
            {
                throw new AuditInputException(
                    $"Could not locate the Dwarf Fortress executable under '{paths.DwarfFortressPath}'.");
            }

            return executablePath;
        }

        private sealed class UsageAccumulator(HardcodedTokenGroupId groupId, string token)
        {
            public HardcodedTokenGroupId GroupId { get; } = groupId;
            public string Token { get; } = token;
            public int Count { get; set; }
            public HashSet<string> Mods { get; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
