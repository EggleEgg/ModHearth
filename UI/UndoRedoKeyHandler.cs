using Avalonia.Input;
using System;
using System.Threading.Tasks;

namespace ModHearth.UI;

public sealed class UndoRedoKeyHandler
{
    private readonly Func<bool> canUndo;
    private readonly Func<Task> undoAsync;
    private readonly Func<bool> canRedo;
    private readonly Action redo;

    public UndoRedoKeyHandler(
        Func<bool> canUndo,
        Func<Task> undoAsync,
        Func<bool> canRedo,
        Action redo)
    {
        this.canUndo = canUndo ?? throw new ArgumentNullException(nameof(canUndo));
        this.undoAsync = undoAsync ?? throw new ArgumentNullException(nameof(undoAsync));
        this.canRedo = canRedo ?? throw new ArgumentNullException(nameof(canRedo));
        this.redo = redo ?? throw new ArgumentNullException(nameof(redo));
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
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
        {
            if (canUndo())
                _ = undoAsync();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
        {
            if (canRedo())
                redo();
            e.Handled = true;
        }
    }
}
