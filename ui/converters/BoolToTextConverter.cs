using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace ModHearth.UI;

/// <summary>
/// Used for changing text dynamically in a context menu
/// </summary>
public class BoolToTextConverter : MarkupExtension, IValueConverter
{
    public string TrueValue { get; set; } = string.Empty;
    public string FalseValue { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        switch (value)
        {
            case bool b:
                return b ? TrueValue : FalseValue;
            default:
                return FalseValue;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
