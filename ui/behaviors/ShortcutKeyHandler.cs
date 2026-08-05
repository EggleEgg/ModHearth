using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ModHearth.UI;

/// <summary>
/// Generic unified class for handling keyboard actions
/// </summary>
public sealed class ShortcutKeyHandler
{
    private readonly Func<bool> canUndo;
    private readonly Func<Task> undoAsync;
    private readonly Func<bool> canRedo;
    private readonly Func<Task> redoAsync;
    private readonly Func<bool>? canSave;
    private readonly Func<Task>? saveAsync;
    private readonly Func<Task>? moveLeftAsync;
    private readonly Func<Task>? moveRightAsync;

    public ShortcutKeyHandler(
        Func<bool> canUndo,
        Func<Task> undoAsync,
        Func<bool> canRedo,
        Func<Task> redoAsync,
        Func<bool>? canSave = null,
        Func<Task>? saveAsync = null,
        Func<Task>? moveLeftAsync = null,
        Func<Task>? moveRightAsync = null)
    {
        this.canUndo = canUndo ?? throw new ArgumentNullException(nameof(canUndo));
        this.undoAsync = undoAsync ?? throw new ArgumentNullException(nameof(undoAsync));
        this.canRedo = canRedo ?? throw new ArgumentNullException(nameof(canRedo));
        this.redoAsync = redoAsync ?? throw new ArgumentNullException(nameof(redoAsync));
        this.canSave = canSave;
        this.saveAsync = saveAsync;
        this.moveLeftAsync = moveLeftAsync;
        this.moveRightAsync = moveRightAsync;
    }

    public void Attach(InputElement element)
    {
        if (element == null)
            throw new ArgumentNullException(nameof(element));
        element.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public void Detach(InputElement element)
    {
        if (element == null)
            throw new ArgumentNullException(nameof(element));
        element.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.Z:
                    if (canUndo())
                        _ = undoAsync();
                    e.Handled = true;
                    return;
                case Key.Y:
                    if (canRedo())
                        _ = redoAsync();
                    e.Handled = true;
                    return;
                case Key.S when saveAsync != null && (canSave == null || canSave()):
                    _ = saveAsync();
                    e.Handled = true;
                    return;

            }
        }

        if (e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key)
            {
                case Key.Left when moveLeftAsync != null:
                    if (!IsTextInputFocused(e.Source))
                    {
                        _ = moveLeftAsync();
                        e.Handled = true;
                    }
                    break;
                case Key.Right when moveRightAsync != null:
                    if (!IsTextInputFocused(e.Source))
                    {
                        _ = moveRightAsync();
                        e.Handled = true;
                    }
                    break;

            }
        }
    }

    private static bool IsTextInputFocused(object? source)
    {
        return source is TextBox || source is ComboBox;
    }
}
