using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModHearth.Models;
using ModHearth.UI.Helpers;
using ModHearth.UI.Rendering;
using ModHearth.UI.Theme;
using ModHearth.Utilities;
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
        public long Sequence { get; set; }
        public string FormattedTime => "At:    " + Timestamp.ToString("HH'h 'mm'm 'ss's'", CultureInfo.InvariantCulture);

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
        private const int MaxNotificationDrawerProbeFrames = 30;
        private readonly List<NotificationRecord> allNotificationRecords = [];
        private readonly BulkObservableCollection<NotificationRecord> notificationRecords = [];
        private readonly ObservableCollection<NotificationRecord> toasts = [];
        private readonly Dictionary<NotificationRecord, CancellationTokenSource> toastTimers = [];
        private bool notificationViewRefreshQueued;
        private long nextNotificationSequence;
        private bool isNotificationLogPopulating;
        private int notificationLogPopulationDepth;
        private long lastNotificationTimestamp;
        private Stopwatch? notificationDrawerProbeWatch;
        private DispatcherTimer? notificationDrawerProbeTimer;
        private int notificationDrawerProbeExpected;
        private int notificationDrawerProbeLayouts;
        private int notificationDrawerProbeFrames;

        /// <summary>
        /// Wires up dev-only diagnostics that explain how the notification log is produced: when
        /// records are appended, when the coalesced refresh actually runs, and how the drawer list
        /// realizes its rows after it opens. All messages go through <c>Loggers.Notifications.Dev</c>,
        /// so they cost nothing outside dev mode.
        /// </summary>
        private void AttachNotificationLogDiagnostics()
        {
            if (!DevMode.IsEnabled)
                return;

            notificationRecords.CollectionChanged += OnNotificationRecordsCollectionChanged;
            NotificationItemsControl.Loaded += (_, _) =>
                Loggers.Notifications.Dev($"Drawer list loaded: items={NotificationItemsControl.ItemCount} realized={CountRealizedNotificationRows()} listHeight={NotificationItemsControl.Bounds.Height:F1}");
        }

        private void OnNotificationRecordsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Counts only: per-item logging would bury the signal during startup.
            Loggers.Notifications.Dev(
                $"Notification view changed: action={e.Action} old={e.OldItems?.Count ?? 0} new={e.NewItems?.Count ?? 0} " +
                $"view={notificationRecords.Count} all={allNotificationRecords.Count} drawerOpen={IsNotificationDrawerOpen} " +
                $"populating={IsNotificationLogPopulating}");
        }

        /// <summary>
        /// Samples the drawer list's realized rows after the drawer opens so the logs show whether
        /// the list had all rows from the first layout pass or whether they appeared over time.
        /// </summary>
        private void BeginNotificationDrawerRealizationProbe()
        {
            if (!DevMode.IsEnabled)
                return;

            StopNotificationDrawerRealizationProbe();

            notificationDrawerProbeExpected = notificationRecords.Count;
            notificationDrawerProbeLayouts = 0;
            notificationDrawerProbeFrames = 0;
            notificationDrawerProbeWatch = Stopwatch.StartNew();
            NotificationItemsControl.LayoutUpdated += OnNotificationDrawerLayoutUpdated;
            NotificationItemsControl.EffectiveViewportChanged += OnNotificationDrawerEffectiveViewportChanged;

            notificationDrawerProbeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            notificationDrawerProbeTimer.Tick += (_, _) => FinishNotificationDrawerRealizationProbe("timer");
            notificationDrawerProbeTimer.Start();

            // Measures how long the dispatcher takes to reach a render-priority job: a long delay
            // means the UI thread was busy and is the reason rows show up late.
            long openedAt = Stopwatch.GetTimestamp();
            Dispatcher.UIThread.Post(
                () => Loggers.Notifications.Dev($"Drawer open -> first Render-priority callback after {ElapsedMs(openedAt):F1}ms"),
                DispatcherPriority.Render);

            Loggers.Notifications.Dev(
                $"Drawer probe started: expected={notificationDrawerProbeExpected} rows, itemCount={NotificationItemsControl.ItemCount} " +
                $"realized={CountRealizedNotificationRows()} listHeight={NotificationItemsControl.Bounds.Height:F1} " +
                $"viewport={NotificationListViewportHeight:F1}");

            ScheduleNotificationDrawerFrameProbe(notificationDrawerProbeWatch, MaxNotificationDrawerProbeFrames);
        }

        /// <summary>
        /// Logs composition commits (real rendered frames) so a slow populate can be attributed to
        /// "no frames were produced" instead of "layout didn't realize the rows".
        /// </summary>
        private void ScheduleNotificationDrawerFrameProbe(Stopwatch watch, int remainingFrames)
        {
            if (remainingFrames <= 0 || !ReferenceEquals(notificationDrawerProbeWatch, watch))
                return;

            Compositor? compositor = Compositor.TryGetDefaultCompositor();
            if (compositor == null)
            {
                Loggers.Notifications.Dev($"Drawer frame probe unavailable: no default compositor.");
                return;
            }

            compositor.RequestCompositionUpdate(() =>
            {
                if (!ReferenceEquals(notificationDrawerProbeWatch, watch))
                    return;

                int frame = ++notificationDrawerProbeFrames;
                long elapsed = watch.ElapsedMilliseconds;

                // The commit callback can run off the UI thread; visual tree reads must not.
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ReferenceEquals(notificationDrawerProbeWatch, watch))
                        return;

                    Loggers.Notifications.Dev(
                        $"Drawer frame #{frame} at {elapsed}ms: realized={CountRealizedNotificationRows()}/{notificationDrawerProbeExpected} " +
                        $"layouts={notificationDrawerProbeLayouts} viewport={NotificationListViewportHeight:F1}");
                });

                ScheduleNotificationDrawerFrameProbe(watch, remainingFrames - 1);
            });
        }

        private void OnNotificationDrawerEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
        {
            if (notificationDrawerProbeWatch is not { } watch)
                return;

            Loggers.Notifications.Dev(
                $"Drawer effective viewport at {watch.ElapsedMilliseconds}ms: x={e.EffectiveViewport.X:F1} y={e.EffectiveViewport.Y:F1} " +
                $"w={e.EffectiveViewport.Width:F1} h={e.EffectiveViewport.Height:F1} " +
                $"realized={CountRealizedNotificationRows()}/{notificationDrawerProbeExpected}");
        }

        private void OnNotificationDrawerLayoutUpdated(object? sender, EventArgs e)
        {
            if (notificationDrawerProbeWatch is not { } watch)
                return;

            notificationDrawerProbeLayouts++;
            int realized = CountRealizedNotificationRows();
            Loggers.Notifications.Dev(
                $"Drawer layout #{notificationDrawerProbeLayouts} at {watch.ElapsedMilliseconds}ms: realized={realized}/{notificationDrawerProbeExpected} " +
                $"items={NotificationItemsControl.ItemCount} listHeight={NotificationItemsControl.Bounds.Height:F1} " +
                $"viewport={NotificationListViewportHeight:F1}");

            // Virtualization stops at the visible row count, so "all realized" only happens when
            // every record fits on screen; otherwise the timer writes the final summary.
            if (realized >= notificationDrawerProbeExpected)
                FinishNotificationDrawerRealizationProbe("all rows realized");
        }

        private void FinishNotificationDrawerRealizationProbe(string reason)
        {
            if (notificationDrawerProbeWatch is not { } watch)
                return;

            Loggers.Notifications.Dev(
                $"Drawer probe finished ({reason}) at {watch.ElapsedMilliseconds}ms: " +
                $"realized={CountRealizedNotificationRows()}/{notificationDrawerProbeExpected} " +
                $"items={NotificationItemsControl.ItemCount} layouts={notificationDrawerProbeLayouts} " +
                $"frames={notificationDrawerProbeFrames} " +
                $"listHeight={NotificationItemsControl.Bounds.Height:F1} viewport={NotificationListViewportHeight:F1}");
            StopNotificationDrawerRealizationProbe();
        }

        private void StopNotificationDrawerRealizationProbe()
        {
            NotificationItemsControl.LayoutUpdated -= OnNotificationDrawerLayoutUpdated;
            NotificationItemsControl.EffectiveViewportChanged -= OnNotificationDrawerEffectiveViewportChanged;
            notificationDrawerProbeTimer?.Stop();
            notificationDrawerProbeTimer = null;
            notificationDrawerProbeWatch = null;
        }

        private int CountRealizedNotificationRows()
            => NotificationItemsControl.GetRealizedContainers().Count();

        private double NotificationListViewportHeight
            => NotificationItemsControl.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault()?.Viewport.Height ?? -1;

        private bool IsNotificationDrawerOpen => NotificationDrawerPanel.Classes.Contains("open");

        private static double ElapsedMs(long startTimestamp)
            => (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

        /// <summary>
        /// True while the initial load or a modpack reload is still expected to produce notification
        /// records. The drawer shows a placeholder at the top of the list until it clears.
        /// </summary>
        public bool IsNotificationLogPopulating
        {
            get => isNotificationLogPopulating;
            private set => SetField(ref isNotificationLogPopulating, value);
        }

        /// <summary>
        /// Starts a population scope. Nested scopes keep the placeholder visible until the outermost
        /// one ends, so a reload queued behind another load does not make it flicker.
        /// </summary>
        private void BeginNotificationLogPopulation()
        {
            notificationLogPopulationDepth++;
            IsNotificationLogPopulating = true;
            Loggers.Notifications.Dev(
                $"Population scope begin: depth={notificationLogPopulationDepth} all={allNotificationRecords.Count} view={notificationRecords.Count}");
        }

        private void EndNotificationLogPopulation()
        {
            if (notificationLogPopulationDepth > 0)
                notificationLogPopulationDepth--;

            if (notificationLogPopulationDepth == 0)
                IsNotificationLogPopulating = false;

            Loggers.Notifications.Dev(
                $"Population scope end: depth={notificationLogPopulationDepth} all={allNotificationRecords.Count} " +
                $"view={notificationRecords.Count} populating={IsNotificationLogPopulating}");
        }

        public void ApplyNotificationFilterAndSort()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ApplyNotificationFilterAndSort);
                return;
            }

            long startedAt = Stopwatch.GetTimestamp();
            int viewCountBefore = notificationRecords.Count;

            string filter = notificationSearchBar?.Text?.Trim() ?? string.Empty;
            SearchFilterMode mode = notificationSearchBar?.SearchMode ?? SearchFilterMode.ModifiedTime;
            bool sortDescending = notificationSearchBar?.SortDescending ?? true;
            bool hideFiltered = notificationSearchBar?.HideFiltered ?? true;

            SearchFilterContext context = new(filter, mode);

            // Single pass: update flags and collect survivors together.
            List<NotificationRecord> visibleRecords = new(allNotificationRecords.Count);
            foreach (NotificationRecord r in allNotificationRecords)
            {
                bool match = !context.HasFilter || context.MatchesText(r.Message);

                r.IsFilteredOut = context.HasFilter && !match;
                r.IsVisible = !hideFiltered || match;
                if (r.IsVisible)
                    visibleRecords.Add(r);
            }

            visibleRecords.Sort(sortDescending ? CompareNotificationsDescending : CompareNotificationsAscending);

            SearchFilterHelper.ReplaceCollection(notificationRecords, visibleRecords);

            Loggers.Notifications.Dev(
                $"ApplyNotificationFilterAndSort: all={allNotificationRecords.Count} visible={visibleRecords.Count} " +
                $"viewBefore={viewCountBefore} viewAfter={notificationRecords.Count} filter='{filter}' mode={mode} " +
                $"descending={sortDescending} hideFiltered={hideFiltered} drawerOpen={IsNotificationDrawerOpen} " +
                $"populating={IsNotificationLogPopulating} elapsed={ElapsedMs(startedAt):F1}ms");
        }

        // Timestamps can collide within a single tick; Sequence preserves insertion order.
        private static int CompareNotificationsDescending(NotificationRecord left, NotificationRecord right)
        {
            int timeComparison = right.Timestamp.CompareTo(left.Timestamp);
            return timeComparison != 0
                ? timeComparison
                : right.Sequence.CompareTo(left.Sequence);
        }

        private static int CompareNotificationsAscending(NotificationRecord left, NotificationRecord right)
        {
            int timeComparison = left.Timestamp.CompareTo(right.Timestamp);
            return timeComparison != 0
                ? timeComparison
                : left.Sequence.CompareTo(right.Sequence);
        }

        /// <summary>
        /// Adds a newly created record without rebuilding the whole history view when the default
        /// view is active. Notifications are already stored newest-first, so an incremental insert
        /// produces the same order while raising a single Add instead of a full Reset.
        /// </summary>
        private bool TryInsertNotificationRecord(NotificationRecord record)
        {
            bool noFilter = string.IsNullOrWhiteSpace(notificationSearchBar?.Text);
            bool sortDescending = notificationSearchBar?.SortDescending ?? true;
            bool viewInSync = notificationRecords.Count == allNotificationRecords.Count - 1
                && (notificationRecords.Count == 0 || ReferenceEquals(notificationRecords[0], allNotificationRecords[1]));
            bool isNewest = notificationRecords.Count == 0 || record.Timestamp >= notificationRecords[0].Timestamp;

            if (!noFilter || !sortDescending || !viewInSync || !isNewest)
            {
                Loggers.Notifications.Dev(
                    $"Incremental insert rejected: noFilter={noFilter} descending={sortDescending} viewInSync={viewInSync} " +
                    $"isNewest={isNewest} view={notificationRecords.Count} all={allNotificationRecords.Count} " +
                    $"viewRefreshQueued={notificationViewRefreshQueued}");
                return false;
            }

            record.IsFilteredOut = false;
            record.IsVisible = true;
            notificationRecords.Insert(0, record);
            TrimNotificationView();
            return true;
        }

        private void TrimNotificationView()
        {
            while (notificationRecords.Count > MaxNotificationRecords)
                notificationRecords.RemoveAt(notificationRecords.Count - 1);
        }

        private void ScheduleNotificationViewRefresh()
        {
            if (notificationViewRefreshQueued)
            {
                Loggers.Notifications.Dev(
                    $"View refresh already queued; coalescing (all={allNotificationRecords.Count} view={notificationRecords.Count})");
                return;
            }

            notificationViewRefreshQueued = true;
            long queuedAt = Stopwatch.GetTimestamp();
            Loggers.Notifications.Dev(
                $"View refresh queued at Background priority: all={allNotificationRecords.Count} view={notificationRecords.Count}");

            Dispatcher.UIThread.Post(() =>
            {
                notificationViewRefreshQueued = false;
                Loggers.Notifications.Dev($"View refresh dequeued after {ElapsedMs(queuedAt):F1}ms");
                ApplyNotificationFilterAndSort();
            }, DispatcherPriority.Background);
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

            long startedAt = Stopwatch.GetTimestamp();
            long iconStartedAt = Stopwatch.GetTimestamp();
            IImage? iconSource = ImageSourceLoader.LoadFromAssetUri(iconResourceName);
            double iconMs = ElapsedMs(iconStartedAt);

            NotificationRecord record = new()
            {
                Message = message,
                IconResourceName = iconResourceName,
                IconSource = iconSource,
                Timestamp = DateTime.Now,
                Sequence = nextNotificationSequence++
            };

            allNotificationRecords.Insert(0, record);

            bool insertedIncrementally = TryInsertNotificationRecord(record);
            if (allNotificationRecords.Count > MaxNotificationRecords)
                allNotificationRecords.RemoveRange(MaxNotificationRecords, allNotificationRecords.Count - MaxNotificationRecords);

            Loggers.Notifications.Dev(
                $"ShowNotification: message='{TrimForLog(message)}' icon='{iconResourceName}' incremental={insertedIncrementally} " +
                $"all={allNotificationRecords.Count} view={notificationRecords.Count} iconLoad={iconMs:F1}ms " +
                $"sinceLast={ElapsedMs(lastNotificationTimestamp):F1}ms total={ElapsedMs(startedAt):F1}ms " +
                $"drawerOpen={IsNotificationDrawerOpen} populating={IsNotificationLogPopulating}");
            lastNotificationTimestamp = Stopwatch.GetTimestamp();

            if (!insertedIncrementally)
                ScheduleNotificationViewRefresh();

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
                CloseNotificationDrawer();
            }
            else
            {
                // Populate the history before the slide-in starts so the drawer never opens empty
                // while a coalesced background refresh is still queued.
                long startedAt = Stopwatch.GetTimestamp();
                int viewCountBefore = notificationRecords.Count;
                ApplyNotificationFilterAndSort();
                double applyMs = ElapsedMs(startedAt);

                Loggers.Notifications.Dev(
                    $"Drawer opening: all={allNotificationRecords.Count} viewBefore={viewCountBefore} viewAfter={notificationRecords.Count} " +
                    $"apply={applyMs:F1}ms listHeight={NotificationItemsControl.Bounds.Height:F1} " +
                    $"populating={IsNotificationLogPopulating} pendingRefresh={notificationViewRefreshQueued}");

                NotificationDrawerPanel.Classes.Add("open");
                NotificationBackdrop.IsVisible = true;

                // The slide-in only animates RenderTransform, which never invalidates layout, and a
                // virtualized ListBox only realizes rows during a layout pass. Invalidate the list
                // while the transform animates so the rows are realized as soon as the drawer
                // becomes visible instead of waiting for some unrelated layout invalidation.
                NotificationDrawerPanel.PropertyChanged += OnNotificationDrawerTransformChanged;
                NotificationItemsControl.InvalidateMeasure();

                BeginNotificationDrawerRealizationProbe();
            }
        }

        private void OnNotificationBackdropClick(object? sender, PointerPressedEventArgs e) => CloseNotificationDrawer();

        /// <summary>
        /// Keeps the virtualized notification list measuring while the drawer's render-only slide
        /// animation runs. A render transform change does not invalidate layout, and a virtualizing
        /// panel only realizes rows during a layout pass, so without this the list can stay at a
        /// single realized row until some unrelated invalidation happens.
        /// </summary>
        private void OnNotificationDrawerTransformChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == RenderTransformProperty)
                NotificationItemsControl.InvalidateMeasure();
        }

        private void CloseNotificationDrawer()
        {
            Loggers.Notifications.Dev(
                $"Drawer closing: all={allNotificationRecords.Count} view={notificationRecords.Count} " +
                $"realized={CountRealizedNotificationRows()} " +
                $"probe={notificationDrawerProbeWatch?.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) ?? "inactive"}ms");

            NotificationDrawerPanel.PropertyChanged -= OnNotificationDrawerTransformChanged;
            _ = NotificationDrawerPanel.Classes.Remove("open");
            NotificationBackdrop.IsVisible = false;
            StopNotificationDrawerRealizationProbe();
        }

        public void DismissAllNotifications()
        {
            foreach (NotificationRecord record in toasts.ToList())
                DismissToast(record);
        }
    }
}