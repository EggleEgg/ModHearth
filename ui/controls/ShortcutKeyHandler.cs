using System;
using System.Threading.Tasks;
using Avalonia.Input;

namespace ModHearth.UI;

public sealed class ShortcutKeyHandler
{
    private readonly Func<bool> canUndo;
    private readonly Func<Task> undoAsync;
    private readonly Func<bool> canRedo;
    private readonly Func<Task> redoAsync;
    private readonly Func<bool>? canSave;
    private readonly Func<Task>? saveAsync;

    public ShortcutKeyHandler(
        Func<bool> canUndo,
        Func<Task> undoAsync,
        Func<bool> canRedo,
        Func<Task> redoAsync,
        Func<bool>? canSave = null,
        Func<Task>? saveAsync = null)
    {
        this.canUndo = canUndo ?? throw new ArgumentNullException(nameof(canUndo));
        this.undoAsync = undoAsync ?? throw new ArgumentNullException(nameof(undoAsync));
        this.canRedo = canRedo ?? throw new ArgumentNullException(nameof(canRedo));
        this.redoAsync = redoAsync ?? throw new ArgumentNullException(nameof(redoAsync));
        this.canSave = canSave;
        this.saveAsync = saveAsync;
    }

    public void Attach(InputElement element)
    {
        if (element == null)
            throw new ArgumentNullException(nameof(element));
        element.KeyDown += OnKeyDown;
    }

    public void Detach(InputElement element)
    {
        if (element == null)
            throw new ArgumentNullException(nameof(element));
        element.KeyDown -= OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.Z)
            {
                if (canUndo())
                    _ = undoAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y)
            {
                if (canRedo())
                    _ = redoAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && saveAsync != null && (canSave == null || canSave()))
            {
                _ = saveAsync();
                e.Handled = true;
            }
        }
    }
}
