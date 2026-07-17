using System.Collections.Concurrent;
using Avalonia;
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

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Source))
            return AvaloniaProperty.UnsetValue;

        return ImageSourceLoader.LoadFromAssetUri(Source) ?? AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Normalizes image formats from the uri and caches them
/// </summary>
internal static class ImageSourceLoader
{
    private const string DefaultResourcesBaseUri = "avares://ModHearth/resources/";
    private const string AlternateResourcesBaseUri = "avares://ModHearth/Resources/";

    // Using Lazy<IImage?> guarantees the factory logic runs exactly once per unique key
    private static readonly ConcurrentDictionary<string, Lazy<IImage?>> imageCache = new();

    public static IImage? LoadFromAssetUri(string assetUri)
    {
        string normalized = NormalizeAssetUri(assetUri);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        // GetOrAdd is fast because creating Lazy is cheap.
        // The heavy loading inside Lazy only happens when .Value is accessed.
        return imageCache.GetOrAdd(normalized, key => new Lazy<IImage?>(() =>
        {
            // Try primary URI
            IImage? primary = LoadFromNormalizedAssetUri(key);
            if (primary != null)
                return primary;

            // Try swap alternate base URI
            string? alternate = TrySwapResourcesBase(key);
            if (string.IsNullOrWhiteSpace(alternate))
                return null;

            // Load alternate
            return LoadFromNormalizedAssetUri(alternate);
        })).Value;
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

            string svgCandidate = ReplaceExtension(fullPath, ".svg");
            IImage? svgCandidateImage = LoadSvgImage(new Uri(svgCandidate, UriKind.Absolute).ToString());
            if (svgCandidateImage != null)
                return svgCandidateImage;

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
        int queryIndex = pathOrUri.IndexOfAny(new[] { '?', '#' });
        string basePart = pathOrUri;
        if (queryIndex >= 0)
        {
            basePart = pathOrUri.Substring(0, queryIndex);
            queryAndFragment = pathOrUri.Substring(queryIndex);
        }

        int dotIndex = basePart.LastIndexOf('.');
        if (dotIndex < 0)
            return basePart + newExtension + queryAndFragment;

        return basePart.Substring(0, dotIndex) + newExtension + queryAndFragment;
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

        int index = value.IndexOfAny(new[] { '?', '#' });
        if (index < 0)
            return value;

        return value.Substring(0, index);
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
            if (source == null)
                return null;
            return new SvgImage { Source = source };
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
