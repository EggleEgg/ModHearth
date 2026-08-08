using SkiaSharp;
using Xunit;

namespace ModHearth.App.Tests;

/// <summary>
/// Smoke tests to verify that SkiaSharp can be used in the current environment, specially on linux
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
        using SKBitmap bitmap = new(4, 4);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        Assert.Equal(4, bitmap.Width);
        Assert.Equal(4, bitmap.Height);
    }
}