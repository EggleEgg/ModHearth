using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
namespace ModHearth.UI;
/// <summary>
/// Converts boolean availability into green/red brushes for status indicators.
/// </summary>
public class BoolToBrushConverter : MarkupExtension, IValueConverter
{
    public IBrush TrueBrush { get; set; } = Brushes.LimeGreen;
    public IBrush FalseBrush { get; set; } = Brushes.Red;
    public override object ProvideValue(IServiceProvider serviceProvider) => this;
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        switch (value)
        {
            case bool b:
                return b ? TrueBrush : FalseBrush;
            default:
                return FalseBrush;
        }
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}