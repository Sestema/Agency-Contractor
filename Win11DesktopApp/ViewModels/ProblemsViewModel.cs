using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class ProblemsViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;

        public ICommand GoBackCommand { get; }
        public ICommand OpenEmployeeCommand { get; }

        private ObservableCollection<DocumentExpiryInfo> _problems = new();
        public ObservableCollection<DocumentExpiryInfo> Problems
        {
            get => _problems;
            set => SetProperty(ref _problems, value);
        }

        private int _totalProblems;
        public int TotalProblems
        {
            get => _totalProblems;
            set => SetProperty(ref _totalProblems, value);
        }

        private int _expiredCount;
        public int ExpiredCount
        {
            get => _expiredCount;
            set => SetProperty(ref _expiredCount, value);
        }

        private int _warningCount;
        public int WarningCount
        {
            get => _warningCount;
            set => SetProperty(ref _warningCount, value);
        }

        private bool _hasProblems;
        public bool HasProblems
        {
            get => _hasProblems;
            set => SetProperty(ref _hasProblems, value);
        }

        public string Title => "Проблеми — всі фірми";

        public ProblemsViewModel()
        {
            _employeeService = App.EmployeeService;

            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));
            OpenEmployeeCommand = new RelayCommand(o =>
            {
                if (o is DocumentExpiryInfo info)
                {
                    // Find the company by name and navigate to its employees
                    var company = App.CompanyService.Companies.FirstOrDefault(c => c.Name == info.FirmName);
                    if (company != null)
                    {
                        App.NavigationService.NavigateTo(new EmployeesViewModel(company));
                    }
                }
            });

            LoadProblems();
        }

        private void LoadProblems()
        {
            try
            {
                var allCompanies = App.CompanyService.Companies;
                var problems = new List<DocumentExpiryInfo>();

                foreach (var company in allCompanies)
                {
                    try
                    {
                        var employees = _employeeService.GetEmployeesForFirm(company.Name);
                        foreach (var emp in employees)
                        {
                            CheckDocument(problems, emp, "Паспорт", emp.PassportExpiry);
                            CheckDocument(problems, emp, "Віза", emp.VisaExpiry);
                            CheckDocument(problems, emp, "Страховка", emp.InsuranceExpiry);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ProblemsViewModel: error loading {company.Name}: {ex.Message}");
                    }
                }

                // Sort: expired first, then by days remaining
                problems = problems.OrderBy(p => p.DaysRemaining).ToList();

                Problems = new ObservableCollection<DocumentExpiryInfo>(problems);
                TotalProblems = problems.Count;
                ExpiredCount = problems.Count(p => p.Severity == "Expired" || p.Severity == "Critical");
                WarningCount = problems.Count(p => p.Severity == "Warning");
                HasProblems = problems.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProblemsViewModel.LoadProblems error: {ex.Message}");
            }
        }

        private static void CheckDocument(List<DocumentExpiryInfo> list, EmployeeSummary emp, string docType, string expiryDate)
        {
            if (string.IsNullOrWhiteSpace(expiryDate)) return;

            var severity = DateParsingHelper.GetSeverity(expiryDate);
            if (severity == "Ok" || severity == "Unknown") return;

            var days = DateParsingHelper.GetDaysRemaining(expiryDate);
            list.Add(new DocumentExpiryInfo
            {
                EmployeeName = emp.FullName,
                EmployeeFolder = emp.EmployeeFolder,
                FirmName = emp.FirmName,
                DocumentType = docType,
                ExpiryDateStr = expiryDate,
                ExpiryDate = DateParsingHelper.TryParseDate(expiryDate) ?? DateTime.MinValue,
                DaysRemaining = days,
                Severity = severity
            });
        }

        /// <summary>
        /// Static helper to count problems across ALL companies (used by MainViewModel for badge).
        /// </summary>
        public static int CountAllProblems()
        {
            try
            {
                int count = 0;
                foreach (var company in App.CompanyService.Companies)
                {
                    try
                    {
                        var employees = App.EmployeeService.GetEmployeesForFirm(company.Name);
                        foreach (var emp in employees)
                        {
                            if (IsProblematic(emp.PassportExpiry)) count++;
                            if (IsProblematic(emp.VisaExpiry)) count++;
                            if (IsProblematic(emp.InsuranceExpiry)) count++;
                        }
                    }
                    catch { /* skip this company */ }
                }
                return count;
            }
            catch { return 0; }
        }

        private static bool IsProblematic(string dateStr)
        {
            var s = DateParsingHelper.GetSeverity(dateStr);
            return s == "Expired" || s == "Critical" || s == "Warning";
        }
    }
}
