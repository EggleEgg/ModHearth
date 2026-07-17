using CodeKicker.BBCode;
using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ModHearth.UI;

/// <summary>
/// Converts Steam Workshop BBCode to a styled HTML document for display in HtmlPanel.
///
/// Supported tags:
///   Formatting : [b] [i] [u] [s] [strike]
///   Headings   : [h1] [h2] [h3]
///   Lists      : [list] [olist] [*]      [*] does not need [/*]
///   Blocks     : [code] [quote] [spoiler] [noparse]
///   Alignment  : [center] [left] [right]
///   Inline     : [color=] [size=]
///   Links      : [url=] [url]
///   Media      : [img]
///   Table      : [table] [tr] [th] [td]
///   Misc       : [hr]
/// </summary>
public static class BBCodeRenderer
{
    // Avalonia.HtmlRenderer has a rendering quirk where the document's true first line gets correct positioning, but every line after it; 
    // whether from a literal newline or the engine's own automatic word- wrap. Renders with an unwanted left offset. Confirmed via testing
    // that this isn't CSS-controllable (text-indent:0 has zero effect), so this works around it instead of trying to fix it

    // This value is hand-tuned, not computed
    private const double LeadingLineFixOffsetPx = -0.7;

    private static readonly BBCodeParser Parser = BuildParser();

    // Matches pre-existing protected blocks (Capture Group 1) OR unformatted raw URLs (Capture Group 2)
    private static readonly Regex AutoLinkRegex = new(
        @"(\[url[=\]].*?\[/url\]|\[img\].*?\[/img\]|\[code\].*?\[/code\]|\[noparse\].*?\[/noparse\])|((?:https?|steam)://[^\s[\]<>""']+(?<![.,:;?!)]))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static BBCodeParser BuildParser()
    {
        BBTag[] tags =
        [
            // ── Text formatting ──────────────────────────────────────────────────
            new BBTag("b",      "<strong>", "</strong>"),
            new BBTag("i",      "<em>",     "</em>"),
            new BBTag("u",      "<u>",      "</u>"),
            new BBTag("s",      "<s>",      "</s>"),
            new BBTag("strike", "<s>",      "</s>"),

            // ── Headings ─────────────────────────────────────────────────────────
            new BBTag("h1", "<h1>", "</h1>"),
            new BBTag("h2", "<h2>", "</h2>"),
            new BBTag("h3", "<h3>", "</h3>"),

            // ── Lists ────────────────────────────────────────────────────────────
            // requireClosingTag:false on [*] lets items auto-close at the next [*]
            // or at [/list]/[/olist] without needing an explicit [/*].
            new BBTag("list",  "<ul>", "</ul>"),
            new BBTag("olist", "<ol>", "</ol>"),
            new BBTag("*",     "<li>", "</li>",
                autoRenderContent: true,
                requireClosingTag: false),

            // ── Blocks ───────────────────────────────────────────────────────────
            new BBTag("code",    "<pre>",         "</pre>"),
            new BBTag("quote",   "<blockquote>",  "</blockquote>"),
            new BBTag("spoiler", "<details><summary>Spoiler</summary>", "</details>"),
            new BBTag("noparse", "",               ""),   // pass content through unmodified

            // ── Alignment ────────────────────────────────────────────────────────
            new BBTag("center", "<div style=\"text-align:center\">", "</div>"),
            new BBTag("left",   "<div style=\"text-align:left\">",   "</div>"),
            new BBTag("right",  "<div style=\"text-align:right\">",  "</div>"),

            // ── Self-closing ─────────────────────────────────────────────────────
            new BBTag("hr", "<hr/>", string.Empty,
                autoRenderContent: false,
                requireClosingTag: false),

            // ── Parameterised ────────────────────────────────────────────────────
            // Two BBAttribute entries per tag handle both [tag=value] and [tag value=x].
            new BBTag("url",
                "<a href=\"${href}\">", "</a>",
                autoRenderContent: true, requireClosingTag: true,
                new BBAttribute("href", ""),
                new BBAttribute("href", "href")),

            new BBTag("img",
                "<img src=\"${src}\" style=\"max-width:100%\"/>", string.Empty,
                autoRenderContent: false, requireClosingTag: false,
                new BBAttribute("src", "")),

            new BBTag("color",
                "<span style=\"color:${color}\">", "</span>",
                autoRenderContent: true, requireClosingTag: true,
                new BBAttribute("color", ""),
                new BBAttribute("color", "color")),

            new BBTag("size",
                "<span style=\"font-size:${size}pt\">", "</span>",
                autoRenderContent: true, requireClosingTag: true,
                new BBAttribute("size", ""),
                new BBAttribute("size", "size")),

            // ── Table ────────────────────────────────────────────────────────────
            new BBTag("table", "<table>", "</table>"),
            new BBTag("tr",    "<tr>",    "</tr>"),
            new BBTag("th",    "<th>",    "</th>"),
            new BBTag("td",    "<td>",    "</td>"),
        ];

        // ErrorMode.ErrorFree silently skips malformed or unknown tags rather than
        // throwing, which is important for user-written mod descriptions.
        return new BBCodeParser(ErrorMode.ErrorFree, null, tags);
    }

    /// <summary>
    /// Safely auto-formats raw web and steam protocol URLs into [url] tags,
    /// excluding content already wrapped inside url, img, code, or noparse blocks.
    /// </summary>
    private static string AutoFormatRawUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        return AutoLinkRegex.Replace(text, match =>
        {
            // If Group 1 succeeded, it matched a protected block. Leave it as-is.
            if (match.Groups[1].Success)
            {
                return match.Value;
            }

            // Group 2 succeeded: it's a raw unformatted link. Wrap it safely.
            string rawUrl = match.Groups[2].Value;
            return $"[url={rawUrl}]{rawUrl}[/url]";
        });
    }

    // ── Public API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses BBCode and returns a full styled HTML document.
    /// Accepts SimpleColor for direct integration with the app's Style system.
    /// Falls back to HTML-escaped plain text if the parser throws.
    /// </summary>
    public static string ToHtml(string? bbcode, SimpleColor textColor, SimpleColor panelColor)
        => ToHtml(bbcode, SimpleColor.ToHex(textColor), SimpleColor.ToHex(panelColor));

    public static string ToHtml(
        string? bbcode,
        string textColor = "#000000",
        string backgroundColor = "transparent")
    {
        string body;
        if (string.IsNullOrWhiteSpace(bbcode))
        {
            body = string.Empty;
        }
        else
        {
            try
            {
                // Normalise line endings before parsing.
                string normalized = bbcode.Replace("\r\n", "\n").Replace("\r", "\n");

                // Safely convert raw URLs into BBCode nodes before parsing.
                string autoLinked = AutoFormatRawUrls(normalized);

                body = Parser.ToHtml(autoLinked);
            }
            catch
            {
                // If the parser fails entirely, display as escaped plain text.
                body = WebUtility.HtmlEncode(bbcode).Replace("\n", "<br/>");
            }
        }

        return BuildDocument(body, textColor, backgroundColor);
    }

    /// <summary>
    /// Converts plain text (no BBCode) to HTML. Escapes entities and maps newlines to &lt;br/&gt;. Used for the readme-based fallback help text.
    /// </summary>
    public static string PlainTextToHtml(
        string? text,
        string textColor = "#000000",
        string backgroundColor = "transparent")
    {
        string body = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : WebUtility.HtmlEncode(text).Replace("\n", "<br/>");

        return BuildDocument(body, textColor, backgroundColor);
    }

    // ── Document builder ─────────────────────────────────────────────────────────────

    private static string BuildDocument(string body, string textColor, string backgroundColor)
    {
        // Explicitly format the double values using the Invariant Culture (forces "." instead of ",")
        string bodyLeftMargin = (8 + Math.Abs(LeadingLineFixOffsetPx)).ToString("F1", CultureInfo.InvariantCulture);
        string divLeftMargin = LeadingLineFixOffsetPx.ToString("F1", CultureInfo.InvariantCulture);

        return $@"
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset=""utf-8""/>
        <style>
        /* 3. Inject the web-safe invariant string values here */
        body            {{ font-family:sans-serif; font-size:13px; color:{textColor}; background:{backgroundColor}; margin:4px 8px; margin-left:{bodyLeftMargin}px; padding:0; word-wrap:break-word; }}
        h1              {{ font-size:1.4em;  margin:10px 0 4px; }}
        h2              {{ font-size:1.2em;  margin:8px 0 4px; }}
        h3              {{ font-size:1.05em; margin:6px 0 3px; }}
        ul, ol          {{ margin:4px 0; padding-left:20px; }}
        li              {{ margin:2px 0; }}
        a               {{ color:#4ea0d1; }}
        pre             {{ white-space:pre-wrap; font-family:monospace; background:rgba(128,128,128,0.15); padding:6px; border-radius:4px; }}
        blockquote      {{ border-left:3px solid #888; margin:4px 0 4px 8px; padding-left:8px; opacity:0.85; }}
        hr              {{ border:none; border-top:1px solid #888; margin:8px 0; }}
        table           {{ border-collapse:collapse; margin:4px 0; }}
        th, td          {{ border:1px solid #888; padding:4px 8px; }}
        details         {{ margin:4px 0; }}
        details summary {{ cursor:pointer; opacity:0.7; }}
        </style>
        </head>
        <body>
        <div style=""margin:0;padding:0;font-size:1px;line-height:1px;"">&nbsp;</div>
        <div style=""margin-left:{divLeftMargin}px;"">{body}</div>
        </body>
        </html>";
    }
}