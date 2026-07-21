using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;
namespace ModHearth.UI;

public partial class MainWindow
{
    public void ShowNotification(string message, string iconResourceName, int notificationDelay)
        => ShowNotification(message, iconResourceName, showAfterReload: false, notificationDelay);
    public void ShowNotification(string message,
        string iconResourceName = "infoCircleWhiteIcon.svg",
        bool showAfterReload = false,
        int notificationDelay = 3500)
    {

        Console.WriteLine($"Notification: {message}");
        Dispatcher.UIThread.Post(() =>
        {
            var container = this.FindControl<StackPanel>("notificationContainer");
            if (container == null)
                return;

            // Limit to 3 notifications by removing the oldest (last in children list)
            while (container.Children.Count >= 3)
            {
                var oldest = container.Children[container.Children.Count - 1];
                if (oldest is Border b && b.Tag is System.Threading.CancellationTokenSource oldCts)
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
                container.Children.RemoveAt(container.Children.Count - 1);
            }

            // Create notification border and elements
            var notificationCts = new System.Threading.CancellationTokenSource();

            var border = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(6, 5.5, 20, 5.5),
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                BorderThickness = new Thickness(0, 1, 1, 1),
                BoxShadow = BoxShadows.Parse("0 4 12 0 #40000000"),
                Tag = notificationCts,
            };

            // Apply theme styling
            IBrush panelBrushClear;
            IBrush buttonOutlineBrush;
            IBrush textBrush;

            if (Style.instance != null)
            {
                panelBrushClear = BrushCache.GetBrush(Style.instance.modRefPanelColorClear.ToAvaloniaColor());
                buttonOutlineBrush = BrushCache.GetBrush(Style.instance.buttonOutlineColor.ToAvaloniaColor());
                textBrush = BrushCache.GetBrush(Style.instance.textColor.ToAvaloniaColor());
            }
            else
            {
                panelBrushClear = BrushCache.GetBrush(Avalonia.Media.Color.Parse("#2D2D30"));
                buttonOutlineBrush = BrushCache.GetBrush(Avalonia.Media.Color.Parse("#3F3F46"));
                textBrush = Brushes.White;
            }

            border.Background = panelBrushClear;
            border.BorderBrush = buttonOutlineBrush;

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            var image = new Image
            {
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Source = ImageSourceLoader.LoadFromAssetUri(iconResourceName)
            };

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = textBrush
            };

            stackPanel.Children.Add(image);
            stackPanel.Children.Add(textBlock);
            border.Child = stackPanel;

            // Set up pointer entered to dismiss immediately
            border.PointerEntered += (s, e) =>
            {
                DismissNotification(border);
            };

            // Insert at top (index 0) so newest is on top, oldest is on bottom
            container.Children.Insert(0, border);

            // Gradually reduce transparency (increase opacity) up to 0.75
            _ = Task.Run(async () =>
            {
                try
                {
                    int steps = 15;
                    int stepDelay = notificationDelay / steps; // animation time in ms = steps * stepDelay
                    double targetOpacity = 0.7;
                    for (int i = 0; i <= steps; i++)
                    {
                        if (notificationCts.Token.IsCancellationRequested)
                            break;

                        //linear interpolation between 1 and target
                        double currentOpacity = 1 + (targetOpacity - 1) * ((double)i / steps);

                        Dispatcher.UIThread.Post(() => border.Opacity = currentOpacity);
                        await Task.Delay(stepDelay, notificationCts.Token);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Graceful cancellation
                }
            });

            //timeout
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(notificationDelay, notificationCts.Token);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!notificationCts.IsCancellationRequested)
                        {
                            DismissNotification(border);
                        }
                    });
                }
                catch (TaskCanceledException)
                {
                    // Graceful cancellation
                }
            });
        });
    }

    public void DismissNotification(Border border)
    {
        if (border.Tag is System.Threading.CancellationTokenSource cts)
        {
            cts.Cancel();
            cts.Dispose();
            border.Tag = null;
        }

        var container = this.FindControl<StackPanel>("notificationContainer");
        if (container != null)
        {
            container.Children.Remove(border);
        }
    }

    public void DismissAllNotifications()
    {
        var container = this.FindControl<StackPanel>("notificationContainer");
        if (container != null)
        {
            foreach (var child in container.Children)
            {
                if (child is Border b && b.Tag is System.Threading.CancellationTokenSource cts)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
            container.Children.Clear();
        }
    }
}