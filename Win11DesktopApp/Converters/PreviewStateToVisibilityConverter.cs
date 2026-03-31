using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Converters
{
    public class PreviewStateToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DocPreviewState state
                && parameter is string target
                && Enum.TryParse(target, ignoreCase: true, out DocPreviewState expected))
            {
                return state == expected ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
