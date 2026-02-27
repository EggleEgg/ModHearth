using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;

namespace ModHearth;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new UI.MainWindow();

            if (IsSmokeTestWindowEnabled())
            {
                var window = desktop.MainWindow;
                window.Opened += (_, _) =>
                {
                    Dispatcher.UIThread.Post(() => window.Close());
                };

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (window.IsVisible)
                        window.Close();
                };
                timer.Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool IsSmokeTestWindowEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW"), "1", StringComparison.OrdinalIgnoreCase);
}
