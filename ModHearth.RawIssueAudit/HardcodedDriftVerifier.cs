using System.Text;
using ModHearth.Utils.ModRaws;

namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Checks the hardcoded-token catalog against a Dwarf Fortress executable and the vanilla raw
    /// definitions. Reaction drift is an exact ID comparison: the executable is expected to contain
    /// the dye reaction ids derived from <c>reaction_dyes.txt</c> and <c>entity_default.txt</c> as
    /// standalone NUL-terminated strings and none of the other vanilla reaction ids. Material and
    /// grammar entries use the same string-presence check. Reaction building ids are verified by
    /// <see cref="HardcodedReportRunner"/> against the buildings vanilla reactions actually use,
    /// because DF inlines several short ids.
    /// </summary>
    internal static class HardcodedDriftVerifier
    {
        public static HardcodedVerificationResult Verify(
            string executablePath,
            IEnumerable<string> vanillaReactionIds,
            IEnumerable<string> cutIgnoredDyeReactionIds)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(executablePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AuditInputException(
                    $"Could not read Dwarf Fortress executable '{executablePath}': {ex.Message}");
            }

            HashSet<string> executableStrings = ExtractAsciiStrings(bytes);
            HashSet<string> vanillaReactions = new(
                vanillaReactionIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            HashSet<string> derivedDyeReactions = new(
                cutIgnoredDyeReactionIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            List<HardcodedCatalogCheck> checks = [];

            // Reaction drift: exact IDs. The executable should contain the raw-derived dye set and
            // no other vanilla reaction id as a standalone string. This catches a DF update adding
            // a hardcoded reaction or a dye that stopped being hardcoded.
            foreach (string reactionId in vanillaReactions)
            {
                if (!executableStrings.Contains(reactionId) || derivedDyeReactions.Contains(reactionId))
                    continue;

                checks.Add(new HardcodedCatalogCheck(
                    HardcodedTokenGroupId.CutImmuneDyeReactions,
                    reactionId,
                    HardcodedCatalogCheckStatus.UnexpectedReactionIdInExecutable,
                    "The vanilla reaction id appears as a standalone string in the executable but is " +
                    "not in the dye set derived from reaction_dyes.txt and entity_default.txt."));
            }

            foreach (string reactionId in derivedDyeReactions)
            {
                if (executableStrings.Contains(reactionId))
                    continue;

                checks.Add(new HardcodedCatalogCheck(
                    HardcodedTokenGroupId.CutImmuneDyeReactions,
                    reactionId,
                    HardcodedCatalogCheckStatus.VanillaReactionMissingFromExecutable,
                    "The dye reaction derived from the vanilla raws is no longer a standalone string " +
                    "in the executable."));
            }

            // Buildings and materials use different evidence. Material tokens and grammar keywords
            // are reliably stored as standalone executable strings; reaction building ids are not
            // (DF inlines several short comparisons and uses WORKSHOP_/FURNACE_ graphics names), so
            // HardcodedReportRunner checks those against vanilla reaction usage instead.
            foreach (HardcodedTokenGroup group in DwarfFortressHardcodedTokens.Groups
                .Where(group => group.Verification == HardcodedTokenVerification.ExecutableString
                    && group.Id != HardcodedTokenGroupId.CutImmuneDyeReactions))
            {
                foreach (string token in group.Tokens)
                {
                    if (executableStrings.Contains(token))
                        continue;

                    checks.Add(new HardcodedCatalogCheck(
                        group.Id,
                        token,
                        HardcodedCatalogCheckStatus.CatalogTokenMissingFromExecutable,
                        "The catalog token is not a standalone string in the executable."));
                }
            }

            checks.Sort(CompareChecks);

            return new HardcodedVerificationResult(
                executablePath,
                vanillaReactions.Count,
                derivedDyeReactions.Count,
                vanillaReactions.Count(executableStrings.Contains),
                checks);
        }

        private static int CompareChecks(HardcodedCatalogCheck left, HardcodedCatalogCheck right)
        {
            int statusComparison = left.Status.CompareTo(right.Status);
            if (statusComparison != 0)
                return statusComparison;

            int groupComparison = Nullable.Compare(left.GroupId, right.GroupId);
            if (groupComparison != 0)
                return groupComparison;

            return string.Compare(left.Token, right.Token, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts standalone printable-ASCII C strings from a binary. The NUL terminator is part of
        /// the match, so a catalog token embedded in a longer diagnostic string is not counted as a
        /// hardcoded id.
        /// </summary>
        private static HashSet<string> ExtractAsciiStrings(byte[] bytes)
        {
            HashSet<string> strings = new(StringComparer.Ordinal);
            int start = -1;

            for (int i = 0; i < bytes.Length; i++)
            {
                byte value = bytes[i];
                if (value is >= 0x20 and <= 0x7E)
                {
                    if (start < 0)
                        start = i;
                    continue;
                }

                if (start >= 0 && value == 0 && i - start >= 3)
                    _ = strings.Add(Encoding.ASCII.GetString(bytes, start, i - start));

                start = -1;
            }

            return strings;
        }
    }
}
