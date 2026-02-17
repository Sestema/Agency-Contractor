using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Win11DesktopApp.Converters
{
    /// <summary>
    /// Converts employee status string to a foreground color brush.
    /// </summary>
    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value?.ToString() ?? string.Empty;
            return status switch
            {
                "Активний" => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                "У відпустці" => new SolidColorBrush(Color.FromRgb(0xF9, 0xA8, 0x25)),
                "Звільнений" => new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75)),
                "Очікує документи" => new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00)),
                _ => new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts employee status string to a background color brush (lighter version).
    /// </summary>
    public class StatusBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value?.ToString() ?? string.Empty;
            return status switch
            {
                "Активний" => new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)),
                "У відпустці" => new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1)),
                "Звільнений" => new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                "Очікує документи" => new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0)),
                _ => new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts document expiry severity to a color brush.
    /// </summary>
    public class ExpirySeverityColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var severity = value?.ToString() ?? "Ok";
            return severity switch
            {
                "Expired" => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                "Critical" => new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)),
                "Warning" => new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00)),
                _ => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts document expiry severity to a background color brush.
    /// </summary>
    public class ExpirySeverityBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var severity = value?.ToString() ?? "Ok";
            return severity switch
            {
                "Expired" => new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE)),
                "Critical" => new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE)),
                "Warning" => new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0)),
                _ => new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts an int > 0 to Visible, otherwise Collapsed.
    /// </summary>
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && count > 0)
                return System.Windows.Visibility.Visible;
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
