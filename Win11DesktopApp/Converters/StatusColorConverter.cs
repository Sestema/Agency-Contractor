using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Win11DesktopApp.Converters
{
    public static class StatusHelper
    {
        public const string Active = "Active";
        public const string OnLeave = "OnLeave";
        public const string Dismissed = "Dismissed";
        public const string AwaitingDocs = "AwaitingDocs";

        public static readonly string[] AllKeys = { Active, OnLeave, Dismissed, AwaitingDocs };

        private static readonly Dictionary<string, string> _legacyMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Активний", Active },
            { "Активный", Active },
            { "Aktivní", Active },
            { "Active", Active },
            { "У відпустці", OnLeave },
            { "В отпуске", OnLeave },
            { "Na dovolené", OnLeave },
            { "On Leave", OnLeave },
            { "Звільнений", Dismissed },
            { "Уволен", Dismissed },
            { "Propuštěn", Dismissed },
            { "Dismissed", Dismissed },
            { "Очікує документи", AwaitingDocs },
            { "Ожидает документы", AwaitingDocs },
            { "Čeká na dokumenty", AwaitingDocs },
            { "Awaiting Documents", AwaitingDocs },
        };

        private static readonly Dictionary<string, string> _keyToResource = new()
        {
            { Active, "StatusActive" },
            { OnLeave, "StatusOnLeave" },
            { Dismissed, "StatusDismissed" },
            { AwaitingDocs, "StatusAwaitingDocs" },
        };

        public static string Normalize(string? status)
        {
            if (string.IsNullOrEmpty(status)) return Active;
            if (_keyToResource.ContainsKey(status)) return status;
            return _legacyMap.TryGetValue(status, out var key) ? key : Active;
        }

        public static string GetDisplayText(string? statusKey)
        {
            var normalized = Normalize(statusKey);
            if (_keyToResource.TryGetValue(normalized, out var resKey))
            {
                var text = Application.Current?.TryFindResource(resKey) as string;
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return normalized;
        }
    }

    public class StatusDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return StatusHelper.GetDisplayText(value?.ToString());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Unified status converter. Parameter: "Foreground" (default) or "Background".
    /// </summary>
    public class StatusColorConverter : IValueConverter
    {
        private static readonly Dictionary<string, (Color fg, Color bg)> StatusColors = new()
        {
            { StatusHelper.Active, (Color.FromRgb(0x2E, 0x7D, 0x32), Color.FromRgb(0xE8, 0xF5, 0xE9)) },
            { StatusHelper.OnLeave, (Color.FromRgb(0xF9, 0xA8, 0x25), Color.FromRgb(0xFF, 0xF8, 0xE1)) },
            { StatusHelper.Dismissed, (Color.FromRgb(0x75, 0x75, 0x75), Color.FromRgb(0xF5, 0xF5, 0xF5)) },
            { StatusHelper.AwaitingDocs, (Color.FromRgb(0xEF, 0x6C, 0x00), Color.FromRgb(0xFF, 0xF3, 0xE0)) },
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = StatusHelper.Normalize(value?.ToString());
            bool isBg = parameter?.ToString() == "Background";
            if (StatusColors.TryGetValue(status, out var c))
                return new SolidColorBrush(isBg ? c.bg : c.fg);
            return new SolidColorBrush(isBg ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x66, 0x66, 0x66));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Kept for backward compatibility — delegates to StatusColorConverter with Background param.
    /// </summary>
    public class StatusBackgroundConverter : IValueConverter
    {
        private static readonly StatusColorConverter _inner = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => _inner.Convert(value, targetType, "Background", culture);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Unified expiry severity converter. Parameter: "Foreground" (default) or "Background".
    /// </summary>
    public class ExpirySeverityColorConverter : IValueConverter
    {
        private static readonly Dictionary<string, (Color fg, Color bg)> SeverityColors = new()
        {
            { "Expired", (Color.FromRgb(0xC6, 0x28, 0x28), Color.FromRgb(0xFF, 0xEB, 0xEE)) },
            { "Critical", (Color.FromRgb(0xD3, 0x2F, 0x2F), Color.FromRgb(0xFF, 0xEB, 0xEE)) },
            { "Warning", (Color.FromRgb(0xEF, 0x6C, 0x00), Color.FromRgb(0xFF, 0xF3, 0xE0)) },
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var severity = value?.ToString() ?? "Ok";
            bool isBg = parameter?.ToString() == "Background";
            if (SeverityColors.TryGetValue(severity, out var c))
                return new SolidColorBrush(isBg ? c.bg : c.fg);
            return new SolidColorBrush(isBg ? Color.FromRgb(0xE8, 0xF5, 0xE9) : Color.FromRgb(0x2E, 0x7D, 0x32));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Kept for backward compatibility — delegates to ExpirySeverityColorConverter with Background param.
    /// </summary>
    public class ExpirySeverityBackgroundConverter : IValueConverter
    {
        private static readonly ExpirySeverityColorConverter _inner = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => _inner.Convert(value, targetType, "Background", culture);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasValue = value is int count && count > 0;
            bool invert = parameter is string p && p == "invert";
            return (hasValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class RatioToWidthConverter : IMultiValueConverter
    {
        public static readonly RatioToWidthConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is double ratio && values[1] is double totalWidth)
                return Math.Max(4, ratio * totalWidth);
            return 4.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NameToColorConverter : IValueConverter
    {
        private static readonly string[] _colors = {
            "#6366F1", "#8B5CF6", "#EC4899", "#EF4444", "#F97316",
            "#EAB308", "#22C55E", "#14B8A6", "#06B6D4", "#3B82F6",
            "#A855F7", "#D946EF", "#0EA5E9", "#10B981", "#F59E0B"
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var name = value as string ?? "";
            var hash = 0;
            foreach (var c in name) hash = hash * 31 + c;
            var idx = Math.Abs(hash) % _colors.Length;
            return (SolidColorBrush)new BrushConverter().ConvertFrom(_colors[idx])!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class IconKeyToGeometryConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string key && !string.IsNullOrEmpty(key))
                return Application.Current?.TryFindResource(key) as Geometry;
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ResourceKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string key && !string.IsNullOrEmpty(key))
                return Application.Current?.TryFindResource(key) as string ?? key;
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class FirstCharConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            return string.IsNullOrEmpty(s) ? "" : s[0].ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class HexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var hex = value?.ToString() ?? "#2196F3";
                var color = (Color)ColorConverter.ConvertFromString(hex);
                double opacity = 1.0;
                if (parameter is string p && double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out var o))
                    opacity = o;
                return new SolidColorBrush(color) { Opacity = opacity };
            }
            catch
            {
                return new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
