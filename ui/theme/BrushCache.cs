using Avalonia.Media;
using Avalonia.Media.Immutable;
using ModHearth.Utilities;

namespace ModHearth.UI.Theme
{
    public static class BrushCache
    {
        // Theme switches and blended overlay colors are finite, but bounding the cache keeps it from
        // growing indefinitely if callers pass computed colors.
        private static readonly BoundedLruCache<Color, IBrush> _cache = new(512);

        /// <summary>
        /// Gets a cached, high-performance ImmutableSolidColorBrush for the given color.
        /// </summary>
        public static IBrush GetBrush(Color color) => _cache.GetOrAdd(color, static c => new ImmutableSolidColorBrush(c));

        /// <summary>
        /// Gets a cached brush for the given color with a specified alpha channel.
        /// </summary>
        public static IBrush EditBrushAlpha(Color color, byte alpha) => GetBrush(Color.FromArgb(alpha, color.R, color.G, color.B));

        /// <inheritdoc cref="EditBrushAlpha(Color, byte)"/>
        public static IBrush EditBrushAlpha(SimpleColor color, byte alpha) => EditBrushAlpha(color.ToAvaloniaColor(), alpha);

        /// <inheritdoc cref="EditBrushAlpha(Color, byte)"/>
        public static IBrush EditBrushAlpha(IBrush brush, byte alpha)
        {
            return brush switch
            {
                ISolidColorBrush solid => EditBrushAlpha(solid.Color, alpha),
                _ => brush,
            };
        }
    }
}