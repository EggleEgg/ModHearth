using System.Text.RegularExpressions;

namespace ModHearth.Utilities;

/// <summary>
/// Scans a single mod's objects/ folder to extract the DF raw-file relationships
/// ModSort's dependency graph needs. The scanner is data-driven: it derives the
/// active object type from [OBJECT:TYPE] blocks and treats SELECT_*, CUT_*, and
/// CUT as generic operations, so it works with new or modded object types without
/// an explicit whitelist.
/// </summary>
internal static class ModRawObjectScanner
{
    // Matches a bracketed raw tag: [TAG_NAME] or [TAG_NAME:arg1:arg2:...]
    private static readonly Regex TagRegex = new(@"\[([A-Za-z_][A-Za-z0-9_]*)(?::([^\]]*))?\]", RegexOptions.Compiled);

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
                    catch
                    {
                        continue;
                    }

                    // Active object type from the most recent [OBJECT:TYPE] block.
                    string? currentObjectType = null;

                    // The most recent SELECT_ target. A plain [CUT] token ends this
                    // selection and applies the cut to it.
                    string? currentSelectObjectType = null;
                    string? currentSelectId = null;

                    foreach (var groups in TagRegex.Matches(text).Select(match => match.Groups))
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
                            string objectType = tag.Substring("SELECT_".Length);
                            if (!string.IsNullOrWhiteSpace(objectType))
                            {
                                objects.Add(new RawObject(objectType, firstArg, sourceMod, false, true, false));
                                currentSelectObjectType = objectType;
                                currentSelectId = firstArg;
                            }
                            continue;
                        }

                        // CUT_<OBJECT_TYPE>:ID -> generic standalone cut.
                        if (tag.StartsWith("CUT_", StringComparison.OrdinalIgnoreCase))
                        {
                            isCutter = true;
                            string objectType = tag.Substring("CUT_".Length);
                            if (!string.IsNullOrWhiteSpace(objectType))
                                objects.Add(new RawObject(objectType, firstArg, sourceMod, false, false, true));
                            continue;
                        }

                        // [CUT] ends the current SELECT_ block and cuts its target.
                        if (string.Equals(tag, "CUT", StringComparison.OrdinalIgnoreCase))
                        {
                            isCutter = true;
                            if (!string.IsNullOrWhiteSpace(currentSelectObjectType)
                                && !string.IsNullOrWhiteSpace(currentSelectId))
                            {
                                objects.Add(new RawObject(currentSelectObjectType, currentSelectId, sourceMod, false, false, true));
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
                            {
                                objects.Add(new RawObject(currentObjectType ?? string.Empty, firstArg, sourceMod, false, false, false)
                                {
                                    IsCopyTagsFrom = true
                                });
                            }
                            continue;
                        }

                        // Direct definition: the tag matches the active [OBJECT:TYPE].
                        if (!string.IsNullOrWhiteSpace(currentObjectType)
                            && string.Equals(tag, currentObjectType, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(firstArg))
                                objects.Add(new RawObject(currentObjectType, firstArg, sourceMod, true, false, false));

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
}