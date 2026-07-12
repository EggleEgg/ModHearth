using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ModHearth.UI;

public static class BrushCache
{
    private static readonly Dictionary<Color, IBrush> _cache = new();

    /// <summary>
    /// Gets a cached, high-performance ImmutableSolidColorBrush for the given color.
    /// </summary>
    public static IBrush GetBrush(Color color)
    {
        if (!_cache.TryGetValue(color, out var brush))
        {
            brush = new ImmutableSolidColorBrush(color);
            _cache[color] = brush;
        }
        return brush;
    }

    // Call this whenever the theme is changed
    public static void Clear()
    {
        _cache.Clear();
    }
}