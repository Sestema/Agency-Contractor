using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    internal sealed record DocumentPackageHistoryGroup(
        string FirmName,
        string PeriodStart,
        string PeriodEnd,
        bool IsClosedPeriod,
        IReadOnlyList<RequiredDocumentStatus> Statuses);

    /// <summary>
    /// Splits an employee's document package into work periods. A period closes when the employee
    /// comes back from the archive, so the next period starts with an empty package.
    /// </summary>
    internal static class DocumentPackagePeriods
    {
        /// <summary>Closes every open status. Must run before ArchivedFromFirm is cleared.</summary>
        public static void CloseOpenPeriods(EmployeeData data)
        {
            if (data.RequiredDocumentStatuses == null)
                return;

            foreach (var status in data.RequiredDocumentStatuses.Where(status => !status.IsPeriodClosed))
            {
                if (string.IsNullOrWhiteSpace(status.FirmName))
                    status.FirmName = data.ArchivedFromFirm ?? string.Empty;

                var period = FindLastPeriod(data.FirmHistory, status.FirmName);
                status.PeriodStart = period?.StartDate ?? string.Empty;
                status.PeriodEnd = period?.EndDate ?? string.Empty;
                status.IsPeriodClosed = true;
            }
        }

        /// <summary>Read-only history, newest first: closed periods plus open statuses of other firms.</summary>
        public static IReadOnlyList<DocumentPackageHistoryGroup> BuildHistory(
            IEnumerable<RequiredDocumentStatus> statuses,
            string? currentFirm,
            IReadOnlyList<FirmHistoryEntry>? firmHistory)
        {
            return statuses
                .Where(status => !string.IsNullOrWhiteSpace(status.FirmName)
                    && !string.IsNullOrWhiteSpace(status.NameSnapshot)
                    && (status.IsPeriodClosed || !string.Equals(status.FirmName, currentFirm, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(status => (
                    Firm: status.FirmName.Trim().ToLowerInvariant(),
                    status.IsPeriodClosed,
                    status.PeriodStart,
                    status.PeriodEnd))
                .Where(group => !group.Key.IsPeriodClosed || group.Any(HasContent))
                .Select(group =>
                {
                    var firmName = group.First().FirmName.Trim();
                    var period = group.Key.IsPeriodClosed ? null : FindLastPeriod(firmHistory, firmName);
                    return new DocumentPackageHistoryGroup(
                        firmName,
                        period?.StartDate ?? group.Key.PeriodStart ?? string.Empty,
                        period?.EndDate ?? group.Key.PeriodEnd ?? string.Empty,
                        group.Key.IsPeriodClosed,
                        group.ToList());
                })
                .OrderByDescending(group => DateParsingHelper.TryParseDate(group.PeriodEnd)
                    ?? DateParsingHelper.TryParseDate(group.PeriodStart)
                    ?? DateTime.MinValue)
                .ToList();
        }

        private static bool HasContent(RequiredDocumentStatus status) =>
            status.IsPresent || status.IsScanned || !string.IsNullOrWhiteSpace(status.Note);

        private static FirmHistoryEntry? FindLastPeriod(IReadOnlyList<FirmHistoryEntry>? firmHistory, string? firmName) =>
            firmHistory?.LastOrDefault(entry =>
                string.Equals(entry.FirmName?.Trim(), firmName?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
