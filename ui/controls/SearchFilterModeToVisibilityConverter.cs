using Avalonia.Markup.Xaml;
using Avalonia.Data.Converters;
using System.Globalization;

namespace ModHearth.UI
{
    public class SearchFilterModeToVisibilityConverter : MarkupExtension, IValueConverter
    {
        public static SearchFilterModeToVisibilityConverter Instance { get; } = new SearchFilterModeToVisibilityConverter();

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool invert = false;
            string? paramStr = parameter as string;
            if (paramStr != null && paramStr.StartsWith('!'))
            {
                invert = true;
                paramStr = paramStr.Substring(1);
            }

            if (value is SearchFilterMode currentMode)
            {
                bool matches;
                if (parameter is SearchFilterMode targetMode)
                {
                    matches = currentMode == targetMode;
                }
                else if (paramStr != null && Enum.TryParse<SearchFilterMode>(paramStr, out var parsedMode))
                {
                    matches = currentMode == parsedMode;
                }
                else
                {
                    matches = true;
                }

                return invert ? !matches : matches;
            }
            return true; // Default to true if conversion fails or no parameter provided
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}