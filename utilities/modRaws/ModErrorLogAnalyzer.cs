using System.Text;
using System.Text.RegularExpressions;
using ModHearth.Models;

namespace ModHearth.Utilities.ModRaws
{
    /// <summary>
    /// One aggregated class of Dwarf Fortress errorlog messages attributed to a mod. Values such as
    /// the exact creature/reaction ID are kept as context instead of being part of the grouping key,
    /// so a log that repeats "STANDARD_BIPED_GAITS" for 40 creatures still produces one readable issue.
    /// </summary>
    internal sealed class ModErrorLogIssue(
        string modId,
        string category,
        string detail,
        int count,
        IEnumerable<string> objects,
        IEnumerable<string> sourceFiles)
    {
        public string ModId { get; } = modId;
        public string Category { get; } = category;
        public string Detail { get; } = detail;
        public int Count { get; } = count;
        public IReadOnlyList<string> Objects { get; } = [.. objects];
        public IReadOnlyList<string> SourceFiles { get; } = [.. sourceFiles];

        /// <summary>
        /// Compact single-line text for a tooltip. Object/file lists are deliberately capped: a mod
        /// with thousands of affected raws must not turn the problem tooltip into a wall of text.
        /// </summary>
        public string ToDisplayString(int maxContextEntries = 3)
        {
            StringBuilder builder = new(Category);
            if (!string.IsNullOrWhiteSpace(Detail))
                _ = builder.Append(": ").Append(Truncate(Detail, 80));

            List<string> context = Objects.Count > 0
                ? [.. Objects]
                : [.. SourceFiles.Select(name => Path.GetFileName(name)).Where(name => !string.IsNullOrWhiteSpace(name))];
            if (context.Count > 0)
            {
                _ = builder.Append(" (");
                _ = builder.Append(string.Join(", ", context.Take(maxContextEntries)));
                if (context.Count > maxContextEntries)
                    _ = builder.Append(", +").Append(context.Count - maxContextEntries).Append(" more");
                _ = builder.Append("; x").Append(Count).Append(')');
            }
            else if (Count > 1)
            {
                _ = builder.Append(" (x").Append(Count).Append(')');
            }

            return builder.ToString();
        }

        private static string Truncate(string value, int maxLength)
            => value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    internal sealed class ModErrorLogAnalysis(
        IReadOnlyList<ModErrorLogIssue> issues,
        int consideredLineCount,
        int attributedLineCount,
        int unattributedLineCount,
        DateTime lastWriteUtc)
    {
        public IReadOnlyList<ModErrorLogIssue> Issues { get; } = issues;
        public int ConsideredLineCount { get; } = consideredLineCount;
        public int AttributedLineCount { get; } = attributedLineCount;
        public int UnattributedLineCount { get; } = unattributedLineCount;
        public DateTime LastWriteUtc { get; } = lastWriteUtc;
    }

    /// <summary>
    /// Reads Dwarf Fortress's errorlog.txt and attributes its errors to installed mods.
    ///
    /// The log has three useful kinds of context:
    ///  1. "*** Error(s) found in the file ..." sets a source file. Graphics errors usually carry a
    ///     full path, which can be mapped straight back to a mod folder (including installed_mods).
    ///  2. "*** Error(s) finalizing the entity/reaction/creature ..." sets the object being built.
    ///     Subsequent headerless token errors belong to that object.
    ///  3. Object-prefixed lines such as "WILD_ELF:Unrecognized Creature Variation: ..." name their
    ///     object directly, even when the file header is stale.
    ///
    /// Object IDs are resolved through the same raw scan ModSort uses (direct definitions first,
    /// then SELECT/CUT patches). File paths are only trusted for inherently file-bound errors
    /// (graphics/tile-page/caste-graphics) and only when absolute; bare filenames are intentionally
    /// not matched because DF commonly emits them after loading a *different* file. Generic
    /// headerless tokens never fall back to the current file header, because DF leaves that header
    /// in place while finalizing unrelated objects (which would attribute vanilla errors to the
    /// last loaded mod).
    /// </summary>
    internal static partial class ModErrorLogAnalyzer
    {
        private const int MaxTrackedObjectsPerIssue = 24;
        private const int MaxTrackedFilesPerIssue = 8;

        public static ModErrorLogAnalysis Analyze(
            string errorLogPath,
            IReadOnlyCollection<ModReference> mods,
            IEnumerable<ModRawDependencyInfo> rawInfos)
        {
            ModLookup lookup = ModLookup.Build(mods, rawInfos);
            Dictionary<string, IssueBuilder> builders = new(StringComparer.OrdinalIgnoreCase);

            int consideredLines = 0;
            int attributedLines = 0;
            int unattributedLines = 0;

            string? currentFile = null;
            ObjectHint? currentObject = null;

            foreach (string rawLine in File.ReadLines(errorLogPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("***", StringComparison.Ordinal))
                {
                    if (TryParseFileHeader(line, out string? file))
                    {
                        currentFile = file;
                        currentObject = null;
                    }
                    else if (TryParseFinalizingObject(line, out ObjectHint? finalizingObject))
                    {
                        currentObject = finalizingObject;
                    }

                    // Other structural lines (most notably "Duplicate Object") are handled by the
                    // dedicated duplicate-raw pipeline and must not be classified as runtime errors.
                    continue;
                }

                ParsedLine parsed = Classify(line, currentObject);
                if (parsed.Skip)
                    continue;

                consideredLines++;

                IReadOnlyCollection<string> modIds = lookup.Resolve(parsed, currentFile);
                if (modIds.Count == 0)
                {
                    unattributedLines++;
                    continue;
                }

                attributedLines++;
                foreach (string modId in modIds)
                {
                    string key = modId + "\u001f" + parsed.Category + "\u001f" + parsed.Detail;
                    if (!builders.TryGetValue(key, out IssueBuilder? builder))
                    {
                        builder = new IssueBuilder(modId, parsed.Category, parsed.Detail);
                        builders[key] = builder;
                    }

                    builder.Count++;
                    if (!string.IsNullOrWhiteSpace(parsed.ObjectId))
                        _ = builder.Objects.Add(parsed.ObjectId);

                    if (!string.IsNullOrWhiteSpace(currentFile))
                        _ = builder.SourceFiles.Add(Path.GetFileName(currentFile));
                }
            }

            List<ModErrorLogIssue> issues = [.. builders.Values
                .Select(builder => builder.Build())
                .OrderBy(issue => issue.ModId, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(issue => issue.Count)
                .ThenBy(issue => issue.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.Detail, StringComparer.OrdinalIgnoreCase)];

            return new ModErrorLogAnalysis(
                issues,
                consideredLines,
                attributedLines,
                unattributedLines,
                File.GetLastWriteTimeUtc(errorLogPath));
        }

        private static ParsedLine Classify(string line, ObjectHint? currentObject)
        {
            // "Duplicate Object" lines do not start with ***, but they are already reported by the
            // duplicate-raw warning system and would otherwise look like a generic error HERE.
            if (line.StartsWith("Duplicate Object:", StringComparison.OrdinalIgnoreCase))
                return ParsedLine.Ignore;

            if (TwoUninstalledModsRegex().Match(line) is { Success: true } duplicateInstall)
            {
                string name = duplicateInstall.Groups["name"].Value.Trim();
                string version = duplicateInstall.Groups["version"].Value.Trim();
                string chosen = duplicateInstall.Groups["chosen"].Value.Trim();
                string detail = version.Length > 0 ? $"version {version}, using {chosen}" : $"using {chosen}";
                return new ParsedLine(
                    "Duplicate mod installation",
                    detail,
                    NormalizeObjectId(name),
                    null,
                    PreferFile: false,
                    ExplicitModId: name);
            }

            if (ModMetadataPrefixRegex().Match(line) is { Success: true } metadata)
                return ClassifyModMetadata(metadata.Groups["mod"].Value, metadata.Groups["message"].Value);

            if (ObjectTokenRegex().Match(line) is { Success: true } objectToken)
            {
                string objectId = NormalizeObjectId(objectToken.Groups["object"].Value);
                string kind = objectToken.Groups["kind"].Value;
                string detail = objectToken.Groups["detail"].Value.Trim();
                return new ParsedLine(
                    FormatTokenCategory(kind, objectToken.Groups["context"].Value),
                    detail,
                    objectId,
                    new ObjectHint(InferTypeFromKind(kind), objectId),
                    PreferFile: IsGraphicsKind(kind));
            }

            if (HeaderlessTokenRegex().Match(line) is { Success: true } token)
            {
                string kind = token.Groups["kind"].Value;
                return new ParsedLine(
                    FormatTokenCategory(kind, token.Groups["context"].Value),
                    token.Groups["detail"].Value.Trim(),
                    currentObject?.Id,
                    currentObject,
                    PreferFile: IsGraphicsKind(kind));
            }

            if (GraphicsTokenRegex().Match(line) is { Success: true } graphicsToken)
            {
                return new ParsedLine(
                    "Unrecognized graphics token",
                    graphicsToken.Groups["detail"].Value.Trim(),
                    null,
                    null,
                    PreferFile: true);
            }

            if (MissingCasteGraphicsRegex().Match(line) is { Success: true } missingGraphics)
            {
                string creature = NormalizeObjectId(missingGraphics.Groups["creature"].Value);
                return new ParsedLine(
                    "Missing creature caste graphics",
                    missingGraphics.Groups["caste"].Value.Trim(),
                    creature,
                    new ObjectHint("CREATURE", creature),
                    PreferFile: true);
            }

            if (PlantMaterialRegex().Match(line) is { Success: true } plantMaterial)
            {
                string detail = plantMaterial.Groups["detail"].Value.Trim();
                string plantId = NormalizeObjectId(detail.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty);

                // DF repeats the plant token as both plant and material ("PALM PALM"); showing it
                // twice adds no information to the problem tooltip.
                string[] detailParts = detail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (detailParts.Length >= 2 && string.Equals(detailParts[0], detailParts[1], StringComparison.OrdinalIgnoreCase))
                    detail = detailParts[0];

                return new ParsedLine(
                    "Undefined plant material",
                    detail,
                    plantId,
                    string.IsNullOrWhiteSpace(plantId) ? null : new ObjectHint("PLANT", plantId),
                    PreferFile: false);
            }

            if (PlantGrowthRegex().Match(line) is { Success: true } plantGrowth)
            {
                string plantId = NormalizeObjectId(plantGrowth.Groups["plant"].Value);
                string growth = plantGrowth.Groups["growth"].Value.Trim();
                return new ParsedLine(
                    "Plant growth not found",
                    growth,
                    plantId,
                    string.IsNullOrWhiteSpace(plantId) ? null : new ObjectHint("PLANT", plantId),
                    PreferFile: false);
            }

            if (ModIdentifierRegex().Match(line) is { Success: true } identifier)
                return new ParsedLine("Invalid mod identifier", identifier.Groups["detail"].Value.Trim(), null, null, PreferFile: false);

            if (ObjectMessageRegex().Match(line) is { Success: true } objectMessage)
            {
                string objectId = NormalizeObjectId(objectMessage.Groups["object"].Value);
                (string category, string detail) = ClassifyObjectMessage(objectMessage.Groups["message"].Value);

                string? type = InferTypeFromMessage(objectMessage.Groups["message"].Value);
                if (type == null && currentObject is { } current && IdEquals(current.Id, objectId))
                    type = current.Type;

                return new ParsedLine(category, detail, objectId, new ObjectHint(type, objectId), PreferFile: false);
            }

            (string fallbackCategory, string fallbackDetail) = ClassifyObjectMessage(line);
            return new ParsedLine(
                fallbackCategory,
                fallbackDetail,
                currentObject?.Id,
                currentObject,
                PreferFile: false);
        }

        private static ParsedLine ClassifyModMetadata(string modToken, string message)
        {
            string modId = NormalizeObjectId(modToken);

            if (message.StartsWith("Unrecognized mod info token:", StringComparison.OrdinalIgnoreCase))
            {
                string token = message["Unrecognized mod info token:".Length..].Trim();
                int end = token.IndexOf('.');
                if (end > 0)
                    token = token[..end];
                return new ParsedLine("Unrecognized mod info token", token.Trim(), modId, new ObjectHint(null, modId), PreferFile: false, ExplicitModId: modId);
            }

            if (message.StartsWith("Mod identifier", StringComparison.OrdinalIgnoreCase))
                return new ParsedLine("Invalid mod identifier", message, modId, null, PreferFile: false, ExplicitModId: modId);

            return new ParsedLine("Invalid mod version metadata", message, modId, null, PreferFile: false, ExplicitModId: modId);
        }

        private static (string Category, string Detail) ClassifyObjectMessage(string message)
        {
            string value = message.Trim().TrimStart(':', ';', ' ');

            if (HeaderlessTokenRegex().Match(value) is { Success: true } embeddedToken)
            {
                return (
                    FormatTokenCategory(embeddedToken.Groups["kind"].Value, embeddedToken.Groups["context"].Value),
                    embeddedToken.Groups["detail"].Value.Trim());
            }

            if (value.StartsWith("Caste Selection Fails:", StringComparison.OrdinalIgnoreCase))
                return ("Caste selection failed", value["Caste Selection Fails:".Length..].Trim());

            // Caste and body-part details precede the tissue message in the log, e.g.
            // "OCTOPUS:FEMALE:ink sac, layer 1: Tissue GIZZARD was not found, using first tissue instead".
            // The tissue note can therefore appear anywhere in the message, not just at the start.
            if (TissueNotFoundRegex().Match(value) is { Success: true } tissue)
                return ("Tissue not found", tissue.Groups["tissue"].Value.Trim());

            if (value.Contains("No tissue thickness", StringComparison.OrdinalIgnoreCase))
                return ("Missing tissue thickness", string.Empty);

            if (value.StartsWith("Tissue layer not added because no BP found:", StringComparison.OrdinalIgnoreCase))
                return ("Tissue layer missing body part", value["Tissue layer not added because no BP found:".Length..].Trim());

            if (value.Contains("could not find proper BPs", StringComparison.OrdinalIgnoreCase))
                return ("Attack body part not found", string.Empty);

            if (value.Contains("has no body parts", StringComparison.OrdinalIgnoreCase))
                return ("Missing body parts", string.Empty);

            if (value.StartsWith("GO_TO_TAG could not find target", StringComparison.OrdinalIgnoreCase))
                return ("GO_TO_TAG target not found", string.Empty);

            if (value.StartsWith("Unrecognized ", StringComparison.OrdinalIgnoreCase))
            {
                int tokenSeparator = value.IndexOf(':');
                if (tokenSeparator > 0)
                    return ("Unrecognized " + value["Unrecognized ".Length..tokenSeparator].Trim().ToLowerInvariant(), value[(tokenSeparator + 1)..].Trim());
            }

            int separator = value.IndexOf(':');
            if (separator > 0)
                return (value[..separator].Trim(), value[(separator + 1)..].Trim());

            return (value, string.Empty);
        }

        private static bool TryParseFileHeader(string line, out string? file)
        {
            Match match = FileHeaderRegex().Match(line);
            file = match.Success ? match.Groups["file"].Value.Trim() : null;
            return match.Success;
        }

        private static bool TryParseFinalizingObject(string line, out ObjectHint? hint)
        {
            Match match = FinalizeRegex().Match(line);
            if (!match.Success)
            {
                hint = null;
                return false;
            }

            string kind = match.Groups["kind"].Value;
            string id = NormalizeObjectId(match.Groups["id"].Value);
            hint = new ObjectHint(kind.ToUpperInvariant(), id);
            return true;
        }

        private static string FormatTokenCategory(string kind, string context)
        {
            string category = "Unrecognized " + kind.Trim().ToLowerInvariant() + " token";
            if (!string.IsNullOrWhiteSpace(context))
                category += " in " + context.Trim().ToLowerInvariant();
            return category;
        }

        private static bool IsGraphicsKind(string kind)
            => kind.Contains("graphics", StringComparison.OrdinalIgnoreCase);

        private static string? InferTypeFromKind(string kind)
        {
            if (kind.Contains("creature", StringComparison.OrdinalIgnoreCase))
                return "CREATURE";
            if (kind.Contains("reaction", StringComparison.OrdinalIgnoreCase))
                return "REACTION";
            if (kind.Contains("plant", StringComparison.OrdinalIgnoreCase))
                return "PLANT";
            if (kind.Contains("inorganic", StringComparison.OrdinalIgnoreCase) || kind.Contains("color", StringComparison.OrdinalIgnoreCase))
                return "INORGANIC";
            if (kind.Contains("entity", StringComparison.OrdinalIgnoreCase) || kind.Contains("permitted", StringComparison.OrdinalIgnoreCase))
                return "ENTITY";
            if (kind.Contains("building", StringComparison.OrdinalIgnoreCase))
                return "BUILDING";
            if (kind.Contains("translation", StringComparison.OrdinalIgnoreCase) || kind.Contains("word", StringComparison.OrdinalIgnoreCase))
                return "TRANSLATION";
            return null;
        }

        private static string? InferTypeFromMessage(string message)
        {
            if (message.Contains("Caste Selection", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Tissue ", StringComparison.OrdinalIgnoreCase)
                || message.Contains("No tissue", StringComparison.OrdinalIgnoreCase)
                || message.Contains("BPs", StringComparison.OrdinalIgnoreCase)
                || message.Contains("body parts", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Unrecognized Creature", StringComparison.OrdinalIgnoreCase))
            {
                return "CREATURE";
            }

            if (message.Contains("Unrecognized Reaction", StringComparison.OrdinalIgnoreCase))
                return "REACTION";
            if (message.Contains("Unrecognized Plant", StringComparison.OrdinalIgnoreCase))
                return "PLANT";
            if (message.Contains("Unrecognized Inorganic", StringComparison.OrdinalIgnoreCase))
                return "INORGANIC";
            if (message.Contains("Unrecognized Entity", StringComparison.OrdinalIgnoreCase)
                || message.Contains("permitted", StringComparison.OrdinalIgnoreCase))
            {
                return "ENTITY";
            }
            if (message.Contains("Unrecognized Building", StringComparison.OrdinalIgnoreCase))
                return "BUILDING";

            return null;
        }

        private static string NormalizeObjectId(string? value)
            => (value ?? string.Empty).Trim().Replace(' ', '_');

        private static bool IdEquals(string left, string right)
            => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static string NormalizeModKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder builder = new(value.Length);
            foreach (char character in value.Trim())
                _ = builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
            return builder.ToString();
        }

        private readonly record struct ObjectHint(string? Type, string Id);

        private sealed record ParsedLine(
            string Category,
            string Detail,
            string? ObjectId,
            ObjectHint? Object,
            bool PreferFile,
            string? ExplicitModId = null)
        {
            public static ParsedLine Ignore { get; } = new(string.Empty, string.Empty, null, null, PreferFile: false) { Skip = true };

            public bool Skip { get; init; }
        }

        private sealed class IssueBuilder(string modId, string category, string detail)
        {
            public string ModId { get; } = modId;
            public string Category { get; } = category;
            public string Detail { get; } = detail;
            public int Count { get; set; }
            public HashSet<string> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> SourceFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

            public ModErrorLogIssue Build()
                => new(
                    ModId,
                    Category,
                    Detail,
                    Count,
                    [.. Objects.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Take(MaxTrackedObjectsPerIssue)],
                    [.. SourceFiles.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Take(MaxTrackedFilesPerIssue)]);
        }

        private sealed class ModLookup
        {
            private readonly Dictionary<string, HashSet<string>> byId = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> byNormalizedKey = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<(string Path, string ModId)> pathsByLengthDesc = [];
            private readonly Dictionary<string, HashSet<string>> directByObjectKey = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> directByObjectId = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> patchByObjectKey = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> patchByObjectId = new(StringComparer.OrdinalIgnoreCase);

            public static ModLookup Build(
                IReadOnlyCollection<ModReference> mods,
                IEnumerable<ModRawDependencyInfo> rawInfos)
            {
                ModLookup lookup = new();

                foreach (ModReference mod in mods)
                {
                    string id = mod.ID?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    Add(lookup.byId, id, id);
                    Add(lookup.byNormalizedKey, NormalizeModKey(id), id);
                    Add(lookup.byNormalizedKey, NormalizeModKey(mod.Name), id);

                    if (!string.IsNullOrWhiteSpace(mod.Path))
                        lookup.pathsByLengthDesc.Add((mod.Path, id));
                }

                lookup.pathsByLengthDesc.Sort((left, right) => right.Path.Length.CompareTo(left.Path.Length));

                foreach (ModRawDependencyInfo info in rawInfos)
                {
                    string modId = info.ModId?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(modId))
                        continue;

                    foreach (string key in info.DirectDefinitionIds)
                        AddObjectKey(lookup.directByObjectKey, lookup.directByObjectId, key, modId);

                    foreach (string key in info.SelectTargetIds)
                        AddObjectKey(lookup.patchByObjectKey, lookup.patchByObjectId, key, modId);

                    foreach (string key in info.CutTargetIds)
                        AddObjectKey(lookup.patchByObjectKey, lookup.patchByObjectId, key, modId);
                }

                return lookup;
            }

            public IReadOnlyCollection<string> Resolve(ParsedLine line, string? currentFile)
            {
                if (!string.IsNullOrWhiteSpace(line.ExplicitModId)
                    && ResolveModId(line.ExplicitModId) is { Count: > 0 } explicitMatches)
                {
                    return explicitMatches;
                }

                if (line.PreferFile && !string.IsNullOrWhiteSpace(currentFile)
                    && ResolveFile(currentFile) is { Count: > 0 } preferredFileMatches)
                {
                    return preferredFileMatches;
                }

                if (line.Object is { } hint)
                {
                    IReadOnlyCollection<string> objectMatches = ResolveObject(hint);
                    if (objectMatches.Count > 0)
                        return objectMatches;
                }

                // No generic file fallback: DF keeps the previous "*** Error(s) found in the file"
                // header while finalizing unrelated objects, so matching an arbitrary headerless
                // token against that path produces false positives (e.g. vanilla STEEL/IRON errors
                // reported against whichever graphics mod was loaded last). Only lines that are
                // inherently file-bound (graphics, tile pages, caste graphics) trust the path.
                return [];
            }

            private HashSet<string> ResolveObject(ObjectHint hint)
            {
                if (string.IsNullOrWhiteSpace(hint.Id))
                    return [];

                if (!string.IsNullOrWhiteSpace(hint.Type)
                    && directByObjectKey.TryGetValue(hint.Type + ":" + hint.Id, out HashSet<string>? directByKey))
                {
                    return directByKey;
                }

                if (directByObjectId.TryGetValue(hint.Id, out HashSet<string>? directById))
                    return directById;

                if (!string.IsNullOrWhiteSpace(hint.Type)
                    && patchByObjectKey.TryGetValue(hint.Type + ":" + hint.Id, out HashSet<string>? patchByKey))
                {
                    return patchByKey;
                }

                return patchByObjectId.TryGetValue(hint.Id, out HashSet<string>? patchById) ? patchById : [];
            }

            private IReadOnlyCollection<string> ResolveFile(string file)
            {
                if (string.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file))
                    return [];

                string[] segments = file.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);

                int installedModsIndex = Array.FindIndex(
                    segments,
                    segment => string.Equals(segment, "installed_mods", StringComparison.OrdinalIgnoreCase));

                if (installedModsIndex >= 0 && installedModsIndex + 1 < segments.Length)
                {
                    string folder = VersionSuffixRegex().Replace(segments[installedModsIndex + 1], string.Empty).Trim();
                    IReadOnlyCollection<string> byFolder = ResolveModId(folder);
                    if (byFolder.Count > 0)
                        return byFolder;
                }

                foreach ((string path, string modId) in pathsByLengthDesc)
                {
                    if (PathHelper.IsUnderRoot(file, path))
                        return [modId];
                }

                return [];
            }

            private HashSet<string> ResolveModId(string value)
            {
                string trimmed = value.Trim();
                if (byId.TryGetValue(trimmed, out HashSet<string>? exact))
                    return exact;
                return byNormalizedKey.TryGetValue(NormalizeModKey(trimmed), out HashSet<string>? normalized) ? normalized : [];
            }

            private static void Add(Dictionary<string, HashSet<string>> map, string key, string value)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    return;

                if (!map.TryGetValue(key, out HashSet<string>? values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[key] = values;
                }

                _ = values.Add(value);
            }

            private static void AddObjectKey(
                Dictionary<string, HashSet<string>> byKey,
                Dictionary<string, HashSet<string>> byId,
                string compositeKey,
                string modId)
            {
                if (string.IsNullOrWhiteSpace(compositeKey))
                    return;

                int separator = compositeKey.IndexOf(':');
                if (separator <= 0 || separator == compositeKey.Length - 1)
                    return;

                string objectType = compositeKey[..separator].Trim();
                string objectId = NormalizeObjectId(compositeKey[(separator + 1)..]);
                Add(byKey, objectType + ":" + objectId, modId);
                Add(byId, objectId, modId);
            }
        }

        [GeneratedRegex(@"^\*\*\* Error\(s\) found in the file ""(?<file>.+)""\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex FileHeaderRegex();

        [GeneratedRegex(@"^\*\*\* Error\(s\) finalizing the (?<kind>\S+) (?<id>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex FinalizeRegex();

        [GeneratedRegex(@"^(?<object>[^:]+):\s*Unrecognized\s+(?<kind>.+?)\s+Token(?:\s+in\s+(?<context>[^:]+))?:\s*(?<detail>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex ObjectTokenRegex();

        [GeneratedRegex(@"^Unrecognized\s+(?<kind>.+?)\s+Token(?:\s+in\s+(?<context>[^:]+))?:\s*(?<detail>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex HeaderlessTokenRegex();

        [GeneratedRegex(@"(?:^|[:\s])Tissue\s+(?<tissue>.+?)\s+was not found", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex TissueNotFoundRegex();

        [GeneratedRegex(@"^(?:Tile page token not recognized|Unknown tool graphics tool token):\s*(?<detail>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex GraphicsTokenRegex();

        [GeneratedRegex(@"^Failed to find CREATURE_CASTE_GRAPHICS creature caste (?<creature>[^:]+):(?<caste>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex MissingCasteGraphicsRegex();

        [GeneratedRegex(@"^undefined (?:local )?plant material set to default:\s*(?<detail>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex PlantMaterialRegex();

        [GeneratedRegex(@"^plant growth not found:\s*(?<growth>\S+)\s+in\s+(?<plant>\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex PlantGrowthRegex();

        [GeneratedRegex(@"^(?<mod>[^:\s]+):\s*(?<message>NUMERIC_VERSION is an integer.*|EARLIEST_COMPATIBLE_NUMERIC_VERSION must be.*|Unrecognized mod info token:.*|Mod identifier .*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex ModMetadataPrefixRegex();

        [GeneratedRegex(@"^Two uninstalled mods have the same id and numeric version:\s*(?<name>.+?)\s*\((?<version>[^)]*)\)\.\s*Using the first encountered:\s*(?<chosen>[^.\s]+)\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex TwoUninstalledModsRegex();

        [GeneratedRegex(@"^Mod identifier (?<detail>.+?) has characters.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex ModIdentifierRegex();

        [GeneratedRegex(@"^(?<object>[^:]+):(?<message>.+)$", RegexOptions.Compiled)]
        private static partial Regex ObjectMessageRegex();

        [GeneratedRegex(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled)]
        private static partial Regex VersionSuffixRegex();
    }
}
