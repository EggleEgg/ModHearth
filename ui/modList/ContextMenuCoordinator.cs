using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModHearth.UI;

internal static class ContextMenuCoordinator
{
    private static readonly object gate = new();

    // Weak so a menu that never fires closed doesn't keep itself and everything it's rooted to (the avalonia visual tree)
    // alive for the rest of the app's lifetime via this static field
    private static WeakReference<ContextMenu>? activeMenuRef;

    public static void Activate(ContextMenu menu)
    {
        lock (gate)
        {
            ContextMenu? current = GetActiveLocked();
            if (current != null && !ReferenceEquals(current, menu))
                DismissActiveLocked();

            activeMenuRef = new WeakReference<ContextMenu>(menu);
            menu.Closed -= OnMenuClosed;
            menu.Closed += OnMenuClosed;
        }
    }

    public static void DismissActive()
    {
        lock (gate)
            DismissActiveLocked();
    }

    private static ContextMenu? GetActiveLocked()
    {
        return activeMenuRef != null && activeMenuRef.TryGetTarget(out ContextMenu? menu) ? menu : null;
    }

    private static void DismissActiveLocked()
    {
        ContextMenu? menu = GetActiveLocked();
        activeMenuRef = null;

        if (menu == null)
            return;

        menu.Closed -= OnMenuClosed;
        menu.Close();
    }

    private static void OnMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        lock (gate)
        {
            menu.Closed -= OnMenuClosed;
            if (ReferenceEquals(GetActiveLocked(), menu))
                activeMenuRef = null;
        }
    }
}