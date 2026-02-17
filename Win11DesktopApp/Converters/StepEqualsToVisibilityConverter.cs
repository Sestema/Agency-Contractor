using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Win11DesktopApp.Converters
{
    public class StepEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int step && parameter is string param && int.TryParse(param, out var expected))
            {
                return step == expected ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
