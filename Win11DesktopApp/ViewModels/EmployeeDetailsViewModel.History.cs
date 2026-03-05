using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public partial class EmployeeDetailsViewModel
    {
        private void LoadHistory()
        {
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
                "Archive" => _allHistoryEntries.Where(e => e.EventType is "Archived" or "Restored"),
                _ => _allHistoryEntries.AsEnumerable()
            };
            HistoryEntries = new ObservableCollection<EmployeeHistoryEntry>(filtered);
            HasHistory = HistoryEntries.Count > 0;
        }

        private void LoadSalaryHistory()
        {
            try
            {
                var financeService = App.FinanceService;
                if (financeService == null) return;
                var records = financeService.LoadSalaryHistory(_employeeFolder);
                SalaryHistoryEntries = new ObservableCollection<SalaryHistoryRecord>(records);
                HasSalaryHistory = SalaryHistoryEntries.Count > 0;
                TotalSalaryEarned = records.Sum(r => r.NetSalary);
                TotalHoursAll = records.Sum(r => r.HoursWorked);

                var advances = financeService.GetAllAdvancesForEmployee(_employeeFolder);
                HasAdvances = advances.Count > 0;
                TotalAdvances = advances.Sum(a => a.Amount);

                var paidMonths = new HashSet<string>(
                    records.Where(r => r.Advance > 0).Select(r => $"{r.Year:D4}-{r.Month:D2}"));

                var allMonthKeys = records
                    .Select(r => $"{r.Year:D4}-{r.Month:D2}")
                    .Union(advances.Select(a => a.Month))
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();

                var displays = new List<SalaryMonthDisplay>();
                decimal runningDebt = 0;

                foreach (var mk in allMonthKeys)
                {
                    var salary = records.FirstOrDefault(r => $"{r.Year:D4}-{r.Month:D2}" == mk);
                    var monthAdvanceSum = advances.Where(a => a.Month == mk).Sum(a => a.Amount);
                    var grossForMonth = salary?.GrossSalary ?? 0m;

                    var customDeductions = salary?.CustomFields?
                        .Where(cf => cf.Operation == "-")
                        .Sum(cf => cf.Value) ?? 0m;

                    var available = grossForMonth - customDeductions - monthAdvanceSum - runningDebt;

                    var isDeducted = salary != null && salary.Advance > 0;

                    var monthAdvances = advances
                        .Where(a => a.Month == mk)
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
                        MonthKey = mk,
                        CarriedDebt = runningDebt
                    };

                    if (available < 0 && salary != null)
                    {
                        display.MonthBalance = available;
                        runningDebt = Math.Abs(available);
                    }
                    else if (salary != null)
                    {
                        display.MonthBalance = available;
                        runningDebt = 0;
                    }
                    else
                    {
                        display.MonthBalance = -monthAdvanceSum;
                        runningDebt += monthAdvanceSum;
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

        private void LogProfileChanges(EmployeeData oldData, EmployeeData newData)
        {
            void Check(string field, string oldVal, string newVal)
            {
                if (string.Equals(oldVal ?? "", newVal ?? "", StringComparison.Ordinal)) return;
                App.ActivityLogService?.Log("ProfileChanged", "Employee", _firmName, FullName,
                    $"{FullName}: {field}: {oldVal} → {newVal}", oldVal ?? "", newVal ?? "",
                    employeeFolder: _employeeFolder);
            }

            Check(Res("HistFieldGender"),
                oldData.Gender == "female" ? Res("GenderFemale") : Res("GenderMale"),
                newData.Gender == "female" ? Res("GenderFemale") : Res("GenderMale"));
            Check(Res("HistFieldPassportNum"), oldData.PassportNumber, newData.PassportNumber);
            Check(Res("HistFieldPassportExp"), oldData.PassportExpiry, newData.PassportExpiry);
            Check(Res("HistFieldPassportCity"), oldData.PassportCity, newData.PassportCity);
            Check(Res("HistFieldPassportCountry"), oldData.PassportCountry, newData.PassportCountry);
            Check(Res("HistFieldVisaNum"), oldData.VisaNumber, newData.VisaNumber);
            Check(Res("HistFieldVisaType"), oldData.VisaType, newData.VisaType);
            Check(Res("HistFieldVisaExp"), oldData.VisaExpiry, newData.VisaExpiry);
            Check(Res("HistFieldInsNum"), oldData.InsuranceNumber, newData.InsuranceNumber);
            Check(Res("HistFieldInsCompany"), oldData.InsuranceCompanyShort, newData.InsuranceCompanyShort);
            Check(Res("HistFieldInsExp"), oldData.InsuranceExpiry, newData.InsuranceExpiry);
            Check(Res("HistFieldPhone"), oldData.Phone, newData.Phone);
            Check(Res("HistFieldEmail"), oldData.Email, newData.Email);
            Check(Res("HistFieldPosition"), oldData.PositionTag, newData.PositionTag);
            Check(Res("HistFieldPosNumber"), oldData.PositionNumber, newData.PositionNumber);
            Check(Res("HistFieldContractType"), oldData.ContractType, newData.ContractType);
            Check(Res("HistFieldWorkPermitName"), oldData.WorkPermitName, newData.WorkPermitName);
            Check(Res("HistFieldDepartment"), oldData.Department, newData.Department);
            Check(Res("HistFieldStartDate"), oldData.StartDate, newData.StartDate);

            if (oldData.MonthlySalaryBrutto != newData.MonthlySalaryBrutto)
                App.ActivityLogService?.Log("SalaryChanged", "Salary", _firmName, FullName,
                    $"{FullName}: зарплата {oldData.MonthlySalaryBrutto} → {newData.MonthlySalaryBrutto}",
                    oldData.MonthlySalaryBrutto.ToString(), newData.MonthlySalaryBrutto.ToString(),
                    employeeFolder: _employeeFolder);

            if (oldData.HourlySalary != newData.HourlySalary)
                App.ActivityLogService?.Log("RateChanged", "Salary", _firmName, FullName,
                    $"{FullName}: ставка змінена {oldData.HourlySalary} → {newData.HourlySalary}",
                    oldData.HourlySalary.ToString(), newData.HourlySalary.ToString(),
                    employeeFolder: _employeeFolder);
        }
    }
}
