using System;
using System.Globalization;
using System.Windows.Data;

namespace Win11DesktopApp.Converters
{
    /// <summary>
    /// Turns the available width into a card width for a two-column wrap layout. Below
    /// <see cref="MinTwoColumnWidth"/> a card takes the whole row, so narrow windows fall
    /// back to one column on their own.
    /// </summary>
    public class ColumnWidthConverter : IValueConverter
    {
        public static readonly ColumnWidthConverter Instance = new();

        public double MinTwoColumnWidth { get; set; } = 1300;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double width || double.IsNaN(width) || width <= 0)
                return double.NaN;

            // Floored so two halves never add up to a fraction more than the row and wrap.
            return width >= MinTwoColumnWidth ? Math.Floor(width / 2) : Math.Floor(width);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
