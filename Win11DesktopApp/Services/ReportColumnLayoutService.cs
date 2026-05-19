using System;
using System.Collections.Generic;
using System.Linq;

namespace Win11DesktopApp.Services
{
    public class ReportColumnLayoutService
    {
        private readonly AppSettingsService _appSettingsService;

        private static readonly List<AppSettingsService.ReportColumnSetting> DefaultEmployeeColumns = new()
        {
            new() { Key = "name", IsVisible = true, DisplayIndex = 0, Width = 200 },
            new() { Key = "type", IsVisible = true, DisplayIndex = 1, Width = 180 },
            new() { Key = "documentType", IsVisible = false, DisplayIndex = 2, Width = 130 },
            new() { Key = "passportNumber", IsVisible = false, DisplayIndex = 3, Width = 170 },
            new() { Key = "visaNumber", IsVisible = false, DisplayIndex = 4, Width = 170 },
            new() { Key = "visaAuthority", IsVisible = false, DisplayIndex = 5, Width = 220 },
            new() { Key = "workAddress", IsVisible = false, DisplayIndex = 6, Width = 220 },
            new() { Key = "highestEducation", IsVisible = false, DisplayIndex = 7, Width = 220 },
            new() { Key = "birthDate", IsVisible = false, DisplayIndex = 8, Width = 100 },
            new() { Key = "rodneCislo", IsVisible = false, DisplayIndex = 9, Width = 150 },
            new() { Key = "gender", IsVisible = false, DisplayIndex = 10, Width = 90 },
            new() { Key = "addressCz", IsVisible = false, DisplayIndex = 11, Width = 220 },
            new() { Key = "addressAbroad", IsVisible = false, DisplayIndex = 12, Width = 220 },
            new() { Key = "passportIssuedBy", IsVisible = false, DisplayIndex = 13, Width = 180 },
            new() { Key = "positionCode", IsVisible = false, DisplayIndex = 14, Width = 110 },
            new() { Key = "agency", IsVisible = false, DisplayIndex = 15, Width = 150 },
            new() { Key = "passportExpiry", IsVisible = true, DisplayIndex = 16, Width = 100 },
            new() { Key = "visaExpiry", IsVisible = true, DisplayIndex = 17, Width = 100 },
            new() { Key = "insuranceExpiry", IsVisible = true, DisplayIndex = 18, Width = 100 },
            new() { Key = "startDate", IsVisible = true, DisplayIndex = 19, Width = 90 },
            new() { Key = "endDate", IsVisible = true, DisplayIndex = 20, Width = 90 },
            new() { Key = "phone", IsVisible = true, DisplayIndex = 21, Width = 110 },
            new() { Key = "bankAccount", IsVisible = false, DisplayIndex = 22, Width = 150 },
            new() { Key = "bankName", IsVisible = false, DisplayIndex = 23, Width = 150 },
            new() { Key = "position", IsVisible = true, DisplayIndex = 24, Width = 110 },
            new() { Key = "visaStartDate", IsVisible = false, DisplayIndex = 25, Width = 110 },
            new() { Key = "citizenship", IsVisible = false, DisplayIndex = 26, Width = 140 },
            new() { Key = "birthCity", IsVisible = false, DisplayIndex = 27, Width = 150 },
            new() { Key = "birthCountry", IsVisible = false, DisplayIndex = 28, Width = 150 },
        };

        public ReportColumnLayoutService(AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService;
        }

        public List<AppSettingsService.ReportColumnSetting> GetEffectiveEmployeeColumns()
        {
            var saved = _appSettingsService.Settings.EmployeeReportColumns;
            return MergeEmployeeColumnsWithDefaults(saved);
        }

        public void SaveEmployeeColumnLayout(IEnumerable<AppSettingsService.ReportColumnSetting> layout)
        {
            _appSettingsService.Settings.EmployeeReportColumns = NormalizeEmployeeColumnLayout(layout);
            _appSettingsService.SaveSettings();
        }

        public List<AppSettingsService.ReportColumnSetting> ResetEmployeeColumnsToDefaults()
        {
            var reset = NormalizeEmployeeColumnLayout(DefaultEmployeeColumns.Select(CopyColumnSetting));
            SaveEmployeeColumnLayout(reset);
            return reset;
        }

        public string GetEmployeeColumnHeaderResourceKey(string key) => key switch
        {
            "name" => "ReportColName",
            "type" => "ReportColType",
            "documentType" => "ReportColDocumentType",
            "passportNumber" => "ReportColPassportNumber",
            "visaNumber" => "ReportColVisaNumber",
            "visaAuthority" => "ReportColVisaAuthority",
            "workAddress" => "ReportColWorkAddress",
            "highestEducation" => "ReportColHighestEducation",
            "addressCz" => "ReportColAddressCz",
            "addressAbroad" => "ReportColAddressAbroad",
            "birthDate" => "ReportColBirthDate",
            "rodneCislo" => "ReportColRodneCislo",
            "gender" => "ReportColGender",
            "passportIssuedBy" => "ReportColPassportIssuedBy",
            "positionCode" => "ReportColPositionCode",
            "agency" => "ReportColAgency",
            "passportExpiry" => "ReportColPassportExpFull",
            "visaExpiry" => "ReportColVisaExpFull",
            "insuranceExpiry" => "ReportColInsExpFull",
            "startDate" => "ReportColStartDateFull",
            "endDate" => "ReportColEndDateFull",
            "phone" => "ReportColPhone",
            "bankAccount" => "EmployeeBankAccountNumber",
            "bankName" => "EmployeeBankName",
            "position" => "ReportColPosition",
            "visaStartDate" => "ReportColVisaStartDate",
            "citizenship" => "ReportColCitizenship",
            "birthCity" => "ReportColBirthCity",
            "birthCountry" => "ReportColBirthCountry",
            _ => key
        };

        private static AppSettingsService.ReportColumnSetting CopyColumnSetting(AppSettingsService.ReportColumnSetting source)
        {
            return new AppSettingsService.ReportColumnSetting
            {
                Key = source.Key,
                IsVisible = source.IsVisible,
                DisplayIndex = source.DisplayIndex,
                Width = source.Width
            };
        }

        private static List<AppSettingsService.ReportColumnSetting> NormalizeEmployeeColumnLayout(
            IEnumerable<AppSettingsService.ReportColumnSetting> layout)
        {
            var normalized = layout
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .Select(CopyColumnSetting)
                .ToList();

            foreach (var col in normalized)
            {
                col.Width = Math.Max(40, col.Width);
                col.DisplayIndex = Math.Max(0, col.DisplayIndex);
                if (string.Equals(col.Key, "name", StringComparison.OrdinalIgnoreCase))
                    col.IsVisible = true;
            }

            var ordered = normalized
                .OrderBy(c => c.DisplayIndex)
                .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
                ordered[i].DisplayIndex = i;

            return ordered;
        }

        private static List<AppSettingsService.ReportColumnSetting> MergeEmployeeColumnsWithDefaults(
            List<AppSettingsService.ReportColumnSetting>? saved)
        {
            var result = new List<AppSettingsService.ReportColumnSetting>();
            var savedByKey = (saved ?? new List<AppSettingsService.ReportColumnSetting>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var def in DefaultEmployeeColumns)
            {
                if (savedByKey.TryGetValue(def.Key, out var existing))
                {
                    result.Add(new AppSettingsService.ReportColumnSetting
                    {
                        Key = def.Key,
                        IsVisible = string.Equals(def.Key, "name", StringComparison.OrdinalIgnoreCase) || existing.IsVisible,
                        DisplayIndex = existing.DisplayIndex,
                        Width = existing.Width
                    });
                }
                else
                {
                    var copy = CopyColumnSetting(def);
                    copy.DisplayIndex = result.Count + 100;
                    result.Add(copy);
                }
            }

            return NormalizeEmployeeColumnLayout(result);
        }
    }
}
