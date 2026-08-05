using Avalonia;
using Avalonia.Controls;

namespace ModHearth.UI;

/// <summary>
/// Reusable helper for adding sideways scrolling behavior to ScrollViewers.
/// </summary>
public static class HorizontalScrollHelper
{
    public static void EnableSidewaysScrolling(ScrollViewer scrollViewer, double scrollSpeed = 40)
    {
        if (scrollViewer == null)
            return;

        scrollViewer.PointerWheelChanged += (sender, args) =>
        {
            if (args.Delta.Y != 0)
            {
                var currentOffset = scrollViewer.Offset;
                double newX = currentOffset.X - (args.Delta.Y * scrollSpeed);
                double maxX = scrollViewer.Extent.Width - scrollViewer.Viewport.Width;
                newX = Math.Clamp(newX, 0, Math.Max(0, maxX));

                scrollViewer.Offset = new Vector(newX, currentOffset.Y);
                args.Handled = true;
            }
        };
    }
}
