using ModHearth.Utilities;

namespace ModHearth.UI;

/// <summary>
/// Provides content for MainWindow default description, extracted from the README.md file.
/// </summary>
internal static class MainWindowHelpContent
{
    private static string? cachedReadmeText;

    public static string GetHelpText()
    {
        cachedReadmeText ??= BuildHelpTextFromReadme();
        return cachedReadmeText;
    }

    public static string? GetCachedReadmeText() => cachedReadmeText;

    private static string BuildHelpTextFromReadme()
    {
        string? readmePath = FindReadmePath();
        if (string.IsNullOrWhiteSpace(readmePath) || !File.Exists(readmePath))
            return "README.md not found. Open README for instructions and shortcuts.";

        try
        {
            string markdown = File.ReadAllText(readmePath);
            string instructions = MarkdownFormatter.ExtractMarkdownSection(markdown, "Instructions");
            string controls = MarkdownFormatter.ExtractMarkdownSection(markdown, "Keyboard Shortcuts and Controls");

            List<string> parts = [];
            if (!string.IsNullOrWhiteSpace(instructions))
                parts.Add($"### Instructions{Environment.NewLine}{instructions}");
            if (!string.IsNullOrWhiteSpace(controls))
                parts.Add($"### Keyboard Shortcuts and Controls{Environment.NewLine}{controls}");

            if (parts.Count > 0)
                return MarkdownFormatter.RenderBasicMarkdownToText(string.Join($"{Environment.NewLine}{Environment.NewLine}", parts));
        }
        catch
        {
            // Ignore README parsing failures and fall back to a short message.
        }

        return "Unable to read README sections. Open README for instructions and shortcuts.";
    }

    private static string? FindReadmePath()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "README.md"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "README.md")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "README.md"))
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
