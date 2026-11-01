using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ModHearth.Models;

namespace ModHearth.UI
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();

                if (IsSmokeTestWindowEnabled())
                {
                    Window window = desktop.MainWindow;
                    window.Opened += (_, _) =>
                    {
                        Dispatcher.UIThread.Post(() => window.Close());
                    };

                    DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
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

            if (DevMode.IsEnabled)
                //Use F12 key to open devtools
                _ = this.AttachDeveloperTools();
        }

        private static bool IsSmokeTestWindowEnabled() => string.Equals(Environment.GetEnvironmentVariable("MODHEARTH_SMOKE_TEST_WINDOW"), "1", StringComparison.OrdinalIgnoreCase);
    }
}
