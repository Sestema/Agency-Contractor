using System;
using System.Collections.Generic;

namespace Win11DesktopApp.EmployeeModels
{
    public class CustomSignedDocument
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string SignDate { get; set; } = string.Empty;
        public string ExpiryDate { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public bool IsHidden { get; set; } = false;
    }

    public class EmployeeAddress
    {
        public string Street { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
    }

    public class EmployeeFiles
    {
        public string Passport { get; set; } = string.Empty;
        public string Visa { get; set; } = string.Empty;
        public string VisaPage2 { get; set; } = string.Empty;
        public string Insurance { get; set; } = string.Empty;
        public string Photo { get; set; } = string.Empty;
        public string PassportPage2 { get; set; } = string.Empty;
        public string WorkPermit { get; set; } = string.Empty;
    }

    public class FirmHistoryEntry
    {
        public string FirmName { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
    }

    public class EmployeeData
    {
        public string UniqueId { get; set; } = string.Empty;
        public string EmployeeType { get; set; } = "visa";
        public string EuDocumentType { get; set; } = "passport";
        public string VisaDocType { get; set; } = "visa_sticker";
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
        public string Gender { get; set; } = "male";
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
        public EmployeeAddress AddressLocal { get; set; } = new EmployeeAddress();
        public EmployeeAddress AddressAbroad { get; set; } = new EmployeeAddress();
        public string WorkAddressTag { get; set; } = string.Empty;
        public string PositionTag { get; set; } = string.Empty;
        public string PositionNumber { get; set; } = string.Empty;
        public decimal MonthlySalaryBrutto { get; set; }
        public decimal HourlySalary { get; set; }
        public string ContractType { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public string StartDate { get; set; } = string.Empty;
        public string ContractSignDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public bool IsArchived { get; set; }
        public string ArchivedFromFirm { get; set; } = string.Empty;
        public List<FirmHistoryEntry> FirmHistory { get; set; } = new List<FirmHistoryEntry>();
        public EmployeeFiles Files { get; set; } = new EmployeeFiles();
        public Dictionary<string, string> IgnoredDocuments { get; set; } = new Dictionary<string, string>();
        public List<CustomSignedDocument> CustomDocuments { get; set; } = new List<CustomSignedDocument>();
    }

    public class EmployeeSummary
    {
        public string UniqueId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string PositionTitle { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }
        public bool HasPassport { get; set; }
        public bool HasVisa { get; set; }
        public bool HasInsurance { get; set; }
        public string PassportNumber { get; set; } = string.Empty;
        public string VisaNumber { get; set; } = string.Empty;
        public string InsuranceNumber { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string PassportExpiry { get; set; } = string.Empty;
        public string VisaExpiry { get; set; } = string.Empty;
        public string InsuranceExpiry { get; set; } = string.Empty;
        public string PassportSeverity { get; set; } = "Ok";
        public string VisaSeverity { get; set; } = "Ok";
        public string InsuranceSeverity { get; set; } = "Ok";
        public string WorkPermitSeverity { get; set; } = "Ok";
        public string Status { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string EmployeeType { get; set; } = "visa";
        public string WorkPermitName { get; set; } = string.Empty;
        public string WorkPermitExpiry { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public class EmployeeDocumentTemp
    {
        public string ImagePath { get; set; } = string.Empty;
        public string PdfPath { get; set; } = string.Empty;
        public string PreviewPath { get; set; } = string.Empty;
        public bool IsPdf { get; set; }
        public string OriginalExtension { get; set; } = string.Empty;
    }
}
