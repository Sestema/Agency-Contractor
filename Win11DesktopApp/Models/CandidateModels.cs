using System;

namespace Win11DesktopApp.Models
{
    public class CandidateData
    {
        public string UniqueId { get; set; } = Guid.NewGuid().ToString();
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PassportNumber { get; set; } = string.Empty;
        public string PassportExpiry { get; set; } = string.Empty;
        public string PassportCity { get; set; } = string.Empty;
        public string PassportCountry { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string DesiredPosition { get; set; } = string.Empty;
        public string LocationPreference { get; set; } = "all";
        public string LocationDetails { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string DateAdded { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
        public CandidateFiles Files { get; set; } = new();
    }

    public class CandidateFiles
    {
        public string Photo { get; set; } = string.Empty;
        public string Passport { get; set; } = string.Empty;
    }

    public class CandidateSummary
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string DesiredPosition { get; set; } = string.Empty;
        public string LocationPreference { get; set; } = string.Empty;
        public string LocationDetails { get; set; } = string.Empty;
        public string DateAdded { get; set; } = string.Empty;
        public string PassportNumber { get; set; } = string.Empty;
        public string PassportCountry { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
        public bool HasPhoto { get; set; }
        public string CandidateFolder { get; set; } = string.Empty;
    }
}
