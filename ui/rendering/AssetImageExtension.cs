using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;

namespace ModHearth.UI;

/// <summary> 
/// Nifty tool to shorten uri from svg paths
/// </summary>
public sealed class AssetImageExtension : MarkupExtension
{
    public AssetImageExtension()
    {
    }

    public AssetImageExtension(string source)
    {
        Source = source;
    }

    public string? Source { get; set; }
    public double? Opacity { get; set; }
    public Color? Tint { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Source))
            return AvaloniaProperty.UnsetValue;

        return ImageSourceLoader.LoadFromAssetUri(Source, Tint, Opacity) ?? AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Normalizes image formats from the uri and caches them
/// </summary>
internal static class ImageSourceLoader
{
    private const string DefaultResourcesBaseUri = "avares://ModHearth/resources/";
    private const string AlternateResourcesBaseUri = "avares://ModHearth/Resources/";

    /// <summary>
    /// Use this in your avalonia element constructor. String input as resource path
    /// </summary>
    public static Image CreateAvaloniaImage(string iconResourceName, int width = 16, int height = 16)
    {
        var image = new Image
        {
            Width = width,
            Height = height,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Source = LoadFromAssetUri(iconResourceName)
        };

        return image;
    }

    // Using Lazy<IImage?> guarantees the factory logic runs exactly once per unique key
    private static readonly ConcurrentDictionary<string, Lazy<IImage?>> imageCache = new();

    private static readonly Lazy<IImage?> missingTextureImage = new(() =>
    {
        try
        {
            string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"#FF00FF\"/></svg>";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svg));
            return RenderSvgStream(stream);
        }
        catch
        {
            return null;
        }
    });

    private static IImage? GetMissingTextureImage() => missingTextureImage.Value;

    public static IImage? LoadFromAssetUri(string assetUri, Color? tint = null, double? opacity = null)
    {
        string normalized = NormalizeAssetUri(assetUri);
        if (string.IsNullOrWhiteSpace(normalized))
            return GetMissingTextureImage();

        string key;
        if (tint.HasValue || opacity.HasValue)
        {
            string opacityPart = opacity.HasValue ? opacity.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            key = $"{normalized}|{tint?.ToString()}|{opacityPart}";
        }
        else
            key = normalized;

        // GetOrAdd is fast because creating Lazy is cheap.
        // The heavy loading inside Lazy only happens when .Value is accessed.
        return imageCache.GetOrAdd(key, k => new Lazy<IImage?>(() =>
        {
            if ((tint.HasValue || opacity.HasValue) && IsSvgPath(normalized))
            {
                var tinted = LoadTintedSvg(normalized, tint, opacity);
                if (tinted != null)
                    return tinted;
            }

            // Try primary URI
            IImage? primary = LoadFromNormalizedAssetUri(normalized);
            if (primary != null)
                return primary;

            // Try swap alternate base URI
            string? alternate = TrySwapResourcesBase(normalized);
            if (!string.IsNullOrWhiteSpace(alternate))
            {
                IImage? alternateImage = LoadFromNormalizedAssetUri(alternate);
                if (alternateImage != null)
                    return alternateImage;
            }

            return GetMissingTextureImage();
        })).Value;
    }

    /// <summary>
    /// Checks if an SVG color value represents black.
    /// </summary>
    private static bool IsPureBlack(string? colorValue)
    {
        if (string.IsNullOrWhiteSpace(colorValue))
            return false;

        string val = colorValue.Trim().ToLowerInvariant();

        if (val is "#000" or "#000000" or "#000000ff" or "black")
            return true;

        if (val.StartsWith("rgb", StringComparison.Ordinal))
        {
            string cleaned = val.Replace("rgba(", "").Replace("rgb(", "").Replace(")", "").Trim();
            string[] parts = cleaned.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0] == "0" && parts[1] == "0" && parts[2] == "0")
            {
                // If alpha channel is specified, ensure it's fully opaque
                switch (parts.Length)
                {
                    case 4:
                        return parts[3] is "1" or "1.0" or "100%" or "255";
                    default:
                        return true;
                }
            }
        }

        return false;
    }

    private static IImage? LoadTintedSvg(string assetUri, Color? tint = null, double? opacity = null)
    {
        try
        {
            Uri uri = new Uri(assetUri, UriKind.Absolute);
            using Stream stream = AssetLoader.Open(uri);
            var xdoc = XDocument.Load(stream);

            if (tint.HasValue)
            {
                var hexColor = $"#{tint.Value.R:X2}{tint.Value.G:X2}{tint.Value.B:X2}";

                foreach (var element in xdoc.Descendants())
                {
                    var name = element.Name.LocalName.ToLowerInvariant();
                    if (name is not ("path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "text"))
                        continue;

                    var fillAttr = element.Attribute("fill");
                    var strokeAttr = element.Attribute("stroke");
                    var styleAttr = element.Attribute("style");

                    // If it has NO attributes related to color, SVG specification defaults to black fill.
                    if (fillAttr == null && strokeAttr == null && styleAttr == null)
                    {
                        element.SetAttributeValue("fill", hexColor);
                        continue;
                    }

                    // Only tint if the fill attribute is explicitly black
                    if (fillAttr != null && IsPureBlack(fillAttr.Value))
                        fillAttr.Value = hexColor;

                    // Only tint if the stroke attribute is explicitly black
                    if (strokeAttr != null && IsPureBlack(strokeAttr.Value))
                        strokeAttr.Value = hexColor;

                    // Parse inline style="..." declarations for black fill/stroke rules
                    if (styleAttr != null)
                    {
                        string val = styleAttr.Value;
                        val = System.Text.RegularExpressions.Regex.Replace(
                            val,
                            @"(?<prefix>fill|stroke)\s*:\s*(?<color>[^;]+)",
                            match =>
                            {
                                string prefix = match.Groups["prefix"].Value;
                                string rawColor = match.Groups["color"].Value;
                                return IsPureBlack(rawColor) ? $"{prefix}:{hexColor}" : match.Value;
                            },
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        styleAttr.Value = val;
                    }
                }
            }

            if (opacity.HasValue)
            {
                var svgRoot = xdoc.Root;
                if (svgRoot != null)
                {
                    svgRoot.SetAttributeValue("opacity", opacity.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            using var memoryStream = new MemoryStream();
            xdoc.Save(memoryStream);
            memoryStream.Position = 0;
            return RenderSvgStream(memoryStream);
        }
        catch
        {
            return null;
        }
    }

    public static IImage? LoadFromAssetUriUncached(string assetUri)
    {
        string normalized = NormalizeAssetUri(assetUri);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return LoadFromNormalizedAssetUri(normalized);
    }

    // Bypasses caching entirely. This is fine for mod previews or anything that isnt called that often
    public static IImage? LoadFromFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            string fullPath = Path.GetFullPath(path);
            if (IsSvgPath(path))
                return LoadSvgImage(new Uri(fullPath, UriKind.Absolute).ToString());

            return new Bitmap(fullPath);
        }
        catch
        {
            return null;
        }
    }

    private static IImage? LoadFromNormalizedAssetUri(string normalizedAssetUri)
    {
        try
        {
            if (IsSvgPath(normalizedAssetUri))
            {
                IImage? svgImage = LoadSvgImage(normalizedAssetUri);
                if (svgImage != null)
                    return svgImage;

                string pngFallback = ReplaceExtension(normalizedAssetUri, ".png");
                return LoadBitmap(pngFallback);
            }

            string svgCandidate = ReplaceExtension(normalizedAssetUri, ".svg");
            IImage? svgCandidateImage = LoadSvgImage(svgCandidate);
            if (svgCandidateImage != null)
                return svgCandidateImage;

            return LoadBitmap(normalizedAssetUri);
        }
        catch
        {
            return null;
        }
    }

    private static string? TrySwapResourcesBase(string normalizedAssetUri)
    {
        if (normalizedAssetUri.StartsWith(DefaultResourcesBaseUri, StringComparison.Ordinal))
            return AlternateResourcesBaseUri + normalizedAssetUri.Substring(DefaultResourcesBaseUri.Length);

        if (normalizedAssetUri.StartsWith(AlternateResourcesBaseUri, StringComparison.Ordinal))
            return DefaultResourcesBaseUri + normalizedAssetUri.Substring(AlternateResourcesBaseUri.Length);

        return null;
    }

    public static string NormalizeAssetUri(string assetUri)
    {
        if (string.IsNullOrWhiteSpace(assetUri))
            return string.Empty;

        string normalized = assetUri.Trim();
        if (normalized.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            return EnsureSvgExtensionIfMissing(normalized);

        if (Uri.TryCreate(normalized, UriKind.Absolute, out _))
            return normalized;

        normalized = normalized.Replace('\\', '/').TrimStart('/');
        const string resourcesPrefix = "resources/";
        if (normalized.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(resourcesPrefix.Length);

        normalized = EnsureSvgExtensionIfMissing(normalized);
        return DefaultResourcesBaseUri + normalized;
    }

    private static bool IsSvgPath(string path)
        => RemoveQueryAndFragment(path).EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    private static IImage? LoadSvgImage(string uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText))
            return null;

        try
        {
            if (uriText.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                Uri assetUri = new Uri(uriText, UriKind.Absolute);
                using Stream stream = AssetLoader.Open(assetUri);
                return RenderSvgStream(stream);
            }

            if (Uri.TryCreate(uriText, UriKind.Absolute, out Uri? absoluteUri) && absoluteUri.IsFile)
                return RenderSvgFile(absoluteUri.LocalPath);

            if (File.Exists(uriText))
                return RenderSvgFile(uriText);
        }
        catch
        {
            // Ignore SVG load failures.
        }

        return null;
    }

    private static IImage? LoadBitmap(string assetUri)
    {
        if (string.IsNullOrWhiteSpace(assetUri))
            return null;

        try
        {
            if (assetUri.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                Uri uri = new Uri(assetUri, UriKind.Absolute);
                using Stream stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }

            if (Uri.TryCreate(assetUri, UriKind.Absolute, out Uri? absoluteUri) &&
                absoluteUri.IsFile &&
                File.Exists(absoluteUri.LocalPath))
            {
                return new Bitmap(absoluteUri.LocalPath);
            }

            if (File.Exists(assetUri))
                return new Bitmap(assetUri);
        }
        catch
        {
            // Ignore bitmap load failures.
        }

        return null;
    }

    private static string ReplaceExtension(string pathOrUri, string newExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrUri))
            return pathOrUri;

        string queryAndFragment = string.Empty;
        int queryIndex = pathOrUri.IndexOfAny(['?', '#']);
        string basePart = pathOrUri;
        if (queryIndex >= 0)
        {
            basePart = pathOrUri.Substring(0, queryIndex);
            queryAndFragment = pathOrUri.Substring(queryIndex);
        }

        int dotIndex = basePart.LastIndexOf('.');
        switch (dotIndex)
        {
            case < 0:
                return basePart + newExtension + queryAndFragment;
            default:
                return basePart.Substring(0, dotIndex) + newExtension + queryAndFragment;
        }
    }

    private static string EnsureSvgExtensionIfMissing(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (!string.IsNullOrWhiteSpace(Path.GetExtension(RemoveQueryAndFragment(value))))
            return value;

        return value + ".svg";
    }

    private static string RemoveQueryAndFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        int index = value.IndexOfAny(['?', '#']);
        switch (index)
        {
            case < 0:
                return value;
            default:
                return value.Substring(0, index);
        }
    }

    private static IImage? RenderSvgFile(string path)
    {
        try
        {
            using Stream stream = File.OpenRead(path);
            return RenderSvgStream(stream);
        }
        catch
        {
            return null;
        }
    }

    private static IImage? RenderSvgStream(Stream stream)
    {
        try
        {
            SvgSource? source = SvgSource.LoadFromStream(stream, null);
            switch (source)
            {
                case null:
                    return null;
                default:
                    return new SvgImage { Source = source };
            }
        }
        catch
        {
            return null;
        }
    }
    public static void ClearCache()
    {
        imageCache.Clear();
    }
}
public sealed class TintedAssetConverter : IValueConverter
{
    public static TintedAssetConverter Instance { get; } = new TintedAssetConverter();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string assetName)
            return null;

        Color? tint = null;
        switch (value)
        {
            case ISolidColorBrush brush:
                tint = brush.Color;
                break;
            case Color color:
                tint = color;
                break;
        }

        return ImageSourceLoader.LoadFromAssetUri(assetName, tint);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
