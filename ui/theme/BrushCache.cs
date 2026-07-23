using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ModHearth.UI;

public static class BrushCache
{
    private static readonly ConcurrentDictionary<Color, IBrush> _cache = new();

    /// <summary>
    /// Gets a cached, high-performance ImmutableSolidColorBrush for the given color.
    /// </summary>
    public static IBrush GetBrush(Color color)
    {
        return _cache.GetOrAdd(color, static c => new ImmutableSolidColorBrush(c));
    }

    // Call this whenever the theme is changed
    public static void Clear()
    {
        _cache.Clear();
    }
}