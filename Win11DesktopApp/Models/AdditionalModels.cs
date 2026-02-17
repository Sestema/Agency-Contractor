using System;

namespace Win11DesktopApp.EmployeeModels
{
    /// <summary>
    /// Represents an archived employee summary for display in the Archive tab.
    /// </summary>
    public class ArchivedEmployeeSummary
    {
        public string FullName { get; set; } = string.Empty;
        public string PositionTitle { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }
    }

    /// <summary>
    /// Represents a document expiry problem for the Problems dashboard.
    /// </summary>
    public class DocumentExpiryInfo
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string ExpiryDateStr { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int DaysRemaining { get; set; }
        public string Severity { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a single change history entry for an employee.
    /// </summary>
    public class EmployeeHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Action { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
