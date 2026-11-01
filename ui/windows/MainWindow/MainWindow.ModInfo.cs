using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ModHearth.Models;
using ModHearth.UI.Rendering;
using ModHearth.UI.Theme;
using ModHearth.Utilities;

namespace ModHearth.UI
{
    public partial class MainWindow
    {
        // Preview state belongs to the window: sharing it statically leaked images across window lifetimes.
        private readonly object previewCacheGate = new();
        private readonly LinkedList<string> previewCacheOrder = new();
        private readonly Dictionary<string, (DateTime StampUtc, IImage Image, LinkedListNode<string> Node)> previewCache
            = new(StringComparer.OrdinalIgnoreCase);

        // Limits to cached previews are important to not hog memory on large modlists
        private const int MaxCachedPreviews = 24;

        // Fallback previews are shared, so they must never be disposed while a window can still show them.
        private static readonly BoundedLruCache<string, IImage> fallbackPreviewCache = new(4, StringComparer.OrdinalIgnoreCase);
        private static readonly IEqualityComparer<IImage> previewReferenceComparer = ReferenceEqualityComparer.Instance;

        private bool TryGetCachedPreview(string path, out IImage? image)
        {
            lock (previewCacheGate)
            {
                if (previewCache.TryGetValue(path, out (DateTime StampUtc, IImage Image, LinkedListNode<string> Node) entry) &&
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

        private void CachePreview(string path, IImage image)
        {
            DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lock (previewCacheGate)
            {
                if (previewCache.TryGetValue(path, out (DateTime StampUtc, IImage Image, LinkedListNode<string> Node) existing))
                {
                    previewCacheOrder.Remove(existing.Node);
                    if (!ReferenceEquals(existing.Image, image))
                        DisposeCachedPreview(existing.Image, "Failed to dispose replaced preview image");
                }

                LinkedListNode<string> node = previewCacheOrder.AddFirst(path);
                previewCache[path] = (stamp, image, node);

                while (previewCache.Count > MaxCachedPreviews && previewCacheOrder.Last is LinkedListNode<string> last)
                {
                    previewCacheOrder.RemoveLast();
                    if (previewCache.TryGetValue(last.Value, out (DateTime StampUtc, IImage Image, LinkedListNode<string> Node) evicted))
                    {
                        DisposeCachedPreview(evicted.Image, "Failed to dispose evicted preview image");
                        _ = previewCache.Remove(last.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Disposes a cache-owned preview unless it is still displayed or shared as a fallback.
        /// </summary>
        private void DisposeCachedPreview(IImage image, string failureMessage)
        {
            if (ReferenceEquals(image, currentPreview) ||
                fallbackPreviewCache.ContainsValue(image, previewReferenceComparer) ||
                image is not IDisposable disposable)
            {
                return;
            }

            try { disposable.Dispose(); }
            catch (Exception ex) { AppLogging.LogException(failureMessage, ex); }
        }

        public void ClearPreviewCache()
        {
            lock (previewCacheGate)
            {
                foreach ((_, IImage Image, _) in previewCache.Values)
                    DisposeCachedPreview(Image, "Failed to dispose cached preview image");

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

        private static string GetDescriptionTextColor() => Style.instance != null ? SimpleColor.ToHex(Style.instance.TextColor) : "#000000";

        private void ShowModInfo(ModReference modref)
        {
            //Add an empty string at the end so that text selection can select the last string (notification button is inlined). Very hacky but whatever
            modTitleRun.Text = (modref.Name + " ") ?? string.Empty;

            currentDescriptionBBCode = modref.Description ?? string.Empty;
            RefreshDescriptionHtml();
            PopulateModDataViewer(modref);

            int requestVersion = ++previewRequestVersion;
            string? previewPath = ResolveFilePathCaseInsensitive(modref.Path, "preview.png");

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

        private static IImage LoadFallbackPreview(string file = "43G6tag.png") => fallbackPreviewCache.GetOrAdd(file, static f => LoadFallbackPreviewCore(f));

        private static IImage LoadFallbackPreviewCore(string file)
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
            IImage? previous = currentPreview;
            currentPreview = image;
            modPreviewPanelViewModel.PreviewImage = image;

            if (previous == null || ReferenceEquals(previous, image))
                return;

            // Images owned by the preview cache survive for reuse; everything else is ours to release.
            if (IsPreviewCached(previous) ||
                fallbackPreviewCache.ContainsValue(previous, previewReferenceComparer))
            {
                return;
            }

            DisposePreviewImage(previous);
        }

        private bool IsPreviewCached(IImage image)
        {
            lock (previewCacheGate)
            {
                foreach ((_, IImage Image, _) in previewCache.Values)
                {
                    if (ReferenceEquals(Image, image))
                        return true;
                }
            }

            return false;
        }

        private static void DisposePreviewImage(IImage? image)
        {
            if (image is IDisposable disposable)
                disposable.Dispose();
        }
    }
}