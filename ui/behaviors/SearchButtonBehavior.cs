using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;
using ModHearth.UI.Theme;

namespace ModHearth.UI.Behaviors
{
    /// <summary>
    /// A clean, scalable behavior that manages hover, press, and normal background brushes of a Button.
    /// </summary>
    public sealed class SearchButtonBehavior : IDisposable
    {
        // ConditionalWeakTable keeps the behaviour attached to the lifetime of the button instead of
        // rooting every button (and its visual tree) for the duration of the process.
        private static readonly ConditionalWeakTable<Button, SearchButtonBehavior> behaviors = [];

        private readonly Button button;
        private IDisposable? pointerOverSubscription;
        private bool isHooked;
        private bool isDisposed;

        private IBrush normalBrush = Brushes.Transparent;
        private IBrush hoverBrush = Brushes.Transparent;
        private IBrush pressedBrush = Brushes.Transparent;
        private bool isPointerOver;
        private bool isPressed;

        public static SearchButtonBehavior GetOrCreate(Button button)
        {
            ArgumentNullException.ThrowIfNull(button);
            return behaviors.GetValue(button, static b => new SearchButtonBehavior(b));
        }

        public static void ApplyStyle(Button button, Style style)
        {
            if (button == null || style == null)
                return;

            SearchButtonBehavior behavior = GetOrCreate(button);
            behavior.ApplyBrushes(
                BrushCache.GetBrush(style.SearchButtonColor.ToAvaloniaColor()),
                BrushCache.GetBrush(style.SearchButtonHoverColor.ToAvaloniaColor()),
                BrushCache.GetBrush(style.SearchButtonPressedColor.ToAvaloniaColor())
            );
            button.Foreground = BrushCache.GetBrush(style.ButtonTextColor.ToAvaloniaColor());
            button.BorderBrush = Brushes.Transparent;
        }

        private SearchButtonBehavior(Button button)
        {
            this.button = button ?? throw new ArgumentNullException(nameof(button));

            Hook();
            button.AttachedToVisualTree += OnAttachedToVisualTree;
            button.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => Hook();

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => Unhook();


        private void Hook()
        {
            if (isHooked || isDisposed)
                return;

            isHooked = true;
            pointerOverSubscription = button.GetObservable(InputElement.IsPointerOverProperty)
                .Subscribe(new AnonymousObserver<bool>(OnPointerOverChanged));

            button.PointerPressed += OnPointerPressed;
            button.PointerReleased += OnPointerReleased;
            button.PointerCaptureLost += OnPointerCaptureLost;
            button.Click += OnClick;
        }

        private void Unhook()
        {
            if (!isHooked)
                return;

            isHooked = false;
            pointerOverSubscription?.Dispose();
            pointerOverSubscription = null;

            button.PointerPressed -= OnPointerPressed;
            button.PointerReleased -= OnPointerReleased;
            button.PointerCaptureLost -= OnPointerCaptureLost;
            button.Click -= OnClick;
        }

        private void OnPointerOverChanged(bool isOver)
        {
            isPointerOver = isOver;
            if (!isOver)
                isPressed = false;
            UpdateBackground();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
        {
            if (args.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                isPressed = true;
                UpdateBackground();
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => ResetState();

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetState();

        private void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ResetState();


        public void ApplyBrushes(IBrush normal, IBrush hover, IBrush pressed)
        {
            normalBrush = normal ?? Brushes.Transparent;
            hoverBrush = hover ?? Brushes.Transparent;
            pressedBrush = pressed ?? Brushes.Transparent;
            UpdateBackground();
        }

        private void ResetState()
        {
            isPressed = false;
            isPointerOver = button.IsPointerOver;
            UpdateBackground();
        }

        private void UpdateBackground()
        {
            if (isPressed)
                button.Background = pressedBrush;
            else
                button.Background = isPointerOver ? hoverBrush : normalBrush;
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            Unhook();
            button.AttachedToVisualTree -= OnAttachedToVisualTree;
            button.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            _ = behaviors.Remove(button);
        }
    }
}
