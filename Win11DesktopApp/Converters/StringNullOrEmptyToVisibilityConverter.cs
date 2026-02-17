using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Win11DesktopApp.Converters
{
    public class StringNullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            var isEmpty = string.IsNullOrWhiteSpace(text);
            return isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
