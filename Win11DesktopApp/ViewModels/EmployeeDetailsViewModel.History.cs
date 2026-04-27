using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public partial class EmployeeDetailsViewModel
    {
        private void LoadHistory()
        {
            if (!EnsureEmployeeFolderAvailable("EmployeeDetailsViewModel.LoadHistory"))
            {
                _allHistoryEntries = new List<EmployeeHistoryEntry>();
                HistoryEntries = new ObservableCollection<EmployeeHistoryEntry>();
                HasHistory = false;
                return;
            }

            try
            {
                _allHistoryEntries = _employeeService.LoadHistory(_employeeFolder);
                _allHistoryEntries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
                ApplyHistoryFilter();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeDetailsViewModel.LoadHistory", ex);
                _allHistoryEntries = new List<EmployeeHistoryEntry>();
                HistoryEntries = new ObservableCollection<EmployeeHistoryEntry>();
                HasHistory = false;
            }
        }

        private void ApplyHistoryFilter()
        {
            var filtered = _historyFilter switch
            {
                "Created" => _allHistoryEntries.Where(e => e.EventType == "Created"),
                "Profile" => _allHistoryEntries.Where(e => e.EventType == "ProfileChanged"),
                "Documents" => _allHistoryEntries.Where(e => e.EventType is "DocumentUpdated" or "DocumentExtended"),
                "Status" => _allHistoryEntries.Where(e => e.EventType == "StatusChanged"),
                "Archive" => _allHistoryEntries.Where(e => e.EventType is "Archived" or "Restored" or "ArchiveUndone"),
                _ => _allHistoryEntries.AsEnumerable()
            };
            HistoryEntries = new ObservableCollection<EmployeeHistoryEntry>(filtered);
            HasHistory = HistoryEntries.Count > 0;
        }

        private async Task DeleteHistoryEntryAsync(EmployeeHistoryEntry entry)
        {
            if (!PolicyService.EnsureWriteAllowed("Видалити запис з історії працівника"))
                return;

            var confirm = MessageBox.Show(
                Res("DetHistoryDeleteConfirm") ?? "Видалити цей запис з історії працівника?",
                Res("TitleConfirmDelete") ?? "Підтвердження видалення",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            await _employeeService.DeleteHistoryEntry(_employeeFolder, Data.UniqueId, entry);
            _allHistoryEntries.Remove(entry);
            ApplyHistoryFilter();
            StatusMessage = Res("DetHistoryDeleteSuccess") ?? "Запис історії видалено.";
        }

        private void LoadSalaryHistory()
        {
            if (!EnsureEmployeeFolderAvailable("EmployeeDetailsViewModel.LoadSalaryHistory"))
            {
                SalaryHistoryEntries = new ObservableCollection<SalaryHistoryRecord>();
                HasSalaryHistory = false;
                TotalSalaryEarned = 0;
                TotalHoursAll = 0;
                SalaryMonthDisplays = new ObservableCollection<SalaryMonthDisplay>();
                HasAdvances = false;
                TotalAdvances = 0;
                return;
            }

            try
            {
                var records = _financeService.LoadSalaryHistory(_employeeFolder);
                SalaryHistoryEntries = new ObservableCollection<SalaryHistoryRecord>(records);
                HasSalaryHistory = SalaryHistoryEntries.Count > 0;
                TotalSalaryEarned = records.Sum(r => r.NetSalary);
                TotalHoursAll = records.Sum(r => r.HoursWorked);

                var advances = _financeService.GetAllAdvancesForEmployee(_employeeFolder);
                HasAdvances = advances.Count > 0;
                TotalAdvances = advances.Sum(a => a.Amount);

                // Group by (Year, Month, FirmName) instead of just (Year, Month).
                // Reason: one calendar month can have salary records from two firms
                // when an employee transitions between firms (e.g. archived from
                // Firm A then restored to Firm B). Previously the aggregation
                // collapsed same-month records via Distinct() on "YYYY-MM" and used
                // FirstOrDefault to pick one, silently hiding the second firm's
                // payment from the UI even though both rows existed in the DB.
                var groupKeys = new List<(int Year, int Month, string FirmName)>();
                foreach (var r in records)
                    groupKeys.Add((r.Year, r.Month, r.FirmName ?? string.Empty));
                foreach (var a in advances)
                {
                    if (!TryParseYearMonthKey(a.Month, out var ay, out var am))
                        continue;
                    groupKeys.Add((ay, am, a.CompanyId ?? string.Empty));
                }

                var uniqueGroups = groupKeys
                    .Distinct()
                    .OrderBy(g => g.Year)
                    .ThenBy(g => g.Month)
                    .ThenBy(g => g.FirmName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var displays = new List<SalaryMonthDisplay>();
                // Running debt is tracked per firm: an unpaid balance in Firm A
                // should not bleed into Firm B's balance calculations.
                var runningDebtByFirm = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                foreach (var g in uniqueGroups)
                {
                    var monthKeyOnly = $"{g.Year:D4}-{g.Month:D2}";

                    var salary = records.FirstOrDefault(r =>
                        r.Year == g.Year && r.Month == g.Month &&
                        string.Equals(r.FirmName ?? string.Empty, g.FirmName, StringComparison.OrdinalIgnoreCase));

                    var firmAdvances = advances
                        .Where(a => a.Month == monthKeyOnly &&
                                    string.Equals(a.CompanyId ?? string.Empty, g.FirmName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var monthAdvanceSum = firmAdvances.Sum(a => a.Amount);
                    var grossForMonth = salary?.GrossSalary ?? 0m;

                    var customDeductions = salary?.CustomFields?
                        .Where(cf => cf.Operation == "-")
                        .Sum(cf => cf.Value) ?? 0m;

                    runningDebtByFirm.TryGetValue(g.FirmName, out var runningDebt);

                    var available = grossForMonth - customDeductions - monthAdvanceSum - runningDebt;
                    var isDeducted = salary != null && salary.Advance > 0;

                    var monthAdvances = firmAdvances
                        .Select(a => new AdvanceDisplayItem
                        {
                            Advance = a,
                            IsDeducted = isDeducted
                        })
                        .ToList();

                    var display = new SalaryMonthDisplay
                    {
                        Salary = salary,
                        Advances = monthAdvances,
                        MonthKey = monthKeyOnly,
                        FirmName = g.FirmName,
                        CarriedDebt = runningDebt
                    };

                    if (available < 0 && salary != null)
                    {
                        display.MonthBalance = available;
                        runningDebtByFirm[g.FirmName] = Math.Abs(available);
                    }
                    else if (salary != null)
                    {
                        display.MonthBalance = available;
                        runningDebtByFirm[g.FirmName] = 0;
                    }
                    else
                    {
                        display.MonthBalance = -monthAdvanceSum;
                        runningDebtByFirm[g.FirmName] = runningDebt + monthAdvanceSum;
                    }

                    displays.Add(display);
                }

                displays.Reverse();
                SalaryMonthDisplays = new ObservableCollection<SalaryMonthDisplay>(displays);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeDetailsViewModel.LoadSalaryHistory", ex);
                SalaryHistoryEntries = new ObservableCollection<SalaryHistoryRecord>();
                HasSalaryHistory = false;
                TotalSalaryEarned = 0;
                TotalHoursAll = 0;
                SalaryMonthDisplays = new ObservableCollection<SalaryMonthDisplay>();
                HasAdvances = false;
                TotalAdvances = 0;
            }
        }

        private static bool TryParseYearMonthKey(string key, out int year, out int month)
        {
            year = 0;
            month = 0;
            if (string.IsNullOrWhiteSpace(key))
                return false;
            var parts = key.Split('-');
            if (parts.Length != 2)
                return false;
            return int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out year)
                && int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out month);
        }

        private void LogProfileChanges(EmployeeData oldData, EmployeeData newData)
        {
            void Check(string field, string oldVal, string newVal)
            {
                if (string.Equals(oldVal, newVal, StringComparison.Ordinal)) return;
                _activityLogService.Log("ProfileChanged", "Employee", _firmName, FullName,
                    $"{FullName}: {field}: {oldVal} → {newVal}", oldVal ?? "", newVal ?? "",
                    employeeFolder: _employeeFolder);
            }

            Check(Res("HistFieldFirstName"), oldData.FirstName, newData.FirstName);
            Check(Res("HistFieldLastName"), oldData.LastName, newData.LastName);
            Check(Res("HistFieldBirthDate"), oldData.BirthDate, newData.BirthDate);
            Check(Res("HistFieldHighestEducation"),
                EducationCatalog.GetFullDisplay(oldData.HighestEducationCode),
                EducationCatalog.GetFullDisplay(newData.HighestEducationCode));
            Check(Res("HistFieldGender"),
                oldData.Gender == "female" ? Res("GenderFemale") : Res("GenderMale"),
                newData.Gender == "female" ? Res("GenderFemale") : Res("GenderMale"));
            Check(Res("HistFieldPassportNum"), oldData.PassportNumber, newData.PassportNumber);
            Check(Res("HistFieldPassportAuthority"), oldData.PassportAuthority, newData.PassportAuthority);
            Check(Res("HistFieldPassportExp"), oldData.PassportExpiry, newData.PassportExpiry);
            Check(Res("HistFieldPassportCity"), oldData.PassportCity, newData.PassportCity);
            Check(Res("HistFieldPassportCountry"), oldData.PassportCountry, newData.PassportCountry);
            Check(Res("HistFieldCitizenship"), oldData.Citizenship, newData.Citizenship);
            Check(Res("HistFieldIssuingCountry"), oldData.IssuingCountry, newData.IssuingCountry);
            Check(Res("HistFieldVisaNum"), oldData.VisaNumber, newData.VisaNumber);
            Check(Res("HistFieldVisaAuthority"), oldData.VisaAuthority, newData.VisaAuthority);
            Check(Res("HistFieldVisaType"), oldData.VisaType, newData.VisaType);
            Check(Res("HistFieldVisaExp"), oldData.VisaExpiry, newData.VisaExpiry);
            Check(Res("HistFieldInsNum"), oldData.InsuranceNumber, newData.InsuranceNumber);
            Check(Res("HistFieldInsCompany"), oldData.InsuranceCompanyShort, newData.InsuranceCompanyShort);
            Check(Res("HistFieldInsCompanyFull"), oldData.InsuranceCompanyFull, newData.InsuranceCompanyFull);
            Check(Res("HistFieldInsExp"), oldData.InsuranceExpiry, newData.InsuranceExpiry);
            Check(Res("HistFieldPhone"), oldData.Phone, newData.Phone);
            Check(Res("HistFieldEmail"), oldData.Email, newData.Email);
            Check(Res("HistFieldStatus"), oldData.Status, newData.Status);
            Check(Res("HistFieldPosition"), oldData.PositionTag, newData.PositionTag);
            Check(Res("HistFieldPosNumber"), oldData.PositionNumber, newData.PositionNumber);
            Check(Res("HistFieldWorkAddr"), oldData.WorkAddressTag, newData.WorkAddressTag);
            Check(Res("HistFieldSignDate"), oldData.ContractSignDate, newData.ContractSignDate);
            Check(Res("HistFieldContractType"), oldData.ContractType, newData.ContractType);
            Check(Res("HistFieldWorkPermitName"), oldData.WorkPermitName, newData.WorkPermitName);
            Check(Res("HistFieldDepartment"), oldData.Department, newData.Department);
            Check(Res("HistFieldStartDate"), oldData.StartDate, newData.StartDate);

            if (oldData.MonthlySalaryBrutto != newData.MonthlySalaryBrutto)
                _activityLogService.Log("SalaryChanged", "Salary", _firmName, FullName,
                    $"{FullName}: зарплата {oldData.MonthlySalaryBrutto} → {newData.MonthlySalaryBrutto}",
                    oldData.MonthlySalaryBrutto.ToString(), newData.MonthlySalaryBrutto.ToString(),
                    employeeFolder: _employeeFolder);

            if (oldData.HourlySalary != newData.HourlySalary)
                _activityLogService.Log("RateChanged", "Salary", _firmName, FullName,
                    $"{FullName}: ставка змінена {oldData.HourlySalary} → {newData.HourlySalary}",
                    oldData.HourlySalary.ToString(), newData.HourlySalary.ToString(),
                    employeeFolder: _employeeFolder);
        }
    }
}
