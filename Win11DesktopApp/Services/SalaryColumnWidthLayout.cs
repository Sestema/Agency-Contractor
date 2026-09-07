using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Services
{
    public static class SalaryColumnWidthLayout
    {
        public const double MinPersistedWidth = 40;

        public static readonly string[] LegacyFixedKeys =
        {
            "DisplayName", "HoursWorked", "HourlyRate", "GrossSalary", "Advance"
        };

        public static readonly string[] LegacyTrailingKeys =
        {
            "NetSalary", "IsPaid", "Note"
        };

        public enum WidthUnit
        {
            Other,
            Pixel,
            Star
        }

        public static bool CanPersistWidth(double width)
            => !double.IsNaN(width)
               && !double.IsInfinity(width)
               && width >= MinPersistedWidth;

        /// <summary>
        /// Star columns keep UnitType=Star while the user resizes them; persist the
        /// displayed pixel width, not the star weight (usually 1).
        /// </summary>
        public static bool TryMeasurePersistedWidth(
            WidthUnit unit,
            double specifiedWidth,
            double actualWidth,
            double displayWidth,
            out double width)
        {
            if (unit == WidthUnit.Pixel && CanPersistWidth(specifiedWidth))
            {
                width = specifiedWidth;
                return true;
            }

            if (unit == WidthUnit.Star)
            {
                var measured = displayWidth >= MinPersistedWidth ? displayWidth : actualWidth;
                if (CanPersistWidth(measured))
                {
                    width = measured;
                    return true;
                }
            }

            width = 0;
            return false;
        }

        public static Dictionary<string, double> CreateStore()
            => new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, double> Sanitize(IReadOnlyDictionary<string, double>? source)
        {
            var result = CreateStore();
            if (source == null || source.Count == 0)
                return result;

            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !CanPersistWidth(pair.Value))
                    continue;

                result[pair.Key.Trim()] = pair.Value;
            }

            return result;
        }

        public static Dictionary<string, double> Merge(
            IReadOnlyDictionary<string, double>? existing,
            IEnumerable<(string Key, double Width)> measured)
        {
            var result = Sanitize(existing);
            if (measured == null)
                return result;

            foreach (var (key, width) in measured)
            {
                if (string.IsNullOrWhiteSpace(key) || !CanPersistWidth(width))
                    continue;

                result[key.Trim()] = width;
            }

            return result;
        }

        /// <summary>
        /// Persist only columns the user actually resized. Grid XAML defaults must not
        /// overwrite a stored layout when restore has not applied yet.
        /// </summary>
        public static Dictionary<string, double> MergeUserChanges(
            IReadOnlyDictionary<string, double>? existing,
            IEnumerable<(string Key, double Width)> measured,
            IReadOnlyCollection<string>? changedKeys)
        {
            if (changedKeys == null || changedKeys.Count == 0)
                return Sanitize(existing);

            var allowed = new HashSet<string>(changedKeys, StringComparer.OrdinalIgnoreCase);
            var result = Sanitize(existing);
            if (measured == null)
                return result;

            foreach (var (key, width) in measured)
            {
                if (string.IsNullOrWhiteSpace(key) || !CanPersistWidth(width))
                    continue;

                var trimmed = key.Trim();
                if (!allowed.Contains(trimmed))
                    continue;

                result[trimmed] = width;
            }

            return result;
        }

        public static Dictionary<string, double> FromLegacyList(IReadOnlyList<double>? widths)
        {
            var result = CreateStore();
            if (widths == null || widths.Count == 0)
                return result;

            for (var i = 0; i < LegacyFixedKeys.Length && i < widths.Count; i++)
            {
                if (CanPersistWidth(widths[i]))
                    result[LegacyFixedKeys[i]] = widths[i];
            }

            if (widths.Count >= LegacyFixedKeys.Length + LegacyTrailingKeys.Length)
            {
                var start = widths.Count - LegacyTrailingKeys.Length;
                for (var i = 0; i < LegacyTrailingKeys.Length; i++)
                {
                    if (CanPersistWidth(widths[start + i]))
                        result[LegacyTrailingKeys[i]] = widths[start + i];
                }
            }

            return result;
        }

        public static Dictionary<string, double> ResolveStore(
            IReadOnlyDictionary<string, double>? keyed,
            IReadOnlyList<double>? legacy)
        {
            var fromKeyed = Sanitize(keyed);
            return fromKeyed.Count > 0 ? fromKeyed : FromLegacyList(legacy);
        }
    }
}
