using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using System;
using System.IO;

namespace ModHearth.UI;

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

internal static class ImageSourceLoader
{
    public static IImage? LoadFromAssetUri(string assetUri)
    {
        if (string.IsNullOrWhiteSpace(assetUri))
            return null;

        try
        {
            if (IsSvgPath(assetUri))
            {
                IImage? svgImage = LoadSvgImage(assetUri);
                if (svgImage != null)
                    return svgImage;

                string pngFallback = ReplaceExtension(assetUri, ".png");
                return LoadBitmapFromAsset(pngFallback);
            }

            string svgCandidate = ReplaceExtension(assetUri, ".svg");
            IImage? svgCandidateImage = LoadSvgImage(svgCandidate);
            if (svgCandidateImage != null)
                return svgCandidateImage;

            return LoadBitmapFromAsset(assetUri);
        }
        catch
        {
            return null;
        }
    }

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

    private static bool IsSvgPath(string path)
        => path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

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

    private static IImage? LoadBitmapFromAsset(string assetUri)
    {
        if (string.IsNullOrWhiteSpace(assetUri))
            return null;

        try
        {
            Uri uri = new Uri(assetUri, UriKind.Absolute);
            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string ReplaceExtension(string pathOrUri, string newExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrUri))
            return pathOrUri;

        int dotIndex = pathOrUri.LastIndexOf('.');
        if (dotIndex < 0)
            return pathOrUri + newExtension;

        return pathOrUri.Substring(0, dotIndex) + newExtension;
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
}
