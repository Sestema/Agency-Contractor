using System;

namespace Win11DesktopApp.Services
{
    public static class PhoneSearchHelper
    {
        public const int MinDigitQueryLength = 6;

        public static bool MatchesPhone(string? phone, string? query)
        {
            if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(query))
                return false;

            var queryDigits = DigitsOnly(query);
            if (IsMostlyDigitQuery(query, queryDigits))
            {
                if (queryDigits.Length < MinDigitQueryLength)
                    return false;

                var phoneDigits = DigitsOnly(phone);
                return phoneDigits.Length >= queryDigits.Length
                    && phoneDigits.Contains(queryDigits, StringComparison.Ordinal);
            }

            return phone.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMostlyDigitQuery(string query, string queryDigits)
        {
            if (queryDigits.Length == 0)
                return false;

            foreach (var ch in query)
            {
                if (char.IsDigit(ch)
                    || char.IsWhiteSpace(ch)
                    || ch is '+' or '-' or '(' or ')' or '.')
                    continue;

                return false;
            }

            return true;
        }

        public static string DigitsOnly(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            Span<char> buffer = value.Length <= 64
                ? stackalloc char[value.Length]
                : new char[value.Length];
            var count = 0;
            foreach (var ch in value)
            {
                if (char.IsDigit(ch))
                    buffer[count++] = ch;
            }

            return count == 0 ? string.Empty : new string(buffer[..count]);
        }
    }
}
