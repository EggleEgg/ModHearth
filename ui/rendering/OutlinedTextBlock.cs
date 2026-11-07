using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ModHearth.UI.Rendering
{
    /// <summary>
    /// Another one of those things that really should be in Avalonia by default
    /// </summary>
    public class OutlinedTextBlock : Control
    {
        public static readonly StyledProperty<string> TextProperty =
                AvaloniaProperty.Register<OutlinedTextBlock, string>(nameof(Text), string.Empty);

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<OutlinedTextBlock, IBrush>(nameof(Foreground), Brushes.White);

        public static readonly StyledProperty<IBrush> StrokeProperty =
            AvaloniaProperty.Register<OutlinedTextBlock, IBrush>(nameof(Stroke), Brushes.Black);

        public static readonly StyledProperty<double> StrokeThicknessProperty =
            AvaloniaProperty.Register<OutlinedTextBlock, double>(nameof(StrokeThickness), 1.0);

        public static readonly StyledProperty<double> FontSizeProperty =
            AvaloniaProperty.Register<OutlinedTextBlock, double>(nameof(FontSize), 12.0);

        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            AvaloniaProperty.Register<OutlinedTextBlock, FontFamily>(nameof(FontFamily), FontFamily.Default);

        public static readonly StyledProperty<FontWeight> FontWeightProperty =
            AvaloniaProperty.Register<OutlinedTextBlock, FontWeight>(nameof(FontWeight), FontWeight.Normal);

        public static readonly StyledProperty<FontStyle> FontStyleProperty =
            AvaloniaProperty.Register<OutlinedTextBlock, FontStyle>(nameof(FontStyle), FontStyle.Normal);

        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
        public IBrush Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
        public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
        public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
        public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
        public FontStyle FontStyle { get => GetValue(FontStyleProperty); set => SetValue(FontStyleProperty, value); }

        static OutlinedTextBlock()
        {
            AffectsRender<OutlinedTextBlock>(
                TextProperty, ForegroundProperty, StrokeProperty, StrokeThicknessProperty,
                FontSizeProperty, FontFamilyProperty, FontWeightProperty, FontStyleProperty);

            AffectsMeasure<OutlinedTextBlock>(
                TextProperty, FontSizeProperty, FontFamilyProperty, FontWeightProperty, FontStyleProperty);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            FormattedText formattedText = CreateFormattedText();
            return new Size(formattedText.Width, formattedText.Height);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            FormattedText formattedText = CreateFormattedText();
            Geometry? geometry = formattedText.BuildGeometry(new Point(0, 0));

            if (geometry != null)
            {
                Pen pen = new(Stroke, StrokeThickness);
                context.DrawGeometry(Foreground, pen, geometry);
            }
        }

        private FormattedText CreateFormattedText()
        {
            return new FormattedText(
                Text ?? string.Empty,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeight),
                FontSize,
                Foreground
            );
        }
    }
}