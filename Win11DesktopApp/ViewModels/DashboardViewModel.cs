using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class DashboardItem
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Severity { get; set; } = "";
        public string SeverityColor { get; set; } = "#FF9800";
        public string SeverityLabel { get; set; } = "";
        public string EmployeeFolder { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public int DaysLeft { get; set; }
    }

    public class SalaryMonthSummary
    {
        public string MonthKey { get; set; } = "";
        public string MonthLabel { get; set; } = "";
        public decimal TotalGross { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TotalAdvances { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal GrandTotal => TotalNet + TotalExpenses;
        public int TotalEntries { get; set; }
        public int PaidEntries { get; set; }
        public bool IsFullyPaid => PaidEntries > 0 && PaidEntries == TotalEntries;
        public double PaidRatio => TotalNet > 0 ? Math.Min(1.0, (double)(TotalPaid / TotalNet)) : 0;
        public string StatusColor => IsFullyPaid ? "#4CAF50" : PaidEntries > 0 ? "#FF9800" : "#E53935";
        public string StatusIcon => IsFullyPaid ? "\uE73E" : PaidEntries > 0 ? "\uE121" : "\uE783";
        public string GrossText => $"{TotalGross:N0} CZK";
        public string NetText { get; set; } = "";
        public string ExpenseText { get; set; } = "";
        public string GrandTotalText { get; set; } = "";
        public string PaidText => $"{TotalPaid:N0} / {TotalNet:N0} CZK";
        public string CountText { get; set; } = "";
        public bool HasExpenses => TotalExpenses != 0;
    }

    public class DashboardViewModel : ViewModelBase
    {
        public ICommand GoBackCommand { get; }
        public ICommand OpenEmployeesCommand { get; }
        public ICommand OpenProblemsCommand { get; }
        public ICommand OpenTemplatesCommand { get; }
        public ICommand OpenEmployeeDetailCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand AIReportCommand { get; }
        public ICommand CloseAIReportCommand { get; }
        public ICommand SwapWidgetsCommand { get; }

        private string _slot0 = "expiring";
        public string Slot0 { get => _slot0; set => SetProperty(ref _slot0, value); }

        private string _slot1 = "companies";
        public string Slot1 { get => _slot1; set => SetProperty(ref _slot1, value); }

        private string _slot2 = "salary";
        public string Slot2 { get => _slot2; set => SetProperty(ref _slot2, value); }

        private double _columnRatio = 1.0;
        public double ColumnRatio { get => _columnRatio; set => SetProperty(ref _columnRatio, value); }

        private double _rowRatio = 0.4;
        public double RowRatio { get => _rowRatio; set => SetProperty(ref _rowRatio, value); }

        private bool _isAIReportOpen;
        public bool IsAIReportOpen
        {
            get => _isAIReportOpen;
            set => SetProperty(ref _isAIReportOpen, value);
        }

        private bool _isAIReportLoading;
        public bool IsAIReportLoading
        {
            get => _isAIReportLoading;
            set => SetProperty(ref _isAIReportLoading, value);
        }

        private string _aiReportText = string.Empty;
        public string AIReportText
        {
            get => _aiReportText;
            set => SetProperty(ref _aiReportText, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private bool _isEmployeeDetailsOpen;
        public bool IsEmployeeDetailsOpen
        {
            get => _isEmployeeDetailsOpen;
            set => SetProperty(ref _isEmployeeDetailsOpen, value);
        }

        private EmployeeDetailsViewModel? _employeeDetailsVm;
        public EmployeeDetailsViewModel? EmployeeDetailsVm
        {
            get => _employeeDetailsVm;
            set => SetProperty(ref _employeeDetailsVm, value);
        }

        private int _totalEmployees;
        public int TotalEmployees
        {
            get => _totalEmployees;
            set => SetProperty(ref _totalEmployees, value);
        }

        private int _totalProblems;
        public int TotalProblems
        {
            get => _totalProblems;
            set => SetProperty(ref _totalProblems, value);
        }

        private int _totalTemplates;
        public int TotalTemplates
        {
            get => _totalTemplates;
            set => SetProperty(ref _totalTemplates, value);
        }

        private int _totalCompanies;
        public int TotalCompanies
        {
            get => _totalCompanies;
            set => SetProperty(ref _totalCompanies, value);
        }

        private string _employeeTrend = "";
        public string EmployeeTrend { get => _employeeTrend; set => SetProperty(ref _employeeTrend, value); }

        private string _problemTrend = "";
        public string ProblemTrend { get => _problemTrend; set => SetProperty(ref _problemTrend, value); }

        private string _templateTrend = "";
        public string TemplateTrend { get => _templateTrend; set => SetProperty(ref _templateTrend, value); }

        private ObservableCollection<DashboardItem> _expiringDocs = new();
        public ObservableCollection<DashboardItem> ExpiringDocs
        {
            get => _expiringDocs;
            set => SetProperty(ref _expiringDocs, value);
        }

        private ObservableCollection<SalaryMonthSummary> _salaryMonths = new();
        public ObservableCollection<SalaryMonthSummary> SalaryMonths
        {
            get => _salaryMonths;
            set => SetProperty(ref _salaryMonths, value);
        }

        private string _salaryTotalText = "";
        public string SalaryTotalText
        {
            get => _salaryTotalText;
            set => SetProperty(ref _salaryTotalText, value);
        }

        private ObservableCollection<CompanyStatItem> _companyStats = new();
        public ObservableCollection<CompanyStatItem> CompanyStats
        {
            get => _companyStats;
            set => SetProperty(ref _companyStats, value);
        }

        public DashboardViewModel()
        {
            GoBackCommand = new RelayCommand(_ => App.NavigationService?.NavigateTo(new MainViewModel()));
            OpenEmployeesCommand = new RelayCommand(_ =>
            {
                var company = App.CompanyService?.SelectedCompany;
                if (company != null)
                    App.NavigationService?.NavigateTo(new EmployeesViewModel(company));
            });
            OpenProblemsCommand = new RelayCommand(_ => App.NavigationService?.NavigateTo(new ProblemsViewModel()));
            OpenTemplatesCommand = new RelayCommand(_ =>
            {
                var company = App.CompanyService?.SelectedCompany;
                if (company != null)
                    App.NavigationService?.NavigateTo(new TemplatesViewModel(company));
            });
            OpenEmployeeDetailCommand = new RelayCommand(o =>
            {
                if (o is DashboardItem item && !string.IsNullOrEmpty(item.EmployeeFolder))
                {
                    if (EmployeeDetailsVm != null)
                    {
                        EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                        EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
                    }
                    EmployeeDetailsVm = new EmployeeDetailsViewModel(item.CompanyName, item.EmployeeFolder);
                    EmployeeDetailsVm.RequestClose += OnDetailsClose;
                    EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
                    IsEmployeeDetailsOpen = true;
                }
            });

            RefreshCommand = new RelayCommand(_ => LoadDataAsync());
            AIReportCommand = new RelayCommand(_ => GenerateAIReport(), _ => !IsAIReportLoading);
            CloseAIReportCommand = new RelayCommand(_ => IsAIReportOpen = false);
            SwapWidgetsCommand = new RelayCommand(o =>
            {
                if (o is string param)
                {
                    var parts = param.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int from) && int.TryParse(parts[1], out int to) && from != to)
                        SwapSlots(from, to);
                }
            });

            LoadLayout();
            LoadDataAsync();
        }

        private async void GenerateAIReport()
        {
            if (!(App.GeminiApiService?.IsConfigured ?? false))
            {
                AIReportText = Res("AIChatNoModel");
                IsAIReportOpen = true;
                return;
            }

            IsAIReportLoading = true;
            AIReportText = Res("AIChatThinking");
            IsAIReportOpen = true;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Agency dashboard data:");
                sb.AppendLine($"- Total employees: {TotalEmployees}");
                sb.AppendLine($"- Total companies: {TotalCompanies}");
                sb.AppendLine($"- Total problems (expiring docs): {TotalProblems}");
                sb.AppendLine($"- Total templates: {TotalTemplates}");
                sb.AppendLine($"- Employee trend: {EmployeeTrend}");
                sb.AppendLine($"- Problem trend: {ProblemTrend}");

                if (ExpiringDocs.Count > 0)
                {
                    sb.AppendLine("\nExpiring/expired documents:");
                    foreach (var doc in ExpiringDocs.Take(15))
                        sb.AppendLine($"  - {doc.Title}: {doc.Subtitle} [{doc.Severity}] ({doc.SeverityLabel}) — {doc.CompanyName}");
                }

                if (CompanyStats.Count > 0)
                {
                    sb.AppendLine("\nCompany statistics:");
                    foreach (var cs in CompanyStats)
                        sb.AppendLine($"  - {cs.CompanyName}: {cs.EmployeeCount} employees, {cs.ProblemCount} problems, {cs.TemplateCount} templates");
                }

                if (SalaryMonths.Count > 0)
                {
                    sb.AppendLine($"\nSalary overview ({SalaryTotalText}):");
                    foreach (var sm in SalaryMonths)
                        sb.AppendLine($"  - {sm.MonthLabel}: Gross={sm.GrossText}, Net={sm.TotalNet:N0} CZK, Paid={sm.TotalPaid:N0}/{sm.TotalNet:N0} CZK, Workers={sm.TotalEntries}, Fully paid={sm.IsFullyPaid}");
                }

                var uiLang = (App.AppSettingsService?.Settings?.LanguageCode ?? "uk") switch
                {
                    "en" => "English",
                    "cs" => "Czech (čeština)",
                    "ru" => "Russian",
                    _ => "Ukrainian"
                };
                var systemPrompt = $@"You are an analytics expert for a Czech employment agency (agentura práce).
Based on the dashboard data, generate a concise analytical report in {uiLang}.

Include:
1. Brief overview of the current state
2. Key problems that need attention (expired documents, unpaid salaries)  
3. Specific recommendations with priorities
4. Risk assessment

Use text section headers like [OVERVIEW], [PROBLEMS], [RECOMMENDATIONS], [RISKS]. NEVER use emoji — they won't render in the app. Be concise but actionable. Max 15-20 lines.";

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                AIReportText = await (App.GeminiApiService?.ChatAsync(sb.ToString(), systemPrompt, cts.Token) ?? Task.FromResult("Error: API service not available"));
            }
            catch (Exception ex)
            {
                AIReportText = $"Error: {ex.Message}";
            }
            finally
            {
                IsAIReportLoading = false;
            }
        }

        private void OnDetailsClose() => IsEmployeeDetailsOpen = false;
        private void OnDetailsDataChanged() => LoadDataAsync();

        private string GetSlot(int index) => index switch { 0 => Slot0, 1 => Slot1, 2 => Slot2, _ => "" };
        private void SetSlot(int index, string value) { switch (index) { case 0: Slot0 = value; break; case 1: Slot1 = value; break; case 2: Slot2 = value; break; } }

        public void SwapSlots(int a, int b)
        {
            var temp = GetSlot(a);
            SetSlot(a, GetSlot(b));
            SetSlot(b, temp);
            SaveLayout();
        }

        private void LoadLayout()
        {
            var s = App.AppSettingsService?.Settings;
            if (s == null) return;
            Slot0 = string.IsNullOrEmpty(s.DashSlot0) ? "expiring" : s.DashSlot0;
            Slot1 = string.IsNullOrEmpty(s.DashSlot1) ? "companies" : s.DashSlot1;
            Slot2 = string.IsNullOrEmpty(s.DashSlot2) ? "salary" : s.DashSlot2;
            ColumnRatio = s.DashColumnRatio > 0.1 ? s.DashColumnRatio : 1.0;
            RowRatio = s.DashRowRatio > 0.05 ? s.DashRowRatio : 0.4;
        }

        public void SaveLayout()
        {
            var s = App.AppSettingsService?.Settings;
            if (s == null) return;
            s.DashSlot0 = Slot0;
            s.DashSlot1 = Slot1;
            s.DashSlot2 = Slot2;
            s.DashColumnRatio = ColumnRatio;
            s.DashRowRatio = RowRatio;
            App.AppSettingsService?.SaveSettings();
        }

        private async void LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                var data = await Task.Run(GatherDashboardData);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TotalEmployees = data.TotalEmployees;
                    TotalProblems = data.TotalProblems;
                    TotalTemplates = data.TotalTemplates;
                    TotalCompanies = data.TotalCompanies;
                    EmployeeTrend = data.EmployeeTrend;
                    ProblemTrend = data.ProblemTrend;
                    TemplateTrend = data.TemplateTrend;
                    ExpiringDocs = new ObservableCollection<DashboardItem>(data.ExpiringDocs);
                    SalaryMonths = new ObservableCollection<SalaryMonthSummary>(data.SalaryMonths);
                    SalaryTotalText = data.SalaryTotalText;
                    CompanyStats = new ObservableCollection<CompanyStatItem>(data.CompanyStats);
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogError("DashboardViewModel.LoadData", ex);
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() => IsLoading = false);
            }
        }

        private static DashboardData GatherDashboardData()
        {
            var result = new DashboardData();
            var companySvc = App.CompanyService;
            var companies = companySvc?.VisibleCompanies?.ToList();
            if (companies == null) return result;

            result.TotalCompanies = companies.Count;
            int thisMonthAdded = 0;
            int expiredCount = 0;
            int criticalCount = 0;

            foreach (var company in companies)
            {
                try
                {
                    var employees = App.EmployeeService?.GetEmployeesForFirm(company.Name) ?? new List<EmployeeSummary>();
                    result.TotalEmployees += employees.Count;

                    var templates = App.TemplateService?.GetTemplates(company.Name) ?? new List<TemplateEntry>();
                    result.TotalTemplates += templates.Count;

                    int companyProblems = 0;
                    var now = DateTime.Today;

                    foreach (var emp in employees)
                    {
                        if (DateTime.TryParse(emp.StartDate, out var start) &&
                            start.Year == now.Year && start.Month == now.Month)
                            thisMonthAdded++;

                        CheckExpiry(emp.PassportExpiry, emp.FullName,
                            Res("DetDocPassport"), company.Name, emp.EmployeeFolder, result.ExpiringDocs, ref companyProblems, ref expiredCount, ref criticalCount);
                        if (emp.EmployeeType != "eu_citizen")
                            CheckExpiry(emp.VisaExpiry, emp.FullName,
                                Res("DetDocVisa"), company.Name, emp.EmployeeFolder, result.ExpiringDocs, ref companyProblems, ref expiredCount, ref criticalCount);
                        CheckExpiry(emp.InsuranceExpiry, emp.FullName,
                            Res("DetDocInsurance"), company.Name, emp.EmployeeFolder, result.ExpiringDocs, ref companyProblems, ref expiredCount, ref criticalCount);
                        if (emp.EmployeeType == "work_permit")
                            CheckExpiry(emp.WorkPermitExpiry, emp.FullName,
                                Res("DetDocWorkPermit"), company.Name, emp.EmployeeFolder, result.ExpiringDocs, ref companyProblems, ref expiredCount, ref criticalCount);
                    }

                    result.TotalProblems += companyProblems;
                    result.CompanyStats.Add(new CompanyStatItem
                    {
                        CompanyName = company.Name,
                        EmployeeCount = employees.Count,
                        ProblemCount = companyProblems,
                        TemplateCount = templates.Count,
                        TotalEmployees = 0
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("Dashboard.GatherData", ex);
                }
            }

            foreach (var cs in result.CompanyStats)
                cs.TotalEmployees = result.TotalEmployees;

            result.EmployeeTrend = thisMonthAdded > 0 ? $"+{thisMonthAdded} {Res("DashThisMonth")}" : Res("DashNoChange");
            result.ProblemTrend = expiredCount > 0 ? $"{expiredCount} {Res("DashExpired")}" : Res("DashAllGood");
            result.TemplateTrend = $"{result.TotalTemplates} {Res("DashAvailable")}";

            result.ExpiringDocs = result.ExpiringDocs
                .OrderBy(d => d.Severity == "Expired" ? 0 : d.Severity == "Critical" ? 1 : 2)
                .Take(15).ToList();

            try
            {
                var availableMonths = new HashSet<(int year, int month)>();
                var folderService = App.FolderService;

                if (folderService != null && !string.IsNullOrEmpty(folderService.RootPath))
                {
                    foreach (var company in companies)
                    {
                        try
                        {
                            var paymentFolder = folderService.GetPaymentFolder(company.Name);
                            if (string.IsNullOrEmpty(paymentFolder) || !System.IO.Directory.Exists(paymentFolder)) continue;
                            foreach (var file in System.IO.Directory.GetFiles(paymentFolder, "salary_*.json"))
                            {
                                var fn = System.IO.Path.GetFileNameWithoutExtension(file);
                                var parts = fn.Split('_');
                                if (parts.Length == 3 && int.TryParse(parts[1], out var y) && int.TryParse(parts[2], out var m))
                                    availableMonths.Add((y, m));
                            }
                        }
                        catch (Exception ex) { LoggingService.LogError("Dashboard.ScanSalaryFiles", ex); }
                    }
                }

                var monthMap = new Dictionary<string, SalaryMonthSummary>();
                decimal grandGross = 0, grandNet = 0, grandPaid = 0;

                foreach (var (year, month) in availableMonths)
                {
                    var (entries, expenses) = App.FinanceService?.LoadAllFirmPayments(year, month) ?? (new List<SalaryEntry>(), new List<FirmExpense>());
                    if (entries.Count == 0) continue;

                    var mk = $"{year:D4}-{month:D2}";
                    var summary = new SalaryMonthSummary { MonthKey = mk, MonthLabel = FormatMonthLabel(year, month) };

                    foreach (var entry in entries)
                    {
                        decimal net = entry.SavedNetSalary > 0 ? entry.SavedNetSalary : entry.GrossSalary - entry.Advance;
                        summary.TotalEntries++;
                        summary.TotalGross += entry.GrossSalary;
                        summary.TotalNet += net;
                        summary.TotalAdvances += entry.Advance;
                        if (entry.IsPaid)
                        {
                            summary.PaidEntries++;
                            summary.TotalPaid += net;
                        }
                    }

                    summary.TotalExpenses = expenses.Sum(e => e.Amount);
                    monthMap[mk] = summary;
                }

                foreach (var s in monthMap.Values)
                {
                    s.CountText = $"{s.PaidEntries}/{s.TotalEntries} {Res("DashSalaryWorkers")}";
                    s.NetText = $"{Res("DashSalaryNet")}: {s.TotalNet:N0} CZK";
                    s.ExpenseText = s.HasExpenses ? $"{Res("DashSalaryExpenses")}: {s.TotalExpenses:N0} CZK" : "";
                    s.GrandTotalText = $"{Res("DashSalaryGrandTotal")}: {s.GrandTotal:N0} CZK";
                    grandGross += s.TotalGross;
                    grandNet += s.TotalNet;
                    grandPaid += s.TotalPaid;
                }

                result.SalaryMonths = monthMap.Values
                    .OrderByDescending(s => s.MonthKey)
                    .ToList();

                result.SalaryTotalText = grandGross > 0
                    ? $"{Res("DashSalaryTotal")}: {grandGross:N0} CZK"
                    : "";
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Dashboard.GatherSalary", ex);
            }

            return result;
        }

        private static string FormatMonthLabel(int year, int month)
        {
            try
            {
                var dt = new DateTime(year, month, 1);
                var culture = System.Threading.Thread.CurrentThread.CurrentUICulture;
                var name = dt.ToString("MMMM", culture);
                return char.ToUpper(name[0]) + name[1..] + " " + year;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Dashboard.FormatMonthLabel", ex);
                return $"{month:D2}.{year}";
            }
        }

        private static void CheckExpiry(string dateStr, string empName, string docType,
            string companyName, string folder, List<DashboardItem> list, ref int problemCount,
            ref int expiredCount, ref int criticalCount)
        {
            var severity = DateParsingHelper.GetSeverity(dateStr);
            if (severity is not ("Expired" or "Critical" or "Warning")) return;
            problemCount++;

            int days = DateParsingHelper.GetDaysRemaining(dateStr);
            string severityLabel, severityColor;
            string icon;

            if (severity == "Expired")
            {
                expiredCount++;
                severityLabel = Res("DashSevExpired");
                severityColor = "#E53935";
                icon = "\uE783";
            }
            else if (severity == "Critical")
            {
                criticalCount++;
                severityLabel = $"{days} {Res("DashSevDays")}";
                severityColor = "#FF5722";
                icon = "\uE121";
            }
            else
            {
                severityLabel = $"{days} {Res("DashSevDays")}";
                severityColor = "#FF9800";
                icon = "\uE121";
            }

            list.Add(new DashboardItem
            {
                Title = empName,
                Subtitle = docType,
                Icon = icon,
                Severity = severity,
                SeverityColor = severityColor,
                SeverityLabel = severityLabel,
                DaysLeft = days,
                EmployeeFolder = folder,
                CompanyName = companyName
            });
        }

        private class DashboardData
        {
            public int TotalEmployees;
            public int TotalProblems;
            public int TotalTemplates;
            public int TotalCompanies;
            public string EmployeeTrend = "";
            public string ProblemTrend = "";
            public string TemplateTrend = "";
            public string SalaryTotalText = "";
            public List<DashboardItem> ExpiringDocs = new();
            public List<SalaryMonthSummary> SalaryMonths = new();
            public List<CompanyStatItem> CompanyStats = new();
        }
    }

    public class CompanyStatItem
    {
        public string CompanyName { get; set; } = "";
        public int EmployeeCount { get; set; }
        public int ProblemCount { get; set; }
        public int TemplateCount { get; set; }
        public int TotalEmployees { get; set; }
        public double EmployeeRatio => TotalEmployees > 0 ? (double)EmployeeCount / TotalEmployees : 0;
    }
}
