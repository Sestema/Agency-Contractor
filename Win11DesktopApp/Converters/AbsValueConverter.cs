using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Win11DesktopApp.Converters
{
    public class AbsValueConverter : IValueConverter
    {
        public static readonly AbsValueConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i) return Math.Abs(i);
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NegativeCheckConverter : IValueConverter
    {
        public static readonly NegativeCheckConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d) return d < 0;
            if (value is double db) return db < 0;
            if (value is int i) return i < 0;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class DaysRemainingTextConverter : IValueConverter
    {
        public static readonly DaysRemainingTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int days)
                return ViewModels.ProblemsViewModel.DaysRemainingText(days);
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
