using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Helpers
{
    /// <summary>
    /// Shared formatting / matching for company work addresses vs employee WorkAddressTag.
    /// </summary>
    public static class WorkAddressHelper
    {
        private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);

        public static string Format(WorkAddress? address)
        {
            if (address == null)
                return string.Empty;

            var streetPart = string.Join(" ", new[] { address.Street, address.Number }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim()));

            var cityPart = string.Join(" ", new[] { address.City, address.ZipCode }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim()));

            if (!string.IsNullOrWhiteSpace(streetPart) && !string.IsNullOrWhiteSpace(cityPart))
                return $"{streetPart}, {cityPart}";

            return !string.IsNullOrWhiteSpace(streetPart) ? streetPart : cityPart;
        }

        public static string NormalizeForCompare(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim()
                .Replace('\u00A0', ' ')
                .Replace(',', ' ');
            normalized = MultiSpace.Replace(normalized, " ");
            return normalized.ToLowerInvariant();
        }

        public static WorkAddress? FindMatch(IEnumerable<WorkAddress>? addresses, string? workAddressTag)
        {
            if (addresses == null || string.IsNullOrWhiteSpace(workAddressTag))
                return null;

            var tagNormalized = NormalizeForCompare(workAddressTag);
            if (tagNormalized.Length == 0)
                return null;

            WorkAddress? best = null;
            foreach (var address in addresses)
            {
                var formatted = Format(address);
                if (string.IsNullOrWhiteSpace(formatted))
                    continue;

                if (string.Equals(NormalizeForCompare(formatted), tagNormalized, StringComparison.Ordinal))
                    return address;

                // Legacy wizard / free-text: all non-empty parts appear in the saved tag.
                if (PartsMatchTag(address, tagNormalized))
                    best ??= address;
            }

            return best;
        }

        /// <summary>
        /// Placeholder so ComboBox can show a saved tag that is no longer in the firm list.
        /// </summary>
        public static WorkAddress CreateOrphanFromTag(string workAddressTag)
        {
            var tag = (workAddressTag ?? string.Empty).Trim();
            return new WorkAddress
            {
                Street = tag,
                Number = string.Empty,
                City = string.Empty,
                ZipCode = string.Empty
            };
        }

        private static bool PartsMatchTag(WorkAddress address, string tagNormalized)
        {
            var parts = new[] { address.Street, address.Number, address.City, address.ZipCode }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => NormalizeForCompare(part!))
                .Where(part => part.Length > 0)
                .ToList();

            if (parts.Count == 0)
                return false;

            return parts.All(part => tagNormalized.Contains(part, StringComparison.Ordinal));
        }
    }
}
