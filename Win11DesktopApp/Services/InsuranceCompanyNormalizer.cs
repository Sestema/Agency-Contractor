using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Win11DesktopApp.Services
{
    public static class InsuranceCompanyNormalizer
    {
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["111"] = "111",
            ["VZP"] = "111",
            ["VZPCR"] = "111",
            ["VZPCR"] = "111",
            ["VSEOBECNAZDRAVOTNIPOJISTOVNACESKEREPUBLIKY"] = "111",

            ["201"] = "201",
            ["VOZP"] = "201",
            ["VOJENSKAZDRAVOTNIPOJISTOVNACESKEREPUBLIKY"] = "201",

            ["205"] = "205",
            ["CPZP"] = "205",
            ["CESKAPRUMYSLOVAZDRAVOTNIPOJISTOVNA"] = "205",

            ["207"] = "207",
            ["OZP"] = "207",
            ["OBOROVAZDRAVOTNIPOJISTOVNAZAMESTNANCUBANKPOJISTOVENASTAVEBNICTVI"] = "207",

            ["209"] = "209",
            ["ZPS"] = "209",
            ["ZAMESTNANECKAPOJISTOVNASKODA"] = "209",

            ["211"] = "211",
            ["ZPMV"] = "211",
            ["ZDRAVOTNIPOJISTOVNAMINISTERSTVAVNITRACESKEREPUBLIKY"] = "211",
            ["MINISTERSTVAVNITRA"] = "211",

            ["213"] = "213",
            ["RBP"] = "213",
            ["REVIRNIBRATSKAPOKLADNAZDRAVOTNIPOJISTOVNA"] = "213"
        };

        public static InsuranceCompanyOption? Normalize(string? rawValue, string? code = null, string? shortName = null, string? fullName = null)
        {
            var candidates = new[] { code, shortName, fullName, rawValue };
            foreach (var candidate in candidates)
            {
                var option = NormalizeSingle(candidate);
                if (option != null)
                    return option;
            }

            return null;
        }

        public static InsuranceCompanyOption? NormalizeSingle(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = NormalizeKey(value);
            if (Aliases.TryGetValue(normalized, out var code))
                return FindByCode(code);

            foreach (var option in InsuranceCompanyCatalog.All)
            {
                var shortKey = NormalizeKey(option.ShortName);
                var fullKey = NormalizeKey(option.FullName);
                if (normalized.Contains(shortKey, StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains(fullKey, StringComparison.OrdinalIgnoreCase)
                    || shortKey.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || fullKey.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return null;
        }

        public static InsuranceCompanyOption? FindByCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            foreach (var option in InsuranceCompanyCatalog.All)
            {
                if (string.Equals(option.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
                    return option;
            }

            return null;
        }

        private static string NormalizeKey(string value)
        {
            var normalized = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return Regex.Replace(builder.ToString(), @"[^A-Z0-9]+", string.Empty);
        }
    }
}
