using System;
using System.Globalization;
using System.Windows.Data;

namespace Win11DesktopApp.Converters
{
    /// <summary>Shows a zero-based position (for example ItemsControl.AlternationIndex) as 1, 2, 3...</summary>
    public class IndexPlusOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int index ? (index + 1).ToString(culture) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
