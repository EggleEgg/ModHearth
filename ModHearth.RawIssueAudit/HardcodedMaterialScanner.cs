using System.Globalization;
using System.Text.RegularExpressions;
using ModHearth.Models;
using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Read-only scan of the active mods' <c>objects/</c> folders for REAGENT/PRODUCT material
    /// fields. The production raw scanner intentionally does not capture materials; this helper is
    /// report-only so the catalog can be evaluated against real mod usage without changing the cache
    /// schema or the runtime analyzer.
    /// </summary>
    internal static partial class HardcodedMaterialScanner
    {
        private const int MaxSampleLocations = 3;
        private const int MaxSampleExamples = 3;

        [GeneratedRegex(@"\[(REAGENT|PRODUCT):([^\]]*)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex MaterialTokenPattern();

        [GeneratedRegex(@"^\s*\[REACTION:([^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ReactionDefinitionPattern();

        public static HardcodedMaterialScanResult Scan(
            IReadOnlyList<ModRawDependencyInfo> activeEntries,
            IReadOnlyDictionary<string, AuditRunner.SnapshotEntry> snapshot,
            VanillaRawBaseline vanillaBaseline,
            ICollection<string> warnings)
        {
            HashSet<string> definedKeys = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModRawDependencyInfo entry in activeEntries)
            {
                foreach (string key in entry.DirectDefinitionIds ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(key))
                        _ = definedKeys.Add(key.Trim());
                }
            }

            Dictionary<(HardcodedTokenGroupId GroupId, string Token), UsageAccumulator> usage = [];
            Dictionary<string, ReferenceAccumulator> references = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, SampleAccumulator> samples = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> scannedPaths = new(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            int scannedModCount = 0;
            int scannedFileCount = 0;

            foreach (ModRawDependencyInfo info in activeEntries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ModId))
                .OrderBy(entry => entry.ModId, StringComparer.OrdinalIgnoreCase))
            {
                if (!snapshot.TryGetValue(info.ModId, out AuditRunner.SnapshotEntry? snapshotEntry)
                    || string.IsNullOrWhiteSpace(snapshotEntry.Path))
                {
                    warnings.Add(
                        $"Active mod '{info.ModId}' has no folder path in mod_folder_snapshot.json; " +
                        "material scan skipped it.");
                    continue;
                }

                string modPath = snapshotEntry.Path;
                string objectsPath = Path.Combine(modPath, "objects");
                if (!Directory.Exists(objectsPath))
                    continue;

                string normalizedObjectsPath;
                try
                {
                    normalizedObjectsPath = Path.GetFullPath(objectsPath);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    warnings.Add($"Could not normalize mod folder '{objectsPath}': {ex.Message}");
                    continue;
                }

                if (!scannedPaths.Add(normalizedObjectsPath))
                    continue;

                List<string> files;
                try
                {
                    files = [.. Directory
                        .EnumerateFiles(objectsPath, "*", SearchOption.AllDirectories)
                        .Where(file => string.Equals(
                            Path.GetExtension(file),
                            ".txt",
                            StringComparison.OrdinalIgnoreCase))];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not list raws under '{objectsPath}': {ex.Message}");
                    continue;
                }

                files.Sort(StringComparer.OrdinalIgnoreCase);
                scannedModCount++;

                foreach (string file in files)
                {
                    scannedFileCount++;
                    ScanFile(
                        file,
                        modPath,
                        info.ModId,
                        definedKeys,
                        vanillaBaseline,
                        usage,
                        references,
                        samples,
                        warnings);
                }
            }

            List<HardcodedTokenUsage> usageList = [.. usage.Values
                .Select(accumulator => new HardcodedTokenUsage(
                    accumulator.GroupId,
                    accumulator.Token,
                    accumulator.Count,
                    [.. accumulator.Mods.OrderBy(mod => mod, StringComparer.OrdinalIgnoreCase)]))
                .OrderBy(entry => entry.GroupId)
                .ThenBy(entry => entry.Token, StringComparer.OrdinalIgnoreCase)];

            List<HardcodedMaterialReference> referenceList = [.. references.Values
                .Select(accumulator => new HardcodedMaterialReference(
                    accumulator.ModId,
                    accumulator.Token,
                    accumulator.Head,
                    accumulator.ObjectId,
                    accumulator.Status,
                    accumulator.Count,
                    [.. accumulator.Locations]))
                .OrderBy(entry => entry.ModId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Status)
                .ThenBy(entry => entry.Token, StringComparer.OrdinalIgnoreCase)];

            List<HardcodedMaterialSample> sampleList = [.. samples.Values
                .Select(accumulator => new HardcodedMaterialSample(
                    accumulator.Head,
                    accumulator.Count,
                    [.. accumulator.Mods.OrderBy(mod => mod, StringComparer.OrdinalIgnoreCase)],
                    [.. accumulator.Examples]))
                .OrderByDescending(entry => entry.OccurrenceCount)
                .ThenBy(entry => entry.Head, StringComparer.OrdinalIgnoreCase)];

            return new HardcodedMaterialScanResult(
                usageList,
                referenceList,
                sampleList,
                scannedModCount,
                scannedFileCount);
        }

        private static void ScanFile(
            string file,
            string modPath,
            string modId,
            IReadOnlySet<string> definedKeys,
            VanillaRawBaseline vanillaBaseline,
            Dictionary<(HardcodedTokenGroupId GroupId, string Token), UsageAccumulator> usage,
            Dictionary<string, ReferenceAccumulator> references,
            Dictionary<string, SampleAccumulator> samples,
            ICollection<string> warnings)
        {
            try
            {
                int lineNumber = 0;
                string? reactionId = null;
                foreach (string line in File.ReadLines(file))
                {
                    lineNumber++;

                    Match reaction = ReactionDefinitionPattern().Match(line);
                    if (reaction.Success)
                        reactionId = reaction.Groups[1].Value.Trim();

                    foreach (Match match in MaterialTokenPattern().Matches(line))
                    {
                        ProcessToken(
                            match.Groups[2].Value,
                            modId,
                            RelativeLocation(modPath, file, lineNumber, reactionId),
                            definedKeys,
                            vanillaBaseline,
                            usage,
                            references,
                            samples);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not read raw file '{file}': {ex.Message}");
            }
        }

        private static void ProcessToken(
            string fieldsText,
            string modId,
            string location,
            IReadOnlySet<string> definedKeys,
            VanillaRawBaseline vanillaBaseline,
            Dictionary<(HardcodedTokenGroupId GroupId, string Token), UsageAccumulator> usage,
            Dictionary<string, ReferenceAccumulator> references,
            Dictionary<string, SampleAccumulator> samples)
        {
            string[] fields = fieldsText.Split(':', StringSplitOptions.None);
            if (fields.Length < 5)
                return;

            string itemType = fields[2].Trim();
            if (itemType.Length == 0
                || int.TryParse(itemType, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                || string.Equals(itemType, "GET_ITEM_DATA_FROM_REAGENT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemType, "FISH_RAW", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string materialField = string.Join(':', fields[4..]).Trim();
            if (materialField.Length == 0)
                return;

            DwarfFortressMaterialToken token = DwarfFortressMaterialToken.Parse(materialField);
            switch (token.Kind)
            {
                case DwarfFortressMaterialTokenKind.Empty:
                case DwarfFortressMaterialTokenKind.None:
                    return;

                case DwarfFortressMaterialTokenKind.BuiltIn:
                    AddUsage(token.Head, modId, usage);
                    return;

                case DwarfFortressMaterialTokenKind.Inorganic:
                case DwarfFortressMaterialTokenKind.Metal:
                    AddUsage(token.Head, modId, usage);
                    if (token.IsInorganicWildcard)
                        return;

                    AddReferenceIfMissing(
                        token,
                        "INORGANIC",
                        HardcodedMaterialReferenceStatus.MissingInorganic,
                        modId,
                        location,
                        definedKeys,
                        vanillaBaseline,
                        references);
                    return;

                case DwarfFortressMaterialTokenKind.Plant:
                    AddUsage(token.Head, modId, usage);
                    AddReferenceIfMissing(
                        token,
                        "PLANT",
                        HardcodedMaterialReferenceStatus.MissingPlant,
                        modId,
                        location,
                        definedKeys,
                        vanillaBaseline,
                        references);
                    return;

                case DwarfFortressMaterialTokenKind.Creature:
                    AddUsage(token.Head, modId, usage);
                    AddReferenceIfMissing(
                        token,
                        "CREATURE",
                        HardcodedMaterialReferenceStatus.MissingCreature,
                        modId,
                        location,
                        definedKeys,
                        vanillaBaseline,
                        references);
                    return;

                case DwarfFortressMaterialTokenKind.LocalPlant:
                case DwarfFortressMaterialTokenKind.LocalCreature:
                case DwarfFortressMaterialTokenKind.FromReagent:
                    // These resolve inside the enclosing raw object or reaction, not against a
                    // top-level object id.
                    AddUsage(token.Head, modId, usage);
                    return;

                default:
                    AddSample(token.Head, token.Raw, location, modId, samples);
                    return;
            }
        }

        private static void AddUsage(
            string token,
            string modId,
            Dictionary<(HardcodedTokenGroupId GroupId, string Token), UsageAccumulator> usage)
        {
            HardcodedTokenGroupId groupId;
            if (DwarfFortressHardcodedTokens.IsConcreteBuiltInMaterial(token))
                groupId = HardcodedTokenGroupId.ConcreteBuiltInMaterials;
            else if (DwarfFortressHardcodedTokens.IsMaterialGrammarToken(token))
                groupId = HardcodedTokenGroupId.MaterialGrammarTokens;
            else
                return;

            string normalized = token.ToUpperInvariant();
            if (!usage.TryGetValue((groupId, normalized), out UsageAccumulator? accumulator))
            {
                accumulator = new UsageAccumulator(groupId, normalized);
                usage[(groupId, normalized)] = accumulator;
            }

            accumulator.Count++;
            _ = accumulator.Mods.Add(modId);
        }

        private static void AddReferenceIfMissing(
            DwarfFortressMaterialToken token,
            string objectType,
            HardcodedMaterialReferenceStatus status,
            string modId,
            string location,
            IReadOnlySet<string> definedKeys,
            VanillaRawBaseline vanillaBaseline,
            Dictionary<string, ReferenceAccumulator> references)
        {
            string? objectId = token.ObjectId;
            if (string.IsNullOrWhiteSpace(objectId))
                return;

            if (vanillaBaseline.Contains(objectType, objectId)
                || definedKeys.Contains(new ObjectKey(objectType, objectId).ToString()))
            {
                return;
            }

            string key = modId.ToUpperInvariant()
                + "\u001f"
                + token.Raw.ToUpperInvariant()
                + "\u001f"
                + ((int)status).ToString(CultureInfo.InvariantCulture);

            if (!references.TryGetValue(key, out ReferenceAccumulator? accumulator))
            {
                accumulator = new ReferenceAccumulator(
                    modId,
                    token.Raw,
                    token.Head,
                    objectId,
                    status);
                references[key] = accumulator;
            }

            accumulator.Count++;
            if (accumulator.Locations.Count < MaxSampleLocations
                && !accumulator.Locations.Contains(location, StringComparer.OrdinalIgnoreCase))
            {
                accumulator.Locations.Add(location);
            }
        }

        private static void AddSample(
            string head,
            string raw,
            string location,
            string modId,
            Dictionary<string, SampleAccumulator> samples)
        {
            string key = string.IsNullOrWhiteSpace(head) ? "(empty)" : head;
            if (!samples.TryGetValue(key, out SampleAccumulator? accumulator))
            {
                accumulator = new SampleAccumulator(key);
                samples[key] = accumulator;
            }

            accumulator.Count++;
            _ = accumulator.Mods.Add(modId);
            if (accumulator.Examples.Count < MaxSampleExamples)
                accumulator.Examples.Add($"{location} => {raw}");
        }

        private static string RelativeLocation(
            string modPath,
            string file,
            int lineNumber,
            string? reactionId)
        {
            string relativePath;
            try
            {
                relativePath = Path.GetRelativePath(modPath, file).Replace('\\', '/');
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                relativePath = file;
            }

            return string.IsNullOrWhiteSpace(reactionId)
                ? $"{relativePath}:{lineNumber}"
                : $"{relativePath}:{lineNumber} (reaction {reactionId})";
        }

        private sealed class UsageAccumulator(HardcodedTokenGroupId groupId, string token)
        {
            public HardcodedTokenGroupId GroupId { get; } = groupId;
            public string Token { get; } = token;
            public int Count { get; set; }
            public HashSet<string> Mods { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ReferenceAccumulator(
            string modId,
            string token,
            string head,
            string objectId,
            HardcodedMaterialReferenceStatus status)
        {
            public string ModId { get; } = modId;
            public string Token { get; } = token;
            public string Head { get; } = head;
            public string ObjectId { get; } = objectId;
            public HardcodedMaterialReferenceStatus Status { get; } = status;
            public int Count { get; set; }
            public List<string> Locations { get; } = [];
        }

        private sealed class SampleAccumulator(string head)
        {
            public string Head { get; } = head;
            public int Count { get; set; }
            public HashSet<string> Mods { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> Examples { get; } = [];
        }
    }

    internal sealed record HardcodedMaterialScanResult(
        IReadOnlyList<HardcodedTokenUsage> Usage,
        IReadOnlyList<HardcodedMaterialReference> UnresolvedMaterials,
        IReadOnlyList<HardcodedMaterialSample> UnclassifiedMaterials,
        int ScannedModCount,
        int ScannedFileCount);
}
