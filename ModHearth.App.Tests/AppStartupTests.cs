using Avalonia;
using Avalonia.Threading;
using ModHearth;
using ModHearth.UI;
using SkiaSharp;
using System;
using Xunit;

namespace ModHearth.Tests;

public class AppStartupTests
{
    [Fact]
    public void MainWindow_Shows_WithoutUnhandledExceptions()
    {
        string? previous = Environment.GetEnvironmentVariable("MODHEARTH_TEST_MODE");
        Environment.SetEnvironmentVariable("MODHEARTH_TEST_MODE", "1");

        Program.BuildAvaloniaApp()
            .SetupWithoutStarting();

        Exception? uiException = null;
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            uiException = e.Exception;
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandled;

        try
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.InvokeAsync(() => { }).GetAwaiter().GetResult();
            Assert.True(window.IsVisible);
            window.Close();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
            Environment.SetEnvironmentVariable("MODHEARTH_TEST_MODE", previous);
        }

        Assert.Null(uiException);
    }

    [Fact]
    public void SkiaSharp_Native_Library_Loads_On_Linux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            var fontManager = SKFontManager.Default;
            Assert.NotNull(fontManager);
        }
        catch (Exception ex)
        {
            Assert.True(false, $"SkiaSharp native library failed to load: {ex}");
        }
    }
}
