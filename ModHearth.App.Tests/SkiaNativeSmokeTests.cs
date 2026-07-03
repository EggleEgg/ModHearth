using SkiaSharp;
using Xunit;

namespace ModHearth.App.Tests;

/// <summary>
/// Directly reproduces the code path that crashed on Linux in July 2026:
/// SkiaSharp.NativeAssets.Linux was pinned a major version ahead of the
/// managed SkiaSharp package, so libSkiaSharp.so no longer exported
/// sk_fontmgr_ref_default. Touching SKFontManager.Default is exactly what
/// Avalonia.Skia.FontManagerImpl does on startup.
/// </summary>
public class SkiaNativeSmokeTests
{
    [Fact]
    public void SkFontManager_Default_Initializes_Without_Native_Mismatch()
    {
        SKFontManager manager = SKFontManager.Default;
        Assert.NotNull(manager);
    }

    [Fact]
    public void Can_Create_And_Draw_On_A_Bitmap()
    {
        using SKBitmap bitmap = new SKBitmap(4, 4);
        using SKCanvas canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        Assert.Equal(4, bitmap.Width);
        Assert.Equal(4, bitmap.Height);
    }
}