using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ModHearth.UI.Helpers;
using ModHearth.UI.Rendering;
using ModHearth.UI.Theme;
using ModHearth.Utilities;
using ModHearth.utilities;
using ModHearth.Utilities.Logging;

namespace ModHearth.UI
{
    public class NotificationRecord : ObservableObject, IThemedViewModel
    {
        // Sub-milliscale opacity changes are visually identical; ignore them so the row is not re-rendered.
        private static readonly IEqualityComparer<double> OpacityComparer = new MMath.ToleranceComparer(0.001);

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
                if (!SetField(ref isFilteredOut, value))
                    return;
                RefreshStyle();
            }
        }

        public bool IsVisible
        {
            get => isVisible;
            set => SetField(ref isVisible, value);
        }

        public TextDecorationCollection? TextDecorations
        {
            get => textDecorations;
            private set => SetField(ref textDecorations, value);
        }

        public IBrush MessageForeground
        {
            get => messageForeground;
            private set => SetField(ref messageForeground, value);
        }

        public double ItemOpacity
        {
            get => itemOpacity;
            private set => SetField(ref itemOpacity, value, OpacityComparer);
        }

        public void RefreshStyle() => RefreshStyle(Style.instance);

        public void RefreshStyle(Style? style)
        {
            IBrush defaultBrush = style != null
                ? BrushCache.GetBrush(style.TextColor.ToAvaloniaColor())
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
    }

    public partial class MainWindow
    {
        private const int MaxNotificationRecords = 500;
        private const int MaxShownNotifications = 4;
        private readonly List<NotificationRecord> allNotificationRecords = [];
        private readonly BulkObservableCollection<NotificationRecord> notificationRecords = [];
        private readonly ObservableCollection<NotificationRecord> toasts = [];
        private readonly Dictionary<NotificationRecord, CancellationTokenSource> toastTimers = [];

        public void ApplyNotificationFilterAndSort()
        {
            string filter = notificationSearchBar?.Text?.Trim() ?? string.Empty;
            SearchFilterMode mode = notificationSearchBar?.SearchMode ?? SearchFilterMode.ModifiedTime;
            bool sortDescending = notificationSearchBar?.SortDescending ?? true;
            bool hideFiltered = notificationSearchBar?.HideFiltered ?? true;

            SearchFilterContext context = new(filter, mode);

            foreach (NotificationRecord r in allNotificationRecords)
            {
                bool match = context.MatchesText(r.Message);

                r.IsFilteredOut = context.HasFilter && !match;
                r.IsVisible = !hideFiltered || match;
            }

            IEnumerable<NotificationRecord> query = allNotificationRecords.Where(r => r.IsVisible);

            query = sortDescending
                ? query.OrderByDescending(r => r.Timestamp)
                : query.OrderBy(r => r.Timestamp);

            List<NotificationRecord> filteredList = [.. query];

            Dispatcher.UIThread.Post(() => SearchFilterHelper.ReplaceCollection(notificationRecords, filteredList));
        }

        public void ShowNotification(string message,
            string iconResourceName = "infoCircleWhiteIcon.svg",
            bool showAfterReload = false,
            int notificationDelay = 3500)
        {
            // Record creation loads icons and touches UI state; callers include background tasks.
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ShowNotification(message, iconResourceName, showAfterReload, notificationDelay));
                return;
            }

            Loggers.Diag.Dev($"Notification: {message}");

            NotificationRecord record = new()
            {
                Message = message,
                IconResourceName = iconResourceName,
                IconSource = ImageSourceLoader.LoadFromAssetUri(iconResourceName),
                Timestamp = DateTime.Now
            };

            allNotificationRecords.Insert(0, record);
            if (allNotificationRecords.Count > MaxNotificationRecords)
                allNotificationRecords.RemoveRange(MaxNotificationRecords, allNotificationRecords.Count - MaxNotificationRecords);
            ApplyNotificationFilterAndSort();

            while (toasts.Count >= MaxShownNotifications)
                DismissToast(toasts[^1]);
            toasts.Insert(0, record); // newest on top, oldest falls off the bottom
            ScheduleToastDismissal(record, notificationDelay);
        }

        private void ScheduleToastDismissal(NotificationRecord record, int delayMs)
        {
            CancellationTokenSource cts = new();
            toastTimers[record] = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, cts.Token);
                    Dispatcher.UIThread.Post(() => DismissToast(record));
                }
                catch (OperationCanceledException)
                {
                    // Dismissed early (hover, cap, or reload).
                }
            });
        }

        private void OnToastPointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is Control { DataContext: NotificationRecord record })
                DismissToast(record);
        }

        public void DismissToast(NotificationRecord record)
        {
            if (toastTimers.Remove(record, out CancellationTokenSource? cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            _ = toasts.Remove(record);
        }

        private void OnToggleNotificationDrawerClick(object? sender, RoutedEventArgs e)
        {
            bool isOpen = NotificationDrawerPanel.Classes.Contains("open");
            if (isOpen)
            {
                _ = NotificationDrawerPanel.Classes.Remove("open");
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
            _ = NotificationDrawerPanel.Classes.Remove("open");
            NotificationBackdrop.IsVisible = false;
        }

        public void DismissAllNotifications()
        {
            foreach (NotificationRecord record in toasts.ToList())
                DismissToast(record);
        }
    }
}