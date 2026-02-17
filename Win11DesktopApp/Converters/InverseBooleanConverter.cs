using System;
using System.Globalization;
using System.Windows.Data;

namespace Win11DesktopApp.Converters
{
    /// <summary>
    /// Converts bool to its inverse. true → false, false → true.
    /// Use for IsReadOnly="{Binding IsEditMode, Converter={StaticResource InverseBool}}"
    /// </summary>
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
}
