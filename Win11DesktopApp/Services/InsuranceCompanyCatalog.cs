using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Win11DesktopApp.Services
{
    public sealed class InsuranceCompanyOption
    {
        public string Code { get; init; } = string.Empty;
        public string ShortName { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string DisplayName => $"{Code} - {FullName}";
    }

    public static class InsuranceCompanyCatalog
    {
        public static readonly ReadOnlyCollection<InsuranceCompanyOption> All = new(new List<InsuranceCompanyOption>
        {
            new() { Code = "111", ShortName = "VZP", FullName = "Všeobecná zdravotní pojišťovna České republiky" },
            new() { Code = "201", ShortName = "VoZP", FullName = "Vojenská zdravotní pojišťovna České republiky" },
            new() { Code = "205", ShortName = "ČPZP", FullName = "Česká průmyslová zdravotní pojišťovna" },
            new() { Code = "207", ShortName = "OZP", FullName = "Oborová zdravotní pojišťovna zaměstnanců bank, pojišťoven a stavebnictví" },
            new() { Code = "209", ShortName = "ZPŠ", FullName = "Zaměstnanecká pojišťovna Škoda" },
            new() { Code = "211", ShortName = "ZPMV", FullName = "Zdravotní pojišťovna ministerstva vnitra České republiky" },
            new() { Code = "213", ShortName = "RBP", FullName = "Revírní bratrská pokladna, zdravotní pojišťovna" }
        });

        public static InsuranceCompanyOption? FindByShortName(string? shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
                return null;

            foreach (var option in All)
            {
                if (string.Equals(option.ShortName, shortName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return option;
            }

            return null;
        }
    }
}
