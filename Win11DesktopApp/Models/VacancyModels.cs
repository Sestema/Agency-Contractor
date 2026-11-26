using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Models
{
    /// <summary>
    /// One open position at a firm ("Вільні позиції"). Saved as its own small json file,
    /// so two PCs editing different positions never touch the same file in OneDrive.
    /// </summary>
    public class Vacancy
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FirmName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;

        /// <summary>City or place shown to job seekers instead of the firm name.</summary>
        public string City { get; set; } = string.Empty;

        public int Men { get; set; }
        public int Women { get; set; }
        /// <summary>Married couples: each couple is one man and one woman.</summary>
        public int Couples { get; set; }
        /// <summary>Anyone, man or woman.</summary>
        public int Anyone { get; set; }

        /// <summary>yyyy-MM-dd, when the people are needed.</summary>
        public string NeededFrom { get; set; } = string.Empty;
        public string Pay { get; set; } = string.Empty;
        /// <summary>Conditions, one per line (housing, transport, shifts...).</summary>
        public string Conditions { get; set; } = string.Empty;
        /// <summary>Public text for job seekers (what the work is).</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Private notes, never exported.</summary>
        public string Notes { get; set; } = string.Empty;
        /// <summary>Advert text to copy into Facebook / Viber.</summary>
        public string AdText { get; set; } = string.Empty;

        /// <summary>open, paused or closed.</summary>
        public string Status { get; set; } = VacancyStatus.Open;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ClosedAt { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>People added by hand (an employee of the firm or just a name).</summary>
        public List<VacancyManualPerson> ManualFound { get; set; } = new();
        /// <summary>Employees that must not be counted automatically for this position.</summary>
        public List<string> ExcludedEmployeeIds { get; set; } = new();

        public int TotalNeeded => Math.Max(0, Men) + Math.Max(0, Women) + Math.Max(0, Anyone) + 2 * Math.Max(0, Couples);
    }

    public static class VacancyStatus
    {
        public const string Open = "open";
        public const string Paused = "paused";
        public const string Closed = "closed";
    }

    public class VacancyManualPerson
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>Employee UniqueId when picked from the firm, empty for a free name.</summary>
        public string EmployeeId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>male, female or empty.</summary>
        public string Gender { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    /// <summary>Shared settings of the page (OneDrive): contact for the PDF and firm notes.</summary>
    public class VacancyPageSettings
    {
        public string AgencyName { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public string ContactNote { get; set; } = "Viber · WhatsApp";
    }

    /// <summary>Employee data the matching needs.</summary>
    public sealed record VacancyEmployee(string UniqueId, string FullName, string Gender, DateTime? StartDate);

    /// <summary>One counted person of a position.</summary>
    public sealed record VacancyFoundPerson(string Key, string Name, string Gender, bool IsAuto, string EmployeeId, string ManualId, DateTime? Date);

    /// <summary>Result of matching for one position.</summary>
    public sealed class VacancyProgress
    {
        public List<VacancyFoundPerson> Found { get; } = new();
        public int MenFound { get; set; }
        public int WomenFound { get; set; }
        public int AnyoneFound { get; set; }
        public int CouplesMenFound { get; set; }
        public int CouplesWomenFound { get; set; }
        public int FoundCount => Found.Count;
    }
}
