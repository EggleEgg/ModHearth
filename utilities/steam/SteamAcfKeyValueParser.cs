using System;
using System.Collections.Generic;
using System.Text;

namespace ModHearth.Utilities;

/// <summary>
/// Parses Valve KeyValue (VDF/ACF) text such as Steam's appworkshop_*.acf files.
/// Supports quoted strings with \\ and \" escapes, // line comments, and brace-nested sections.
/// </summary>
internal static class SteamAcfKeyValueParser
{
    public static Dictionary<string, object> Parse(string content)
    {
        Dictionary<string, object> root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        Stack<Dictionary<string, object>> stack = new Stack<Dictionary<string, object>>();
        Dictionary<string, object> current = root;
        string? currentKey = null;

        foreach (string token in Tokenize(content))
        {
            if (token == "{")
            {
                Dictionary<string, object> child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(currentKey))
                {
                    current[currentKey] = child;
                    currentKey = null;
                }

                stack.Push(current);
                current = child;
                continue;
            }

            if (token == "}")
            {
                if (stack.Count > 0)
                    current = stack.Pop();
                continue;
            }

            if (currentKey == null)
                currentKey = token;
            else
            {
                current[currentKey] = token;
                currentKey = null;
            }
        }

        return root;
    }

    private static IEnumerable<string> Tokenize(string content)
    {
        if (string.IsNullOrEmpty(content))
            yield break;

        StringBuilder builder = new StringBuilder();
        bool inString = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (!inString)
            {
                if (c == '/' && i + 1 < content.Length && content[i + 1] == '/')
                {
                    while (i < content.Length && content[i] != '\n')
                        i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                    continue;

                if (c == '{' || c == '}')
                {
                    yield return c.ToString();
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    builder.Clear();
                }

                continue;
            }

            if (c == '"')
            {
                inString = false;
                yield return builder.ToString();
                continue;
            }

            if (c == '\\' && i + 1 < content.Length)
            {
                char next = content[i + 1];
                if (next == '"' || next == '\\')
                {
                    builder.Append(next);
                    i++;
                    continue;
                }
            }

            builder.Append(c);
        }
    }
}
