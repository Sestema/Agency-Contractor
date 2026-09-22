using System;
using System.Collections.Generic;
using System.Linq;

namespace Win11DesktopApp.Models
{
    public static class SalaryExportColumnKeys
    {
        public const string Firm = "firm";
        public const string Name = "name";
        public const string Hours = "hours";
        public const string Rate = "rate";
        public const string Gross = "gross";
        public const string Advance = "advance";
        public const string Net = "net";
        public const string Note = "note";
        public const string Paid = "paid";

        public static string Custom(string fieldId) => "custom:" + (fieldId ?? string.Empty);
    }

    public sealed class SalaryExportColumnSelection
    {
        public static SalaryExportColumnSelection All { get; } = new(null);

        private readonly HashSet<string> _hidden;

        public SalaryExportColumnSelection(IEnumerable<string>? hiddenColumns)
        {
            _hidden = new HashSet<string>(
                hiddenColumns?.Where(key => !string.IsNullOrWhiteSpace(key)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            _hidden.Remove(SalaryExportColumnKeys.Name);
            _hidden.Remove(SalaryExportColumnKeys.Firm);
        }

        public bool IsVisible(string key)
            => string.Equals(key, SalaryExportColumnKeys.Name, StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, SalaryExportColumnKeys.Firm, StringComparison.OrdinalIgnoreCase)
               || !_hidden.Contains(key);

        public int CountVisible(IReadOnlyCollection<CustomSalaryField>? visibleCustomFields)
        {
            var count = CountVisibleForPdf(visibleCustomFields);
            if (IsVisible(SalaryExportColumnKeys.Firm)) count++;
            return count;
        }

        public int CountVisibleForPdf(IReadOnlyCollection<CustomSalaryField>? visibleCustomFields)
        {
            var count = 1;
            if (IsVisible(SalaryExportColumnKeys.Hours)) count++;
            if (IsVisible(SalaryExportColumnKeys.Rate)) count++;
            if (IsVisible(SalaryExportColumnKeys.Gross)) count++;
            if (IsVisible(SalaryExportColumnKeys.Advance)) count++;
            count += visibleCustomFields?.Count ?? 0;
            if (IsVisible(SalaryExportColumnKeys.Net)) count++;
            if (IsVisible(SalaryExportColumnKeys.Note)) count++;
            if (IsVisible(SalaryExportColumnKeys.Paid)) count++;
            return count;
        }
    }

    public static class SalaryExportFontScale
    {
        public const int MinStep = 1;
        public const int MaxStep = 5;
        public const int DefaultStep = 1;

        public static int Clamp(int step)
            => step < MinStep ? MinStep : (step > MaxStep ? MaxStep : step);

        public static float PdfTable(int step) => 7f + Clamp(step);

        public static float PdfFirm(int step) => 7f + Clamp(step);

        public static double ExcelTable(int step) => 12 + Clamp(step);

        public static double ExcelFirm(int step) => 14 + Clamp(step);

        public static double ExcelSmall(int step) => 11 + Clamp(step);
    }
}
