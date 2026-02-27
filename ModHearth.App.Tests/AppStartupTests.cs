using ModHearth;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ModHearth.Tests;

public class AppStartupTests
{
    [Fact]
    public async Task App_Starts_WithoutFatalErrors()
    {
        string? previousTestMode = Environment.GetEnvironmentVariable("MODHEARTH_TEST_MODE");
        string? previousSmokeMode = Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST");
        string? previousSmokeWindowMode = Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW");

        try
        {
            Environment.SetEnvironmentVariable("MODHEARTH_TEST_MODE", "1");
            Environment.SetEnvironmentVariable("MODHEARTH_SMOKE_TEST", "1");
            Environment.SetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW", "1");

            string appPath = typeof(Program).Assembly.Location;
            Assert.True(File.Exists(appPath), $"Expected app assembly at {appPath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(appPath);
            startInfo.ArgumentList.Add("--smoke-test-window");
            startInfo.Environment["MODHEARTH_TEST_MODE"] = "1";
            startInfo.Environment["MODHEARTH_SMOKE_TEST"] = "1";
            startInfo.Environment["MODHEARTH_SMOKE_TEST_WINDOW"] = "1";

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);

            Task<string> stdOutTask = process!.StandardOutput.ReadToEndAsync();
            Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore failures during kill.
                }

                Assert.Fail("App smoke test timed out.");
            }

            string stdout = await stdOutTask;
            string stderr = await stdErrTask;

            Assert.True(process.ExitCode == 0, $"App smoke test failed with exit code {process.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MODHEARTH_TEST_MODE", previousTestMode);
            Environment.SetEnvironmentVariable("MODHEARTH_SMOKE_TEST", previousSmokeMode);
            Environment.SetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW", previousSmokeWindowMode);
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
