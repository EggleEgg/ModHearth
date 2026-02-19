using Avalonia.Media;
using ModHearth.UI;
using System;
using System.IO;
using Xunit;

namespace ModHearth.App.Tests;

public class SvgLoaderTests
{
    [Fact]
    public void SvgFile_CanLoad()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.svg");
        const string svgContent = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"red\"/></svg>";
        File.WriteAllText(tempPath, svgContent);

        IImage? image = null;
        try
        {
            image = ImageSourceLoader.LoadFromFilePath(tempPath);
            Assert.NotNull(image);
        }
        finally
        {
            if (image is IDisposable disposable)
                disposable.Dispose();

            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
