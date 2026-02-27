using Avalonia;
using Avalonia.Threading;
using ModHearth;
using ModHearth.UI;
using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ModHearth.Tests;

public class AppStartupTests
{
    [Fact]
    public async Task MainWindow_Shows_WithoutUnhandledExceptions()
    {
        string? previous = Environment.GetEnvironmentVariable("MODHEARTH_TEST_MODE");
        Environment.SetEnvironmentVariable("MODHEARTH_TEST_MODE", "1");

        var tcs = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? uiException = null;
            void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
            {
                uiException = e.Exception;
                e.Handled = true;
            }

            try
            {
                Program.BuildAvaloniaApp()
                    .SetupWithoutStarting();

                Dispatcher.UIThread.UnhandledException += OnUnhandled;

                var window = new MainWindow();
                window.Show();
                Assert.True(window.IsVisible);
                window.Close();

                Dispatcher.UIThread.InvokeShutdown();
            }
            catch (Exception ex)
            {
                uiException = ex;
            }
            finally
            {
                Dispatcher.UIThread.UnhandledException -= OnUnhandled;
                tcs.TrySetResult(uiException);
            }
        })
        {
            IsBackground = true
        };

        try
        {
            thread.Start();
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != tcs.Task)
                Assert.Fail("UI startup test timed out.");

            var exception = await tcs.Task;
            Assert.Null(exception);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MODHEARTH_TEST_MODE", previous);
        }
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
            Assert.Fail($"SkiaSharp native library failed to load: {ex}");
        }
    }
}
