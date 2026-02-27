using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ModHearth.UI;
using System;
using Xunit;

namespace ModHearth.Tests;

public class AppStartupTests
{
    [AvaloniaFact]
    public void MainWindow_Shows_WithoutUnhandledExceptions()
    {
        string? previous = Environment.GetEnvironmentVariable("MODHEARTH_TEST_MODE");
        Environment.SetEnvironmentVariable("MODHEARTH_TEST_MODE", "1");

        AppBuilder.Configure<global::ModHearth.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
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
}
