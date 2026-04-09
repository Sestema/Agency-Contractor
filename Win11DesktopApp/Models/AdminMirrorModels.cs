using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Models;

public sealed class AdminMirrorAgencyDto
{
    public string AgencyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Ico { get; set; } = string.Empty;
    public string FullAddress { get; set; } = string.Empty;
    public DateTime? SourceUpdatedAt { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class AdminMirrorEmployerAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

public sealed class AdminMirrorEmployerPositionDto
{
    public string Title { get; set; } = string.Empty;
    public string PositionNumber { get; set; } = string.Empty;
    public decimal MonthlySalaryBrutto { get; set; }
    public decimal HourlySalary { get; set; }
    public int SortOrder { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

public sealed class AdminMirrorEmployerDto
{
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
    public DateTime LastSyncedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<AdminMirrorEmployerAddressDto> Addresses { get; set; } = new();
    public List<AdminMirrorEmployerPositionDto> Positions { get; set; } = new();
}

public sealed class AdminMirrorEmployeeFirmHistoryDto
{
    public string FirmName { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

public sealed class AdminMirrorEmployeeDto
{
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
    public string Citizenship { get; set; } = string.Empty;
    public string IssuingCountry { get; set; } = string.Empty;
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
    public DateTime LastSyncedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<AdminMirrorEmployeeFirmHistoryDto> FirmHistory { get; set; } = new();
}
