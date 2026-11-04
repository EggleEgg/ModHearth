using System.Globalization;
using System.Text.RegularExpressions;
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
using ModHearth.Utilities;

namespace ModHearth.UI.Rendering
{
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
    internal static partial class ImageSourceLoader
    {
        private const string DefaultResourcesBaseUri = "avares://ModHearth/resources/";
        private const string AlternateResourcesBaseUri = "avares://ModHearth/Resources/";

        /// <summary>
        /// Use this in your avalonia element constructor. String input as resource path
        /// </summary>
        public static Image CreateAvaloniaImage(string iconResourceName, int width = 16, int height = 16)
        {
            Image image = new()

            {
                Width = width,
                Height = height,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Source = LoadFromAssetUri(iconResourceName)
            };

            return image;
        }

        // Bounded so tinted variants and one-off assets cannot grow the cache without limit.
        // Evicted images are not disposed: they may still be bound to live controls.
        private const int MaxCachedImages = 256;
        private static readonly BoundedLruCache<string, IImage?> imageCache = new(MaxCachedImages, StringComparer.Ordinal);

        private static readonly Lazy<IImage?> missingTextureImage = new(() =>
        {
            try
            {
                string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"#FF00FF\"/></svg>";
                using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(svg));
                return RenderSvgStream(stream);
            }
            catch (Exception ex)
            {
                LogLoadFailure("missing texture placeholder", ex);
                return null;
            }
        });

        private static IImage? GetMissingTextureImage() => missingTextureImage.Value;

        private static void LogLoadFailure(string what, Exception ex)
            => AppLogging.LogError($"[ImageLoader] Failed to load {what}: {ex.Message}");

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
            {
                key = normalized;
            }

            return imageCache.GetOrAdd(key, _ =>
            {
                if ((tint.HasValue || opacity.HasValue) && IsSvgPath(normalized))
                {
                    IImage? tinted = LoadTintedSvg(normalized, tint, opacity);
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

                AppLogging.LogError($"[ImageLoader] Asset not found: {normalized}");
                return GetMissingTextureImage();
            });
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
                    return parts.Length != 4 || parts[3] is "1" or "1.0" or "100%" or "255";
                }
            }

            return false;
        }

        private static SvgImage? LoadTintedSvg(string assetUri, Color? tint = null, double? opacity = null)
        {
            try
            {
                Uri uri = new(assetUri, UriKind.Absolute);
                using Stream stream = AssetLoader.Open(uri);
                XDocument xdoc = XDocument.Load(stream);

                if (tint.HasValue)
                {
                    string hexColor = $"#{tint.Value.R:X2}{tint.Value.G:X2}{tint.Value.B:X2}";

                    foreach (XElement element in xdoc.Descendants())
                    {
                        string name = element.Name.LocalName.ToLowerInvariant();
                        if (name is not ("path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "text"))
                            continue;

                        XAttribute? fillAttr = element.Attribute("fill");
                        XAttribute? strokeAttr = element.Attribute("stroke");
                        XAttribute? styleAttr = element.Attribute("style");

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
                            val = SvgColorFilterRegex().Replace(val, match =>
                                {
                                    string prefix = match.Groups["prefix"].Value;
                                    string rawColor = match.Groups["color"].Value;
                                    return IsPureBlack(rawColor) ? $"{prefix}:{hexColor}" : match.Value;
                                });

                            styleAttr.Value = val;
                        }
                    }
                }

                if (opacity.HasValue)
                {
                    XElement? svgRoot = xdoc.Root;
                    svgRoot?.SetAttributeValue("opacity", opacity.Value.ToString(CultureInfo.InvariantCulture));
                }

                using MemoryStream memoryStream = new();
                xdoc.Save(memoryStream);
                memoryStream.Position = 0;
                return RenderSvgStream(memoryStream);
            }
            catch (Exception ex)
            {
                LogLoadFailure($"tinted SVG '{assetUri}'", ex);
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
            catch (Exception ex)
            {
                LogLoadFailure($"'{path}'", ex);
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
            catch (Exception ex)
            {
                LogLoadFailure($"'{normalizedAssetUri}'", ex);
                return null;
            }
        }

        private static string? TrySwapResourcesBase(string normalizedAssetUri)
        {
            if (normalizedAssetUri.StartsWith(DefaultResourcesBaseUri, StringComparison.Ordinal))
                return AlternateResourcesBaseUri + normalizedAssetUri[DefaultResourcesBaseUri.Length..];

            if (normalizedAssetUri.StartsWith(AlternateResourcesBaseUri, StringComparison.Ordinal))
                return DefaultResourcesBaseUri + normalizedAssetUri[AlternateResourcesBaseUri.Length..];

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
                normalized = normalized[resourcesPrefix.Length..];

            normalized = EnsureSvgExtensionIfMissing(normalized);
            return DefaultResourcesBaseUri + normalized;
        }

        private static bool IsSvgPath(string path) => RemoveQueryAndFragment(path).EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

        private static SvgImage? LoadSvgImage(string uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText))
                return null;

            try
            {
                if (uriText.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    Uri assetUri = new(uriText, UriKind.Absolute);
                    using Stream stream = AssetLoader.Open(assetUri);
                    return RenderSvgStream(stream);
                }

                if (Uri.TryCreate(uriText, UriKind.Absolute, out Uri? absoluteUri) && absoluteUri.IsFile)
                    return RenderSvgFile(absoluteUri.LocalPath);

                if (File.Exists(uriText))
                    return RenderSvgFile(uriText);
            }
            catch (FileNotFoundException)
            {
                // probing a missing asset is expected
            }
            catch (Exception ex)
            {
                LogLoadFailure($"'{uriText}'", ex);
            }

            return null;
        }

        private static Bitmap? LoadBitmap(string assetUri)
        {
            if (string.IsNullOrWhiteSpace(assetUri))
                return null;

            try
            {
                if (assetUri.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri = new(assetUri, UriKind.Absolute);
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
            catch (FileNotFoundException)
            {
                // probing a missing asset is expected
            }
            catch (Exception ex)
            {
                LogLoadFailure($"'{assetUri}'", ex);
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
                basePart = pathOrUri[..queryIndex];
                queryAndFragment = pathOrUri[queryIndex..];
            }

            string stem = basePart.LastIndexOf('.') is var dot and >= 0 ? basePart[..dot] : basePart;
            return stem + newExtension + queryAndFragment;
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
            return index < 0 ? value : value[..index];
        }

        private static SvgImage? RenderSvgFile(string path)
        {
            try
            {
                using Stream stream = File.OpenRead(path);
                return RenderSvgStream(stream);
            }
            catch (Exception ex)
            {
                LogLoadFailure($"SVG file '{path}'", ex);
                return null;
            }
        }

        private static SvgImage? RenderSvgStream(Stream stream)
        {
            try
            {
                SvgSource? source = SvgSource.LoadFromStream(stream, null);
                return source switch
                {
                    null => null,
                    _ => new SvgImage { Source = source },
                };
            }
            catch (Exception ex)
            {
                LogLoadFailure("SVG stream", ex);
                return null;
            }
        }

        public static void ClearCache() => imageCache.Clear();
        [GeneratedRegex(@"(?<prefix>fill|stroke)\s*:\s*(?<color>[^;]+)", RegexOptions.IgnoreCase)]
        private static partial Regex SvgColorFilterRegex();
    }
    public sealed class TintedAssetConverter : IValueConverter
    {
        public static TintedAssetConverter Instance { get; } = new TintedAssetConverter();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (parameter is not string assetName)
                return null;

            Color? tint = value switch
            {
                ISolidColorBrush brush => brush.Color,
                Color color => color,
                _ => null,
            };
            return ImageSourceLoader.LoadFromAssetUri(assetName, tint);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    }
}
