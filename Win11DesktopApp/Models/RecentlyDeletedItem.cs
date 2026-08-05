using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Models
{
    public class RecentlyDeletedItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string UniqueId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string PositionTitle { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string OriginalEmployeeFolder { get; set; } = string.Empty;
        public string DeletedEmployeeFolder { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }
        public string DeletedBy { get; set; } = string.Empty;
        public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime PurgeAfterUtc { get; set; } = DateTime.UtcNow.AddDays(30);

        public int DaysRemaining => Math.Max(0, (int)Math.Ceiling((PurgeAfterUtc - DateTime.UtcNow).TotalDays));
        public DateTime DeletedAtLocal => DeletedAtUtc.ToLocalTime();
        public DateTime PurgeAfterLocal => PurgeAfterUtc.ToLocalTime();
    }

    public sealed class RecentlyDeletedOperationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public RecentlyDeletedItem? Item { get; init; }
    }

    /// <summary>
    /// Fast matcher for hiding recently-deleted employees in Finance while keeping DB rows intact.
    /// Match by UniqueId first; folder paths are a fallback for older rows without an id.
    /// </summary>
    public sealed class RecentlyDeletedFinanceHideIndex
    {
        private readonly HashSet<string> _uniqueIds;
        private readonly HashSet<string> _folders;

        private RecentlyDeletedFinanceHideIndex(HashSet<string> uniqueIds, HashSet<string> folders)
        {
            _uniqueIds = uniqueIds;
            _folders = folders;
        }

        public static RecentlyDeletedFinanceHideIndex Empty { get; } = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public bool IsEmpty => _uniqueIds.Count == 0 && _folders.Count == 0;

        public static RecentlyDeletedFinanceHideIndex FromItems(IEnumerable<RecentlyDeletedItem> items)
        {
            var uniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items ?? Array.Empty<RecentlyDeletedItem>())
            {
                if (!string.IsNullOrWhiteSpace(item.UniqueId))
                    uniqueIds.Add(item.UniqueId.Trim());

                AddFolder(folders, item.OriginalEmployeeFolder);
                AddFolder(folders, item.DeletedEmployeeFolder);
            }

            return uniqueIds.Count == 0 && folders.Count == 0
                ? Empty
                : new RecentlyDeletedFinanceHideIndex(uniqueIds, folders);
        }

        public bool Matches(string? employeeId, string? employeeFolder)
        {
            if (!string.IsNullOrWhiteSpace(employeeId) && _uniqueIds.Contains(employeeId.Trim()))
                return true;

            var normalized = NormalizeFolder(employeeFolder);
            return !string.IsNullOrWhiteSpace(normalized) && _folders.Contains(normalized);
        }

        private static void AddFolder(HashSet<string> folders, string? folder)
        {
            var normalized = NormalizeFolder(folder);
            if (!string.IsNullOrWhiteSpace(normalized))
                folders.Add(normalized);
        }

        private static string NormalizeFolder(string? path)
            => (path ?? string.Empty).Replace('/', '\\').Trim().TrimEnd('\\');
    }
}
