using System.Text;
using System.Text.RegularExpressions;

namespace ModHearth.Utilities;

// <summary>
// Used to extract usable text from the readme. Unlike the html renderer it does not create new containers/panels
// </summary>
public static class MarkdownFormatter
{
    public static string RenderBasicMarkdownToText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        StringBuilder builder = new();
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                _ = builder.AppendLine();
                continue;
            }

            Match headingMatch = Regex.Match(trimmed, @"^(?<level>#{1,6})\s+(?<title>.+)$");
            if (headingMatch.Success)
            {
                string heading = DecodeInlineMarkdown(headingMatch.Groups["title"].Value).Trim();
                _ = builder.AppendLine();
                _ = builder.AppendLine(heading);
                //size of underline
                _ = builder.AppendLine(new string('-', 50));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                _ = builder.Append("- ");
                _ = builder.AppendLine(DecodeInlineMarkdown(trimmed.Substring(2)).Trim());
                continue;
            }

            Match numberedMatch = Regex.Match(trimmed, @"^(?<num>\d+)\.\s+(?<text>.+)$");
            if (numberedMatch.Success)
            {
                _ = builder.Append(numberedMatch.Groups["num"].Value);
                _ = builder.Append(". ");
                _ = builder.AppendLine(DecodeInlineMarkdown(numberedMatch.Groups["text"].Value).Trim());
                continue;
            }

            _ = builder.AppendLine(DecodeInlineMarkdown(trimmed));
        }

        return builder.ToString().Trim();
    }

    public static string DecodeInlineMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string output = text;

        // [label](url) -> label (url)
        output = Regex.Replace(output, @"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)", "${label} (${url})");
        // `code` -> code
        output = Regex.Replace(output, @"`([^`]+)`", "$1");
        // **bold** / __bold__ -> bold
        output = Regex.Replace(output, @"\*\*(.+?)\*\*", "$1");
        output = Regex.Replace(output, @"__(.+?)__", "$1");
        // *italic* / _italic_ -> italic
        output = Regex.Replace(output, @"\*(.+?)\*", "$1");
        output = Regex.Replace(output, @"_(.+?)_", "$1");

        return output;
    }

    public static string ExtractMarkdownSection(string markdown, string sectionTitle)
    {
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        StringBuilder builder = new();
        bool inSection = false;
        int sectionLevel = 0;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#')
                    level++;

                string title = trimmed.Substring(level).Trim();
                if (!inSection)
                {
                    if (string.Equals(title, sectionTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        sectionLevel = level;
                    }
                    continue;
                }

                if (level <= sectionLevel)
                    break;
            }

            if (!inSection)
                continue;

            _ = builder.AppendLine(line);
        }

        return builder.ToString().Trim();
    }
}