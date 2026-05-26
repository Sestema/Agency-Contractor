using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    internal sealed class SalaryHistoryDuplicateCleanupRow
    {
        public string Id { get; init; } = string.Empty;
        public string EmployeeId { get; init; } = string.Empty;
        public string EmployeeFolder { get; init; } = string.Empty;
        public int Year { get; init; }
        public int Month { get; init; }
        public string FirmName { get; init; } = string.Empty;
        public DateTime PaidAt { get; init; }
    }

    internal static class SalaryHistoryDuplicateCleanup
    {
        public static string NormalizeSalaryHistoryFirmKey(string? firmName)
        {
            if (string.IsNullOrWhiteSpace(firmName))
                return string.Empty;

            return string.Join(
                " ",
                firmName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Trim()
                .ToUpperInvariant();
        }

        public static string BuildSalaryHistoryDedupeKey(int year, int month, string? firmName)
        {
            return string.Join(
                "|",
                year.ToString("D4", CultureInfo.InvariantCulture),
                month.ToString("D2", CultureInfo.InvariantCulture),
                NormalizeSalaryHistoryFirmKey(firmName));
        }

        public static string BuildSalaryHistoryDedupeKey(SalaryHistoryRecord record)
            => BuildSalaryHistoryDedupeKey(record.Year, record.Month, record.FirmName);

        public static string BuildEmployeeScopeKey(string? employeeId, string? employeeFolder)
        {
            if (!string.IsNullOrWhiteSpace(employeeId))
                return "id:" + employeeId.Trim().ToUpperInvariant();

            return "folder:" + (employeeFolder ?? string.Empty).Trim().ToUpperInvariant();
        }

        public static List<SalaryHistoryRecord> DeduplicateRecords(IEnumerable<SalaryHistoryRecord> records)
        {
            return records
                .Where(record => record != null)
                .GroupBy(record => BuildSalaryHistoryDedupeKey(record), StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(record => record.PaidAt)
                    .ThenByDescending(record => record.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderByDescending(record => record.Year)
                .ThenByDescending(record => record.Month)
                .ThenByDescending(record => record.PaidAt)
                .ToList();
        }

        public static IReadOnlyList<string> GetDuplicateRecordIdsToRemove(IEnumerable<SalaryHistoryDuplicateCleanupRow> rows)
        {
            var idsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var employeeGroup in rows.GroupBy(
                         row => BuildEmployeeScopeKey(row.EmployeeId, row.EmployeeFolder),
                         StringComparer.OrdinalIgnoreCase))
            {
                foreach (var dedupeGroup in employeeGroup.GroupBy(
                             row => BuildSalaryHistoryDedupeKey(row.Year, row.Month, row.FirmName),
                             StringComparer.OrdinalIgnoreCase))
                {
                    if (dedupeGroup.Count() <= 1)
                        continue;

                    var winner = dedupeGroup
                        .OrderByDescending(row => row.PaidAt)
                        .ThenByDescending(row => row.Id, StringComparer.OrdinalIgnoreCase)
                        .First();

                    foreach (var duplicate in dedupeGroup)
                    {
                        if (string.IsNullOrWhiteSpace(duplicate.Id))
                            continue;

                        if (!string.Equals(duplicate.Id, winner.Id, StringComparison.OrdinalIgnoreCase))
                            idsToRemove.Add(duplicate.Id);
                    }
                }
            }

            return idsToRemove.ToList();
        }
    }
}
