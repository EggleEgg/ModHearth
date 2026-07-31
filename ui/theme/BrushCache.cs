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

    /// <summary>
    /// Gets a cached brush with RGB values adjusted by the specified delta, clamped to valid byte range [0, 255] to avoid overflow/underflow.
    /// </summary>
    public static IBrush EditBrushDelta(Color color, int delta)
    {
        byte r = (byte)Math.Clamp(color.R + delta, 0, 255);
        byte g = (byte)Math.Clamp(color.G + delta, 0, 255);
        byte b = (byte)Math.Clamp(color.B + delta, 0, 255);
        return GetBrush(Color.FromArgb(color.A, r, g, b));
    }

    /// <summary>
    /// Gets a cached brush with RGB values adjusted by the specified delta, clamped to valid byte range [0, 255] to avoid overflow/underflow.
    /// </summary>
    public static IBrush EditBrushDelta(SimpleColor color, int delta)
    {
        return EditBrushDelta(color.ToAvaloniaColor(), delta);
    }

    /// <summary>
    /// Gets a cached brush for the given color with a specified alpha channel.
    /// </summary>
    public static IBrush EditBrushAlpha(Color color, byte alpha)
    {
        return GetBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>
    /// Gets a cached brush for the given color with a specified alpha channel.
    /// </summary>
    public static IBrush EditBrushAlpha(SimpleColor color, byte alpha)
    {
        return EditBrushAlpha(color.ToAvaloniaColor(), alpha);
    }

    // Call this whenever the theme is changed
    public static void Clear()
    {
        _cache.Clear();
    }
}