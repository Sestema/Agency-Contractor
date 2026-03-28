using System;
using System.Collections.Generic;
using System.Linq;

namespace AdminPanel
{
    public sealed class ClientMirrorStateRecord
    {
        public string ClientId { get; set; } = string.Empty;
        public string SchemaVersion { get; set; } = string.Empty;
        public DateTime? LastFullSyncAt { get; set; }
        public DateTime? LastDeltaSyncAt { get; set; }
        public string LastErrorText { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
    }

    public sealed class AdminMirrorAgencyRecord
    {
        public string ClientId { get; set; } = string.Empty;
        public string AgencyId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Ico { get; set; } = string.Empty;
        public string FullAddress { get; set; } = string.Empty;
        public DateTime? SourceUpdatedAt { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? AgencyId : Name;
    }

    public sealed class AdminMirrorEmployerRecord
    {
        public string ClientId { get; set; } = string.Empty;
        public Guid EmployerId { get; set; }
        public string AgencyId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Ico { get; set; } = string.Empty;
        public string LegalAddress { get; set; } = string.Empty;
        public decimal WeeklyWorkHours { get; set; }
        public decimal DailyWorkHours { get; set; }
        public int ShiftCount { get; set; }
        public int HiddenFromYear { get; set; }
        public int HiddenFromMonth { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? SourceUpdatedAt { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        public List<AdminMirrorEmployerAddressRecord> Addresses { get; set; } = new();
        public List<AdminMirrorEmployerPositionRecord> Positions { get; set; } = new();

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? EmployerId.ToString() : Name;
    }

    public sealed class AdminMirrorEmployerAddressRecord
    {
        public string ClientId { get; set; } = string.Empty;
        public Guid EmployerId { get; set; }
        public Guid AddressId { get; set; }
        public string Street { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime? LastSyncedAt { get; set; }

        public string FullAddress => string.Join(", ", new[]
        {
            string.Join(" ", new[] { Street, Number }.Where(value => !string.IsNullOrWhiteSpace(value))),
            string.Join(" ", new[] { ZipCode, City }.Where(value => !string.IsNullOrWhiteSpace(value)))
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public sealed class AdminMirrorEmployerPositionRecord
    {
        public string ClientId { get; set; } = string.Empty;
        public Guid EmployerId { get; set; }
        public Guid PositionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PositionNumber { get; set; } = string.Empty;
        public decimal MonthlySalaryBrutto { get; set; }
        public decimal HourlySalary { get; set; }
        public int SortOrder { get; set; }
        public DateTime? LastSyncedAt { get; set; }
    }

    public sealed class AdminMirrorEmployeeRecord
    {
        public string ClientId { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public Guid? EmployerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
        public string EmployeeType { get; set; } = string.Empty;
        public string EuDocumentType { get; set; } = string.Empty;
        public string VisaDocType { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string PassportNumber { get; set; } = string.Empty;
        public string PassportCity { get; set; } = string.Empty;
        public string PassportCountry { get; set; } = string.Empty;
        public string PassportExpiry { get; set; } = string.Empty;
        public string VisaNumber { get; set; } = string.Empty;
        public string VisaType { get; set; } = string.Empty;
        public string VisaExpiry { get; set; } = string.Empty;
        public string InsuranceCompanyShort { get; set; } = string.Empty;
        public string InsuranceNumber { get; set; } = string.Empty;
        public string InsuranceExpiry { get; set; } = string.Empty;
        public string WorkPermitName { get; set; } = string.Empty;
        public string WorkPermitNumber { get; set; } = string.Empty;
        public string WorkPermitType { get; set; } = string.Empty;
        public string WorkPermitIssueDate { get; set; } = string.Empty;
        public string WorkPermitExpiry { get; set; } = string.Empty;
        public string WorkPermitAuthority { get; set; } = string.Empty;
        public string AddressLocalStreet { get; set; } = string.Empty;
        public string AddressLocalNumber { get; set; } = string.Empty;
        public string AddressLocalCity { get; set; } = string.Empty;
        public string AddressLocalZip { get; set; } = string.Empty;
        public string AddressAbroadStreet { get; set; } = string.Empty;
        public string AddressAbroadNumber { get; set; } = string.Empty;
        public string AddressAbroadCity { get; set; } = string.Empty;
        public string AddressAbroadZip { get; set; } = string.Empty;
        public string WorkAddressTag { get; set; } = string.Empty;
        public string PositionTag { get; set; } = string.Empty;
        public string PositionNumber { get; set; } = string.Empty;
        public decimal MonthlySalaryBrutto { get; set; }
        public decimal HourlySalary { get; set; }
        public string ContractType { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string ContractSignDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public bool IsArchived { get; set; }
        public string ArchivedFromFirm { get; set; } = string.Empty;
        public DateTime? SourceUpdatedAt { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string EmployerDisplayName { get; set; } = string.Empty;
        public string VisaTitleDisplay { get; set; } = string.Empty;

        public List<AdminMirrorEmployeeFirmHistoryRecord> FirmHistory { get; set; } = new();

        public string LocalAddress => string.Join(", ", new[]
        {
            string.Join(" ", new[] { AddressLocalStreet, AddressLocalNumber }.Where(value => !string.IsNullOrWhiteSpace(value))),
            string.Join(" ", new[] { AddressLocalZip, AddressLocalCity }.Where(value => !string.IsNullOrWhiteSpace(value)))
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        public string AbroadAddress => string.Join(", ", new[]
        {
            string.Join(" ", new[] { AddressAbroadStreet, AddressAbroadNumber }.Where(value => !string.IsNullOrWhiteSpace(value))),
            string.Join(" ", new[] { AddressAbroadZip, AddressAbroadCity }.Where(value => !string.IsNullOrWhiteSpace(value)))
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        public string DisplayStatus => IsDeleted
            ? "Deleted"
            : IsArchived
                ? "Archived"
                : string.IsNullOrWhiteSpace(Status) ? "Active" : Status;

        public string StatusCode => IsDeleted
            ? "deleted"
            : IsArchived
                ? "archived"
                : "active";
    }

    public sealed class AdminMirrorEmployeeFirmHistoryRecord
    {
        public string ClientId { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public Guid HistoryId { get; set; }
        public string FirmName { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime? LastSyncedAt { get; set; }
    }

    public sealed class ClientMirrorSnapshot
    {
        public ClientMirrorStateRecord? State { get; set; }
        public List<AdminMirrorAgencyRecord> Agencies { get; set; } = new();
        public List<AdminMirrorEmployerRecord> Employers { get; set; } = new();
        public List<AdminMirrorEmployeeRecord> Employees { get; set; } = new();
    }
}
