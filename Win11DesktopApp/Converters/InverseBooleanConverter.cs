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
            => Binding.DoNothing;
    }

    public class ZoomDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var zoom = ToDouble(value, 1.0);
            var baseValue = ToDouble(parameter, 0.0);
            return Math.Round(baseValue * zoom, 2);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static double ToDouble(object? value, double fallback)
        {
            return value switch
            {
                double d => d,
                int i => i,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => fallback
            };
        }

        internal static double ToDoublePublic(object? value, double fallback) => ToDouble(value, fallback);
    }

    public class ZoomFontSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var zoom = ZoomDoubleConverter.ToDoublePublic(value, 1.0);
            var baseValue = ZoomDoubleConverter.ToDoublePublic(parameter, 12.0);
            return Math.Max(8.0, Math.Round(baseValue * zoom, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public class ZoomThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var zoom = value is double d ? d : 1.0;
            var parts = (parameter as string ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1 && TryPart(parts[0], out var all))
                return new Thickness(Math.Round(all * zoom, 2));

            if (parts.Length == 4
                && TryPart(parts[0], out var left)
                && TryPart(parts[1], out var top)
                && TryPart(parts[2], out var right)
                && TryPart(parts[3], out var bottom))
            {
                return new Thickness(
                    Math.Round(left * zoom, 2),
                    Math.Round(top * zoom, 2),
                    Math.Round(right * zoom, 2),
                    Math.Round(bottom * zoom, 2));
            }

            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static bool TryPart(string value, out double result) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    public class DocumentSeverityBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var severity = value as string ?? "Ok";
            var kind = (parameter as string ?? "Foreground").Trim();

            var tier = severity switch
            {
                "Expired" => "Error",
                "Warning" or "Critical" or "Unknown" => "Warning",
                _ => "Success"
            };

            var resourceKey = (tier, kind) switch
            {
                ("Error", "Background") => "EmployeeDocErrorBackgroundBrush",
                ("Error", "CardBorder") => "EmployeeDocErrorCardBorderBrush",
                ("Error", "Tile") => "EmployeeDocErrorTileBrush",
                ("Error", _) => "EmployeeDocErrorForegroundBrush",

                ("Warning", "Background") => "EmployeeDocWarningBackgroundBrush",
                ("Warning", "CardBorder") => "EmployeeDocWarningCardBorderBrush",
                ("Warning", "Tile") => "EmployeeDocWarningTileBrush",
                ("Warning", _) => "EmployeeDocWarningForegroundBrush",

                ("Success", "Background") => "EmployeeDocSuccessBackgroundBrush",
                ("Success", "CardBorder") => "EmployeeDocSuccessCardBorderBrush",
                ("Success", "Tile") => "EmployeeDocSuccessTileBrush",
                _ => "EmployeeDocSuccessForegroundBrush"
            };

            return Application.Current?.TryFindResource(resourceKey) ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public class ZoomCornerRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var zoom = value is double d ? d : 1.0;
            var baseRadius = ZoomDoubleConverter.ToDoublePublic(parameter, 14.0);
            var radius = Math.Round(baseRadius * zoom, 2);
            return new CornerRadius(radius);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public class ZoomRectConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var zoom = value is double d ? d : 1.0;
            var parts = (parameter as string ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
            {
                return Rect.Empty;
            }

            return new Rect(
                Math.Round(x * zoom, 2),
                Math.Round(y * zoom, 2),
                Math.Round(width * zoom, 2),
                Math.Round(height * zoom, 2));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
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
            => Binding.DoNothing;
    }

    /// <summary>
    /// Status "traffic light" outline for a document card: red (expired), orange (soon),
    /// green (valid). Independent of theme, matching the Problems view severity palette.
    /// </summary>
    public class ExpiryWarningToBorderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var warning = value as string ?? string.Empty;
            return warning switch
            {
                "expired" or "critical" => new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x8A)),
                "warning" => new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0x77)),
                _ => new SolidColorBrush(Color.FromRgb(0xA8, 0xD5, 0xB0))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
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
            => Binding.DoNothing;
    }

    public class ExpiryWarningToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var warning = value as string ?? string.Empty;
            return string.IsNullOrEmpty(warning) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Returns just the last path segment (folder or file name) of a full path,
    /// so cards can show a short, readable label while keeping the full path in a tooltip.
    /// </summary>
    public class PathLeafConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var trimmed = path.TrimEnd('\\', '/');
            var idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return idx >= 0 && idx < trimmed.Length - 1 ? trimmed.Substring(idx + 1) : trimmed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
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
            => Binding.DoNothing;
    }

    /// <summary>
    /// Maps a "Table"/"List"/"Tiles"/"Icons" view-mode string to a pixel offset
    /// (index * segment width) used to position the sliding pill in a segmented
    /// view-mode switcher. Unlike an animation-clock-held RenderTransform, this is
    /// a plain data-bound value: it can never get "lost" if the visual tree around
    /// it is rebuilt, because WPF simply re-evaluates the binding.
    /// </summary>
    public class ViewModeToPillOffsetConverter : IValueConverter
    {
        private static readonly string[] Order = { "Table", "List", "Tiles", "Icons" };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var mode = value as string ?? string.Empty;
            var segmentWidth = ZoomDoubleConverter.ToDoublePublic(parameter, 32.0);
            var index = Array.IndexOf(Order, mode);
            return index < 0 ? 0.0 : index * segmentWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
