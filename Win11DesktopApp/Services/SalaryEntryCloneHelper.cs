using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    internal static class SalaryEntryCloneHelper
    {
        internal static SalaryEntry CloneEntry(SalaryEntry entry)
        {
#pragma warning disable CS0618
            return new SalaryEntry
            {
                EmployeeId = entry.EmployeeId,
                EmployeeFolder = entry.EmployeeFolder,
                FullName = entry.FullName,
                FirmName = entry.FirmName,
                UpdatedAt = entry.UpdatedAt,
                HoursWorked = entry.HoursWorked,
                HourlyRate = entry.HourlyRate,
                Advance = entry.Advance,
                Advances = entry.Advances,
                Surcharge = entry.Surcharge,
                Accommodation = entry.Accommodation,
                OtherDeductions = entry.OtherDeductions,
                CustomValues = new Dictionary<string, decimal>(entry.CustomValues, StringComparer.OrdinalIgnoreCase),
                SavedNetSalary = entry.SavedNetSalary,
                Status = entry.Status,
                Note = entry.Note,
                ColorTag = entry.ColorTag,
                IsFinished = entry.IsFinished
            };
#pragma warning restore CS0618
        }

        internal static List<SalaryEntry> CloneEntries(IEnumerable<SalaryEntry> source)
            => source.Select(CloneEntry).ToList();
    }
}
