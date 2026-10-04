using SkiaSharp;
using Xunit;

namespace ModHearth.App.Tests;

/// <summary>
/// Smoke tests to verify that SkiaSharp can be used in the current environment, specially on linux
/// </summary>
public class SkiaNativeSmokeTests
{
    [Fact]
    public void SkFontManagerDefaultInitializesWithoutNativeMismatch()
    {
        SKFontManager manager = SKFontManager.Default;
        Assert.NotNull(manager);
    }

    [Fact]
    public void CanCreateAndDrawOnABitmap()
    {
        using SKBitmap bitmap = new(4, 4);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        Assert.Equal(4, bitmap.Width);
        Assert.Equal(4, bitmap.Height);
    }
}