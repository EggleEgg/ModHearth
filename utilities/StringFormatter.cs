namespace ModHearth.Utilities;

public static class StringFormatter
{
    /// <summary>
    /// For UI display, truncates string and adds "..." if it exceeds a maximum length.
    /// </summary>
    public static string TruncateForDisplay(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string text = value.Trim();
        if (text.Length <= maxLength)
            return text;

        return text[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// For logging, truncates string to a fixed character length and escapes newlines.
    /// </summary>
    public static string TrimForLog(string? value, int maxLength = 80)
    {
        string text = value ?? string.Empty;
        text = text.Replace("\r", "\\r").Replace("\n", "\\n");
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    /// <summary>
    /// For formatting lists with a "more" indicator.
    /// </summary>
    public static string FormatListWithMoreIndicator(IEnumerable<string>? items, int maxItems)
    {
        if (items == null)
            return "(none)";

        List<string> list = items.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

        if (list.Count == 0)
            return "(none)";

        if (list.Count <= maxItems)
            return string.Join(" | ", list);

        IEnumerable<string> shown = list.Take(maxItems);
        return $"{string.Join(" | ", shown)} | ... (+{list.Count - maxItems} more)";
    }
}
