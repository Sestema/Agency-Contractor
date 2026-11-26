using System;
using System.Globalization;
using System.Windows.Data;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Converters
{
    /// <summary>Shows an amount as "362 712" (spaces between thousands), the same way across the finance page.</summary>
    public sealed class MoneySpacesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                decimal amount => SalaryViewModel.FormatMoney(amount),
                double number => SalaryViewModel.FormatMoney((decimal)number),
                int whole => SalaryViewModel.FormatMoney(whole),
                _ => value?.ToString() ?? string.Empty
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
