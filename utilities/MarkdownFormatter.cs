using System.Text;
using System.Text.RegularExpressions;

namespace ModHearth.Utilities
{
    // <summary>
    // Used to extract usable text from the readme. Unlike the html renderer it does not create new containers/panels
    // </summary>
    public static partial class MarkdownFormatter
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

                Match headingMatch = MyRegex().Match(trimmed);
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
                    _ = builder.AppendLine(DecodeInlineMarkdown(trimmed[2..]).Trim());
                    continue;
                }

                Match numberedMatch = MyRegex1().Match(trimmed);
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
            output = MyRegex2().Replace(output, "${label} (${url})");
            // `code` -> code
            output = MyRegex3().Replace(output, "$1");
            // **bold** / __bold__ -> bold
            output = MyRegex4().Replace(output, "$1");
            output = MyRegex5().Replace(output, "$1");
            // *italic* / _italic_ -> italic
            output = MyRegex6().Replace(output, "$1");
            output = MyRegex7().Replace(output, "$1");

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

                    string title = trimmed[level..].Trim();
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

        [GeneratedRegex(@"^(?<level>#{1,6})\s+(?<title>.+)$")]
        private static partial Regex MyRegex();
        [GeneratedRegex(@"^(?<num>\d+)\.\s+(?<text>.+)$")]
        private static partial Regex MyRegex1();
        [GeneratedRegex(@"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)")]
        private static partial Regex MyRegex2();
        [GeneratedRegex(@"`([^`]+)`")]
        private static partial Regex MyRegex3();
        [GeneratedRegex(@"\*\*(.+?)\*\*")]
        private static partial Regex MyRegex4();
        [GeneratedRegex(@"__(.+?)__")]
        private static partial Regex MyRegex5();
        [GeneratedRegex(@"\*(.+?)\*")]
        private static partial Regex MyRegex6();
        [GeneratedRegex(@"_(.+?)_")]
        private static partial Regex MyRegex7();
    }
}