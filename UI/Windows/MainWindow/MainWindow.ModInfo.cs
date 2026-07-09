using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Text.RegularExpressions;

namespace ModHearth.UI;

public partial class MainWindow
{
    private void ShowFallbackInfo()
    {
        leftModlist.SelectedItems?.Clear();
        rightModlist.SelectedItems?.Clear();
        modListController.UpdateSelectionState(leftModlist);
        modListController.UpdateSelectionState(rightModlist);
        currentSelectedModId = null;
        previousSelectedModId = null;
        SetPreviewImage(LoadFallbackPreview());
        ShowFallbackHelpText();
        PopulateModDataViewer(null);
    }

    private void RefreshDescriptionHtml()
    {
        if (modDescriptionHtml == null)
            return;

        string? sanitizedBBCode = currentDescriptionBBCode;
        if (string.IsNullOrWhiteSpace(sanitizedBBCode))
            sanitizedBBCode = MainWindowHelpContent.GetCachedReadmeText();

        if (!string.IsNullOrWhiteSpace(sanitizedBBCode))
        {
            sanitizedBBCode = Regex.Replace(sanitizedBBCode, @"\[[a-zA-Z0-9_]+(?![^\]]*\])$", "", RegexOptions.RightToLeft);
        }

        modDescriptionHtml.Text = BBCodeRenderer.ToHtml(
            sanitizedBBCode, GetDescriptionTextColor(), "transparent");
    }

    private static string GetDescriptionTextColor()
        => Style.instance != null ? SimpleColor.ToHex(Style.instance.textColor) : "#000000";

    private void ShowModInfo(ModReference modref)
    {
        modTitleLabel.Text = modref.name ?? string.Empty;
        modDescriptionHtml.Text = modref.description ?? string.Empty;

        currentDescriptionBBCode = modref.description ?? string.Empty;
        RefreshDescriptionHtml();
        PopulateModDataViewer(modref);

        IImage? previewImage = null;
        string? previewSvgPath = ResolveFilePathCaseInsensitive(modref.path, "preview.svg");
        if (!string.IsNullOrWhiteSpace(previewSvgPath))
            previewImage = ImageSourceLoader.LoadFromFilePath(previewSvgPath);

        if (previewImage == null)
        {
            string? previewPath = ResolveFilePathCaseInsensitive(modref.path, "preview.png");
            if (!string.IsNullOrWhiteSpace(previewPath))
                previewImage = ImageSourceLoader.LoadFromFilePath(previewPath);
        }

        SetPreviewImage(previewImage ?? LoadFallbackPreview());
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

    private static IImage LoadFallbackPreview()
    {
        IImage? fallback = ImageSourceLoader.LoadFromAssetUri("43G6tag.png");
        if (fallback != null)
            return fallback;

        try
        {
            Uri uri = new Uri($"{AvaloniaUri}/resources/43G6tag.png");
            using Stream stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            // Linux CI can be case-sensitive on embedded resource paths.
        }

        try
        {
            Uri uri = new Uri($"{AvaloniaUri}/Resources/43G6tag.png");
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
        if (currentPreview is IDisposable disposable)
            disposable.Dispose();
        currentPreview = image;
        modPreviewImage.Source = image;
    }
}
