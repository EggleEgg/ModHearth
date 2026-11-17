using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using ModHearth.Config;
using ModHearth.Utils;

namespace ModHearth.UI.Theme
{
    /// <summary>
    /// Applies the configured interface scale to every ModHearth window and popup. Window content
    /// is wrapped in a <see cref="LayoutTransformControl"/> so the scale participates in measure
    /// and arrange instead of stretching a rendered bitmap. Popups inherit the scale of the
    /// control that opens them. Existing hardcoded sizes therefore scale together without needing
    /// a binding on every control.
    /// </summary>
    internal static class UiScaleManager
    {
        private const string HostName = "UiScaleHost";

        private static readonly ConditionalWeakTable<Window, WindowScaleState> States = [];

        static UiScaleManager()
        {
            // Popups (flyouts, context menus, tooltips) are separate visual roots, so wrapping the
            // window content does not reach them. Avalonia's InheritsTransform applies the
            // placement target's scale to the popup host, keeping popup size and placement aligned
            // with the scaled control that opened it.
            _ = Popup.ChildProperty.Changed.AddClassHandler<Popup>((popup, _) =>
                popup.InheritsTransform = true);
        }

        public static double GetScaleFactor() => GetScaleFactor(ConfigManager.GetUiScalePercent());

        internal static double GetScaleFactor(int percent)
            => ConfigManager.NormalizeUiScalePercent(percent) / 100.0;

        /// <summary>
        /// Returns the scale currently applied to <paramref name="window"/>, or 1.0 when the window
        /// has not been wrapped (for example a plain test window).
        /// </summary>
        public static double GetAppliedScaleFactor(Window window)
            => window != null && States.TryGetValue(window, out WindowScaleState? state)
                ? state.AppliedScale
                : 1.0;

        /// <summary>
        /// Applies the current config scale to one window. Safe to call repeatedly; content is
        /// wrapped once and window dimensions are only adjusted when the factor changes.
        /// </summary>
        public static void ApplyToWindow(Window window)
        {
            if (window == null)
                return;

            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ApplyToWindow(window));
                return;
            }

            ApplyToWindow(window, GetScaleFactor());
        }

        internal static void ApplyToWindow(Window window, double scaleFactor)
        {
            if (window == null)
                return;

            double scale = double.IsFinite(scaleFactor) && scaleFactor > 0
                ? scaleFactor
                : 1.0;

            if (States.TryGetValue(window, out WindowScaleState? state))
            {
                if (!ReferenceEquals(window.Content, state.Host))
                {
                    // Some windows replace their XAML content after construction. Keep the same
                    // applied scale so the window dimensions are not scaled twice.
                    if (window.Content is not Control replacement)
                        return;

                    // Attach the host to the window first so the old content is detached before it
                    // is re-parented under the new host.
                    LayoutTransformControl replacementHost = CreateHost(scale);
                    window.Content = replacementHost;
                    replacementHost.Child = replacement;
                    state.Host = replacementHost;
                    state.Transform = (ScaleTransform)replacementHost.LayoutTransform!;
                }

                ApplyScale(window, state, scale);
                return;
            }

            if (window.Content is not Control content)
                return;

            LayoutTransformControl host = CreateHost(scale);
            window.Content = host;
            host.Child = content;
            state = new WindowScaleState(host, (ScaleTransform)host.LayoutTransform!);
            States.Add(window, state);
            AttachContentWatcher(window);
            ApplyScale(window, state, scale);
        }

        /// <summary>
        /// Keeps wrapping transparent when a window replaces or clears its Content: the previously
        /// wrapped control is released from the scale host so it can be re-parented. The docking
        /// manager relies on this when it moves a shared control into a new floating window.
        /// </summary>
        private static void AttachContentWatcher(Window window)
        {
            void OnContentChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != ContentControl.ContentProperty)
                    return;

                if (!States.TryGetValue(window, out WindowScaleState? state))
                    return;

                if (ReferenceEquals(window.Content, state.Host))
                    return;

                if (state.Host.Child != null)
                    state.Host.Child = null;
            }

            window.PropertyChanged += OnContentChanged;
        }

        /// <summary>Applies a changed scale to every window in the current desktop lifetime.</summary>
        public static void ApplyToOpenWindows()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ApplyToOpenWindows);
                return;
            }

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            foreach (Window window in desktop.Windows)
                ApplyToWindow(window);
        }

        private static LayoutTransformControl CreateHost(double scale)
        {
            ScaleTransform transform = new(scale, scale);
            return new LayoutTransformControl
            {
                Name = HostName,
                LayoutTransform = transform,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };
        }

        private static void ApplyScale(Window window, WindowScaleState state, double scale)
        {
            if (!MMath.Equals(state.Transform.ScaleX, scale) || !MMath.Equals(state.Transform.ScaleY, scale))
            {
                state.Transform.ScaleX = scale;
                state.Transform.ScaleY = scale;
            }

            // Windows may assign Width/Height after registering (ProgressDialogBase does), before
            // the opening pass. Treat such pre-show assignments as logical design sizes.
            if (!state.HasBeenShown && state.MetricsApplied)
                NormalizePreShowMetrics(window, state);

            if (!MMath.Equals(state.AppliedScale, scale))
            {
                double ratio = scale / state.AppliedScale;

                // Bounds are updated before Width/Height so scaling down cannot be clamped by the
                // previous (larger) minimum, and scaling up cannot be clamped by the previous maximum.
                window.MinWidth = ScaleLength(window.MinWidth, ratio);
                window.MinHeight = ScaleLength(window.MinHeight, ratio);
                window.MaxWidth = ScaleLength(window.MaxWidth, ratio);
                window.MaxHeight = ScaleLength(window.MaxHeight, ratio);
                window.Width = ScaleLength(window.Width, ratio);
                window.Height = ScaleLength(window.Height, ratio);
            }

            state.AppliedScale = scale;
            state.LastWidth = window.Width;
            state.LastHeight = window.Height;
            state.LastMinWidth = window.MinWidth;
            state.LastMinHeight = window.MinHeight;
            state.LastMaxWidth = window.MaxWidth;
            state.LastMaxHeight = window.MaxHeight;
            state.MetricsApplied = true;

            if (window.IsVisible)
                state.HasBeenShown = true;
        }

        /// <summary>
        /// Re-bases window metrics that were assigned after the previous scaling pass. Only applies
        /// before the window is first shown, so later user resizes are left untouched.
        /// </summary>
        private static void NormalizePreShowMetrics(Window window, WindowScaleState state)
        {
            double previousScale = state.AppliedScale;

            if (!MMath.Equals(window.Width, state.LastWidth))
                window.Width = ScaleLength(window.Width, previousScale);
            if (!MMath.Equals(window.Height, state.LastHeight))
                window.Height = ScaleLength(window.Height, previousScale);
            if (!MMath.Equals(window.MinWidth, state.LastMinWidth))
                window.MinWidth = ScaleLength(window.MinWidth, previousScale);
            if (!MMath.Equals(window.MinHeight, state.LastMinHeight))
                window.MinHeight = ScaleLength(window.MinHeight, previousScale);
            if (!MMath.Equals(window.MaxWidth, state.LastMaxWidth))
                window.MaxWidth = ScaleLength(window.MaxWidth, previousScale);
            if (!MMath.Equals(window.MaxHeight, state.LastMaxHeight))
                window.MaxHeight = ScaleLength(window.MaxHeight, previousScale);
        }

        private static double ScaleLength(double value, double ratio)
            => double.IsFinite(value) ? value * ratio : value;

        private sealed class WindowScaleState(LayoutTransformControl host, ScaleTransform transform)
        {
            public LayoutTransformControl Host { get; set; } = host;

            public ScaleTransform Transform { get; set; } = transform;

            public double AppliedScale { get; set; } = 1.0;

            public double LastWidth { get; set; } = double.NaN;

            public double LastHeight { get; set; } = double.NaN;

            public double LastMinWidth { get; set; } = double.NaN;

            public double LastMinHeight { get; set; } = double.NaN;

            public double LastMaxWidth { get; set; } = double.NaN;

            public double LastMaxHeight { get; set; } = double.NaN;

            public bool MetricsApplied { get; set; }

            public bool HasBeenShown { get; set; }
        }
    }
}
