using System.Text.Json.Serialization;

namespace ModHearth.Models
{
    public sealed class ModRelationshipRule
    {
        [JsonPropertyName("beforeIds")]
        public List<string> BeforeIds { get; set; } = [];

        [JsonPropertyName("afterIds")]
        public List<string> AfterIds { get; set; } = [];

        [JsonPropertyName("requiredIds")]
        public List<string> RequiredIds { get; set; } = [];

        [JsonPropertyName("incompatibleIds")]
        public List<string> IncompatibleIds { get; set; } = [];

        [JsonIgnore]
        public bool IsEmpty =>
            BeforeIds.Count == 0 &&
            AfterIds.Count == 0 &&
            RequiredIds.Count == 0 &&
            IncompatibleIds.Count == 0;

        public ModRelationshipRule Clone() => new()
        {
            BeforeIds = [.. BeforeIds],
            AfterIds = [.. AfterIds],
            RequiredIds = [.. RequiredIds],
            IncompatibleIds = [.. IncompatibleIds]
        };

        /// <summary>
        /// Returns the live id list backing the given relationship kind, so callers can read or
        /// mutate it without duplicating the four-way switch.
        /// </summary>
        public List<string> GetIds(ModRelationshipKind kind) => kind switch
        {
            ModRelationshipKind.Before => BeforeIds,
            ModRelationshipKind.After => AfterIds,
            ModRelationshipKind.Required => RequiredIds,
            ModRelationshipKind.Incompatible => IncompatibleIds,
            _ => BeforeIds
        };

        /// <summary>
        /// Trims, de-duplicates (case-insensitively) and drops blank ids from every list in place.
        /// </summary>
        public ModRelationshipRule NormalizeInPlace()
        {
            BeforeIds = NormalizeIds(BeforeIds);
            AfterIds = NormalizeIds(AfterIds);
            RequiredIds = NormalizeIds(RequiredIds);
            IncompatibleIds = NormalizeIds(IncompatibleIds);
            return this;
        }

        public static List<string> NormalizeIds(IEnumerable<string>? ids)
        {
            List<string> normalized = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            if (ids == null)
                return normalized;

            foreach (string id in ids)
            {
                string trimmed = id?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(trimmed) && seen.Add(trimmed))
                    normalized.Add(trimmed);
            }

            return normalized;
        }

        /// <summary>
        /// Deep-clones a rule dictionary, trimming keys and skipping blank/null entries.
        /// </summary>
        public static Dictionary<string, ModRelationshipRule> CloneAll(
            IDictionary<string, ModRelationshipRule>? rules)
        {
            Dictionary<string, ModRelationshipRule> clone = new(StringComparer.OrdinalIgnoreCase);
            if (rules == null)
                return clone;

            foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
            {
                string key = kvp.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key) || kvp.Value == null)
                    continue;
                clone[key] = kvp.Value.Clone();
            }

            return clone;
        }

        /// <summary>
        /// Clones and normalizes a rule dictionary, dropping rules that end up empty.
        /// </summary>
        public static Dictionary<string, ModRelationshipRule> NormalizeAll(
            IDictionary<string, ModRelationshipRule>? rules)
        {
            Dictionary<string, ModRelationshipRule> normalized = new(StringComparer.OrdinalIgnoreCase);
            if (rules == null)
                return normalized;

            foreach (KeyValuePair<string, ModRelationshipRule> kvp in rules)
            {
                string key = kvp.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key) || kvp.Value == null)
                    continue;

                ModRelationshipRule rule = kvp.Value.Clone().NormalizeInPlace();
                if (!rule.IsEmpty)
                    normalized[key] = rule;
            }

            return normalized;
        }
    }

    public enum ModRelationshipKind
    {
        // Unlike vanilla df these 2 only care about position, not dependencies (except against each other)
        Before,
        After,

        // Needs to be anywhere in the list
        Required,
        // Conflicts with everything else
        Incompatible
    }
}
