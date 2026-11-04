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
        private sealed record CachedPreview(DateTime StampUtc, IImage Image);

        // Limits to cached previews are important to not hog memory on large modlists
        private const int MaxCachedPreviews = 24;

        // Assigned in the constructor: eviction consults this window's currentPreview to decide what is safe to dispose.
        private readonly BoundedLruCache<string, CachedPreview> previewCache;

        // Fallback previews are shared, so they must never be disposed while a window can still show them.
        private static readonly BoundedLruCache<string, IImage> fallbackPreviewCache = new(4, StringComparer.OrdinalIgnoreCase);
        private static readonly IEqualityComparer<IImage> previewReferenceComparer = ReferenceEqualityComparer.Instance;

        private bool TryGetCachedPreview(string path, out IImage? image)
        {
            if (previewCache.TryGetValue(path, out CachedPreview entry)
                && File.Exists(path) && File.GetLastWriteTimeUtc(path) == entry.StampUtc)
            {
                image = entry.Image;
                return true;
            }
            image = null;
            return false;
        }

        private void CachePreview(string path, IImage image)
            => previewCache.Set(path, new CachedPreview(File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue, image));

        private bool IsPreviewCached(IImage image) => previewCache.Any(entry => ReferenceEquals(entry.Image, image));

        public void ClearPreviewCache()
        {
            previewCache.Clear();
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
                sanitizedBBCode = SanitizeBBCodeRegex().Replace(sanitizedBBCode, "");
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
            string? previewPath = PathHelper.ResolveFile(modref.Path, "preview.png");

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

        private static void DisposePreviewImage(IImage? image)
        {
            if (image is IDisposable disposable)
                disposable.Dispose();
        }

        [GeneratedRegex(@"\[[a-zA-Z0-9_]+(?![^\]]*\])$", RegexOptions.RightToLeft)]
        private static partial Regex SanitizeBBCodeRegex();

    }
}