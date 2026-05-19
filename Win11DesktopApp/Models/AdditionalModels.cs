using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.EmployeeModels
{
    /// <summary>
    /// Represents an archived employee summary for display in the Archive tab.
    /// </summary>
    public class ArchivedEmployeeSummary
    {
        public string UniqueId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string PositionTitle { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }
        public DateTime? ParsedStartDate { get; set; }
        public DateTime? ParsedEndDate { get; set; }
    }

    /// <summary>
    /// Represents a document expiry problem for the Problems dashboard.
    /// </summary>
    public class DocumentExpiryInfo
    {
        public string UniqueId { get; set; } = string.Empty;
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
        public string UniqueId { get; set; } = string.Empty;
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
        public long Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string EventType { get; set; } = "ProfileChanged";
        public string Action { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;

        public string EventIcon => EventType switch
        {
            "Created" => "\uE8FA",
            "DocumentUpdated" => "\uE8A5",
            "DocumentExtended" => "\uE8AB",
            "StatusChanged" => "\uE8B5",
            "Archived" => "\uE7B8",
            "ArchiveUndone" => "\uE777",
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
            "ArchiveUndone" => "#1976D2",
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
        public string OperationId { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public bool IsReverted { get; set; }
        public string? RevertedAt { get; set; }
        public string? RevertedByOperationId { get; set; }
    }

    public sealed class EmployeeIndexRow
    {
        public string UniqueId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string EmployeeType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public string PositionTitle { get; set; } = string.Empty;
        public string PositionNumber { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PassportNumber { get; set; } = string.Empty;
        public string VisaNumber { get; set; } = string.Empty;
        public string InsuranceNumber { get; set; } = string.Empty;
        public string PassportExpiry { get; set; } = string.Empty;
        public string VisaExpiry { get; set; } = string.Empty;
        public string InsuranceExpiry { get; set; } = string.Empty;
        public string WorkPermitName { get; set; } = string.Empty;
        public string WorkPermitExpiry { get; set; } = string.Empty;
        public string BankAccountNumber { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public bool IsArchived { get; set; }
        public string ArchivedFromFirm { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }
        public bool HasPassport { get; set; }
        public bool HasVisa { get; set; }
        public bool HasInsurance { get; set; }
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public class EmployeeReportRow
    {
        public string FullName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string EmployeeType { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string PassportNumber { get; set; } = string.Empty;
        public string VisaNumber { get; set; } = string.Empty;
        public string VisaAuthority { get; set; } = string.Empty;
        public string VisaStartDate { get; set; } = string.Empty;
        public string PassportExpiry { get; set; } = string.Empty;
        public string VisaExpiry { get; set; } = string.Empty;
        public string InsuranceExpiry { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string BankAccountNumber { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string PositionCode { get; set; } = string.Empty;
        public string WorkAddress { get; set; } = string.Empty;
        public string AddressCz { get; set; } = string.Empty;
        public string AddressAbroad { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
        public string RodneCislo { get; set; } = string.Empty;
        public string Citizenship { get; set; } = string.Empty;
        public string BirthCity { get; set; } = string.Empty;
        public string BirthCountry { get; set; } = string.Empty;
        public string HighestEducation { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string PassportIssuedBy { get; set; } = string.Empty;
        public string Agency { get; set; } = string.Empty;
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
        public string RelatedOperationId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public string TimeDisplay => DateTime.TryParse(Timestamp, out var dt) ? dt.ToString("HH:mm") : Timestamp;
        public string DisplayDescription => ActivityLogTextLocalizer.LocalizeDescription(this);
        public string DisplayDetails => ActivityLogTextLocalizer.LocalizeDetails(Details);
    }

    internal static class ActivityLogTextLocalizer
    {
        private static readonly Dictionary<string, string> UkrainianFieldResourceKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ім'я"] = "HistFieldFirstName",
            ["Прізвище"] = "HistFieldLastName",
            ["Дата народження"] = "HistFieldBirthDate",
            ["Найвищий досягнутий рівень освіти"] = "HistFieldHighestEducation",
            ["Номер паспорту"] = "HistFieldPassportNum",
            ["Тип працівника / комплект документів"] = "HistFieldDocumentProfileType",
            ["Орган, що видав документ"] = "HistFieldPassportAuthority",
            ["Термін паспорту"] = "HistFieldPassportExp",
            ["Номер візи"] = "HistFieldVisaNum",
            ["Орган, що видав візу"] = "HistFieldVisaAuthority",
            ["Тип візи"] = "HistFieldVisaType",
            ["Початок візи"] = "HistFieldVisaStartDate",
            ["Термін візи"] = "HistFieldVisaExp",
            ["Номер страховки"] = "HistFieldInsNum",
            ["Термін страховки"] = "HistFieldInsExp",
            ["Страхова компанія"] = "HistFieldInsCompany",
            ["Страхова компанія - повна назва"] = "HistFieldInsCompanyFull",
            ["Телефон"] = "HistFieldPhone",
            ["Email"] = "HistFieldEmail",
            ["Статус"] = "HistFieldStatus",
            ["Позиція"] = "HistFieldPosition",
            ["Адреса виконання роботи"] = "HistFieldWorkAddr",
            ["Дата підписання"] = "HistFieldSignDate",
            ["Тип договору"] = "HistFieldContractType",
            ["Зарплата (брутто)"] = "HistFieldSalary",
            ["Годинна зарплата"] = "HistFieldHourly",
            ["Відділ"] = "HistFieldDepartment",
            ["Дата наступу"] = "HistFieldStartDate",
            ["Місто народження"] = "HistFieldPassportCity",
            ["Країна народження"] = "HistFieldPassportCountry",
            ["Громадянство"] = "HistFieldCitizenship",
            ["Країна видачі"] = "HistFieldIssuingCountry",
            ["Назва дозволу"] = "HistFieldWorkPermitName",
            ["Номер позиції"] = "HistFieldPosNumber",
            ["зарплата"] = "HistFieldSalary",
            ["ставка"] = "HistFieldHourly"
        };

        public static string LocalizeDescription(ActivityLogEntry entry)
        {
            var description = entry.Description ?? string.Empty;
            var employeeName = string.IsNullOrWhiteSpace(entry.EmployeeName) ? ExtractEmployeeName(description) : entry.EmployeeName;

            return entry.ActionType switch
            {
                "EmployeeAdded" => string.Format(Res("ActLogMsgEmployeeAddedFmt"), employeeName, entry.FirmName),
                "ProfileChanged" => string.Format(Res("ActLogMsgProfileChangedFmt"), employeeName, LocalizeFieldName(ExtractProfileField(description))),
                "SalaryChanged" => string.Format(Res("ActLogMsgSalaryChangedFmt"), employeeName),
                "RateChanged" => string.Format(Res("ActLogMsgRateChangedFmt"), employeeName),
                "VisaExtended" => string.Format(Res("ActLogMsgFieldChangedFmt"), LocalizeFieldName(ExtractLeadingField(description))),
                "InsuranceExtended" => string.Format(Res("ActLogMsgFieldChangedFmt"), LocalizeFieldName(ExtractLeadingField(description))),
                "DocGenerated" => string.Format(Res("ActLogMsgDocGeneratedFmt"), ExtractQuoted(description), employeeName),
                "BatchDocGenerated" => string.Format(Res("ActLogMsgBatchDocGeneratedFmt"), ExtractQuoted(description), ExtractBetween(description, "успішно ", ","), ExtractAfter(description, "помилки ")),
                "TemplateAdded" => string.Format(Res("ActLogMsgTemplateAddedFmt"), ExtractQuoted(description), entry.FirmName),
                "TemplateDeleted" => string.Format(Res("ActLogMsgTemplateDeletedFmt"), ExtractQuoted(description), entry.FirmName),
                "TemplateRenamed" => string.Format(Res("ActLogMsgTemplateRenamedFmt"), entry.OldValue, entry.NewValue, entry.FirmName),
                "CompanyAdded" => string.Format(Res("ActLogMsgCompanyAddedFmt"), entry.FirmName),
                "CompanyDeleted" => string.Format(Res("ActLogMsgCompanyDeletedFmt"), entry.FirmName),
                "EmployeeArchived" => string.Format(Res("ActLogMsgEmployeeArchivedFmt"), employeeName, entry.FirmName, entry.NewValue),
                "EmployeeRestored" => string.Format(Res("ActLogMsgEmployeeRestoredFmt"), employeeName, entry.FirmName, entry.NewValue),
                "AdvanceAdded" => string.Format(Res("ActLogMsgAdvanceAddedFmt"), entry.NewValue, employeeName, entry.FirmName),
                "AdvanceDeleted" => string.Format(Res("ActLogMsgAdvanceDeletedFmt"), ExtractAmount(description), employeeName, entry.FirmName),
                "MonthPaid" => LocalizeMonthPaid(description),
                "RootFolderChanged" => Res("ActLogMsgRootFolderChanged"),
                "ExportExcel" => LocalizeExport(description, "Excel"),
                "ExportPdf" => LocalizeExport(description, "PDF"),
                "CandidateUpdated" => string.Format(Res("ActLogMsgCandidateUpdatedFmt"), employeeName),
                "CandidateDeleted" => string.Format(Res("ActLogMsgCandidateDeletedFmt"), employeeName),
                "CandidateAdded" => string.IsNullOrWhiteSpace(employeeName) ? description : string.Format(Res("ActLogMsgCandidateAddedFmt"), employeeName),
                _ => LocalizeLegacyPhrases(description, employeeName, entry.FirmName)
            };
        }

        public static string LocalizeDetails(string details)
        {
            if (string.IsNullOrWhiteSpace(details))
                return string.Empty;

            return details
                .Replace("Фірма:", Res("ActLogDetailsFirm") + ":", StringComparison.Ordinal)
                .Replace("Документ:", Res("ActLogDetailsDocument") + ":", StringComparison.Ordinal)
                .Replace("Файл:", Res("ActLogDetailsFile") + ":", StringComparison.Ordinal)
                .Replace("Місяць:", Res("ActLogDetailsMonth") + ":", StringComparison.Ordinal)
                .Replace("Працівників:", Res("ActLogDetailsEmployees") + ":", StringComparison.Ordinal)
                .Replace("Оплачено:", Res("ActLogDetailsPaid") + ":", StringComparison.Ordinal)
                .Replace("Кастомні колонки:", Res("ActLogDetailsCustomColumns") + ":", StringComparison.Ordinal);
        }

        private static string LocalizeLegacyPhrases(string description, string employeeName, string firmName)
        {
            if (description.StartsWith("Видано шаблон", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgTemplateIssuedFmt"), ExtractQuoted(description), string.IsNullOrWhiteSpace(firmName) ? ExtractAfter(description, " з ") : firmName);

            if (description.StartsWith("Оновлено кандидата:", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgCandidateUpdatedFmt"), ExtractAfter(description, ": "));

            if (description.StartsWith("Видалено кандидата:", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgCandidateDeletedFmt"), ExtractAfter(description, ": "));

            return description;
        }

        private static string LocalizeExport(string description, string format)
        {
            if (description.Contains("анкету", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgProfileExportFmt"), ExtractBetween(description, "анкету ", " →"), format);

            if (description.Contains("виплату на підписи", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgPaymentSignExportFmt"), format);

            if (description.Contains("виплату", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgSalaryExportFmt"), ExtractBetween(description, "виплату ", " →"), format);

            if (description.Contains("звіт проблем", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgProblemsExportFmt"), format);

            if (description.Contains("звіт", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgReportExportFmt"), format);

            if (description.Contains("авансову відомість", StringComparison.OrdinalIgnoreCase))
                return string.Format(Res("ActLogMsgAdvanceTableExportFmt"), format);

            return description;
        }

        private static string LocalizeMonthPaid(string description)
        {
            var value = ExtractAfter(description, "Позначено оплачено: ");
            return string.IsNullOrWhiteSpace(value)
                ? description
                : string.Format(Res("ActLogMsgMonthPaidFmt"), value);
        }

        private static string LocalizeFieldName(string field)
        {
            field = field.Trim().Trim(':');
            return UkrainianFieldResourceKeys.TryGetValue(field, out var key) ? Res(key) : field;
        }

        private static string ExtractProfileField(string description)
        {
            var arrowIndex = description.IndexOf("→", StringComparison.Ordinal);
            var left = arrowIndex >= 0 ? description[..arrowIndex] : description;
            left = left.Trim().TrimEnd(':').Trim();
            var firstColon = left.IndexOf(':');
            return firstColon >= 0 ? left[(firstColon + 1)..].Trim() : left;
        }

        private static string ExtractLeadingField(string description)
        {
            var colon = description.IndexOf(':');
            return colon >= 0 ? description[..colon].Trim() : description;
        }

        private static string ExtractEmployeeName(string description)
        {
            var colon = description.IndexOf(':');
            return colon > 0 ? description[..colon].Trim() : string.Empty;
        }

        private static string ExtractQuoted(string text)
        {
            var start = text.IndexOf('«');
            var end = text.IndexOf('»', start + 1);
            return start >= 0 && end > start ? text.Substring(start + 1, end - start - 1) : string.Empty;
        }

        private static string ExtractAmount(string text)
        {
            var prefix = "аванс ";
            var start = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += prefix.Length;
            var end = text.IndexOf(" Kč", start, StringComparison.OrdinalIgnoreCase);
            return end > start ? text[start..end].Trim() : string.Empty;
        }

        private static string ExtractAfter(string text, string marker)
        {
            var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return start < 0 ? string.Empty : text[(start + marker.Length)..].Trim();
        }

        private static string ExtractBetween(string text, string startMarker, string endMarker)
        {
            var start = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += startMarker.Length;
            var end = text.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
            return end > start ? text[start..end].Trim() : text[start..].Trim();
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;
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

    public class AIFieldCheckItem : ViewModelBase
    {
        public string FieldKey { get; set; } = string.Empty;
        public string FieldDisplayName { get; set; } = string.Empty;
        public string SourceDocument { get; set; } = string.Empty;
        public string SourceDocumentDisplay { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string SuggestedValue { get; set; } = string.Empty;
        public string Severity { get; set; } = "ok";
        public string Message { get; set; } = string.Empty;
        public bool CanAutofill { get; set; }

        private bool _isApplied;
        public bool IsApplied
        {
            get => _isApplied;
            set => SetProperty(ref _isApplied, value);
        }
    }
}
