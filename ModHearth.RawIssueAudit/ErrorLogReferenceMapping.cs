using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    internal enum ReferenceAuditKind
    {
        PermittedReaction,
        PermittedBuilding,
        ReactionBuilding
    }

    /// <summary>
    /// Maps the errorlog wording produced by <c>ModErrorLogAnalyzer</c> onto the reference kinds
    /// <c>ModPossibleIssueAnalyzer</c> predicts. DF may append an "in &lt;context&gt;" suffix, so
    /// category matching is prefix-based.
    /// </summary>
    internal static class ErrorLogReferenceMapping
    {
        public static bool TryMapCategory(string? category, out ReferenceAuditKind kind)
        {
            string value = (category ?? string.Empty).Trim();
            if (value.StartsWith("Unrecognized permitted reaction token", StringComparison.OrdinalIgnoreCase))
            {
                kind = ReferenceAuditKind.PermittedReaction;
                return true;
            }

            if (value.StartsWith("Unrecognized permitted building token", StringComparison.OrdinalIgnoreCase))
            {
                kind = ReferenceAuditKind.PermittedBuilding;
                return true;
            }

            if (value.StartsWith("Unrecognized building token", StringComparison.OrdinalIgnoreCase))
            {
                kind = ReferenceAuditKind.ReactionBuilding;
                return true;
            }

            kind = default;
            return false;
        }

        public static ModPossibleIssueKind ToIssueKind(ReferenceAuditKind kind) => kind switch
        {
            ReferenceAuditKind.PermittedReaction => ModPossibleIssueKind.MissingPermittedReaction,
            ReferenceAuditKind.PermittedBuilding => ModPossibleIssueKind.MissingPermittedBuilding,
            _ => ModPossibleIssueKind.MissingReactionBuilding
        };

        public static string ObjectType(ReferenceAuditKind kind) => kind switch
        {
            ReferenceAuditKind.PermittedReaction => "REACTION",
            _ => "BUILDING"
        };

        public static string ToTargetKey(ReferenceAuditKind kind, string token)
            => new ObjectKey(ObjectType(kind), token.Trim()).ToString();

        public static string ObjectType(ModPossibleIssueKind kind) => kind switch
        {
            ModPossibleIssueKind.MissingPermittedReaction => "REACTION",
            ModPossibleIssueKind.MissingPermittedBuilding => "BUILDING",
            ModPossibleIssueKind.MissingReactionBuilding => "BUILDING",
            _ => string.Empty
        };

        public static string TargetId(string targetKey)
            => ObjectKey.TryParse(targetKey, out _, out string id) ? id : targetKey.Trim();

        public static string ExpectedCategory(ReferenceAuditKind kind) => kind switch
        {
            ReferenceAuditKind.PermittedReaction => "Unrecognized permitted reaction token",
            ReferenceAuditKind.PermittedBuilding => "Unrecognized permitted building token",
            _ => "Unrecognized building token"
        };

        /// <summary>
        /// Categories that plausibly describe a raw reference but are not mapped yet. These are
        /// surfaced for review when DF changes its diagnostics; <c>Unrecognized reaction token</c>
        /// is a token *inside* a reaction and intentionally not treated as a missing reference.
        /// </summary>
        public static bool LooksReferenceRelated(string? category)
        {
            string value = (category ?? string.Empty).Trim();
            if (value.Length == 0 || TryMapCategory(value, out _))
                return false;

            // "Unrecognized reaction token" describes a token *inside* a reaction (e.g. TOOL_USE)
            // and is intentionally not a missing-object reference. A future DF diagnostic such as
            // "Unrecognized Reaction Building Token" still matches the building+token rule below.
            return value.Contains("permitted", StringComparison.OrdinalIgnoreCase)
                || (value.Contains("building", StringComparison.OrdinalIgnoreCase)
                    && value.Contains("token", StringComparison.OrdinalIgnoreCase))
                || (value.Contains("select", StringComparison.OrdinalIgnoreCase)
                    && value.Contains("token", StringComparison.OrdinalIgnoreCase))
                || (value.Contains("cut", StringComparison.OrdinalIgnoreCase)
                    && value.Contains("token", StringComparison.OrdinalIgnoreCase))
                || (value.Contains("copy", StringComparison.OrdinalIgnoreCase)
                    && value.Contains("token", StringComparison.OrdinalIgnoreCase));
        }

        public static string DescribeIssueKind(ModPossibleIssueKind kind) => kind switch
        {
            ModPossibleIssueKind.MissingSelectTarget => "SELECT target",
            ModPossibleIssueKind.MissingCutTarget => "CUT target",
            ModPossibleIssueKind.MissingCopyTagsFromSource => "COPY_TAGS_FROM source",
            ModPossibleIssueKind.MissingPermittedReaction => "PERMITTED_REACTION target",
            ModPossibleIssueKind.MissingPermittedBuilding => "PERMITTED_BUILDING target",
            _ => "reaction BUILDING target"
        };

        public static bool IsComparable(ModPossibleIssueKind kind)
            => kind is ModPossibleIssueKind.MissingPermittedReaction
                or ModPossibleIssueKind.MissingPermittedBuilding
                or ModPossibleIssueKind.MissingReactionBuilding;
    }
}
