using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using OxyPlot;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Win11DesktopApp.Helpers;
using OxyPlot.Axes;
using OxyPlot.Series;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class ReportViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;

        public ICommand GoBackCommand { get; }
        public ICommand GenerateReportCommand { get; }
        public ICommand ExportToExcelCommand { get; }
        public ICommand ToggleAllCompaniesCommand { get; }
        public ICommand ToggleAllAgenciesCommand { get; }
        public ICommand SwitchToSummaryCommand { get; }
        public ICommand SwitchToEmployeesCommand { get; }
        public ICommand ShowExportDialogCommand { get; }
        public ICommand ConfirmExportCommand { get; }
        public ICommand CancelExportCommand { get; }
        public ICommand OpenEmployeeCommand { get; }

        // ===== View Toggle =====
        private bool _isSummaryView = true;
        public bool IsSummaryView
        {
            get => _isSummaryView;
            set { if (SetProperty(ref _isSummaryView, value)) OnPropertyChanged(nameof(IsEmployeesView)); }
        }
        public bool IsEmployeesView => !IsSummaryView;

        // ===== Export Dialog =====
        public ObservableCollection<ExportSheetOption> ExportSheets { get; } = new();

        private bool _isExportDialogOpen;
        public bool IsExportDialogOpen
        {
            get => _isExportDialogOpen;
            set => SetProperty(ref _isExportDialogOpen, value);
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

        private bool _exportAsXlsx = true;
        public bool ExportAsXlsx
        {
            get => _exportAsXlsx;
            set { if (SetProperty(ref _exportAsXlsx, value)) OnPropertyChanged(nameof(ExportAsPdf)); }
        }
        public bool ExportAsPdf
        {
            get => !_exportAsXlsx;
            set => ExportAsXlsx = !value;
        }

        // ===== Firm Filters =====
        public ObservableCollection<CompanyFilter> CompanyFilters { get; } = new();

        private bool _allCompaniesSelected = true;
        public bool AllCompaniesSelected
        {
            get => _allCompaniesSelected;
            set
            {
                if (SetProperty(ref _allCompaniesSelected, value))
                {
                    foreach (var f in CompanyFilters) f.IsChecked = value;
                }
            }
        }

        // ===== Agency Filters =====
        public ObservableCollection<CompanyFilter> AgencyFilters { get; } = new();

        private bool _allAgenciesSelected = true;
        public bool AllAgenciesSelected
        {
            get => _allAgenciesSelected;
            set
            {
                if (SetProperty(ref _allAgenciesSelected, value))
                {
                    foreach (var f in AgencyFilters) f.IsChecked = value;
                }
            }
        }

        // ===== Date Filters =====
        private DateTime _dateFrom;
        public DateTime DateFrom
        {
            get => _dateFrom;
            set
            {
                if (SetProperty(ref _dateFrom, value))
                {
                    if (App.AppSettingsService?.Settings != null)
                    {
                        App.AppSettingsService.Settings.ReportDateFrom = value.ToString("yyyy-MM-dd");
                        App.AppSettingsService.SaveSettings();
                    }
                }
            }
        }

        private DateTime _dateTo;
        public DateTime DateTo
        {
            get => _dateTo;
            set
            {
                if (SetProperty(ref _dateTo, value))
                {
                    if (App.AppSettingsService?.Settings != null)
                    {
                        App.AppSettingsService.Settings.ReportDateTo = value.ToString("yyyy-MM-dd");
                        App.AppSettingsService.SaveSettings();
                    }
                }
            }
        }

        // ===== Summary stats =====
        private int _totalEmployees;
        public int TotalEmployees { get => _totalEmployees; set => SetProperty(ref _totalEmployees, value); }

        private int _activeEmployees;
        public int ActiveEmployees { get => _activeEmployees; set => SetProperty(ref _activeEmployees, value); }

        private int _archivedInPeriod;
        public int ArchivedInPeriod { get => _archivedInPeriod; set => SetProperty(ref _archivedInPeriod, value); }

        private int _endedInPeriod;
        public int EndedInPeriod { get => _endedInPeriod; set => SetProperty(ref _endedInPeriod, value); }

        private int _restoredInPeriod;
        public int RestoredInPeriod { get => _restoredInPeriod; set => SetProperty(ref _restoredInPeriod, value); }

        private int _totalArchiveActions;
        public int TotalArchiveActions { get => _totalArchiveActions; set => SetProperty(ref _totalArchiveActions, value); }

        private int _newInPeriod;
        public int NewInPeriod { get => _newInPeriod; set => SetProperty(ref _newInPeriod, value); }

        // ===== Summary totals row =====
        private int _summaryTotal;
        public int SummaryTotal { get => _summaryTotal; set => SetProperty(ref _summaryTotal, value); }

        private int _summaryActive;
        public int SummaryActive { get => _summaryActive; set => SetProperty(ref _summaryActive, value); }

        private int _summaryPassportOnly;
        public int SummaryPassportOnly { get => _summaryPassportOnly; set => SetProperty(ref _summaryPassportOnly, value); }

        private int _summaryArchived;
        public int SummaryArchived { get => _summaryArchived; set => SetProperty(ref _summaryArchived, value); }

        // ===== Report tables =====
        public ObservableCollection<FirmReportRow> FirmDetails { get; } = new();
        public ObservableCollection<AgencyReportRow> AgencyDetails { get; } = new();
        public ObservableCollection<ArchiveLogEntry> ArchiveHistory { get; } = new();

        // ===== Employee list =====
        public ObservableCollection<FirmEmployeeGroup> EmployeeGroups { get; } = new();
        private List<EmployeeReportRow> _allEmployees = new();

        private string _employeeSearchText = string.Empty;
        public string EmployeeSearchText
        {
            get => _employeeSearchText;
            set { if (SetProperty(ref _employeeSearchText, value)) FilterEmployees(); }
        }

        // ===== Chart =====
        private PlotModel _archiveChartModel = new();
        public PlotModel ArchiveChartModel { get => _archiveChartModel; set => SetProperty(ref _archiveChartModel, value); }

        private bool _hasData;
        public bool HasData { get => _hasData; set => SetProperty(ref _hasData, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ReportViewModel()
        {
            var s = App.AppSettingsService?.Settings;
            _dateFrom = s != null && DateTime.TryParse(s.ReportDateFrom, out var df) ? df : DateTime.Today.AddMonths(-1);
            _dateTo = s != null && DateTime.TryParse(s.ReportDateTo, out var dt) ? dt : DateTime.Today;

            _employeeService = App.EmployeeService;

            GoBackCommand = new RelayCommand(o => App.NavigationService?.NavigateTo(new MainViewModel()));
            GenerateReportCommand = new RelayCommand(async o => await GenerateReportAsync());
            ExportToExcelCommand = new RelayCommand(o => ExportToExcel(), o => HasData);
            ToggleAllCompaniesCommand = new RelayCommand(o => AllCompaniesSelected = !AllCompaniesSelected);
            ToggleAllAgenciesCommand = new RelayCommand(o => AllAgenciesSelected = !AllAgenciesSelected);
            SwitchToSummaryCommand = new RelayCommand(o => IsSummaryView = true);
            SwitchToEmployeesCommand = new RelayCommand(o => IsSummaryView = false);
            ShowExportDialogCommand = new RelayCommand(o => OpenExportDialog(), o => HasData);
            ConfirmExportCommand = new RelayCommand(o => { IsExportDialogOpen = false; if (ExportAsXlsx) ExportToExcel(); else ExportToPdf(); });
            CancelExportCommand = new RelayCommand(o => IsExportDialogOpen = false);
            OpenEmployeeCommand = new RelayCommand(o => OpenEmployee(o as EmployeeReportRow));

            InitExportSheets();
            LoadFilters();
            _ = GenerateReportAsync();
        }

        private void InitExportSheets()
        {
            ExportSheets.Clear();
            ExportSheets.Add(new ExportSheetOption { SheetKey = "firms", DisplayName = GetString("ReportExportSheetFirms") });
            ExportSheets.Add(new ExportSheetOption { SheetKey = "agencies", DisplayName = GetString("ReportExportSheetAgencies") });
            ExportSheets.Add(new ExportSheetOption { SheetKey = "employees", DisplayName = GetString("ReportExportSheetEmployees") });
            ExportSheets.Add(new ExportSheetOption { SheetKey = "archive", DisplayName = GetString("ReportExportSheetArchive") });
        }

        private void OpenExportDialog()
        {
            InitExportSheets();
            IsExportDialogOpen = true;
        }

        private static string GetString(string key)
        {
            return Application.Current.Resources[key] as string ?? key;
        }

        private static string DocString(string key) =>
            App.DocumentLocalizationService?.Get(key) ?? GetString(key);

        private static string GetTypeDisplay(string type)
        {
            var key = type switch
            {
                "visa" => "EmpTypeVisa",
                "eu_citizen" => "EmpTypeEuCitizen",
                "work_permit" => "EmpTypeWorkPermit",
                "passport_only" => "EmpTypePassportOnly",
                _ => null
            };
            if (key != null && Application.Current.Resources[key] is string s) return s;
            return type;
        }

        private static string GetDocTypeDisplay(string type)
        {
            var key = type switch
            {
                "visa" => "EmpTypeVisa",
                "eu_citizen" => "EmpTypeEuCitizen",
                "work_permit" => "EmpTypeWorkPermit",
                "passport_only" => "EmpTypePassportOnly",
                _ => null
            };
            if (key != null) return DocString(key);
            return type;
        }

        private void LoadFilters()
        {
            CompanyFilters.Clear();
            AgencyFilters.Clear();

            var agencyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var cs = App.CompanyService;
            var companies = cs?.Companies;
            if (companies == null) return;

            foreach (var company in companies.Where(c => cs!.IsCompanyVisible(c)))
            {
                CompanyFilters.Add(new CompanyFilter { CompanyName = company.Name, IsChecked = true });

                if (company.Agency != null && !string.IsNullOrWhiteSpace(company.Agency.Name))
                {
                    agencyNames.Add(company.Agency.Name.Trim());
                }
            }

            foreach (var agencyName in agencyNames.OrderBy(n => n))
            {
                AgencyFilters.Add(new CompanyFilter { CompanyName = agencyName, IsChecked = true });
            }
        }

        private async Task GenerateReportAsync()
        {
            IsLoading = true;
            await Task.Delay(50);

            try
            {
                FirmDetails.Clear();
                AgencyDetails.Clear();
                ArchiveHistory.Clear();
                _allEmployees.Clear();
                EmployeeGroups.Clear();

                int totalEmp = 0, activeEmp = 0, newInPeriod = 0, endedInPeriod = 0;

                var selectedFirms = CompanyFilters
                    .Where(f => f.IsChecked)
                    .Select(f => f.CompanyName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var selectedAgencies = AgencyFilters
                    .Where(f => f.IsChecked)
                    .Select(f => f.CompanyName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var effectiveFirms = new List<string>();

                var csReport = App.CompanyService;
                var companiesForReport = csReport?.Companies;
                if (companiesForReport != null)
                {
                foreach (var company in companiesForReport.Where(c => csReport!.IsCompanyVisible(c)))
                {
                    if (!selectedFirms.Contains(company.Name)) continue;

                    bool hasAgency = company.Agency != null && !string.IsNullOrWhiteSpace(company.Agency.Name);

                    if (!AgencyFilters.Any())
                    {
                        effectiveFirms.Add(company.Name);
                    }
                    else if (!hasAgency)
                    {
                        effectiveFirms.Add(company.Name);
                    }
                    else if (selectedAgencies.Contains(company.Agency!.Name.Trim()))
                    {
                        effectiveFirms.Add(company.Name);
                    }
                }
                }

                var archiveLog = new List<ArchiveLogEntry>();
                try { archiveLog = _employeeService.LoadArchiveLog(); } catch (Exception ex) { LoggingService.LogError("ReportViewModel.LoadArchiveLog", ex); }

                var effectiveFirmsSet = effectiveFirms.ToHashSet(StringComparer.OrdinalIgnoreCase);

                var archivedByFirm = LoadArchivedEmployeesForReport(effectiveFirmsSet);

                var agencyData = new Dictionary<string, (int firms, int total, int active)>(StringComparer.OrdinalIgnoreCase);

                foreach (var firmName in effectiveFirms)
                {
                    try
                    {
                        var employees = _employeeService.GetEmployeesForFirm(firmName);
                        var filtered = FilterByDateRange(employees);

                        int firmActive = filtered.Count(x =>
                            string.IsNullOrEmpty(x.Summary.Status) || x.Summary.Status == "Active");

                        int firmNew = filtered.Count(x =>
                        {
                            var sd = DateParsingHelper.TryParseDate(x.Summary.StartDate);
                            return sd != null && sd.Value.Date >= DateFrom.Date && sd.Value.Date <= DateTo.Date;
                        });

                        int firmPassportOnly = filtered.Count(x => x.Summary.EmployeeType == "passport_only");

                        foreach (var (e, endDate) in filtered)
                        {
                            _allEmployees.Add(new EmployeeReportRow
                            {
                                FullName = e.FullName,
                                FirmName = firmName,
                                EmployeeFolder = e.EmployeeFolder,
                                EmployeeType = !string.IsNullOrEmpty(e.WorkPermitName) ? e.WorkPermitName : GetDocTypeDisplay(e.EmployeeType),
                                PassportExpiry = e.PassportExpiry,
                                VisaExpiry = e.VisaExpiry,
                                InsuranceExpiry = e.InsuranceExpiry,
                                StartDate = e.StartDate,
                                EndDate = endDate,
                                Phone = e.Phone,
                                Position = e.PositionTitle,
                                IsArchived = false
                            });
                        }

                        var archivedForFirm = archivedByFirm.ContainsKey(firmName)
                            ? archivedByFirm[firmName] : new List<EmployeeReportRow>();

                        foreach (var ae in archivedForFirm)
                            _allEmployees.Add(ae);

                        int firmArchivedCount = archivedForFirm.Count;
                        firmPassportOnly += archivedForFirm.Count(a =>
                            a.EmployeeType == GetDocTypeDisplay("passport_only"));

                        int firmTotal = filtered.Count + firmArchivedCount;

                        int firmEnded = 0;
                        foreach (var (emp, ed) in filtered)
                        {
                            var endDt = DateParsingHelper.TryParseDate(ed);
                            if (endDt != null && endDt.Value.Date >= DateFrom.Date && endDt.Value.Date <= DateTo.Date)
                                firmEnded++;
                        }
                        foreach (var ae in archivedForFirm)
                        {
                            var endDt = DateParsingHelper.TryParseDate(ae.EndDate);
                            if (endDt != null && endDt.Value.Date >= DateFrom.Date && endDt.Value.Date <= DateTo.Date)
                                firmEnded++;
                        }

                        FirmDetails.Add(new FirmReportRow
                        {
                            FirmName = firmName,
                            TotalEmployees = firmTotal,
                            ActiveEmployees = firmActive,
                            ArchivedEmployees = firmEnded,
                            PassportOnlyCount = firmPassportOnly
                        });

                        totalEmp += firmTotal;
                        activeEmp += firmActive;
                        newInPeriod += firmNew;
                        endedInPeriod += firmEnded;

                        var company = App.CompanyService?.Companies
                            ?.FirstOrDefault(c => string.Equals(c.Name, firmName, StringComparison.OrdinalIgnoreCase));
                        if (company?.Agency != null && !string.IsNullOrWhiteSpace(company.Agency.Name))
                        {
                            var agName = company.Agency.Name.Trim();
                            if (agencyData.TryGetValue(agName, out var existing))
                                agencyData[agName] = (existing.firms + 1, existing.total + firmTotal, existing.active + firmActive);
                            else
                                agencyData[agName] = (1, firmTotal, firmActive);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ReportViewModel: error for {firmName}: {ex.Message}");
                    }
                }

                foreach (var kvp in agencyData.OrderBy(k => k.Key))
                {
                    AgencyDetails.Add(new AgencyReportRow
                    {
                        AgencyName = kvp.Key,
                        FirmCount = kvp.Value.firms,
                        TotalEmployees = kvp.Value.total,
                        ActiveEmployees = kvp.Value.active
                    });
                }

                int archivedPeriod = archiveLog.Count(l =>
                    l.Action == "Archived" && IsTimestampInRange(l.Timestamp)
                    && (effectiveFirmsSet.Contains(l.FirmName) || string.IsNullOrEmpty(l.FirmName)));

                int restoredPeriod = archiveLog.Count(l =>
                    l.Action == "Restored" && IsTimestampInRange(l.Timestamp));

                int totalActions = archiveLog.Count(l => IsTimestampInRange(l.Timestamp));

                TotalEmployees = totalEmp;
                ActiveEmployees = activeEmp;
                NewInPeriod = newInPeriod;
                EndedInPeriod = endedInPeriod;
                ArchivedInPeriod = archivedPeriod;
                RestoredInPeriod = restoredPeriod;
                TotalArchiveActions = totalActions;

                SummaryTotal = FirmDetails.Sum(f => f.TotalEmployees);
                SummaryActive = FirmDetails.Sum(f => f.ActiveEmployees);
                SummaryPassportOnly = FirmDetails.Sum(f => f.PassportOnlyCount);
                SummaryArchived = FirmDetails.Sum(f => f.ArchivedEmployees);

                var filteredLog = archiveLog
                    .Where(l => IsTimestampInRange(l.Timestamp))
                    .OrderByDescending(l => l.Timestamp)
                    .ToList();
                foreach (var entry in filteredLog)
                    ArchiveHistory.Add(entry);

                BuildArchiveChart(archiveLog);
                FilterEmployees();

                HasData = totalEmp > 0 || endedInPeriod > 0;
                StatusMessage = string.Format(GetString("ReportStatusFmt"), effectiveFirms.Count, totalEmp, newInPeriod, endedInPeriod);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private Dictionary<string, List<EmployeeReportRow>> LoadArchivedEmployeesForReport(HashSet<string> firmFilter)
        {
            var result = new Dictionary<string, List<EmployeeReportRow>>(StringComparer.OrdinalIgnoreCase);
            var financeService = App.FinanceService;
            if (financeService == null) return result;
            var alreadyAdded = new HashSet<string>();

            try
            {
                var archiveLog = _employeeService.LoadArchiveLog();

                var archivedEvents = archiveLog
                    .Where(l => l.Action == "Archived" && firmFilter.Contains(l.FirmName))
                    .ToList();

                foreach (var evt in archivedEvents)
                {
                    var endDate = DateParsingHelper.TryParseDate(evt.Date);
                    if (endDate != null && endDate.Value.Date < DateFrom.Date) continue;

                    var dedupKey = evt.EmployeeName + "|" + evt.FirmName + "|" + evt.Date;
                    if (alreadyAdded.Contains(dedupKey)) continue;
                    alreadyAdded.Add(dedupKey);

                    var folder = ResolveEmployeeFolderByName(evt.EmployeeName, financeService);
                    var row = BuildArchivedRowFromLog(evt, folder);
                    if (row == null) continue;

                    if (!result.ContainsKey(evt.FirmName))
                        result[evt.FirmName] = new List<EmployeeReportRow>();
                    result[evt.FirmName].Add(row);
                }

                var archivedList = _employeeService.GetArchivedEmployees();
                foreach (var arch in archivedList)
                {
                    if (string.IsNullOrWhiteSpace(arch.FirmName) || !firmFilter.Contains(arch.FirmName)) continue;

                    var dedupKey = arch.FullName + "|" + arch.FirmName + "|" + arch.EndDate;
                    if (alreadyAdded.Contains(dedupKey)) continue;

                    var endDate = DateParsingHelper.TryParseDate(arch.EndDate);
                    if (endDate != null && endDate.Value.Date < DateFrom.Date) continue;

                    var archStart = DateParsingHelper.TryParseDate(arch.StartDate);
                    if (archStart != null && archStart.Value.Date > DateTo.Date) continue;

                    alreadyAdded.Add(dedupKey);

                    var resolvedFolder = financeService.ResolveEmployeeFolder(arch.EmployeeFolder);

                    if (!result.ContainsKey(arch.FirmName))
                        result[arch.FirmName] = new List<EmployeeReportRow>();
                    result[arch.FirmName].Add(new EmployeeReportRow
                    {
                        FullName = arch.FullName,
                        FirmName = arch.FirmName,
                        EmployeeFolder = resolvedFolder,
                        EmployeeType = "—",
                        StartDate = arch.StartDate,
                        EndDate = arch.EndDate,
                        Position = arch.PositionTitle,
                        IsArchived = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ReportViewModel.LoadArchivedEmployeesForReport: {ex.Message}");
            }

            return result;
        }

        private string ResolveEmployeeFolderByName(string employeeName, FinanceService financeService)
        {
            var companies = App.CompanyService?.Companies;
            if (companies != null)
            {
            foreach (var company in companies)
            {
                var employees = _employeeService.GetEmployeesForFirm(company.Name);
                var match = employees.FirstOrDefault(e =>
                    string.Equals(e.FullName, employeeName, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.EmployeeFolder;
            }
            }

            var archived = _employeeService.GetArchivedEmployees();
            var archMatch = archived.FirstOrDefault(a =>
                string.Equals(a.FullName, employeeName, StringComparison.OrdinalIgnoreCase));
            if (archMatch != null)
                return financeService.ResolveEmployeeFolder(archMatch.EmployeeFolder);

            return string.Empty;
        }

        private EmployeeReportRow? BuildArchivedRowFromLog(ArchiveLogEntry evt, string employeeFolder)
        {
            try
            {
                var resolvedFolder = employeeFolder;
                if (!string.IsNullOrEmpty(resolvedFolder))
                {
                    var financeService = App.FinanceService;
                    resolvedFolder = financeService?.ResolveEmployeeFolder(resolvedFolder) ?? resolvedFolder;
                }

                var jsonPath = !string.IsNullOrEmpty(resolvedFolder) ? Path.Combine(resolvedFolder, "employee.json") : "";
                if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    var data = JsonSerializer.Deserialize<EmployeeData>(json);
                    if (data != null)
                    {
                        return new EmployeeReportRow
                        {
                            FullName = evt.EmployeeName,
                            FirmName = evt.FirmName,
                            EmployeeFolder = resolvedFolder,
                            EmployeeType = !string.IsNullOrEmpty(data.WorkPermitName) ? data.WorkPermitName : GetDocTypeDisplay(data.EmployeeType ?? "visa"),
                            PassportExpiry = data.PassportExpiry,
                            VisaExpiry = data.VisaExpiry,
                            InsuranceExpiry = data.InsuranceExpiry,
                            StartDate = data.StartDate,
                            EndDate = evt.Date,
                            Phone = data.Phone,
                            Position = data.PositionTag,
                            IsArchived = true
                        };
                    }
                }

                return new EmployeeReportRow
                {
                    FullName = evt.EmployeeName,
                    FirmName = evt.FirmName,
                    EmployeeFolder = resolvedFolder,
                    EmployeeType = "—",
                    EndDate = evt.Date,
                    IsArchived = true
                };
            }
            catch
            {
                return null;
            }
        }

        private void FilterEmployees()
        {
            EmployeeGroups.Clear();
            var search = EmployeeSearchText?.Trim() ?? "";

            var dateFiltered = _allEmployees.Where(e =>
            {
                if (!string.IsNullOrEmpty(e.EndDate))
                {
                    var ed = DateParsingHelper.TryParseDate(e.EndDate);
                    if (ed != null && ed.Value.Date < DateFrom.Date)
                        return false;
                }
                if (!string.IsNullOrEmpty(e.StartDate))
                {
                    var sd = DateParsingHelper.TryParseDate(e.StartDate);
                    if (sd != null && sd.Value.Date > DateTo.Date)
                        return false;
                }
                return true;
            }).ToList();

            var filtered = string.IsNullOrEmpty(search)
                ? dateFiltered
                : dateFiltered.Where(e =>
                    (e.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Phone?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Position?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.FirmName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

            var groups = filtered.GroupBy(e => e.FirmName).OrderBy(g => g.Key);
            foreach (var g in groups)
            {
                EmployeeGroups.Add(new FirmEmployeeGroup
                {
                    FirmName = g.Key,
                    EmployeeCount = g.Count(),
                    Employees = new ObservableCollection<EmployeeReportRow>(g)
                });
            }
        }

        private void OpenEmployee(EmployeeReportRow? row)
        {
            if (row == null || string.IsNullOrEmpty(row.EmployeeFolder)) return;
            var financeService = App.FinanceService;
            if (financeService == null) return;
            var resolvedFolder = financeService.ResolveEmployeeFolder(row.EmployeeFolder);
            CleanupDetailsVm();
            EmployeeDetailsVm = new EmployeeDetailsViewModel(row.FirmName, resolvedFolder, _employeeService);
            EmployeeDetailsVm.RequestClose += OnDetailsClose;
            EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
            IsEmployeeDetailsOpen = true;
        }

        private void CleanupDetailsVm()
        {
            if (EmployeeDetailsVm != null)
            {
                EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
            }
        }

        private void OnDetailsClose() => IsEmployeeDetailsOpen = false;
        private async void OnDetailsDataChanged() => await GenerateReportAsync();

        private bool IsTimestampInRange(string timestampStr)
        {
            if (string.IsNullOrWhiteSpace(timestampStr)) return true;
            if (DateTime.TryParse(timestampStr, out var ts))
            {
                return ts.Date >= DateFrom.Date && ts.Date <= DateTo.Date;
            }
            return true;
        }

        private List<(EmployeeSummary Summary, string EndDate)> FilterByDateRange(List<EmployeeSummary> employees)
        {
            var result = new List<(EmployeeSummary, string)>();
            foreach (var e in employees)
            {
                var startDate = DateParsingHelper.TryParseDate(e.StartDate);
                if (startDate != null && startDate.Value.Date > DateTo.Date)
                    continue;

                string endDate = "";
                var data = _employeeService.LoadEmployeeData(e.EmployeeFolder);
                if (data != null)
                    endDate = data.EndDate;

                var ed = DateParsingHelper.TryParseDate(endDate);
                if (ed != null && ed.Value.Date < DateFrom.Date)
                    continue;

                result.Add((e, endDate));
            }
            return result;
        }

        private void BuildArchiveChart(List<ArchiveLogEntry> archiveLog)
        {
            var monthlyData = new SortedDictionary<string, (int archived, int restored)>();

            var current = new DateTime(DateFrom.Year, DateFrom.Month, 1);
            var end = new DateTime(DateTo.Year, DateTo.Month, 1);
            while (current <= end)
            {
                monthlyData[current.ToString("yyyy-MM")] = (0, 0);
                current = current.AddMonths(1);
            }

            foreach (var entry in archiveLog)
            {
                if (!DateTime.TryParse(entry.Timestamp, out var ts)) continue;
                if (ts.Date < DateFrom.Date || ts.Date > DateTo.Date) continue;

                var key = ts.ToString("yyyy-MM");
                if (!monthlyData.ContainsKey(key))
                    monthlyData[key] = (0, 0);

                var val = monthlyData[key];
                if (entry.Action == "Archived")
                    monthlyData[key] = (val.archived + 1, val.restored);
                else if (entry.Action == "Restored")
                    monthlyData[key] = (val.archived, val.restored + 1);
            }

            var model = new PlotModel();
            model.PlotAreaBorderThickness = new OxyThickness(0);

            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                GapWidth = 0.3,
                TextColor = OxyColors.Gray,
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.None,
                FontSize = 11
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                MinimumPadding = 0,
                AbsoluteMinimum = 0,
                TextColor = OxyColors.Gray,
                TicklineColor = OxyColors.Transparent,
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(40, 128, 128, 128),
                FontSize = 11
            };

            var archivedSeries = new BarSeries
            {
                Title = GetString("ReportChartArchived"),
                FillColor = OxyColor.FromRgb(173, 20, 87),
                StrokeThickness = 0,
                BarWidth = 0.35
            };

            var restoredSeries = new BarSeries
            {
                Title = GetString("ReportChartRestored"),
                FillColor = OxyColor.FromRgb(46, 125, 50),
                StrokeThickness = 0,
                BarWidth = 0.35
            };

            foreach (var kvp in monthlyData)
            {
                var dt = DateTime.ParseExact(kvp.Key, "yyyy-MM", CultureInfo.InvariantCulture);
                categoryAxis.Labels.Add(dt.ToString("MMM yyyy", CultureInfo.CurrentUICulture));
                archivedSeries.Items.Add(new BarItem(kvp.Value.archived));
                restoredSeries.Items.Add(new BarItem(kvp.Value.restored));
            }

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);
            model.Series.Add(archivedSeries);
            model.Series.Add(restoredSeries);

            model.Legends.Add(new OxyPlot.Legends.Legend
            {
                LegendPosition = OxyPlot.Legends.LegendPosition.TopRight,
                LegendPlacement = OxyPlot.Legends.LegendPlacement.Inside,
                LegendTextColor = OxyColors.Gray,
                LegendFontSize = 11
            });

            ArchiveChartModel = model;
        }

        private bool IsSheetSelected(string key)
        {
            return ExportSheets.FirstOrDefault(s => s.SheetKey == key)?.IsSelected ?? true;
        }

        private void ExportToExcel()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Excel|*.xlsx",
                    FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };
                if (dialog.ShowDialog() != true) return;

                using var workbook = new XLWorkbook();

                if (IsSheetSelected("firms"))
                {
                    var ws1 = workbook.Worksheets.Add(DocString("ReportSheetSummary"));
                    ws1.Cell(1, 1).Value = DocString("ReportColFirm");
                    ws1.Cell(1, 2).Value = DocString("ReportColTotal");
                    ws1.Cell(1, 3).Value = DocString("ReportColActive");
                    ws1.Cell(1, 4).Value = DocString("ReportColNoPermit");
                    ws1.Cell(1, 5).Value = DocString("ReportColArchivedPeriod");

                    var headerRange1 = ws1.Range(1, 1, 1, 5);
                    headerRange1.Style.Font.Bold = true;
                    headerRange1.Style.Fill.BackgroundColor = XLColor.CornflowerBlue;
                    headerRange1.Style.Font.FontColor = XLColor.White;

                    int row = 2;
                    foreach (var firm in FirmDetails)
                    {
                        ws1.Cell(row, 1).Value = firm.FirmName;
                        ws1.Cell(row, 2).Value = firm.TotalEmployees;
                        ws1.Cell(row, 3).Value = firm.ActiveEmployees;
                        ws1.Cell(row, 4).Value = firm.PassportOnlyCount;
                        ws1.Cell(row, 5).Value = firm.ArchivedEmployees;
                        row++;
                    }

                    ws1.Cell(row, 1).Value = DocString("ReportTotal");
                    ws1.Cell(row, 1).Style.Font.Bold = true;
                    ws1.Cell(row, 2).Value = SummaryTotal;
                    ws1.Cell(row, 3).Value = SummaryActive;
                    ws1.Cell(row, 4).Value = SummaryPassportOnly;
                    ws1.Cell(row, 5).Value = SummaryArchived;
                    ws1.Range(row, 1, row, 5).Style.Font.Bold = true;
                    ws1.Range(row, 1, row, 5).Style.Fill.BackgroundColor = XLColor.LightGray;

                    row += 2;
                    ws1.Cell(row, 1).Value = DocString("ReportNewPeriodLabel");
                    ws1.Cell(row, 1).Style.Font.Bold = true;
                    ws1.Cell(row, 2).Value = NewInPeriod;
                    row++;
                    ws1.Cell(row, 1).Value = DocString("ReportDismissedPeriodLabel");
                    ws1.Cell(row, 1).Style.Font.Bold = true;
                    ws1.Cell(row, 2).Value = EndedInPeriod;
                    row++;
                    ws1.Cell(row, 1).Value = DocString("ReportRestoredPeriodLabel");
                    ws1.Cell(row, 1).Style.Font.Bold = true;
                    ws1.Cell(row, 2).Value = RestoredInPeriod;

                    ws1.Columns().AdjustToContents();
                }

                if (IsSheetSelected("agencies") && AgencyDetails.Count > 0)
                {
                    var wsAg = workbook.Worksheets.Add(DocString("ReportSheetAgencies"));
                    wsAg.Cell(1, 1).Value = DocString("ReportColAgencyName");
                    wsAg.Cell(1, 2).Value = DocString("ReportColFirmCountExcel");
                    wsAg.Cell(1, 3).Value = DocString("ReportColTotalEmp");
                    wsAg.Cell(1, 4).Value = DocString("ReportColActive");

                    var headerAg = wsAg.Range(1, 1, 1, 4);
                    headerAg.Style.Font.Bold = true;
                    headerAg.Style.Fill.BackgroundColor = XLColor.Amethyst;
                    headerAg.Style.Font.FontColor = XLColor.White;

                    int row = 2;
                    foreach (var ag in AgencyDetails)
                    {
                        wsAg.Cell(row, 1).Value = ag.AgencyName;
                        wsAg.Cell(row, 2).Value = ag.FirmCount;
                        wsAg.Cell(row, 3).Value = ag.TotalEmployees;
                        wsAg.Cell(row, 4).Value = ag.ActiveEmployees;
                        row++;
                    }
                    wsAg.Columns().AdjustToContents();
                }

                if (IsSheetSelected("employees") && _allEmployees.Count > 0)
                {
                    var wsE = workbook.Worksheets.Add(DocString("ReportSheetEmployees"));
                    const int colCount = 9;

                    int row = 1;
                    var groups = _allEmployees
                        .OrderBy(e => e.FirmName).ThenBy(e => e.IsArchived).ThenBy(e => e.FullName)
                        .GroupBy(e => e.FirmName);

                    foreach (var group in groups)
                    {
                        var firmCell = wsE.Cell(row, 1);
                        firmCell.Value = string.Format(DocString("ReportEmpCountFmt"), group.Key, group.Count());
                        wsE.Range(row, 1, row, colCount).Merge();
                        wsE.Range(row, 1, row, colCount).Style.Font.Bold = true;
                        wsE.Range(row, 1, row, colCount).Style.Font.FontSize = 12;
                        wsE.Range(row, 1, row, colCount).Style.Fill.BackgroundColor = XLColor.CornflowerBlue;
                        wsE.Range(row, 1, row, colCount).Style.Font.FontColor = XLColor.White;
                        wsE.Range(row, 1, row, colCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        row++;

                        wsE.Cell(row, 1).Value = DocString("ReportColName");
                        wsE.Cell(row, 2).Value = DocString("ReportColType");
                        wsE.Cell(row, 3).Value = DocString("ReportColPassportExpFull");
                        wsE.Cell(row, 4).Value = DocString("ReportColVisaExpFull");
                        wsE.Cell(row, 5).Value = DocString("ReportColInsExpFull");
                        wsE.Cell(row, 6).Value = DocString("ReportColStartDateFull");
                        wsE.Cell(row, 7).Value = DocString("ReportColEndDateFull");
                        wsE.Cell(row, 8).Value = DocString("ReportColPhone");
                        wsE.Cell(row, 9).Value = DocString("ReportColPosition");
                        var hdr = wsE.Range(row, 1, row, colCount);
                        hdr.Style.Font.Bold = true;
                        hdr.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
                        wsE.Range(row, 2, row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        row++;

                        foreach (var emp in group)
                        {
                            wsE.Cell(row, 1).Value = emp.FullName;
                            wsE.Cell(row, 2).Value = emp.EmployeeType;
                            wsE.Cell(row, 3).Value = emp.PassportExpiry;
                            wsE.Cell(row, 4).Value = emp.VisaExpiry;
                            wsE.Cell(row, 5).Value = emp.InsuranceExpiry;
                            wsE.Cell(row, 6).Value = emp.StartDate;
                            wsE.Cell(row, 7).Value = emp.EndDate;
                            wsE.Cell(row, 8).Value = emp.Phone;
                            wsE.Cell(row, 9).Value = emp.Position;

                            wsE.Range(row, 2, row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                            if (emp.IsArchived)
                            {
                                wsE.Range(row, 1, row, colCount).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 205, 210);
                                wsE.Range(row, 1, row, colCount).Style.Font.FontColor = XLColor.FromArgb(183, 28, 28);
                                wsE.Range(row, 1, row, colCount).Style.Font.Italic = true;
                            }
                            else
                            {
                                ColorExpiryCell(wsE.Cell(row, 3), emp.PassportExpiryStatus);
                                ColorExpiryCell(wsE.Cell(row, 4), emp.VisaExpiryStatus);
                                ColorExpiryCell(wsE.Cell(row, 5), emp.InsuranceExpiryStatus);
                            }

                            row++;
                        }

                        row++;
                    }
                    wsE.Columns().AdjustToContents();
                }

                if (IsSheetSelected("archive") && ArchiveHistory.Count > 0)
                {
                    var wsA = workbook.Worksheets.Add(DocString("ReportSheetArchive"));
                    wsA.Cell(1, 1).Value = DocString("ReportColEmployee");
                    wsA.Cell(1, 2).Value = DocString("ReportColFirm");
                    wsA.Cell(1, 3).Value = DocString("ReportColAction");
                    wsA.Cell(1, 4).Value = DocString("ReportColDate");
                    wsA.Cell(1, 5).Value = DocString("ReportColTimestamp");

                    var headerA = wsA.Range(1, 1, 1, 5);
                    headerA.Style.Font.Bold = true;
                    headerA.Style.Fill.BackgroundColor = XLColor.Amethyst;
                    headerA.Style.Font.FontColor = XLColor.White;

                    int row = 2;
                    foreach (var entry in ArchiveHistory)
                    {
                        wsA.Cell(row, 1).Value = entry.EmployeeName;
                        wsA.Cell(row, 2).Value = entry.FirmName;
                        wsA.Cell(row, 3).Value = entry.Action == "Archived" ? DocString("ReportActionArchived") : DocString("ReportActionRestored");
                        wsA.Cell(row, 4).Value = entry.Date;
                        wsA.Cell(row, 5).Value = entry.Timestamp;

                        if (entry.Action == "Archived")
                            wsA.Range(row, 1, row, 5).Style.Fill.BackgroundColor = XLColor.MistyRose;
                        else
                            wsA.Range(row, 1, row, 5).Style.Fill.BackgroundColor = XLColor.Honeydew;

                        row++;
                    }
                    wsA.Columns().AdjustToContents();
                }

                if (workbook.Worksheets.Count == 0)
                {
                    MessageBox.Show(GetString("ReportNoSheets"), GetString("ReportNoSheetsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                workbook.SaveAs(dialog.FileName);
                StatusMessage = GetString("ReportExported");
                App.ActivityLogService?.Log("ExportExcel", "Export", "", "",
                    $"Експортовано звіт → Excel");

                try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch (Exception ex2) { LoggingService.LogWarning("ReportViewModel.ExportExcel", $"Open file failed: {ex2.Message}"); }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(GetString("ReportExportError"), ex.Message), GetString("ReportExportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToPdf()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "PDF|*.pdf",
                    FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
                };
                if (dialog.ShowDialog() != true) return;

                var doc = new PdfDocument();
                doc.Info.Title = "Report";

                const double marginLeft = 20;
                const double marginTop = 40;
                const double marginBottom = 40;
                const double rowHeight = 18;

                var fontTitle = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
                var fontSubtitle = new XFont("Segoe UI", 10);
                var fontHeader = new XFont("Segoe UI", 8, XFontStyleEx.Bold);
                var fontCell = new XFont("Segoe UI", 8);
                var fontCellItalic = new XFont("Segoe UI", 8, XFontStyleEx.Italic);
                var fontFirmTitle = new XFont("Segoe UI", 10, XFontStyleEx.Bold);

                var accentBrush = new XSolidBrush(XColor.FromArgb(100, 149, 237));
                var headerBg = new XSolidBrush(XColor.FromArgb(100, 149, 237));
                var subHeaderBg = new XSolidBrush(XColor.FromArgb(198, 219, 239));
                var grayBrush = new XSolidBrush(XColor.FromArgb(150, 150, 150));
                var redBrush = new XSolidBrush(XColor.FromArgb(211, 47, 47));
                var orangeBrush = new XSolidBrush(XColor.FromArgb(245, 127, 23));
                var greenBrush = new XSolidBrush(XColor.FromArgb(46, 125, 50));

                PdfPage? page = null;
                XGraphics? gfx = null;
                double y = 0;
                double pageW = 0;

                PdfPage AddPage(bool landscape = true)
                {
                    var p = doc.AddPage();
                    p.Size = PdfSharp.PageSize.A4;
                    if (landscape) p.Orientation = PdfSharp.PageOrientation.Landscape;
                    gfx?.Dispose();
                    gfx = XGraphics.FromPdfPage(p);
                    y = marginTop;
                    pageW = p.Width.Point;
                    return p;
                }

                bool EnsureSpace(double needed)
                {
                    if (page == null || y + needed > (page.Height.Point - marginBottom))
                    {
                        page = AddPage();
                        return true;
                    }
                    return false;
                }

                void DrawRow(double x, double[] colWidths, string[] texts, XFont font, XBrush? bg, XBrush? fg = null, XStringFormat[]? fmts = null)
                {
                    var useFg = fg ?? XBrushes.Black;
                    if (bg != null)
                        gfx!.DrawRectangle(bg, x, y, colWidths.Sum(), rowHeight);

                    double cx = x;
                    for (int i = 0; i < texts.Length && i < colWidths.Length; i++)
                    {
                        var fmt = (fmts != null && i < fmts.Length) ? fmts[i] : XStringFormats.CenterLeft;
                        gfx!.DrawString(texts[i] ?? "", font, useFg,
                            new XRect(cx + 4, y, colWidths[i] - 8, rowHeight), fmt);
                        cx += colWidths[i];
                    }
                    y += rowHeight;
                }

                XBrush GetExpiryBrush(string status) => status switch
                {
                    "expired" => redBrush,
                    "warning" => orangeBrush,
                    "ok" => greenBrush,
                    _ => XBrushes.Black
                };

                page = AddPage();

                gfx!.DrawString(DocString("ReportPdfTitle"), fontTitle, XBrushes.Black, new XRect(marginLeft, y, 300, 24), XStringFormats.CenterLeft);
                y += 28;
                gfx.DrawString(string.Format(DocString("ReportPdfPeriod"), DateFrom.ToString("dd.MM.yyyy"), DateTo.ToString("dd.MM.yyyy")), fontSubtitle, grayBrush,
                    new XRect(marginLeft, y, 400, 16), XStringFormats.CenterLeft);
                y += 14;
                gfx.DrawString(string.Format(DocString("ReportPdfStatsFmt"), TotalEmployees, ActiveEmployees, NewInPeriod, EndedInPeriod),
                    fontSubtitle, XBrushes.Black, new XRect(marginLeft, y, 600, 16), XStringFormats.CenterLeft);
                y += 24;

                if (IsSheetSelected("firms") && FirmDetails.Count > 0)
                {
                    double contentW = pageW - marginLeft * 2;
                    double[] firmCols = { contentW * 0.4, contentW * 0.15, contentW * 0.15, contentW * 0.15, contentW * 0.15 };
                    XStringFormat[] firmFmts = {
                        XStringFormats.CenterLeft, XStringFormats.Center,
                        XStringFormats.Center, XStringFormats.Center, XStringFormats.Center
                    };

                    string[] firmHeaders = { DocString("ReportColFirm"), DocString("ReportColTotal"), DocString("ReportColActive"), DocString("ReportColNoPermit"), DocString("ReportColArchived") };

                    EnsureSpace(rowHeight * 2);
                    DrawRow(marginLeft, firmCols, firmHeaders, fontHeader, headerBg, XBrushes.White, firmFmts);

                    foreach (var f in FirmDetails)
                    {
                        if (EnsureSpace(rowHeight))
                            DrawRow(marginLeft, firmCols, firmHeaders, fontHeader, headerBg, XBrushes.White, firmFmts);
                        DrawRow(marginLeft, firmCols, new[] { f.FirmName, f.TotalEmployees.ToString(), f.ActiveEmployees.ToString(),
                            f.PassportOnlyCount.ToString(), f.ArchivedEmployees.ToString() }, fontCell, null, fmts: firmFmts);
                    }

                    if (EnsureSpace(rowHeight))
                        DrawRow(marginLeft, firmCols, firmHeaders, fontHeader, headerBg, XBrushes.White, firmFmts);
                    DrawRow(marginLeft, firmCols, new[] { DocString("ReportTotal"), SummaryTotal.ToString(), SummaryActive.ToString(),
                        SummaryPassportOnly.ToString(), SummaryArchived.ToString() }, fontHeader, subHeaderBg, fmts: firmFmts);
                    y += 12;
                }

                if (IsSheetSelected("agencies") && AgencyDetails.Count > 0)
                {
                    double contentW = pageW - marginLeft * 2;
                    double[] agCols = { contentW * 0.4, contentW * 0.2, contentW * 0.2, contentW * 0.2 };

                    string[] agHeaders = { DocString("ReportColAgencyName"), DocString("ReportColFirmCount"), DocString("ReportColTotal"), DocString("ReportColActive") };
                    var agHeaderBg = new XSolidBrush(XColor.FromArgb(123, 31, 162));

                    EnsureSpace(rowHeight * 2);
                    gfx!.DrawString(DocString("ReportPdfAgencySection"), fontFirmTitle, XBrushes.Black,
                        new XRect(marginLeft, y, 400, 18), XStringFormats.CenterLeft);
                    y += 22;

                    DrawRow(marginLeft, agCols, agHeaders, fontHeader, agHeaderBg, XBrushes.White);

                    foreach (var a in AgencyDetails)
                    {
                        if (EnsureSpace(rowHeight))
                            DrawRow(marginLeft, agCols, agHeaders, fontHeader, agHeaderBg, XBrushes.White);
                        DrawRow(marginLeft, agCols, new[] { a.AgencyName, a.FirmCount.ToString(),
                            a.TotalEmployees.ToString(), a.ActiveEmployees.ToString() }, fontCell, null);
                    }
                    y += 12;
                }

                if (IsSheetSelected("employees") && _allEmployees.Count > 0)
                {
                    var groups = _allEmployees
                        .OrderBy(e => e.FirmName).ThenBy(e => e.IsArchived).ThenBy(e => e.FullName)
                        .GroupBy(e => e.FirmName);

                    double contentW = pageW - marginLeft * 2;
                    double[] empCols = { contentW * 0.15, contentW * 0.10, contentW * 0.10, contentW * 0.10,
                        contentW * 0.10, contentW * 0.07, contentW * 0.07, contentW * 0.31 };
                    string[] empHeaders = { DocString("ReportColName"), DocString("ReportColType"), DocString("ReportColPassportExp"), DocString("ReportColVisaExp"), DocString("ReportColInsExp"), DocString("ReportColStartDate"), DocString("ReportColEndDate"), DocString("ReportColPosition") };

                    // i=0 Jméno → left, i=1 Typ → center, i=2-6 dates → center, i=7 Pozice → left
                    XStringFormat[] empFmts = {
                        XStringFormats.CenterLeft, XStringFormats.Center, XStringFormats.Center,
                        XStringFormats.Center, XStringFormats.Center, XStringFormats.Center,
                        XStringFormats.Center, XStringFormats.CenterLeft
                    };

                    foreach (var group in groups)
                    {
                        var groupTitle = string.Format(DocString("ReportEmpCountFmt"), group.Key, group.Count());

                        void DrawGroupHeader()
                        {
                            gfx!.DrawRectangle(accentBrush, marginLeft, y, contentW, rowHeight + 2);
                            gfx.DrawString(groupTitle, fontFirmTitle, XBrushes.White,
                                new XRect(marginLeft + 6, y, contentW - 12, rowHeight + 2), XStringFormats.Center);
                            y += rowHeight + 2;
                            DrawRow(marginLeft, empCols, empHeaders, fontHeader, subHeaderBg, fmts: empFmts);
                        }

                        EnsureSpace(rowHeight * 3);
                        DrawGroupHeader();

                        foreach (var emp in group)
                        {
                            if (EnsureSpace(rowHeight))
                                DrawGroupHeader();

                            var usedFont = emp.IsArchived ? fontCellItalic : fontCell;
                            XBrush baseFg;

                            if (emp.IsArchived)
                            {
                                gfx!.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 205, 210)), marginLeft, y, contentW, rowHeight);
                                baseFg = new XSolidBrush(XColor.FromArgb(183, 28, 28));
                            }
                            else
                            {
                                baseFg = XBrushes.Black;
                            }

                            double cx = marginLeft;
                            string[] vals = { emp.FullName, emp.EmployeeType, emp.PassportExpiry, emp.VisaExpiry,
                                emp.InsuranceExpiry, emp.StartDate, emp.EndDate, emp.Position };

                            for (int i = 0; i < vals.Length && i < empCols.Length; i++)
                            {
                                XBrush cellFg = baseFg;
                                if (!emp.IsArchived)
                                {
                                    if (i == 2) cellFg = GetExpiryBrush(emp.PassportExpiryStatus);
                                    else if (i == 3) cellFg = GetExpiryBrush(emp.VisaExpiryStatus);
                                    else if (i == 4) cellFg = GetExpiryBrush(emp.InsuranceExpiryStatus);
                                }
                                gfx!.DrawString(vals[i] ?? "", usedFont, cellFg,
                                    new XRect(cx + 4, y, empCols[i] - 8, rowHeight), empFmts[i]);
                                cx += empCols[i];
                            }
                            y += rowHeight;
                        }
                        y += 8;
                    }
                }

                if (IsSheetSelected("archive") && ArchiveHistory.Count > 0)
                {
                    double contentW = pageW - marginLeft * 2;
                    double[] archCols = { contentW * 0.3, contentW * 0.25, contentW * 0.15, contentW * 0.15, contentW * 0.15 };

                    string[] archHeaders = { DocString("ReportColEmployee"), DocString("ReportColFirm"), DocString("ReportColAction"), DocString("ReportColDate"), DocString("ReportColTimestamp") };
                    var archHeaderBg = new XSolidBrush(XColor.FromArgb(123, 31, 162));

                    EnsureSpace(rowHeight * 3);
                    gfx!.DrawString(DocString("ReportSheetArchive"), fontFirmTitle, XBrushes.Black,
                        new XRect(marginLeft, y, 400, 18), XStringFormats.CenterLeft);
                    y += 22;

                    DrawRow(marginLeft, archCols, archHeaders, fontHeader, archHeaderBg, XBrushes.White);

                    foreach (var entry in ArchiveHistory)
                    {
                        if (EnsureSpace(rowHeight))
                            DrawRow(marginLeft, archCols, archHeaders, fontHeader, archHeaderBg, XBrushes.White);
                        var action = entry.Action == "Archived" ? DocString("ReportActionArchived") : DocString("ReportActionRestored");
                        var bg = entry.Action == "Archived"
                            ? new XSolidBrush(XColor.FromArgb(255, 228, 225))
                            : new XSolidBrush(XColor.FromArgb(240, 255, 240));
                        DrawRow(marginLeft, archCols, new[] { entry.EmployeeName, entry.FirmName, action, entry.Date, entry.Timestamp },
                            fontCell, bg);
                    }
                }

                gfx?.Dispose();
                doc.Save(dialog.FileName);
                StatusMessage = GetString("ReportExportedPdf");
                App.ActivityLogService?.Log("ExportPdf", "Export", "", "",
                    $"Експортовано звіт → PDF");

                try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch (Exception ex2) { LoggingService.LogWarning("ReportViewModel.ExportPdf", $"Open file failed: {ex2.Message}"); }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(GetString("ReportExportError"), ex.Message), GetString("ReportExportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ColorExpiryCell(IXLCell cell, string status)
        {
            switch (status)
            {
                case "expired":
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.Red;
                    break;
                case "warning":
                    cell.Style.Fill.BackgroundColor = XLColor.LightYellow;
                    cell.Style.Font.FontColor = XLColor.DarkGoldenrod;
                    break;
                case "ok":
                    cell.Style.Font.FontColor = XLColor.DarkGreen;
                    break;
            }
        }
    }
}
