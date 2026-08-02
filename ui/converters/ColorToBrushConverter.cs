using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;
namespace ModHearth.UI
{
    public class ColorToBrushConverter : IValueConverter
    {
        public static ColorToBrushConverter Instance { get; } = new ColorToBrushConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                return BrushCache.GetBrush(color);
            }
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
