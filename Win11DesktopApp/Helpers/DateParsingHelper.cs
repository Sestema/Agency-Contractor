using System;
using System.Globalization;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Helper for parsing dates in various formats and determining document expiry severity.
    /// </summary>
    public static class DateParsingHelper
    {
        private static readonly string[] DateFormats = new[]
        {
            "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy",
            "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy",
            "dd.MM.yy", "d.M.yy"
        };

        public static DateTime? TryParseDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                return null;

            if (DateTime.TryParseExact(dateStr.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                return result;

            if (DateTime.TryParse(dateStr.Trim(), CultureInfo.GetCultureInfo("cs-CZ"), DateTimeStyles.None, out result))
                return result;

            if (DateTime.TryParse(dateStr.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return result;

            return null;
        }

        public static int GetDaysRemaining(string dateStr)
        {
            var date = TryParseDate(dateStr);
            if (date == null) return int.MaxValue;
            return (date.Value - DateTime.Today).Days;
        }

        /// <summary>
        /// Returns severity level: "Expired", "Critical" (0-7 days), "Warning" (8-30 days), "Ok", "Unknown"
        /// </summary>
        public static string GetSeverity(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                return "Unknown";

            var date = TryParseDate(dateStr);
            if (date == null)
                return "Unknown";

            var days = (date.Value - DateTime.Today).Days;

            if (days < 0) return "Expired";
            if (days <= 7) return "Critical";
            if (days <= 30) return "Warning";
            return "Ok";
        }
    }
}
