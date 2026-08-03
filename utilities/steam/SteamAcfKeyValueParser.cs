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
            switch (token)
            {
                case "{":
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

                case "}":
                    if (stack.Count > 0)
                        current = stack.Pop();
                    continue;
            }

            switch (currentKey)
            {
                case null:
                    currentKey = token;
                    break;
                default:
                    current[currentKey] = token;
                    currentKey = null;
                    break;
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
        int i = 0;
        int length = content.Length;

        while (i < length)
        {
            char c = content[i];

            if (!inString)
            {
                if (c == '/' && i + 1 < length && content[i + 1] == '/')
                {
                    int j = i + 1;
                    while (j < length && content[j] != '\n')
                        j++;

                    i = j;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '{':
                    case '}':
                        yield return c.ToString();
                        i++;
                        continue;
                    case '"':
                        inString = true;
                        _ = builder.Clear();
                        i++;
                        continue;
                }

                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = false;
                    yield return builder.ToString();
                    i++;
                    continue;
                case '\\' when i + 1 < length:
                    {
                        char next = content[i + 1];
                        switch (next)
                        {
                            case '"':
                            case '\\':
                                _ = builder.Append(next);
                                i += 2;
                                continue;
                        }

                        break;
                    }
            }

            _ = builder.Append(c);
            i++;
        }
    }
}
