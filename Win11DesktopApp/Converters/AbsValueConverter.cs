using System;
using System.Globalization;
using System.Windows.Data;

namespace Win11DesktopApp.Converters
{
    /// <summary>
    /// Returns the absolute value of an integer.
    /// </summary>
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

    /// <summary>
    /// Converts DaysRemaining to human-readable text like "через 15 дн." or "прострочено 5 дн."
    /// </summary>
    public class DaysRemainingTextConverter : IValueConverter
    {
        public static readonly DaysRemainingTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int days)
            {
                if (days < 0)
                    return $"прострочено {Math.Abs(days)} дн.";
                if (days == 0)
                    return "закінчується сьогодні";
                return $"через {days} дн.";
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
