using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ModHearth.UI.Theme;
namespace ModHearth.UI.Converters
{
    public class ColorToBrushConverter : IValueConverter
    {
        public static ColorToBrushConverter Instance { get; } = new ColorToBrushConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                Color color => BrushCache.GetBrush(color),
                _ => null,
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
