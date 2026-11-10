using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    internal sealed record CustomDocumentPeriodGroup(
        string PeriodStart,
        string PeriodEnd,
        bool IsOpen,
        IReadOnlyList<CustomSignedDocument> Documents);

    /// <summary>
    /// Groups one firm's signed documents into work periods by sign date. Nothing is stored:
    /// a document belongs to the first period whose end date is not before its sign date.
    /// </summary>
    internal static class CustomDocumentPeriods
    {
        /// <summary>Returns only periods that hold documents, newest first.</summary>
        public static IReadOnlyList<CustomDocumentPeriodGroup> Split(
            IEnumerable<CustomSignedDocument> documents,
            string firmName,
            IEnumerable<FirmHistoryEntry>? firmHistory,
            bool hasOpenPeriod,
            string openPeriodStart)
        {
            var closed = (firmHistory ?? Enumerable.Empty<FirmHistoryEntry>())
                .Where(entry => string.Equals(entry.FirmName?.Trim(), firmName?.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(entry => (Entry: entry, End: DateParsingHelper.TryParseDate(entry.EndDate)))
                .Where(item => item.End != null)
                .OrderBy(item => item.End)
                .ToList();

            var openIndex = hasOpenPeriod || closed.Count == 0 ? closed.Count : -1;
            var buckets = Enumerable.Range(0, closed.Count + 1)
                .Select(_ => new List<CustomSignedDocument>())
                .ToList();

            foreach (var document in documents)
            {
                var signed = DateParsingHelper.TryParseDate(document.SignDate);
                var index = signed == null ? -1 : closed.FindIndex(item => signed <= item.End);
                if (index < 0)
                    index = openIndex >= 0 ? openIndex : closed.Count - 1;
                buckets[index].Add(document);
            }

            var groups = new List<CustomDocumentPeriodGroup>();
            for (var index = closed.Count; index >= 0; index--)
            {
                if (buckets[index].Count == 0)
                    continue;

                groups.Add(index == closed.Count
                    ? new CustomDocumentPeriodGroup(openPeriodStart ?? string.Empty, string.Empty, IsOpen: true, buckets[index])
                    : new CustomDocumentPeriodGroup(
                        closed[index].Entry.StartDate ?? string.Empty,
                        closed[index].Entry.EndDate ?? string.Empty,
                        IsOpen: false,
                        buckets[index]));
            }

            return groups;
        }
    }
}
