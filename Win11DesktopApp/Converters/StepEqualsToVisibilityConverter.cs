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
            if (value is not int step) return Visibility.Collapsed;
            var paramStr = parameter?.ToString();
            if (string.IsNullOrEmpty(paramStr) || !int.TryParse(paramStr, out var expected))
                return Visibility.Collapsed;
            return step == expected ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
