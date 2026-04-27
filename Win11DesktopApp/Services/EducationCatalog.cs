using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Win11DesktopApp.Services
{
    public sealed class EducationOption
    {
        public string Code { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string FullDisplay => $"{Code} - {DisplayName}";
    }

    public static class EducationCatalog
    {
        public const string DefaultCode = "C";

        public static readonly ReadOnlyCollection<EducationOption> All = new(new List<EducationOption>
        {
            new() { Code = "A", DisplayName = "Bez vzdělání" },
            new() { Code = "B", DisplayName = "Neúplné základní vzdělání" },
            new() { Code = "C", DisplayName = "Základní vzdělání" },
            new() { Code = "D", DisplayName = "Nižší střední vzdělání" },
            new() { Code = "E", DisplayName = "Nižší střední odborné vzdělání" },
            new() { Code = "H", DisplayName = "Střední odborné vzdělání s výučním listem" },
            new() { Code = "J", DisplayName = "Střední nebo střední odborné vzdělání bez maturity i výučního listu" },
            new() { Code = "K", DisplayName = "Úplné střední všeobecné vzdělání" },
            new() { Code = "L", DisplayName = "Úplné střední odborné vzdělání s výučním i maturitou" },
            new() { Code = "M", DisplayName = "Úplné střední odborné vzdělání s maturitou (bez vyučení)" },
            new() { Code = "N", DisplayName = "Vyšší odborné vzdělání" },
            new() { Code = "P", DisplayName = "Vyšší odborné vzdělání v konzervatoři" },
            new() { Code = "R", DisplayName = "Vysokoškolské bakalářské vzdělání" },
            new() { Code = "T", DisplayName = "Vysokoškolské magisterské vzdělání" },
            new() { Code = "V", DisplayName = "Vysokoškolské doktorské vzdělání" }
        });

        public static EducationOption? FindByCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            foreach (var option in All)
            {
                if (string.Equals(option.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
                    return option;
            }

            return null;
        }

        public static string NormalizeCode(string? code)
        {
            var option = FindByCode(code);
            return option?.Code ?? DefaultCode;
        }

        public static string GetFullDisplay(string? code)
        {
            var option = FindByCode(code) ?? FindByCode(DefaultCode);
            return option?.FullDisplay ?? $"{DefaultCode} - Základní vzdělání";
        }
    }
}
