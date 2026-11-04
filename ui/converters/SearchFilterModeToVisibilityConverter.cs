using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace ModHearth.UI.Converters
{
    public class SearchFilterModeToVisibilityConverter : MarkupExtension, IValueConverter
    {
        public static SearchFilterModeToVisibilityConverter Instance { get; } = new SearchFilterModeToVisibilityConverter();

        public override object ProvideValue(IServiceProvider serviceProvider) => this;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool invert = false;
            string? paramStr = parameter as string;
            if (paramStr != null && paramStr.StartsWith('!'))
            {
                invert = true;
                paramStr = paramStr[1..];
            }

            if (value is not SearchFilterMode currentMode)
                return true; // no usable mode: visible regardless of the parameter

            bool matches = parameter switch
            {
                SearchFilterMode targetMode => currentMode == targetMode,
                _ => paramStr == null || !Enum.TryParse(paramStr, out SearchFilterMode parsedMode) || currentMode == parsedMode,
            };
            return invert ? !matches : matches;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}