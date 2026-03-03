using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Win11DesktopApp.Converters
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool flag)
                return !flag;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool flag)
                return !flag;
            return false;
        }
    }

    public class PercentToScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int percent)
                return Math.Max(0.0, Math.Min(1.0, percent / 100.0));
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class ExpiryWarningToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var warning = value as string ?? string.Empty;
            return warning switch
            {
                "expired" or "critical" => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                "warning" => new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)),
                _ => Brushes.Transparent
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class ExpiryWarningToBgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var warning = value as string ?? string.Empty;
            return warning switch
            {
                "expired" or "critical" => new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE)),
                "warning" => new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0)),
                _ => Brushes.Transparent
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class ExpiryWarningToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var warning = value as string ?? string.Empty;
            return string.IsNullOrEmpty(warning) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class StringEqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return parameter?.ToString() ?? "";
            return Binding.DoNothing;
        }
    }

    public class ExpiryWarningToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var warning = value as string ?? string.Empty;
            return warning switch
            {
                "expired" => Application.Current?.TryFindResource("ExpiryExpired") as string ?? "⚠ Expired",
                "critical" => Application.Current?.TryFindResource("ExpiryCritical") as string ?? "⚠ < 7 days",
                "warning" => Application.Current?.TryFindResource("ExpiryWarning") as string ?? "⚠ < 30 days",
                _ => string.Empty
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
