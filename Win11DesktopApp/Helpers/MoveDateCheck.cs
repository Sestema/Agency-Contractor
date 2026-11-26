using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Helpers
{
    /// <summary>
    /// Checks the dates typed when archiving or restoring an employee.
    /// A date that cannot be read is refused; an unusual but possible date only gives a warning,
    /// because real cases exist (for example a person who never came is archived before the start date).
    /// </summary>
    internal static class MoveDateCheck
    {
        public const string Format = "dd.MM.yyyy";

        // Day, month and year must all be there: "26.11" or "abc" are refused.
        private static readonly Regex FullDate = new(
            @"^\s*(\d{1,2}[./-]\d{1,2}[./-](\d{2}|\d{4})|\d{4}-\d{1,2}-\d{1,2})\s*$",
            RegexOptions.CultureInvariant);

        public enum Warning
        {
            EndBeforeStart,
            StartBeforeArchiveEnd,
            FarFromToday
        }

        /// <summary>Reads a full date and gives it back as dd.MM.yyyy; false when it cannot be read.</summary>
        public static bool TryNormalize(string? text, out string normalized, out DateTime date)
        {
            normalized = string.Empty;
            date = default;
            if (string.IsNullOrWhiteSpace(text) || !FullDate.IsMatch(text))
                return false;

            var parsed = DateParsingHelper.TryParseDate(text.Trim());
            if (parsed == null || parsed.Value.Year < 1900 || parsed.Value.Year > 2200)
                return false;

            date = parsed.Value.Date;
            normalized = date.ToString(Format, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Warnings for an archive end date.</summary>
        public static List<Warning> ForArchive(DateTime endDate, DateTime? startDate, DateTime today)
        {
            var result = new List<Warning>();
            if (startDate != null && endDate.Date < startDate.Value.Date)
                result.Add(Warning.EndBeforeStart);
            if (IsFar(endDate, today))
                result.Add(Warning.FarFromToday);
            return result;
        }

        /// <summary>Warnings for the start date in the new firm when restoring from the archive.</summary>
        public static List<Warning> ForRestore(DateTime startDate, DateTime? archiveEndDate, DateTime today)
        {
            var result = new List<Warning>();
            if (archiveEndDate != null && startDate.Date < archiveEndDate.Value.Date)
                result.Add(Warning.StartBeforeArchiveEnd);
            if (IsFar(startDate, today))
                result.Add(Warning.FarFromToday);
            return result;
        }

        private static bool IsFar(DateTime date, DateTime today)
            => date.Date > today.Date.AddYears(1) || date.Date < today.Date.AddYears(-1);
    }
}
