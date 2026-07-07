using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModHearth.UI;

internal static class ContextMenuCoordinator
{
    private static readonly object gate = new();
    private static ContextMenu? activeMenu;

    public static void Activate(ContextMenu menu)
    {
        lock (gate)
        {
            if (activeMenu != null && !ReferenceEquals(activeMenu, menu))
                DismissActiveLocked();

            activeMenu = menu;
            menu.Closed -= OnMenuClosed;
            menu.Closed += OnMenuClosed;
        }
    }

    public static void DismissActive()
    {
        lock (gate)
            DismissActiveLocked();
    }

    private static void DismissActiveLocked()
    {
        if (activeMenu == null)
            return;

        ContextMenu menu = activeMenu;
        activeMenu = null;
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
            if (ReferenceEquals(activeMenu, menu))
                activeMenu = null;
        }
    }
}