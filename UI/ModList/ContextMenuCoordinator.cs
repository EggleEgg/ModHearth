using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModHearth.UI;

internal static class ContextMenuCoordinator
{
    private static ContextMenu? activeMenu;

    public static void Activate(ContextMenu menu)
    {
        if (activeMenu != null && !ReferenceEquals(activeMenu, menu))
            DismissActive();

        activeMenu = menu;
        menu.Closed -= OnMenuClosed;
        menu.Closed += OnMenuClosed;
    }

    public static void DismissActive()
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

        menu.Closed -= OnMenuClosed;
        if (ReferenceEquals(activeMenu, menu))
            activeMenu = null;
    }
}
