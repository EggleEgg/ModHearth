using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ModHearth.UI;

/// <summary>
/// Renders a 45º diagonal pattern instead of using the buggy and prone to leak VisualBrush.
/// </summary>
public class ModBackground : Control
{
    public static readonly StyledProperty<IBrush?> UnderlayBrushProperty =
        AvaloniaProperty.Register<ModBackground, IBrush?>(nameof(UnderlayBrush));

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<ModBackground, double>(nameof(LineThickness), 2);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ModBackground, double>(nameof(Spacing), 4);

    public IBrush? UnderlayBrush
    {
        get => GetValue(UnderlayBrushProperty);
        set => SetValue(UnderlayBrushProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    static ModBackground()
    {
        // Re-render the control automatically when any of these properties change
        AffectsRender<ModBackground>(UnderlayBrushProperty, LineThicknessProperty, SpacingProperty);
    }

    private Pen? _cachedPen;
    private IBrush? _cachedBrush;
    private double _cachedThickness = -1;

    public override void Render(DrawingContext context)
    {
        var brush = UnderlayBrush;
        if (brush == null)
            return;

        var bounds = Bounds;
        double width = bounds.Width;
        double height = bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        double thickness = LineThickness;
        var pen = _cachedPen;
        if (pen == null || _cachedBrush != brush || _cachedThickness != thickness)
        {
            pen = new Pen(brush, thickness);
            _cachedPen = pen;
            _cachedBrush = brush;
            _cachedThickness = thickness;
        }

        // Use the configured Spacing (safety check to prevent infinite loops)
        double spacing = Spacing;
        if (spacing <= 0.1)
            spacing = 0.1;

        // Clip the drawings strictly to this control's bounding box
        using (context.PushClip(new Rect(0, 0, width, height)))
        {
            // Mathematically draw parallel 45-degree diagonal lines.
            // We start our offset sequence at 3.5 to perfectly match your original canvas offsets.
            for (double offset = 3.5; offset < width + height; offset += spacing)
            {
                // Find line-intersection with the top edge (y=0)
                double x1 = offset;
                double y1 = 0;
                if (x1 > width)
                {
                    x1 = width;
                    y1 = offset - width;
                }

                // Find line-intersection with the left edge (x=0)
                double y2 = offset;
                double x2 = 0;
                if (y2 > height)
                {
                    y2 = height;
                    x2 = offset - height;
                }

                // If the segment is within bounds, draw it
                if (y1 < height && x2 < width)
                {
                    context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
                }
            }
        }
    }
}