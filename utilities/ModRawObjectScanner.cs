using System.Text.RegularExpressions;

namespace ModHearth.Utilities
{
    // Scans a single mod's objects/ folder to extract the DF raw-file relationships AutoSort's dependency graph needs. Deliberately not a
    // full DF raw-syntax parser — it only tracks the handful of token shapes that actually affect load order:
    //   SELECT_*        -> extract target ID -> dependency
    //   CUT_* / CUT      -> mod is a "cutter"; recorded against whichever
    //                       SELECT_x target it's nested under
    //   COPY_TAGS_FROM   -> extract source ID -> hard dependency
    //   direct object tag (outside SELECT) -> new definition, for
    //                       duplicate-raw-definition detection
    internal static class ModRawObjectScanner
    {
        // Matches a bracketed raw tag: [TAG_NAME] or [TAG_NAME:arg1:arg2:...]
        private static readonly Regex TagRegex = new(@"\[([A-Za-z_][A-Za-z0-9_]*)(?::([^\]]*))?\]", RegexOptions.Compiled);

        // Object-defining tags that start a brand-new object outside SELECT.
        // Not exhaustive of every DF object type, but covers the ones that
        // actually participate in cross-mod conflicts/dependencies.
        private static readonly HashSet<string> DirectDefinitionTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "ENTITY", "CREATURE", "PLANT", "INORGANIC", "REACTION",
            "ITEM_WEAPON", "ITEM_ARMOR", "ITEM_TOOL", "ITEM_AMMO", "ITEM_SHIELD",
            "ITEM_HELM", "ITEM_GLOVES", "ITEM_PANTS", "ITEM_SHOES", "ITEM_INSTRUMENT",
            "ITEM_TOY", "ITEM_FOOD", "ITEM_TRAPCOMP", "ITEM_SIEGEAMMO"
        };

        public static ModRawDependencyInfo Scan(string modId, string numericVersion, string modPath, long objectsFolderStampTicks)
        {
            ModRawDependencyInfo info = new ModRawDependencyInfo
            {
                ModId = modId,
                NumericVersion = numericVersion,
                ObjectsFolderStampTicks = objectsFolderStampTicks
            };

            string objectsPath = Path.Combine(modPath, "objects");
            if (!Directory.Exists(objectsPath))
                return info;

            HashSet<string> cutTargets = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> selectTargets = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> copyTagsFromSources = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> directDefinitions = new(StringComparer.OrdinalIgnoreCase);
            bool isCutter = false;
            bool hasReaction = false;
            bool hasCreature = false;
            bool hasNewStuff = false;
            bool hasVanillaEntity = false;
            bool hasNewEntity = false;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(objectsPath, "*.txt", SearchOption.AllDirectories);
            }
            catch
            {
                return info;
            }

            foreach (string file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }

                // Tracks which SELECT_x target we're currently "inside" —
                // CUT tokens found before the next object-defining or
                // SELECT_x tag are attributed to this target. Reset per file:
                // a CUT is only meaningfully paired with a SELECT in the
                // same file in any realistic mod layout.
                string? currentSelectTarget = null;

                foreach (var groups in TagRegex.Matches(text).Select(match => match.Groups))
                {
                    string tag = groups[1].Value;
                    string arg = groups[2].Success ? groups[2].Value.Trim() : string.Empty;
                    string firstArg = arg.Length == 0 ? string.Empty : arg.Split(':')[0].Trim();

                    if (tag.StartsWith("SELECT_", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(firstArg))
                        {
                            selectTargets.Add(firstArg);
                            currentSelectTarget = firstArg;
                        }
                        continue;
                    }

                    if (tag.StartsWith("CUT_", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tag, "CUT", StringComparison.OrdinalIgnoreCase))
                    {
                        isCutter = true;
                        if (currentSelectTarget != null)
                            cutTargets.Add(currentSelectTarget);
                        continue;
                    }

                    if (string.Equals(tag, "COPY_TAGS_FROM", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(firstArg))
                            copyTagsFromSources.Add(firstArg);
                        continue;
                    }

                    if (DirectDefinitionTags.Contains(tag))
                    {
                        // A direct object-defining tag closes out whatever
                        // SELECT_x block we were tracking.
                        currentSelectTarget = null;

                        if (!string.IsNullOrWhiteSpace(firstArg))
                            directDefinitions.Add(firstArg);

                        if (string.Equals(tag, "ENTITY", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsVanillaEntity(firstArg))
                                hasVanillaEntity = true;
                            else if (!string.IsNullOrWhiteSpace(firstArg))
                                hasNewEntity = true;
                        }
                        else if (string.Equals(tag, "REACTION", StringComparison.OrdinalIgnoreCase))
                        {
                            hasReaction = true;
                        }
                        else if (string.Equals(tag, "CREATURE", StringComparison.OrdinalIgnoreCase))
                        {
                            hasCreature = true;
                        }
                        else if (string.Equals(tag, "INORGANIC", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(tag, "PLANT", StringComparison.OrdinalIgnoreCase) ||
                                 tag.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase))
                        {
                            hasNewStuff = true;
                        }
                    }
                }
            }

            if (hasCreature)
                hasNewStuff = true;

            bool hasGraphics =
                Directory.Exists(Path.Combine(modPath, "graphics")) ||
                Directory.Exists(Path.Combine(modPath, "raw", "graphics"));

            info.IsCutter = isCutter;
            info.CutTargetIds = cutTargets.ToList();
            info.SelectTargetIds = selectTargets.ToList();
            info.CopyTagsFromSourceIds = copyTagsFromSources.ToList();
            info.DirectDefinitionIds = directDefinitions.ToList();
            info.HasVanillaEntity = hasVanillaEntity;
            info.HasNewEntity = hasNewEntity;
            info.HasReaction = hasReaction;
            info.HasCreature = hasCreature;
            info.HasNewStuff = hasNewStuff;
            info.HasGraphics = hasGraphics;

            return info;
        }

        // Deliberately a self-contained copy of ModHearthManager's vanilla
        // entity list rather than a cross-class call — keeps this scanner
        // independent of ModHearthManager entirely.
        private static bool IsVanillaEntity(string id)
        {
            switch (id.ToUpperInvariant())
            {
                case "DWARF":
                case "ELF":
                case "HUMAN":
                case "GOBLIN":
                case "KOBOLD":
                    return true;
            }
            return false;
        }
    }
}