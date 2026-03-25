using System;

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
}
