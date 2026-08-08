using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ModHearth.UI;

public partial class MainWindow
{
    private static readonly object previewCacheGate = new();
    private static readonly LinkedList<string> previewCacheOrder = new();
    private static readonly Dictionary<string, (DateTime StampUtc, IImage Image, LinkedListNode<string> Node)> previewCache
        = new(StringComparer.OrdinalIgnoreCase);

    // Limits to cached previews are important to not hog memory on large modlists
    private const int MaxCachedPreviews = 24;

    private static bool TryGetCachedPreview(string path, out IImage? image)
    {
        lock (previewCacheGate)
        {
            if (previewCache.TryGetValue(path, out var entry) &&
                File.Exists(path) && File.GetLastWriteTimeUtc(path) == entry.StampUtc)
            {
                previewCacheOrder.Remove(entry.Node);
                previewCacheOrder.AddFirst(entry.Node);
                image = entry.Image;
                return true;
            }
        }
        image = null;
        return false;
    }

    private static void CachePreview(string path, IImage image)
    {
        DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        lock (previewCacheGate)
        {
            if (previewCache.TryGetValue(path, out var existing))
            {
                previewCacheOrder.Remove(existing.Node);
                if (existing.Image != image && existing.Image is IDisposable dispOld)
                {
                    dispOld.Dispose();
                }
            }
            var node = previewCacheOrder.AddFirst(path);
            previewCache[path] = (stamp, image, node);
            while (previewCache.Count > MaxCachedPreviews)
            {
                var last = previewCacheOrder.Last!;
                previewCacheOrder.RemoveLast();
                if (previewCache.TryGetValue(last.Value, out var evicted))
                {
                    if (evicted.Image is IDisposable dispEvicted)
                        dispEvicted.Dispose();
                    previewCache.Remove(last.Value);
                }
            }
        }
    }

    public static void ClearPreviewCache()
    {
        lock (previewCacheGate)
        {
            foreach (var entry in previewCache.Values)
            {
                if (entry.Image is IDisposable disp)
                {
                    try { disp.Dispose(); }
                    catch (Exception ex) { AppLogging.LogException("Failed to dispose cached preview image", ex); }
                }
            }
            previewCache.Clear();
            previewCacheOrder.Clear();
        }
        ImageSourceLoader.ClearCache();
    }

    private int previewRequestVersion;

    private void ShowFallbackInfo()
    {
        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();
        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
        currentSelectedModId = null;
        previousSelectedModId = null;
        SetPreviewImage(LoadFallbackPreview("modhearth_icon_v2.ico"));
        ShowFallbackHelpText();
        PopulateModDataViewer(null);
        UpdateModlistHeaders();
    }

    private void RefreshDescriptionHtml()
    {
        string? sanitizedBBCode = currentDescriptionBBCode;
        if (string.IsNullOrWhiteSpace(sanitizedBBCode))
            sanitizedBBCode = MainWindowHelpContent.GetCachedReadmeText();

        if (!string.IsNullOrWhiteSpace(sanitizedBBCode))
        {
            sanitizedBBCode = Regex.Replace(sanitizedBBCode, @"\[[a-zA-Z0-9_]+(?![^\]]*\])$", "", RegexOptions.RightToLeft);
        }

        modDescriptionPanelViewModel.DescriptionHtml = BBCodeRenderer.ToHtml(
            sanitizedBBCode, GetDescriptionTextColor(), "transparent");
    }

    private static string GetDescriptionTextColor()
        => Style.instance != null ? SimpleColor.ToHex(Style.instance.textColor) : "#000000";

    private void ShowModInfo(ModReference modref)
    {
        //Add an empty string at the end so that text selection can select the last string (notification button is inlined). Very hacky but whatever
        modTitleRun.Text = modref.name + " " ?? string.Empty;

        currentDescriptionBBCode = modref.description ?? string.Empty;
        RefreshDescriptionHtml();
        PopulateModDataViewer(modref);

        int requestVersion = ++previewRequestVersion;
        string? previewPath = ResolveFilePathCaseInsensitive(modref.path, "preview.png");

        if (string.IsNullOrWhiteSpace(previewPath))
        {
            SetPreviewImage(LoadFallbackPreview());
            return;
        }

        if (TryGetCachedPreview(previewPath, out IImage? cached))
        {
            SetPreviewImage(cached);
            return;
        }

        _ = Task.Run(() =>
        {
            IImage? image = ImageSourceLoader.LoadFromFilePath(previewPath);
            if (image != null)
                CachePreview(previewPath, image);

            Dispatcher.UIThread.Post(() =>
            {
                if (requestVersion != previewRequestVersion)
                    return; // a newer selection has already superseded this one
                SetPreviewImage(image ?? LoadFallbackPreview());
            });
        });
    }

    private static string? ResolveFilePathCaseInsensitive(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return null;
        if (!Directory.Exists(directory))
            return null;

        string exactPath = Path.Combine(directory, fileName);
        if (File.Exists(exactPath))
            return exactPath;

        try
        {
            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(file =>
                    string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static IImage LoadFallbackPreview(string file = "43G6tag.png")
    {
        IImage? fallback = ImageSourceLoader.LoadFromAssetUriUncached(file);
        if (fallback != null)
            return fallback;

        try
        {
            Uri uri = new($"{AvaloniaUri}/resources/{file}");
            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            // Linux CI can be case-sensitive on embedded resource paths.
        }

        try
        {
            Uri uri = new($"{AvaloniaUri}/Resources/{file}");
            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            // If fallback preview cannot be loaded, return a tiny placeholder image.
        }

        return new RenderTargetBitmap(new PixelSize(1, 1));
    }

    private void SetPreviewImage(IImage? image)
    {
        if (currentPreview != null && currentPreview != image)
        {
            bool isCached = false;
            lock (previewCacheGate)
            {
                foreach (var entry in previewCache.Values)
                {
                    if (entry.Image == currentPreview)
                    {
                        isCached = true;
                        break;
                    }
                }
            }
            if (!isCached)
            {
                DisposePreviewImage(currentPreview);
            }
        }
        currentPreview = image;
        modPreviewPanelViewModel.PreviewImage = image;
    }

    private static void DisposePreviewImage(IImage? image)
    {
        if (image is IDisposable disposable)
            disposable.Dispose();
    }
}