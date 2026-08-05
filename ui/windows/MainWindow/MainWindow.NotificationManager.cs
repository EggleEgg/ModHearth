using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.UI;

public class NotificationRecord : INotifyPropertyChanged, IThemedViewModel
{
    private bool isFilteredOut;
    private bool isVisible = true;
    private TextDecorationCollection? textDecorations;
    private IBrush messageForeground = Brushes.White;
    private double itemOpacity = 1.0;

    public NotificationRecord()
    {
        RefreshStyle(Style.instance);
        ThemedViewModelRegistry.Register(this);
    }

    public string Message { get; set; } = string.Empty;
    public string IconResourceName { get; set; } = "infoCircleWhiteIcon.svg";
    public IImage? IconSource { get; set; }
    public DateTime Timestamp { get; set; }
    public string FormattedTime => "At:    " + Timestamp.ToString("HH'h 'mm'm 'ss's'");

    public bool IsFilteredOut
    {
        get => isFilteredOut;
        set
        {
            if (isFilteredOut == value)
                return;
            isFilteredOut = value;
            RefreshStyle();
            OnPropertyChanged();
        }
    }

    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (isVisible == value)
                return;
            isVisible = value;
            OnPropertyChanged();
        }
    }

    public TextDecorationCollection? TextDecorations
    {
        get => textDecorations;
        private set
        {
            if (Equals(textDecorations, value))
                return;
            textDecorations = value;
            OnPropertyChanged();
        }
    }

    public IBrush MessageForeground
    {
        get => messageForeground;
        private set
        {
            if (Equals(messageForeground, value))
                return;
            messageForeground = value;
            OnPropertyChanged();
        }
    }

    public double ItemOpacity
    {
        get => itemOpacity;
        private set
        {
            if (Math.Abs(itemOpacity - value) < 0.001)
                return;
            itemOpacity = value;
            OnPropertyChanged();
        }
    }

    public void RefreshStyle()
    {
        RefreshStyle(Style.instance);
    }

    public void RefreshStyle(Style? style)
    {
        IBrush defaultBrush = style != null
            ? BrushCache.GetBrush(style.textColor.ToAvaloniaColor())
            : Brushes.White;

        if (IsFilteredOut)
        {
            MessageForeground = BrushCache.EditBrushAlpha(Brushes.Gray, 160);
            TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
            ItemOpacity = 0.5;
        }
        else
        {
            MessageForeground = defaultBrush;
            TextDecorations = null;
            ItemOpacity = 1.0;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public partial class MainWindow
{
    private const int MaxNotificationRecords = 2000;
    private const int MaxShownNotifications = 4;
    private readonly List<NotificationRecord> allNotificationRecords = [];
    private readonly ObservableCollection<NotificationRecord> notificationRecords = [];

    public void ApplyNotificationFilterAndSort()
    {
        string filter = notificationSearchBar?.Text?.Trim() ?? string.Empty;
        SearchFilterMode mode = notificationSearchBar?.SearchMode ?? SearchFilterMode.ModifiedTime;
        bool sortDescending = notificationSearchBar?.SortDescending ?? true;
        bool hideFiltered = notificationSearchBar?.HideFiltered ?? true;
        bool hasFilter = !string.IsNullOrWhiteSpace(filter);

        foreach (var r in allNotificationRecords)
        {
            bool match = !hasFilter || (mode == SearchFilterMode.Regex
                ? IsRegexMatch(r.Message, filter)
                : r.Message.Contains(filter, StringComparison.OrdinalIgnoreCase));

            r.IsFilteredOut = hasFilter && !match;
            r.IsVisible = !hideFiltered || match;
        }

        IEnumerable<NotificationRecord> query = allNotificationRecords.Where(r => r.IsVisible);

        query = sortDescending
            ? query.OrderByDescending(r => r.Timestamp)
            : query.OrderBy(r => r.Timestamp);

        var filteredList = query.ToList();

        Dispatcher.UIThread.Post(() =>
        {
            SearchFilterHelper.ReplaceCollection(notificationRecords, filteredList);
            if (NotificationItemsControl != null && NotificationItemsControl.ItemsSource == null)
            {
                NotificationItemsControl.ItemsSource = notificationRecords;
            }
        });
    }

    private static bool IsRegexMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            return false;
        }
    }

    public void ShowNotification(string message, string iconResourceName, int notificationDelay)
        => ShowNotification(message, iconResourceName, showAfterReload: false, notificationDelay);
    public void ShowNotification(string message,
        string iconResourceName = "infoCircleWhiteIcon.svg",
        bool showAfterReload = false,
        int notificationDelay = 3500)
    {
        if (DevMode.IsEnabled)
            Console.WriteLine($"Notification: {message}");

        DateTime now = DateTime.Now;
        var iconSource = ImageSourceLoader.LoadFromAssetUri(iconResourceName);
        var record = new NotificationRecord
        {
            Message = message,
            IconResourceName = iconResourceName,
            IconSource = iconSource,
            Timestamp = now
        };

        Dispatcher.UIThread.Post(() =>
        {
            allNotificationRecords.Insert(0, record);
            if (allNotificationRecords.Count > MaxNotificationRecords)
            {
                allNotificationRecords.RemoveRange(MaxNotificationRecords, allNotificationRecords.Count - MaxNotificationRecords);
            }
            ApplyNotificationFilterAndSort();

            var container = this.FindControl<StackPanel>("notificationContainer");
            if (container == null)
                return;

            // Limit to 3 notifications by removing the oldest (last in children list)
            while (container.Children.Count >= MaxShownNotifications)
            {
                var oldest = container.Children[container.Children.Count - 1];
                if (oldest is Border b && b.Tag is CancellationTokenSource oldCts)
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
                container.Children.RemoveAt(container.Children.Count - 1);
            }

            // Create notification border and elements
            var notificationCts = new CancellationTokenSource();

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
                panelBrushClear = BrushCache.GetBrush(Style.instance.panelColorClear.ToAvaloniaColor());
                buttonOutlineBrush = BrushCache.GetBrush(Style.instance.buttonOutlineColor.ToAvaloniaColor());
                textBrush = BrushCache.GetBrush(Style.instance.textColor.ToAvaloniaColor());
            }
            else
            {
                panelBrushClear = BrushCache.GetBrush(Color.Parse("#2D2D30"));
                buttonOutlineBrush = BrushCache.GetBrush(Color.Parse("#3F3F46"));
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
                Source = iconSource
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

    private void OnToggleNotificationDrawerClick(object? sender, RoutedEventArgs e)
    {
        bool isOpen = NotificationDrawerPanel.Classes.Contains("open");
        if (isOpen)
        {
            NotificationDrawerPanel.Classes.Remove("open");
            NotificationBackdrop.IsVisible = false;
        }
        else
        {
            NotificationDrawerPanel.Classes.Add("open");
            NotificationBackdrop.IsVisible = true;
        }
    }

    private void OnNotificationBackdropClick(object? sender, PointerPressedEventArgs e)
    {
        NotificationDrawerPanel.Classes.Remove("open");
        NotificationBackdrop.IsVisible = false;
    }

    public void DismissNotification(Border border)
    {
        if (border.Tag is CancellationTokenSource cts)
        {
            cts.Cancel();
            cts.Dispose();
            border.Tag = null;
        }

        var container = this.FindControl<StackPanel>("notificationContainer");
        if (container != null)
        {
            _ = container.Children.Remove(border);
        }
    }

    public void DismissAllNotifications()
    {
        var container = this.FindControl<StackPanel>("notificationContainer");
        if (container != null)
        {
            foreach (var child in container.Children)
            {
                if (child is Border b && b.Tag is CancellationTokenSource cts)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
            container.Children.Clear();
        }
    }
}