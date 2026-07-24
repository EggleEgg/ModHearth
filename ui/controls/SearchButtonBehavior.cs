using System;
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
    private readonly Button button;
    private IBrush normalBrush = Brushes.Transparent;
    private IBrush hoverBrush = Brushes.Transparent;
    private IBrush pressedBrush = Brushes.Transparent;
    private bool isPointerOver;
    private bool isPressed;

    public SearchButtonBehavior(Button button)
    {
        this.button = button ?? throw new ArgumentNullException(nameof(button));

        button.GetObservable(InputElement.IsPointerOverProperty)
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
        button.Background = isPressed ? pressedBrush : (isPointerOver ? hoverBrush : normalBrush);
    }
}
