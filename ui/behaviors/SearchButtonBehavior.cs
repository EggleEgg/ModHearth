using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;

namespace ModHearth.UI;

/// <summary>
/// A clean, scalable behavior that manages hover, press, and normal background brushes of a Button.
/// </summary>
public sealed class SearchButtonBehavior
{
    private static readonly Dictionary<Button, SearchButtonBehavior> behaviors = [];

    public static SearchButtonBehavior GetOrCreate(Button button)
    {
        if (!behaviors.TryGetValue(button, out var behavior))
        {
            behavior = new SearchButtonBehavior(button);
            behaviors[button] = behavior;
        }
        return behavior;
    }

    public static void ApplyStyle(Button button, Style style)
    {
        if (button == null || style == null)
            return;

        var behavior = GetOrCreate(button);
        behavior.ApplyBrushes(
            BrushCache.GetBrush(style.searchButtonColor.ToAvaloniaColor()),
            BrushCache.GetBrush(style.searchButtonHoverColor.ToAvaloniaColor()),
            BrushCache.GetBrush(style.searchButtonPressedColor.ToAvaloniaColor())
        );
        button.Foreground = BrushCache.GetBrush(style.buttonTextColor.ToAvaloniaColor());
        button.BorderBrush = Brushes.Transparent;
    }

    private readonly Button button;
    private IBrush normalBrush = Brushes.Transparent;
    private IBrush hoverBrush = Brushes.Transparent;
    private IBrush pressedBrush = Brushes.Transparent;
    private bool isPointerOver;
    private bool isPressed;

    public SearchButtonBehavior(Button button)
    {
        this.button = button ?? throw new ArgumentNullException(nameof(button));

        _ = button.GetObservable(InputElement.IsPointerOverProperty)
              .Subscribe(new AnonymousObserver<bool>(isOver =>
              {
                  isPointerOver = isOver;
                  if (!isOver)
                      isPressed = false;
                  UpdateBackground();
              }));

        button.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                isPressed = true;
                UpdateBackground();
            }
        };

        button.PointerReleased += (_, _) => ResetState();
        button.PointerCaptureLost += (_, _) => ResetState();
        button.Click += (_, _) => ResetState();
    }

    public void ApplyBrushes(IBrush normal, IBrush hover, IBrush pressed)
    {
        normalBrush = normal ?? Brushes.Transparent;
        hoverBrush = hover ?? Brushes.Transparent;
        pressedBrush = pressedBrush ?? Brushes.Transparent;
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
        {
            button.Background = isPointerOver ? hoverBrush : normalBrush;
        }
    }
}
