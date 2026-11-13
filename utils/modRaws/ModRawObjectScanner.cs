using System.Text.RegularExpressions;
using ModHearth.Utils.Logging;

namespace ModHearth.Utils.ModRaws
{
    /// <summary>
    /// Scans a single mod's objects/ folder to extract the DF raw-file relationships
    /// ModSort's dependency graph needs. The scanner is data-driven: it derives the
    /// active object type from [OBJECT:TYPE] blocks, still recognizes [REACTION:ID]
    /// definitions when a file declares the wrong object type, and treats SELECT_*,
    /// CUT_*, and CUT as generic operations, so it works with new or modded object
    /// types without an explicit whitelist.
    /// </summary>
    internal static partial class ModRawObjectScanner
    {
        /// <summary>
        /// Parses the objects/ folder under <paramref name="modPath"/> into a
        /// <see cref="RawDatabase"/>.
        /// </summary>
        /// <param name="modPath">The full path to the mod folder.</param>
        /// <param name="sourceMod">The identifier of the mod being scanned.</param>
        public static RawDatabase Scan(string modPath, string sourceMod)
        {
            List<RawObject> objects = [];
            bool isCutter = false;

            bool hasGraphics =
                Directory.Exists(Path.Combine(modPath, "graphics")) ||
                Directory.Exists(Path.Combine(modPath, "raw", "graphics"));

            string objectsPath = Path.Combine(modPath, "objects");
            if (Directory.Exists(objectsPath))
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(objectsPath, "*.txt", SearchOption.AllDirectories))
                    {
                        string text;
                        try
                        {
                            text = File.ReadAllText(file);
                        }
                        catch (Exception ex)
                        {
                            Loggers.Diag.Dev($"Skipping unreadable raw file '{file}': {ex.Message}");
                            continue;
                        }

                        // Active object type from the most recent [OBJECT:TYPE] block.
                        string? currentObjectType = null;

                        // The most recent SELECT_ target. A plain [CUT] token ends this
                        // selection and applies the cut to it.
                        string? currentSelectObjectType = null;
                        string? currentSelectId = null;

                        foreach (GroupCollection? groups in TagRegex().Matches(text).Select(match => match.Groups))
                        {
                            string tag = groups[1].Value;
                            string arg = groups[2].Success ? groups[2].Value.Trim() : string.Empty;
                            string firstArg = arg.Length == 0 ? string.Empty : arg.Split(':')[0].Trim();

                            // [OBJECT:TYPE] establishes the active context for the file.
                            if (string.Equals(tag, "OBJECT", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(firstArg))
                                    currentObjectType = firstArg;

                                currentSelectObjectType = null;
                                currentSelectId = null;
                                continue;
                            }

                            // SELECT_<OBJECT_TYPE>:ID -> generic patch target.
                            if (tag.StartsWith("SELECT_", StringComparison.OrdinalIgnoreCase))
                            {
                                string objectType = tag["SELECT_".Length..];
                                if (!string.IsNullOrWhiteSpace(objectType))
                                {
                                    objects.Add(RawObject.Selection(objectType, firstArg, sourceMod));
                                    currentSelectObjectType = objectType;
                                    currentSelectId = firstArg;
                                }
                                continue;
                            }

                            // CUT_<OBJECT_TYPE>:ID -> generic standalone cut.
                            if (tag.StartsWith("CUT_", StringComparison.OrdinalIgnoreCase))
                            {
                                isCutter = true;
                                string objectType = tag["CUT_".Length..];
                                if (!string.IsNullOrWhiteSpace(objectType))
                                    objects.Add(RawObject.Cut(objectType, firstArg, sourceMod));
                                continue;
                            }

                            // [CUT] ends the current SELECT_ block and cuts its target.
                            if (string.Equals(tag, "CUT", StringComparison.OrdinalIgnoreCase))
                            {
                                isCutter = true;
                                if (!string.IsNullOrWhiteSpace(currentSelectObjectType)
                                    && !string.IsNullOrWhiteSpace(currentSelectId))
                                {
                                    objects.Add(RawObject.Cut(currentSelectObjectType, currentSelectId, sourceMod));
                                }

                                currentSelectObjectType = null;
                                currentSelectId = null;
                                continue;
                            }

                            // [COPY_TAGS_FROM:ID] -> hard dependency on the source object
                            // within the active object type.
                            if (string.Equals(tag, "COPY_TAGS_FROM", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(firstArg))
                                    objects.Add(RawObject.CopyTagsFrom(currentObjectType ?? string.Empty, firstArg, sourceMod));
                                continue;
                            }

                            // [PERMITTED_REACTION:ID] (entity) -> reference resolved against the
                            // union of loaded REACTION definitions, including vanilla raws.
                            if (string.Equals(tag, "PERMITTED_REACTION", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(firstArg))
                                    objects.Add(RawObject.PermittedReaction(firstArg, sourceMod));
                                continue;
                            }

                            // [PERMITTED_BUILDING:ID] (entity) -> reference resolved against the
                            // union of loaded BUILDING definitions, including vanilla raws.
                            if (string.Equals(tag, "PERMITTED_BUILDING", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(firstArg))
                                    objects.Add(RawObject.PermittedBuilding(firstArg, sourceMod));
                                continue;
                            }

                            // [BUILDING:ID] inside a reaction lists the workshops that can run it.
                            // DF reports "Unrecognized Building Token" when the id has no definition.
                            if (string.Equals(tag, "BUILDING", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(currentObjectType, "REACTION", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(firstArg))
                                    objects.Add(RawObject.ReactionBuilding(firstArg, sourceMod));
                                continue;
                            }

                            // Direct definition: the tag matches the active [OBJECT:TYPE].
                            // Buildings and items are declared below their object type:
                            // [OBJECT:BUILDING] uses [BUILDING_WORKSHOP:ID]/[BUILDING_FURNACE:ID]
                            // and [OBJECT:ITEM] uses [ITEM_WEAPON:ID], [ITEM_AMMO:ID], ...
                            bool isBuildingDefinition =
                                string.Equals(currentObjectType, "BUILDING", StringComparison.OrdinalIgnoreCase)
                                && tag.StartsWith("BUILDING_", StringComparison.OrdinalIgnoreCase);
                            bool isItemDefinition =
                                string.Equals(currentObjectType, "ITEM", StringComparison.OrdinalIgnoreCase)
                                && tag.StartsWith("ITEM_", StringComparison.OrdinalIgnoreCase);

                            // [REACTION:ID] always starts a reaction definition, even when a mod
                            // put it under the wrong [OBJECT:TYPE] header (some files say
                            // [OBJECT:INORGANIC] or [OBJECT:ENTITY] by mistake). Unlike CREATURE,
                            // BODY, SOUND, and similar tags, the bare REACTION tag is never used as
                            // a nested property or reference; permitted reactions use
                            // [PERMITTED_REACTION:ID].
                            bool isReactionDefinition =
                                string.Equals(tag, "REACTION", StringComparison.OrdinalIgnoreCase);

                            if (isBuildingDefinition
                                || isItemDefinition
                                || isReactionDefinition
                                || (!string.IsNullOrWhiteSpace(currentObjectType)
                                    && string.Equals(tag, currentObjectType, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!string.IsNullOrWhiteSpace(firstArg))
                                {
                                    string objectType;
                                    if (isBuildingDefinition)
                                        objectType = "BUILDING";
                                    else if (isItemDefinition)
                                        objectType = "ITEM";
                                    else if (isReactionDefinition)
                                        objectType = "REACTION";
                                    else
                                        objectType = currentObjectType!;

                                    objects.Add(RawObject.Definition(objectType, firstArg, sourceMod));

                                    // A reaction definition under a wrong header still starts a
                                    // reaction object; nested tokens such as [BUILDING:...] belong
                                    // to that reaction, not to the file's declared object type.
                                    if (isReactionDefinition)
                                        currentObjectType = "REACTION";
                                }

                                currentSelectObjectType = null;
                                currentSelectId = null;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore unreadable objects/ folders; return the objects we parsed.
                }
            }

            return new RawDatabase(sourceMod, objects, isCutter, hasGraphics);
        }

        // Raw tags never span lines. Excluding '[' as well keeps an unterminated tag from
        // swallowing the next tag on a later line (seen in mods with a missing ']').
        [GeneratedRegex(@"\[([A-Za-z_][A-Za-z0-9_]*)(?::([^\[\]\r\n]*))?\]", RegexOptions.Compiled)]
        private static partial Regex TagRegex();
    }
}