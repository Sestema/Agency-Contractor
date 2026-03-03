using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

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
        public string DocumentTypeDisplay { get; set; } = string.Empty;
        public string ExpiryDateStr { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int DaysRemaining { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string IgnoredUntil { get; set; } = string.Empty;
    }

    /// <summary>
    /// Groups all document problems for a single employee.
    /// </summary>
    public class EmployeeProblemGroup : System.ComponentModel.INotifyPropertyChanged
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public System.Collections.ObjectModel.ObservableCollection<DocumentExpiryInfo> Issues { get; set; } = new();

        public string WorstSeverity
        {
            get
            {
                if (Issues.Any(i => i.Severity == "Expired" || i.Severity == "Critical")) return "Expired";
                if (Issues.Any(i => i.Severity == "Warning")) return "Warning";
                return "Ok";
            }
        }

        public int IssueCount => Issues.Count;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public void Refresh()
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Issues)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IssueCount)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(WorstSeverity)));
        }
    }

    /// <summary>
    /// Represents a single change history entry for an employee.
    /// </summary>
    public class EmployeeHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string EventType { get; set; } = "ProfileChanged";
        public string Action { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string EventIcon => EventType switch
        {
            "Created" => "\uE8FA",
            "DocumentUpdated" => "\uE8A5",
            "DocumentExtended" => "\uE8AB",
            "StatusChanged" => "\uE8B5",
            "Archived" => "\uE7B8",
            "Restored" => "\uE72C",
            _ => "\uE70F"
        };

        public string EventColor => EventType switch
        {
            "Created" => "#4CAF50",
            "DocumentUpdated" => "#FF9800",
            "DocumentExtended" => "#FF9800",
            "StatusChanged" => "#2196F3",
            "Archived" => "#9E9E9E",
            "Restored" => "#8BC34A",
            _ => "#2196F3"
        };
    }

    /// <summary>
    /// Filter item for company selection in the Report view.
    /// </summary>
    public class CompanyFilter : System.ComponentModel.INotifyPropertyChanged
    {
        public string CompanyName { get; set; } = string.Empty;

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// One row of the per-firm report table.
    /// </summary>
    public class FirmReportRow
    {
        public string FirmName { get; set; } = string.Empty;
        public int TotalEmployees { get; set; }
        public int ActiveEmployees { get; set; }
        public int ArchivedEmployees { get; set; }
        public int PassportOnlyCount { get; set; }
    }

    /// <summary>
    /// One row of the per-agency report table.
    /// </summary>
    public class AgencyReportRow
    {
        public string AgencyName { get; set; } = string.Empty;
        public int FirmCount { get; set; }
        public int TotalEmployees { get; set; }
        public int ActiveEmployees { get; set; }
    }

    /// <summary>
    /// One data point for the monthly archive dynamics chart.
    /// </summary>
    public class MonthlyArchiveStat
    {
        public string MonthLabel { get; set; } = string.Empty;
        public int Archived { get; set; }
        public int Restored { get; set; }
    }

    /// <summary>
    /// Persistent log entry for every archive/restore action.
    /// Stored in archive_log.json — never deleted, even after restore.
    /// </summary>
    public class ArchiveLogEntry
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }

    public class EmployeeReportRow
    {
        public string FullName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string EmployeeType { get; set; } = string.Empty;
        public string PassportExpiry { get; set; } = string.Empty;
        public string VisaExpiry { get; set; } = string.Empty;
        public string InsuranceExpiry { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public bool IsArchived { get; set; }

        public string PassportExpiryStatus => ComputeExpiryStatus(PassportExpiry);
        public string VisaExpiryStatus => ComputeExpiryStatus(VisaExpiry);
        public string InsuranceExpiryStatus => ComputeExpiryStatus(InsuranceExpiry);

        private static string ComputeExpiryStatus(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return "none";
            string[] formats = { "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "dd.MM.yy", "d.M.yy" };
            if (DateTime.TryParseExact(dateStr.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                var days = (date - DateTime.Today).TotalDays;
                if (days < 0) return "expired";
                if (days < 30) return "warning";
                return "ok";
            }
            return "none";
        }
    }

    public class FirmEmployeeGroup
    {
        public string FirmName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
        public ObservableCollection<EmployeeReportRow> Employees { get; set; } = new();
    }

    public class ActivityLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Timestamp { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public string ActionType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class ExportSheetOption : System.ComponentModel.INotifyPropertyChanged
    {
        public string SheetKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
