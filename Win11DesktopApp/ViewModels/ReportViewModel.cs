using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using OxyPlot;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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
        private readonly NavigationService _navigationService;
        private readonly EmployeeService _employeeService;
        private readonly AppSettingsService _appSettingsService;
        private readonly CompanyService _companyService;
        private readonly FinanceService _financeService;
        private readonly ActivityLogService _activityLogService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly ReportColumnLayoutService _reportColumnLayoutService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private CancellationTokenSource? _refreshCts;
        private CancellationTokenSource? _searchCts;
        private List<EmployeeReportRow>? _dateFilteredCache;
        private static readonly List<AppSettingsService.ReportColumnSetting> DefaultEmployeeColumns = new()
        {
            new() { Key = "name", IsVisible = true, DisplayIndex = 0, Width = 200 },
            new() { Key = "type", IsVisible = true, DisplayIndex = 1, Width = 180 },
            new() { Key = "documentType", IsVisible = false, DisplayIndex = 2, Width = 130 },
            new() { Key = "passportNumber", IsVisible = false, DisplayIndex = 3, Width = 170 },
            new() { Key = "workAddress", IsVisible = false, DisplayIndex = 4, Width = 220 },
            new() { Key = "highestEducation", IsVisible = false, DisplayIndex = 5, Width = 220 },
            new() { Key = "birthDate", IsVisible = false, DisplayIndex = 6, Width = 100 },
            new() { Key = "gender", IsVisible = false, DisplayIndex = 7, Width = 90 },
            new() { Key = "addressCz", IsVisible = false, DisplayIndex = 8, Width = 220 },
            new() { Key = "addressAbroad", IsVisible = false, DisplayIndex = 9, Width = 220 },
            new() { Key = "passportIssuedBy", IsVisible = false, DisplayIndex = 10, Width = 180 },
            new() { Key = "positionCode", IsVisible = false, DisplayIndex = 11, Width = 110 },
            new() { Key = "agency", IsVisible = false, DisplayIndex = 12, Width = 150 },
            new() { Key = "passportExpiry", IsVisible = true, DisplayIndex = 13, Width = 100 },
            new() { Key = "visaExpiry", IsVisible = true, DisplayIndex = 14, Width = 100 },
            new() { Key = "insuranceExpiry", IsVisible = true, DisplayIndex = 15, Width = 100 },
            new() { Key = "startDate", IsVisible = true, DisplayIndex = 16, Width = 90 },
            new() { Key = "endDate", IsVisible = true, DisplayIndex = 17, Width = 90 },
            new() { Key = "phone", IsVisible = true, DisplayIndex = 18, Width = 110 },
            new() { Key = "bankAccount", IsVisible = false, DisplayIndex = 19, Width = 150 },
            new() { Key = "bankName", IsVisible = false, DisplayIndex = 20, Width = 150 },
            new() { Key = "position", IsVisible = true, DisplayIndex = 21, Width = 110 },
        };

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
        internal ReportColumnLayoutService ReportColumnLayoutService => _reportColumnLayoutService;

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
                    _appSettingsService.Settings.ReportDateFrom = value.ToString("yyyy-MM-dd");
                    _appSettingsService.SaveSettings();

                    _ = RefreshReportAsync(reloadFilters: true);
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
                    _appSettingsService.Settings.ReportDateTo = value.ToString("yyyy-MM-dd");
                    _appSettingsService.SaveSettings();

                    _ = RefreshReportAsync(reloadFilters: true);
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
        private ObservableCollection<FirmReportRow> _firmDetails = new();
        public ObservableCollection<FirmReportRow> FirmDetails
        {
            get => _firmDetails;
            set => SetProperty(ref _firmDetails, value);
        }

        private ObservableCollection<AgencyReportRow> _agencyDetails = new();
        public ObservableCollection<AgencyReportRow> AgencyDetails
        {
            get => _agencyDetails;
            set => SetProperty(ref _agencyDetails, value);
        }

        private ObservableCollection<ArchiveLogEntry> _archiveHistory = new();
        public ObservableCollection<ArchiveLogEntry> ArchiveHistory
        {
            get => _archiveHistory;
            set => SetProperty(ref _archiveHistory, value);
        }

        // ===== Employee list =====
        private ObservableCollection<FirmEmployeeGroup> _employeeGroups = new();
        public ObservableCollection<FirmEmployeeGroup> EmployeeGroups
        {
            get => _employeeGroups;
            set => SetProperty(ref _employeeGroups, value);
        }
        private List<EmployeeReportRow> _allEmployees = new();

        private string _employeeSearchText = string.Empty;
        public string EmployeeSearchText
        {
            get => _employeeSearchText;
            set { if (SetProperty(ref _employeeSearchText, value)) FilterEmployeesDebounced(); }
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

        public ReportViewModel(
            NavigationService? navigationService = null,
            EmployeeService? employeeService = null,
            AppSettingsService? appSettingsService = null,
            CompanyService? companyService = null,
            FinanceService? financeService = null,
            ActivityLogService? activityLogService = null,
            DocumentLocalizationService? documentLocalizationService = null,
            ReportColumnLayoutService? reportColumnLayoutService = null,
            EmployeeDetailsViewModelFactory? employeeDetailsViewModelFactory = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _financeService = financeService ?? throw new InvalidOperationException("FinanceService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");
            _reportColumnLayoutService = reportColumnLayoutService ?? throw new InvalidOperationException("ReportColumnLayoutService is not initialized.");
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new InvalidOperationException("EmployeeDetailsViewModelFactory is not initialized.");

            var s = _appSettingsService.Settings;
            _dateFrom = DateTime.TryParse(s.ReportDateFrom, out var df) ? df : DateTime.Today.AddMonths(-1);
            _dateTo = DateTime.TryParse(s.ReportDateTo, out var dt) ? dt : DateTime.Today;

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());
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
            _ = RefreshReportAsync(reloadFilters: true);
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
            if (!PolicyService.EnsureExportsAllowed("відкрити експорт звіту"))
                return;

            InitExportSheets();
            IsExportDialogOpen = true;
        }

        private static string GetString(string key)
        {
            return Application.Current.Resources[key] as string ?? key;
        }

        private string DocString(string key) =>
            _documentLocalizationService.Get(key) ?? GetString(key);

        private static AppSettingsService.ReportColumnSetting CopyColumnSetting(AppSettingsService.ReportColumnSetting source)
        {
            return new AppSettingsService.ReportColumnSetting
            {
                Key = source.Key,
                IsVisible = source.IsVisible,
                DisplayIndex = source.DisplayIndex,
                Width = source.Width
            };
        }

        public static List<AppSettingsService.ReportColumnSetting> NormalizeEmployeeColumnLayout(
            IEnumerable<AppSettingsService.ReportColumnSetting> layout)
        {
            var normalized = layout
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .Select(CopyColumnSetting)
                .ToList();

            foreach (var col in normalized)
            {
                col.Width = Math.Max(40, col.Width);
                col.DisplayIndex = Math.Max(0, col.DisplayIndex);
                if (string.Equals(col.Key, "name", StringComparison.OrdinalIgnoreCase))
                    col.IsVisible = true;
            }

            var ordered = normalized
                .OrderBy(c => c.DisplayIndex)
                .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
                ordered[i].DisplayIndex = i;

            return ordered;
        }

        public static List<AppSettingsService.ReportColumnSetting> MergeEmployeeColumnsWithDefaults(
            List<AppSettingsService.ReportColumnSetting>? saved)
        {
            var result = new List<AppSettingsService.ReportColumnSetting>();
            var savedByKey = (saved ?? new List<AppSettingsService.ReportColumnSetting>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var def in DefaultEmployeeColumns)
            {
                if (savedByKey.TryGetValue(def.Key, out var existing))
                {
                    result.Add(new AppSettingsService.ReportColumnSetting
                    {
                        Key = def.Key,
                        IsVisible = string.Equals(def.Key, "name", StringComparison.OrdinalIgnoreCase) || existing.IsVisible,
                        DisplayIndex = existing.DisplayIndex,
                        Width = existing.Width
                    });
                }
                else
                {
                    var copy = CopyColumnSetting(def);
                    copy.DisplayIndex = result.Count + 100;
                    result.Add(copy);
                }
            }

            return NormalizeEmployeeColumnLayout(result);
        }

        public List<AppSettingsService.ReportColumnSetting> GetEffectiveEmployeeColumns()
        {
            var saved = _appSettingsService.Settings.EmployeeReportColumns;
            return MergeEmployeeColumnsWithDefaults(saved);
        }

        public void SaveEmployeeColumnLayout(IEnumerable<AppSettingsService.ReportColumnSetting> layout)
        {
            _appSettingsService.Settings.EmployeeReportColumns = NormalizeEmployeeColumnLayout(layout);
            _appSettingsService.SaveSettings();
        }

        public List<AppSettingsService.ReportColumnSetting> ResetEmployeeColumnsToDefaults()
        {
            var reset = NormalizeEmployeeColumnLayout(DefaultEmployeeColumns.Select(CopyColumnSetting));
            SaveEmployeeColumnLayout(reset);
            return reset;
        }

        public static string GetEmployeeColumnHeaderResourceKey(string key) => key switch
        {
            "name" => "ReportColName",
            "type" => "ReportColType",
            "documentType" => "ReportColDocumentType",
            "passportNumber" => "ReportColPassportNumber",
            "workAddress" => "ReportColWorkAddress",
            "highestEducation" => "ReportColHighestEducation",
            "addressCz" => "ReportColAddressCz",
            "addressAbroad" => "ReportColAddressAbroad",
            "birthDate" => "ReportColBirthDate",
            "gender" => "ReportColGender",
            "passportIssuedBy" => "ReportColPassportIssuedBy",
            "positionCode" => "ReportColPositionCode",
            "agency" => "ReportColAgency",
            "passportExpiry" => "ReportColPassportExpFull",
            "visaExpiry" => "ReportColVisaExpFull",
            "insuranceExpiry" => "ReportColInsExpFull",
            "startDate" => "ReportColStartDateFull",
            "endDate" => "ReportColEndDateFull",
            "phone" => "ReportColPhone",
            "bankAccount" => "EmployeeBankAccountNumber",
            "bankName" => "EmployeeBankName",
            "position" => "ReportColPosition",
            _ => key
        };

        private List<AppSettingsService.ReportColumnSetting> GetVisibleEmployeeColumnsForExport()
        {
            return _reportColumnLayoutService.GetEffectiveEmployeeColumns()
                .Where(c => c.IsVisible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();
        }

        private static string GetEmployeeColumnValue(EmployeeReportRow employee, string key) => key switch
        {
            "name" => employee.FullName,
            "type" => employee.EmployeeType,
            "documentType" => employee.DocumentType,
            "passportNumber" => employee.PassportNumber,
            "workAddress" => employee.WorkAddress,
            "highestEducation" => employee.HighestEducation,
            "addressCz" => employee.AddressCz,
            "addressAbroad" => employee.AddressAbroad,
            "birthDate" => employee.BirthDate,
            "gender" => employee.Gender,
            "passportIssuedBy" => employee.PassportIssuedBy,
            "positionCode" => employee.PositionCode,
            "agency" => employee.Agency,
            "passportExpiry" => employee.PassportExpiry,
            "visaExpiry" => employee.VisaExpiry,
            "insuranceExpiry" => employee.InsuranceExpiry,
            "startDate" => employee.StartDate,
            "endDate" => employee.EndDate,
            "phone" => employee.Phone,
            "bankAccount" => employee.BankAccountNumber,
            "bankName" => employee.BankName,
            "position" => employee.Position,
            _ => string.Empty
        };

        private static string? GetEmployeeColumnStatus(EmployeeReportRow employee, string key) => key switch
        {
            "passportExpiry" => employee.PassportExpiryStatus,
            "visaExpiry" => employee.VisaExpiryStatus,
            "insuranceExpiry" => employee.InsuranceExpiryStatus,
            _ => null
        };

        private static XLAlignmentHorizontalValues GetEmployeeExcelAlignment(string key) => key switch
        {
            "name" => XLAlignmentHorizontalValues.Left,
            "addressCz" => XLAlignmentHorizontalValues.Left,
            "addressAbroad" => XLAlignmentHorizontalValues.Left,
            "bankAccount" => XLAlignmentHorizontalValues.Left,
            "bankName" => XLAlignmentHorizontalValues.Left,
            "position" => XLAlignmentHorizontalValues.Left,
            _ => XLAlignmentHorizontalValues.Center
        };

        private static XStringFormat GetEmployeePdfFormat(string key) => key switch
        {
            "name" => XStringFormats.CenterLeft,
            "addressCz" => XStringFormats.CenterLeft,
            "addressAbroad" => XStringFormats.CenterLeft,
            "bankAccount" => XStringFormats.CenterLeft,
            "bankName" => XStringFormats.CenterLeft,
            "position" => XStringFormats.CenterLeft,
            _ => XStringFormats.Center
        };

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

        private string GetGenderDisplay(string gender)
        {
            var key = string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase)
                ? "GenderFemale"
                : "GenderMale";
            return DocString(key);
        }

        private static string FormatAddress(EmployeeAddress? address)
        {
            if (address == null)
                return string.Empty;

            return string.Join(", ",
                new[]
                {
                    $"{address.Street} {address.Number}".Trim(),
                    address.City?.Trim(),
                    address.Zip?.Trim()
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private string GetDocTypeDisplay(string type)
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

        private static string GetDocTypeDisplay(string type, IReadOnlyDictionary<string, string> typeDisplayMap)
        {
            if (typeDisplayMap.TryGetValue(type ?? string.Empty, out var display))
                return display;

            return type ?? string.Empty;
        }

        private async Task LoadFiltersAsync(CancellationToken token)
        {
            var selectedCompanies = CompanyFilters.ToDictionary(f => f.CompanyName, f => f.IsChecked, StringComparer.OrdinalIgnoreCase);
            var selectedAgencies = AgencyFilters.ToDictionary(f => f.CompanyName, f => f.IsChecked, StringComparer.OrdinalIgnoreCase);
            var companies = _companyService.Companies.ToList();
            var companyService = _companyService;
            var dateFrom = DateFrom;
            var dateTo = DateTo;

            var result = await Task.Run(() =>
                BuildFilterLoadResult(selectedCompanies, selectedAgencies, companies, companyService, dateFrom, dateTo, token), token);

            token.ThrowIfCancellationRequested();

            CompanyFilters.Clear();
            foreach (var filter in result.CompanyFilters)
            {
                CompanyFilters.Add(new CompanyFilter
                {
                    CompanyName = filter.Name,
                    IsChecked = filter.IsChecked
                });
            }

            AgencyFilters.Clear();
            foreach (var filter in result.AgencyFilters)
            {
                AgencyFilters.Add(new CompanyFilter
                {
                    CompanyName = filter.Name,
                    IsChecked = filter.IsChecked
                });
            }

            _allCompaniesSelected = result.AllCompaniesSelected;
            _allAgenciesSelected = result.AllAgenciesSelected;
            OnPropertyChanged(nameof(AllCompaniesSelected));
            OnPropertyChanged(nameof(AllAgenciesSelected));
        }

        private async Task GenerateReportAsync()
        {
            await RefreshReportAsync(reloadFilters: false);
        }

        private async Task RefreshReportAsync(bool reloadFilters)
        {
            var cts = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _refreshCts, cts);
            previous?.Cancel();
            previous?.Dispose();

            IsLoading = true;

            try
            {
                await Task.Delay(50, cts.Token);

                if (reloadFilters)
                    await LoadFiltersAsync(cts.Token);

                var companiesSnapshot = _companyService.Companies.ToList();
                var filterSnapshot = CreateFilterSelectionSnapshot();
                var dateFrom = DateFrom;
                var dateTo = DateTo;
                var typeDisplayMap = CreateDocTypeDisplayMap();

                var result = await Task.Run(() =>
                    BuildReportResult(companiesSnapshot, filterSnapshot, dateFrom, dateTo, typeDisplayMap, cts.Token), cts.Token);

                cts.Token.ThrowIfCancellationRequested();
                ApplyReportResult(result, dateFrom, dateTo);
            }
            catch (OperationCanceledException)
            {
                // A newer refresh superseded this one.
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ReportViewModel.GenerateReportAsync", ex);
                StatusMessage = ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_refreshCts, cts))
                {
                    _refreshCts = null;
                    IsLoading = false;
                }

                cts.Dispose();
            }
        }

        private FilterSelectionSnapshot CreateFilterSelectionSnapshot()
        {
            return new FilterSelectionSnapshot(
                CompanyFilters.Where(f => f.IsChecked).Select(f => f.CompanyName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                AgencyFilters.Where(f => f.IsChecked).Select(f => f.CompanyName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                AgencyFilters.Count > 0);
        }

        private Dictionary<string, string> CreateDocTypeDisplayMap()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["visa"] = GetDocTypeDisplay("visa"),
                ["eu_citizen"] = GetDocTypeDisplay("eu_citizen"),
                ["work_permit"] = GetDocTypeDisplay("work_permit"),
                ["passport_only"] = GetDocTypeDisplay("passport_only")
            };
        }

        private FilterLoadResult BuildFilterLoadResult(
            Dictionary<string, bool> selectedCompanies,
            Dictionary<string, bool> selectedAgencies,
            List<EmployerCompany> companies,
            CompanyService? companyService,
            DateTime dateFrom,
            DateTime dateTo,
            CancellationToken token)
        {
            var companyFilters = new List<FilterItemState>();
            var agencyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var archiveLog = TryLoadArchiveLog();
            var archivedEmployees = _employeeService.GetArchivedEmployees();
            var activeFirmHistory = _employeeService.GetActiveEmployeeFirmHistory();
            var employeesCache = new Dictionary<string, List<EmployeeSummary>>(StringComparer.OrdinalIgnoreCase);

            List<EmployeeSummary> GetEmployeesCached(string firmName)
            {
                token.ThrowIfCancellationRequested();

                if (!employeesCache.TryGetValue(firmName, out var employees))
                {
                    employees = _employeeService.GetEmployeesForFirm(firmName);
                    employeesCache[firmName] = employees;
                }

                return employees;
            }

            foreach (var company in companies)
            {
                token.ThrowIfCancellationRequested();

                if (companyService != null && !companyService.IsCompanyVisibleForRange(company, dateFrom, dateTo))
                    continue;

                if (!HasFirmDataInRange(company.Name, archiveLog, archivedEmployees, activeFirmHistory, GetEmployeesCached, dateFrom, dateTo))
                    continue;

                companyFilters.Add(new FilterItemState(
                    company.Name,
                    selectedCompanies.TryGetValue(company.Name, out var isChecked) ? isChecked : true));

                if (company.Agency != null && !string.IsNullOrWhiteSpace(company.Agency.Name))
                    agencyNames.Add(company.Agency.Name.Trim());
            }

            var agencyFilters = agencyNames
                .OrderBy(n => n)
                .Select(agencyName => new FilterItemState(
                    agencyName,
                    selectedAgencies.TryGetValue(agencyName, out var isChecked) ? isChecked : true))
                .ToList();

            return new FilterLoadResult(
                companyFilters,
                agencyFilters,
                companyFilters.Count > 0 && companyFilters.All(f => f.IsChecked),
                agencyFilters.Count > 0 && agencyFilters.All(f => f.IsChecked));
        }

        private ReportComputationResult BuildReportResult(
            List<EmployerCompany> companies,
            FilterSelectionSnapshot filters,
            DateTime dateFrom,
            DateTime dateTo,
            IReadOnlyDictionary<string, string> typeDisplayMap,
            CancellationToken token)
        {
            int totalEmp = 0;
            int activeEmp = 0;
            int newInPeriod = 0;
            int endedInPeriod = 0;

            var selectedFirms = filters.SelectedFirms;
            var selectedAgencies = filters.SelectedAgencies;
            var effectiveFirms = new List<string>();
            var archiveLog = TryLoadArchiveLog();
            var visibleArchiveLog = GetVisibleArchiveLogEntries(archiveLog);
            var archivedEmployees = _employeeService.GetArchivedEmployees();
            var activeFirmHistory = _employeeService.GetActiveEmployeeFirmHistory();
            var employeesCache = new Dictionary<string, List<EmployeeSummary>>(StringComparer.OrdinalIgnoreCase);

            List<EmployeeSummary> GetEmployeesCached(string firmName)
            {
                token.ThrowIfCancellationRequested();

                if (!employeesCache.TryGetValue(firmName, out var employees))
                {
                    employees = _employeeService.GetEmployeesForFirm(firmName);
                    employeesCache[firmName] = employees;
                }

                return employees;
            }

            foreach (var company in companies)
            {
                token.ThrowIfCancellationRequested();

                var companyService = _companyService;
                if (companyService != null && !companyService.IsCompanyVisibleForRange(company, dateFrom, dateTo))
                    continue;

                if (!HasFirmDataInRange(company.Name, archiveLog, archivedEmployees, activeFirmHistory, GetEmployeesCached, dateFrom, dateTo))
                    continue;

                if (!selectedFirms.Contains(company.Name))
                    continue;

                bool hasAgency = company.Agency != null && !string.IsNullOrWhiteSpace(company.Agency.Name);
                if (!filters.HasAgencyFilters || !hasAgency)
                {
                    effectiveFirms.Add(company.Name);
                }
                else if (selectedAgencies.Contains(company.Agency!.Name.Trim()))
                {
                    effectiveFirms.Add(company.Name);
                }
            }

            var effectiveFirmsSet = effectiveFirms.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var archivedByFirm = LoadArchivedEmployeesForReport(
                effectiveFirmsSet,
                visibleArchiveLog,
                archivedEmployees,
                activeFirmHistory,
                GetEmployeesCached,
                dateFrom,
                dateTo,
                typeDisplayMap,
                token);

            var allEmployees = new List<EmployeeReportRow>();
            var firmDetails = new List<FirmReportRow>();
            var agencyData = new Dictionary<string, (int firms, int total, int active)>(StringComparer.OrdinalIgnoreCase);

            foreach (var firmName in effectiveFirms)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var employees = GetEmployeesCached(firmName);
                    var filtered = FilterByDateRange(employees, dateFrom, dateTo);

                    int firmActive = filtered.Count(x =>
                        string.IsNullOrEmpty(x.Summary.Status) || x.Summary.Status == "Active");

                    int firmNew = filtered.Count(x =>
                    {
                        var sd = DateParsingHelper.TryParseDate(x.Summary.StartDate);
                        return sd != null && sd.Value.Date >= dateFrom.Date && sd.Value.Date <= dateTo.Date;
                    });

                    int firmPassportOnly = filtered.Count(x => string.Equals(x.Summary.EmployeeType, "passport_only", StringComparison.OrdinalIgnoreCase));

                    var company = companies.FirstOrDefault(c => string.Equals(c.Name, firmName, StringComparison.OrdinalIgnoreCase));
                    var agencyName = company?.Agency?.Name?.Trim() ?? string.Empty;
                    foreach (var (employee, endDate) in filtered)
                        allEmployees.Add(BuildEmployeeReportRow(employee, firmName, endDate, agencyName, typeDisplayMap));

                    var archivedForFirm = archivedByFirm.TryGetValue(firmName, out var archivedRows)
                        ? archivedRows
                        : new List<EmployeeReportRow>();

                    foreach (var archived in archivedForFirm)
                        archived.Agency = agencyName;

                    allEmployees.AddRange(archivedForFirm);

                    int firmArchivedCount = archivedForFirm.Count;
                    firmPassportOnly += archivedForFirm.Count(a =>
                        string.Equals(a.EmployeeType, GetDocTypeDisplay("passport_only", typeDisplayMap), StringComparison.OrdinalIgnoreCase));

                    int firmTotal = filtered.Count + firmArchivedCount;

                    int firmEnded = 0;
                    foreach (var (_, endDate) in filtered)
                    {
                        var endDt = DateParsingHelper.TryParseDate(endDate);
                        if (endDt != null && endDt.Value.Date >= dateFrom.Date && endDt.Value.Date <= dateTo.Date)
                            firmEnded++;
                    }

                    foreach (var archived in archivedForFirm)
                    {
                        var endDt = DateParsingHelper.TryParseDate(archived.EndDate);
                        if (endDt != null && endDt.Value.Date >= dateFrom.Date && endDt.Value.Date <= dateTo.Date)
                            firmEnded++;
                    }

                    firmDetails.Add(new FirmReportRow
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

                    if (company?.Agency != null && !string.IsNullOrWhiteSpace(company.Agency.Name))
                    {
                        if (agencyData.TryGetValue(agencyName, out var existing))
                            agencyData[agencyName] = (existing.firms + 1, existing.total + firmTotal, existing.active + firmActive);
                        else
                            agencyData[agencyName] = (1, firmTotal, firmActive);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ReportViewModel: error for {firmName}: {ex.Message}");
                }
            }

            var agencyDetails = agencyData
                .OrderBy(k => k.Key)
                .Select(kvp => new AgencyReportRow
                {
                    AgencyName = kvp.Key,
                    FirmCount = kvp.Value.firms,
                    TotalEmployees = kvp.Value.total,
                    ActiveEmployees = kvp.Value.active
                })
                .ToList();

            int archivedPeriod = visibleArchiveLog.Count(l =>
                l.Action == "Archived"
                && IsTimestampInRange(l.Timestamp, dateFrom, dateTo)
                && (effectiveFirmsSet.Contains(l.FirmName) || string.IsNullOrEmpty(l.FirmName)));

            int restoredPeriod = visibleArchiveLog.Count(l =>
                l.Action == "Restored" && IsTimestampInRange(l.Timestamp, dateFrom, dateTo));

            int totalActions = visibleArchiveLog.Count(l => IsTimestampInRange(l.Timestamp, dateFrom, dateTo));

            var archiveHistory = visibleArchiveLog
                .Where(l => IsTimestampInRange(l.Timestamp, dateFrom, dateTo))
                .OrderByDescending(l => l.Timestamp)
                .ToList();

            return new ReportComputationResult(
                firmDetails,
                agencyDetails,
                archiveHistory,
                allEmployees,
                visibleArchiveLog,
                effectiveFirms,
                totalEmp,
                activeEmp,
                newInPeriod,
                endedInPeriod,
                archivedPeriod,
                restoredPeriod,
                totalActions);
        }

        private void ApplyReportResult(ReportComputationResult result, DateTime dateFrom, DateTime dateTo)
        {
            FirmDetails = new ObservableCollection<FirmReportRow>(result.FirmDetails);
            AgencyDetails = new ObservableCollection<AgencyReportRow>(result.AgencyDetails);
            ArchiveHistory = new ObservableCollection<ArchiveLogEntry>(result.ArchiveHistory);

            _allEmployees = result.AllEmployees;
            _dateFilteredCache = null;
            EmployeeGroups = new ObservableCollection<FirmEmployeeGroup>();

            TotalEmployees = result.TotalEmployees;
            ActiveEmployees = result.ActiveEmployees;
            NewInPeriod = result.NewInPeriod;
            EndedInPeriod = result.EndedInPeriod;
            ArchivedInPeriod = result.ArchivedInPeriod;
            RestoredInPeriod = result.RestoredInPeriod;
            TotalArchiveActions = result.TotalArchiveActions;

            SummaryTotal = result.FirmDetails.Sum(f => f.TotalEmployees);
            SummaryActive = result.FirmDetails.Sum(f => f.ActiveEmployees);
            SummaryPassportOnly = result.FirmDetails.Sum(f => f.PassportOnlyCount);
            SummaryArchived = result.FirmDetails.Sum(f => f.ArchivedEmployees);

            BuildArchiveChart(result.VisibleArchiveLog, dateFrom, dateTo);
            FilterEmployees();

            HasData = result.TotalEmployees > 0 || result.EndedInPeriod > 0;
            StatusMessage = string.Format(GetString("ReportStatusFmt"), result.EffectiveFirms.Count, result.TotalEmployees, result.NewInPeriod, result.EndedInPeriod);
        }

        private List<ArchiveLogEntry> TryLoadArchiveLog()
        {
            try
            {
                return _employeeService.LoadArchiveLog();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ReportViewModel.LoadArchiveLog", ex);
                return new List<ArchiveLogEntry>();
            }
        }

        internal static List<ArchiveLogEntry> GetVisibleArchiveLogEntries(IEnumerable<ArchiveLogEntry> archiveLog)
        {
            return archiveLog
                .Where(l => !l.IsReverted)
                .ToList();
        }

        private bool HasFirmDataInRange(
            string firmName,
            List<ArchiveLogEntry> archiveLog,
            List<ArchivedEmployeeSummary> archivedEmployees,
            List<ArchivedEmployeeSummary> activeFirmHistory,
            Func<string, List<EmployeeSummary>> getEmployeesCached,
            DateTime dateFrom,
            DateTime dateTo)
        {
            var employees = getEmployeesCached(firmName);
            if (FilterByDateRange(employees, dateFrom, dateTo).Count > 0)
                return true;

            foreach (var archived in archivedEmployees.Where(a => string.Equals(a.FirmName, firmName, StringComparison.OrdinalIgnoreCase)))
            {
                if (IsHistoricalRecordInRange(archived.StartDate, archived.EndDate, dateFrom, dateTo))
                    return true;
            }

            foreach (var history in activeFirmHistory.Where(a => string.Equals(a.FirmName, firmName, StringComparison.OrdinalIgnoreCase)))
            {
                if (IsHistoricalRecordInRange(history.StartDate, history.EndDate, dateFrom, dateTo))
                    return true;
            }

            return archiveLog.Any(l =>
                string.Equals(l.FirmName, firmName, StringComparison.OrdinalIgnoreCase)
                && IsArchiveLogDateInRange(l.Date, dateFrom, dateTo));
        }

        private static bool IsHistoricalRecordInRange(string startDateRaw, string endDateRaw, DateTime dateFrom, DateTime dateTo)
        {
            var endDate = DateParsingHelper.TryParseDate(endDateRaw);
            if (endDate != null && endDate.Value.Date < dateFrom.Date)
                return false;

            var startDate = DateParsingHelper.TryParseDate(startDateRaw);
            if (startDate != null && startDate.Value.Date > dateTo.Date)
                return false;

            return true;
        }

        private static bool IsArchiveLogDateInRange(string dateRaw, DateTime dateFrom, DateTime dateTo)
        {
            var date = DateParsingHelper.TryParseDate(dateRaw);
            if (date == null)
                return false;

            return date.Value.Date >= dateFrom.Date && date.Value.Date <= dateTo.Date;
        }

        private Dictionary<string, List<EmployeeReportRow>> LoadArchivedEmployeesForReport(
            HashSet<string> firmFilter,
            List<ArchiveLogEntry> archiveLog,
            List<ArchivedEmployeeSummary> archivedList,
            List<ArchivedEmployeeSummary> activeFirmHistory,
            Func<string, List<EmployeeSummary>> getEmployeesCached,
            DateTime dateFrom,
            DateTime dateTo,
            IReadOnlyDictionary<string, string> typeDisplayMap,
            CancellationToken token)
        {
            var result = new Dictionary<string, List<EmployeeReportRow>>(StringComparer.OrdinalIgnoreCase);
            var financeService = _financeService;
            if (financeService == null) return result;
            var alreadyAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var looseAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static string NormalizeDedupPart(string? value) => (value ?? string.Empty).Trim();

            static string NormalizeDedupPath(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return string.Empty;

                return value.Trim()
                    .Replace('/', '\\')
                    .TrimEnd('\\');
            }

            try
            {
                var archivedEvents = archiveLog
                    .Where(l => l.Action == "Archived" && firmFilter.Contains(l.FirmName))
                    .ToList();

                foreach (var arch in archivedList)
                {
                    token.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(arch.FirmName) || !firmFilter.Contains(arch.FirmName)) continue;
                    var resolvedFolder = financeService.ResolveEmployeeFolder(arch.EmployeeFolder ?? string.Empty);
                    var dedupKey = !string.IsNullOrWhiteSpace(arch.UniqueId)
                        ? string.Join("|",
                            NormalizeDedupPart(arch.UniqueId),
                            NormalizeDedupPart(arch.FirmName),
                            NormalizeDedupPart(arch.EndDate))
                        : string.Join("|",
                            NormalizeDedupPart(arch.FullName),
                            NormalizeDedupPart(arch.FirmName),
                            NormalizeDedupPart(arch.EndDate),
                            NormalizeDedupPath(resolvedFolder));

                    if (alreadyAdded.Contains(dedupKey)) continue;

                    if (!IsHistoricalRecordInRange(arch.StartDate, arch.EndDate, dateFrom, dateTo)) continue;

                    alreadyAdded.Add(dedupKey);
                    looseAdded.Add(string.Join("|",
                        NormalizeDedupPart(arch.FullName),
                        NormalizeDedupPart(arch.FirmName),
                        NormalizeDedupPart(arch.EndDate)));
                    var row = BuildArchivedRowFromSummary(
                        arch.FullName,
                        arch.FirmName,
                        resolvedFolder,
                        arch.StartDate,
                        arch.EndDate,
                        arch.PositionTitle,
                        typeDisplayMap);

                    if (!result.ContainsKey(arch.FirmName))
                        result[arch.FirmName] = new List<EmployeeReportRow>();
                    result[arch.FirmName].Add(row);
                }

                foreach (var history in activeFirmHistory)
                {
                    token.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(history.FirmName) || !firmFilter.Contains(history.FirmName)) continue;
                    var resolvedFolder = financeService.ResolveEmployeeFolder(history.EmployeeFolder ?? string.Empty);
                    var dedupKey = !string.IsNullOrWhiteSpace(history.UniqueId)
                        ? string.Join("|",
                            NormalizeDedupPart(history.UniqueId),
                            NormalizeDedupPart(history.FirmName),
                            NormalizeDedupPart(history.EndDate))
                        : string.Join("|",
                            NormalizeDedupPart(history.FullName),
                            NormalizeDedupPart(history.FirmName),
                            NormalizeDedupPart(history.EndDate),
                            NormalizeDedupPath(resolvedFolder));

                    if (alreadyAdded.Contains(dedupKey)) continue;

                    if (!IsHistoricalRecordInRange(history.StartDate, history.EndDate, dateFrom, dateTo)) continue;

                    alreadyAdded.Add(dedupKey);
                    looseAdded.Add(string.Join("|",
                        NormalizeDedupPart(history.FullName),
                        NormalizeDedupPart(history.FirmName),
                        NormalizeDedupPart(history.EndDate)));
                    var row = BuildArchivedRowFromSummary(
                        history.FullName,
                        history.FirmName,
                        resolvedFolder,
                        history.StartDate,
                        history.EndDate,
                        history.PositionTitle,
                        typeDisplayMap);

                    if (!result.ContainsKey(history.FirmName))
                        result[history.FirmName] = new List<EmployeeReportRow>();
                    result[history.FirmName].Add(row);
                }

                foreach (var evt in archivedEvents)
                {
                    token.ThrowIfCancellationRequested();

                    var endDate = DateParsingHelper.TryParseDate(evt.Date);
                    if (endDate != null && endDate.Value.Date < dateFrom.Date) continue;

                    var resolvedFolder = !string.IsNullOrEmpty(evt.EmployeeFolder)
                        ? financeService.ResolveEmployeeFolder(evt.EmployeeFolder)
                        : ResolveEmployeeFolderByName(evt.FirmName, evt.EmployeeName, financeService, archivedList, getEmployeesCached);
                    var looseKey = string.Join("|",
                        NormalizeDedupPart(evt.EmployeeName),
                        NormalizeDedupPart(evt.FirmName),
                        NormalizeDedupPart(evt.Date));
                    if (looseAdded.Contains(looseKey))
                        continue;

                    var dedupKey = string.Join("|",
                        NormalizeDedupPart(evt.EmployeeName),
                        NormalizeDedupPart(evt.FirmName),
                        NormalizeDedupPart(evt.Date),
                        NormalizeDedupPath(resolvedFolder));
                    if (alreadyAdded.Contains(dedupKey)) continue;
                    alreadyAdded.Add(dedupKey);

                    var row = BuildArchivedRowFromLog(evt, resolvedFolder, typeDisplayMap);
                    if (row == null) continue;

                    if (!result.ContainsKey(evt.FirmName))
                        result[evt.FirmName] = new List<EmployeeReportRow>();
                    result[evt.FirmName].Add(row);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ReportViewModel.LoadArchivedEmployeesForReport: {ex.Message}");
            }

            return result;
        }

        private string ResolveEmployeeFolderByName(
            string firmName,
            string employeeName,
            FinanceService financeService,
            List<ArchivedEmployeeSummary> archivedEmployees,
            Func<string, List<EmployeeSummary>> getEmployeesCached)
        {
            var employeesInFirm = getEmployeesCached(firmName);
            var directMatch = employeesInFirm.FirstOrDefault(e =>
                string.Equals(e.FullName, employeeName, StringComparison.OrdinalIgnoreCase));
            if (directMatch != null)
                return directMatch.EmployeeFolder;

            var companies = _companyService.Companies;
            foreach (var company in companies)
            {
                if (string.Equals(company.Name, firmName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var employees = getEmployeesCached(company.Name);
                var match = employees.FirstOrDefault(e =>
                    string.Equals(e.FullName, employeeName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match.EmployeeFolder;
            }

            var archivedMatchInFirm = archivedEmployees.FirstOrDefault(a =>
                string.Equals(a.FirmName, firmName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.FullName, employeeName, StringComparison.OrdinalIgnoreCase));
            if (archivedMatchInFirm != null)
                return financeService.ResolveEmployeeFolder(archivedMatchInFirm.EmployeeFolder);

            var archMatch = archivedEmployees.FirstOrDefault(a =>
                string.Equals(a.FullName, employeeName, StringComparison.OrdinalIgnoreCase));
            if (archMatch != null)
                return financeService.ResolveEmployeeFolder(archMatch.EmployeeFolder);

            return string.Empty;
        }

        private EmployeeReportRow? BuildArchivedRowFromLog(
            ArchiveLogEntry evt,
            string employeeFolder,
            IReadOnlyDictionary<string, string> typeDisplayMap)
        {
            try
            {
                var resolvedFolder = employeeFolder;
                if (!string.IsNullOrEmpty(resolvedFolder))
                {
                    var financeService = _financeService;
                    resolvedFolder = financeService?.ResolveEmployeeFolder(resolvedFolder) ?? resolvedFolder;
                }

                var jsonPath = !string.IsNullOrEmpty(resolvedFolder) ? Path.Combine(resolvedFolder, "employee.json") : "";
                if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
                {
                    var json = SafeFileService.ReadAllText(jsonPath);
                    var data = JsonSerializer.Deserialize<EmployeeData>(json);
                    if (data != null)
                        return BuildArchivedRowFromData(
                            evt.EmployeeName,
                            evt.FirmName,
                            resolvedFolder,
                            evt.Date,
                            data,
                            typeDisplayMap);
                }

                return new EmployeeReportRow
                {
                    FullName = evt.EmployeeName,
                    FirmName = evt.FirmName,
                    EmployeeFolder = resolvedFolder,
                    EmployeeType = "—",
                    DocumentType = "—",
                    EndDate = evt.Date,
                    IsArchived = true
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ReportViewModel.BuildArchivedRowFromLog", ex);
                return null;
            }
        }

        private EmployeeReportRow BuildArchivedRowFromSummary(
            string fullName,
            string firmName,
            string employeeFolder,
            string startDate,
            string endDate,
            string positionTitle,
            IReadOnlyDictionary<string, string> typeDisplayMap)
        {
            try
            {
                var resolvedFolder = employeeFolder;
                if (!string.IsNullOrWhiteSpace(resolvedFolder))
                {
                    var financeService = _financeService;
                    resolvedFolder = financeService?.ResolveEmployeeFolder(resolvedFolder) ?? resolvedFolder;
                }

                var jsonPath = !string.IsNullOrEmpty(resolvedFolder) ? Path.Combine(resolvedFolder, "employee.json") : string.Empty;
                if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
                {
                    var data = SafeFileService.ReadJson<EmployeeData>(jsonPath);
                    if (data != null)
                        return BuildArchivedRowFromData(fullName, firmName, resolvedFolder, endDate, data, typeDisplayMap);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ReportViewModel.BuildArchivedRowFromSummary", ex.Message);
            }

            return new EmployeeReportRow
            {
                FullName = fullName,
                FirmName = firmName,
                EmployeeFolder = employeeFolder,
                EmployeeType = "—",
                DocumentType = "—",
                StartDate = startDate,
                EndDate = endDate,
                Position = positionTitle,
                IsArchived = true
            };
        }

        private EmployeeReportRow BuildArchivedRowFromData(
            string fullName,
            string firmName,
            string employeeFolder,
            string endDate,
            EmployeeData data,
            IReadOnlyDictionary<string, string> typeDisplayMap)
        {
            return new EmployeeReportRow
            {
                FullName = fullName,
                FirmName = firmName,
                EmployeeFolder = employeeFolder,
                EmployeeType = !string.IsNullOrEmpty(data.WorkPermitName)
                    ? data.WorkPermitName
                    : GetDocTypeDisplay(data.EmployeeType ?? "visa", typeDisplayMap),
                DocumentType = GetDocTypeDisplay(data.EmployeeType ?? "visa", typeDisplayMap),
                PassportNumber = data.PassportNumber ?? string.Empty,
                PassportExpiry = data.PassportExpiry,
                VisaExpiry = data.VisaExpiry,
                InsuranceExpiry = data.InsuranceExpiry,
                StartDate = data.StartDate,
                EndDate = endDate,
                Phone = data.Phone,
                BankAccountNumber = data.HasBankAccountData ? data.BankAccountNumber : string.Empty,
                BankName = data.HasBankAccountData ? data.BankName : string.Empty,
                Position = data.PositionTag,
                PositionCode = data.PositionNumber,
                WorkAddress = data.WorkAddressTag,
                HighestEducation = EducationCatalog.GetFullDisplay(data.HighestEducationCode),
                AddressCz = FormatAddress(data.AddressLocal),
                AddressAbroad = FormatAddress(data.AddressAbroad),
                BirthDate = data.BirthDate,
                Gender = GetGenderDisplay(data.Gender ?? string.Empty),
                PassportIssuedBy = data.PassportAuthority,
                IsArchived = true
            };
        }

        private EmployeeReportRow BuildEmployeeReportRow(
            EmployeeSummary employee,
            string firmName,
            string endDate,
            string agencyName,
            IReadOnlyDictionary<string, string> typeDisplayMap)
        {
            var row = new EmployeeReportRow
            {
                FullName = employee.FullName,
                FirmName = firmName,
                EmployeeFolder = employee.EmployeeFolder,
                EmployeeType = !string.IsNullOrEmpty(employee.WorkPermitName)
                    ? employee.WorkPermitName
                    : GetDocTypeDisplay(employee.EmployeeType, typeDisplayMap),
                DocumentType = GetDocTypeDisplay(employee.EmployeeType, typeDisplayMap),
                PassportNumber = employee.PassportNumber,
                PassportExpiry = employee.PassportExpiry,
                VisaExpiry = employee.VisaExpiry,
                InsuranceExpiry = employee.InsuranceExpiry,
                StartDate = employee.StartDate,
                EndDate = endDate,
                Phone = employee.Phone,
                BankAccountNumber = employee.BankAccountNumber,
                BankName = employee.BankName,
                Position = employee.PositionTitle,
                Agency = agencyName,
                IsArchived = false
            };

            try
            {
                var jsonPath = !string.IsNullOrWhiteSpace(employee.EmployeeFolder)
                    ? Path.Combine(employee.EmployeeFolder, "employee.json")
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath))
                {
                    var data = SafeFileService.ReadJson<EmployeeData>(jsonPath);
                    if (data != null)
                    {
                        row.DocumentType = GetDocTypeDisplay(data.EmployeeType ?? employee.EmployeeType ?? "visa", typeDisplayMap);
                        row.PassportNumber = data.PassportNumber ?? employee.PassportNumber ?? string.Empty;
                        row.Position = string.IsNullOrWhiteSpace(data.PositionTag) ? row.Position : data.PositionTag;
                        row.PositionCode = data.PositionNumber ?? string.Empty;
                        row.WorkAddress = data.WorkAddressTag ?? string.Empty;
                        row.HighestEducation = EducationCatalog.GetFullDisplay(data.HighestEducationCode);
                        row.AddressCz = FormatAddress(data.AddressLocal);
                        row.AddressAbroad = FormatAddress(data.AddressAbroad);
                        row.BirthDate = data.BirthDate ?? string.Empty;
                        row.Gender = GetGenderDisplay(data.Gender ?? string.Empty);
                        row.PassportIssuedBy = data.PassportAuthority ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ReportViewModel.BuildEmployeeReportRow", ex.Message);
            }

            return row;
        }

        private List<EmployeeReportRow> GetDateFilteredEmployees()
        {
            if (_dateFilteredCache != null)
                return _dateFilteredCache;

            var dateFrom = DateFrom.Date;
            var dateTo = DateTo.Date;

            _dateFilteredCache = _allEmployees.Where(e =>
            {
                if (!string.IsNullOrEmpty(e.EndDate))
                {
                    var ed = DateParsingHelper.TryParseDate(e.EndDate);
                    if (ed != null && ed.Value.Date < dateFrom)
                        return false;
                }
                if (!string.IsNullOrEmpty(e.StartDate))
                {
                    var sd = DateParsingHelper.TryParseDate(e.StartDate);
                    if (sd != null && sd.Value.Date > dateTo)
                        return false;
                }
                return true;
            }).ToList();

            return _dateFilteredCache;
        }

        private void FilterEmployees()
        {
            var search = EmployeeSearchText?.Trim() ?? "";
            var dateFiltered = GetDateFilteredEmployees();

            var filtered = string.IsNullOrEmpty(search)
                ? dateFiltered
                : dateFiltered.Where(e =>
                    (e.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Phone?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.BankAccountNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.BankName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Position?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.FirmName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

            var groups = filtered.GroupBy(e => e.FirmName).OrderBy(g => g.Key);
            EmployeeGroups = new ObservableCollection<FirmEmployeeGroup>(
                groups.Select(g => new FirmEmployeeGroup
                {
                    FirmName = g.Key,
                    EmployeeCount = g.Count(),
                    Employees = new ObservableCollection<EmployeeReportRow>(g)
                }));
        }

        private async void FilterEmployeesDebounced()
        {
            var cts = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _searchCts, cts);
            previous?.Cancel();
            previous?.Dispose();

            try
            {
                await Task.Delay(300, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                    FilterEmployees();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OpenEmployee(EmployeeReportRow? row)
        {
            if (row == null || string.IsNullOrEmpty(row.EmployeeFolder)) return;
            var financeService = _financeService;
            if (financeService == null) return;
            var resolvedFolder = financeService.ResolveEmployeeFolder(row.EmployeeFolder);
            if (!Directory.Exists(resolvedFolder))
            {
                MessageBox.Show(
                    $"{GetString("MsgOpenFolderFail")}\n\n{resolvedFolder}",
                    GetString("TitleWarning"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            CleanupDetailsVm();
            EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(row.FirmName, resolvedFolder, _employeeService);
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
        private void OnDetailsDataChanged() => _ = RefreshReportAsync(reloadFilters: false);

        private static bool IsTimestampInRange(string timestampStr, DateTime dateFrom, DateTime dateTo)
        {
            if (string.IsNullOrWhiteSpace(timestampStr)) return true;
            if (DateTime.TryParse(timestampStr, out var ts))
            {
                return ts.Date >= dateFrom.Date && ts.Date <= dateTo.Date;
            }
            return true;
        }

        private static List<(EmployeeSummary Summary, string EndDate)> FilterByDateRange(List<EmployeeSummary> employees, DateTime dateFrom, DateTime dateTo)
        {
            var result = new List<(EmployeeSummary, string)>();
            foreach (var e in employees)
            {
                var startDate = DateParsingHelper.TryParseDate(e.StartDate);
                if (startDate != null && startDate.Value.Date > dateTo.Date)
                    continue;

                var endDate = e.EndDate ?? string.Empty;

                var ed = DateParsingHelper.TryParseDate(endDate);
                if (ed != null && ed.Value.Date < dateFrom.Date)
                    continue;

                result.Add((e, endDate));
            }
            return result;
        }

        private void BuildArchiveChart(List<ArchiveLogEntry> archiveLog, DateTime dateFrom, DateTime dateTo)
        {
            var monthlyData = new SortedDictionary<string, (int archived, int restored)>();

            var current = new DateTime(dateFrom.Year, dateFrom.Month, 1);
            var end = new DateTime(dateTo.Year, dateTo.Month, 1);
            while (current <= end)
            {
                monthlyData[current.ToString("yyyy-MM")] = (0, 0);
                current = current.AddMonths(1);
            }

            foreach (var entry in archiveLog)
            {
                if (!DateTime.TryParse(entry.Timestamp, out var ts)) continue;
                if (ts.Date < dateFrom.Date || ts.Date > dateTo.Date) continue;

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

        private static string SummarizeForLog(IEnumerable<string> items, string emptyFallback)
        {
            var values = items
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            if (values.Count == 0)
                return emptyFallback;

            if (values.Count <= 5)
                return string.Join(", ", values);

            return $"{string.Join(", ", values.Take(5))} +{values.Count - 5}";
        }

        private string BuildReportExportDetails(string outputPath)
        {
            var selectedSheets = ExportSheets.Where(s => s.IsSelected).Select(s => s.DisplayName);
            var selectedFirms = CompanyFilters.Where(f => f.IsChecked).Select(f => f.CompanyName);
            var selectedAgencies = AgencyFilters.Where(f => f.IsChecked).Select(f => f.CompanyName);

            return $"Період: {DateFrom:dd.MM.yyyy} - {DateTo:dd.MM.yyyy}; " +
                   $"Листи: {SummarizeForLog(selectedSheets, "не вибрано")}; " +
                   $"Фірми: {SummarizeForLog(selectedFirms, "усі")}; " +
                   $"Агентури: {SummarizeForLog(selectedAgencies, "усі")}; " +
                   $"Працівників: {_allEmployees.Count}; " +
                   $"Файл: {Path.GetFileName(outputPath)}";
        }

        private bool EnsureExportPathReady(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(filePath);
                if (Directory.Exists(fullPath))
                    throw new IOException("Selected path is a directory.");

                var outputDirectory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                    throw new DirectoryNotFoundException("Export folder not found.");

                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ReportViewModel.EnsureExportPathReady", ex.Message);
                MessageBox.Show(string.Format(GetString("ReportExportError"), ex.Message),
                    GetString("ReportExportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ExportToExcel()
        {
            if (!PolicyService.EnsureExportsAllowed("експортувати звіт в Excel"))
                return;

            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Excel|*.xlsx",
                    FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };
                if (dialog.ShowDialog() != true) return;
                if (!EnsureExportPathReady(dialog.FileName)) return;

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
                    var visibleColumns = GetVisibleEmployeeColumnsForExport();
                    int colCount = visibleColumns.Count;
                    if (colCount == 0)
                        visibleColumns = NormalizeEmployeeColumnLayout(DefaultEmployeeColumns.Select(CopyColumnSetting))
                            .Where(c => c.IsVisible)
                            .ToList();
                    colCount = visibleColumns.Count;

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

                        for (int colIndex = 0; colIndex < visibleColumns.Count; colIndex++)
                        {
                            var column = visibleColumns[colIndex];
                            wsE.Cell(row, colIndex + 1).Value = DocString(_reportColumnLayoutService.GetEmployeeColumnHeaderResourceKey(column.Key));
                            wsE.Cell(row, colIndex + 1).Style.Alignment.Horizontal = GetEmployeeExcelAlignment(column.Key);
                        }
                        var hdr = wsE.Range(row, 1, row, colCount);
                        hdr.Style.Font.Bold = true;
                        hdr.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
                        row++;

                        foreach (var emp in group)
                        {
                            for (int colIndex = 0; colIndex < visibleColumns.Count; colIndex++)
                            {
                                var column = visibleColumns[colIndex];
                                var cell = wsE.Cell(row, colIndex + 1);
                                cell.Value = GetEmployeeColumnValue(emp, column.Key);
                                cell.Style.Alignment.Horizontal = GetEmployeeExcelAlignment(column.Key);
                            }

                            if (emp.IsArchived)
                            {
                                wsE.Range(row, 1, row, colCount).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 205, 210);
                                wsE.Range(row, 1, row, colCount).Style.Font.FontColor = XLColor.FromArgb(183, 28, 28);
                                wsE.Range(row, 1, row, colCount).Style.Font.Italic = true;
                            }
                            else
                            {
                                for (int colIndex = 0; colIndex < visibleColumns.Count; colIndex++)
                                {
                                    var status = GetEmployeeColumnStatus(emp, visibleColumns[colIndex].Key);
                                    if (!string.IsNullOrEmpty(status))
                                        ColorExpiryCell(wsE.Cell(row, colIndex + 1), status);
                                }
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
                _activityLogService.Log("ExportExcel", "Export", "", "",
                    $"Експортовано звіт → Excel", details: BuildReportExportDetails(dialog.FileName));

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
            if (!PolicyService.EnsureExportsAllowed("експортувати звіт в PDF"))
                return;

            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "PDF|*.pdf",
                    FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
                };
                if (dialog.ShowDialog() != true) return;
                if (!EnsureExportPathReady(dialog.FileName)) return;

                var accentColor = QuestPDF.Helpers.Colors.Blue.Medium;
                var headerColor = "#6495ED";
                var subHeaderColor = "#C6DBEF";
                var agencyHeaderColor = "#7B1FA2";

                var visibleColumns = GetVisibleEmployeeColumnsForExport();
                if (visibleColumns.Count == 0)
                {
                    visibleColumns = NormalizeEmployeeColumnLayout(DefaultEmployeeColumns.Select(CopyColumnSetting))
                        .Where(c => c.IsVisible)
                        .ToList();
                }

                QuestPDF.Fluent.Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.MarginHorizontal(20);
                        page.MarginVertical(30);
                        page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(8));

                        page.Header().Column(col =>
                        {
                            col.Item().Text(DocString("ReportPdfTitle")).FontSize(16).Bold();
                            col.Item().Text(string.Format(DocString("ReportPdfPeriod"),
                                DateFrom.ToString("dd.MM.yyyy"), DateTo.ToString("dd.MM.yyyy")))
                                .FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
                            col.Item().Text(string.Format(DocString("ReportPdfStatsFmt"),
                                TotalEmployees, ActiveEmployees, NewInPeriod, EndedInPeriod))
                                .FontSize(10);
                            col.Item().PaddingBottom(8);
                        });

                        page.Content().Column(content =>
                        {
                            if (IsSheetSelected("firms") && FirmDetails.Count > 0)
                            {
                                content.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(4);
                                        c.RelativeColumn(1.5f);
                                        c.RelativeColumn(1.5f);
                                        c.RelativeColumn(1.5f);
                                        c.RelativeColumn(1.5f);
                                    });

                                    table.Header(header =>
                                    {
                                        void HeaderCell(string text) => header.Cell()
                                            .Background(headerColor).Padding(4)
                                            .Text(text).FontColor(QuestPDF.Helpers.Colors.White).Bold().FontSize(8);

                                        HeaderCell(DocString("ReportColFirm"));
                                        HeaderCell(DocString("ReportColTotal"));
                                        HeaderCell(DocString("ReportColActive"));
                                        HeaderCell(DocString("ReportColNoPermit"));
                                        HeaderCell(DocString("ReportColArchived"));
                                    });

                                    foreach (var f in FirmDetails)
                                    {
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).Text(f.FirmName);
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).AlignCenter().Text(f.TotalEmployees.ToString());
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).AlignCenter().Text(f.ActiveEmployees.ToString());
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).AlignCenter().Text(f.PassportOnlyCount.ToString());
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).AlignCenter().Text(f.ArchivedEmployees.ToString());
                                    }

                                    table.Cell().Background(subHeaderColor).Padding(4).Text(DocString("ReportTotal")).Bold();
                                    table.Cell().Background(subHeaderColor).Padding(4).AlignCenter().Text(SummaryTotal.ToString()).Bold();
                                    table.Cell().Background(subHeaderColor).Padding(4).AlignCenter().Text(SummaryActive.ToString()).Bold();
                                    table.Cell().Background(subHeaderColor).Padding(4).AlignCenter().Text(SummaryPassportOnly.ToString()).Bold();
                                    table.Cell().Background(subHeaderColor).Padding(4).AlignCenter().Text(SummaryArchived.ToString()).Bold();
                                });
                                content.Item().PaddingBottom(12);
                            }

                            if (IsSheetSelected("agencies") && AgencyDetails.Count > 0)
                            {
                                content.Item().Text(DocString("ReportPdfAgencySection")).FontSize(10).Bold();
                                content.Item().PaddingTop(4).Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(4);
                                        c.RelativeColumn(2);
                                        c.RelativeColumn(2);
                                        c.RelativeColumn(2);
                                    });

                                    table.Header(header =>
                                    {
                                        void HeaderCell(string text) => header.Cell()
                                            .Background(agencyHeaderColor).Padding(4)
                                            .Text(text).FontColor(QuestPDF.Helpers.Colors.White).Bold().FontSize(8);

                                        HeaderCell(DocString("ReportColAgencyName"));
                                        HeaderCell(DocString("ReportColFirmCount"));
                                        HeaderCell(DocString("ReportColTotal"));
                                        HeaderCell(DocString("ReportColActive"));
                                    });

                                    foreach (var a in AgencyDetails)
                                    {
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).Text(a.AgencyName);
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).AlignCenter().Text(a.FirmCount.ToString());
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).AlignCenter().Text(a.TotalEmployees.ToString());
                                        table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).AlignCenter().Text(a.ActiveEmployees.ToString());
                                    }
                                });
                                content.Item().PaddingBottom(12);
                            }

                            if (IsSheetSelected("employees") && _allEmployees.Count > 0)
                            {
                                var groups = _allEmployees
                                    .OrderBy(e => e.FirmName).ThenBy(e => e.IsArchived).ThenBy(e => e.FullName)
                                    .GroupBy(e => e.FirmName);

                                foreach (var group in groups)
                                {
                                    var groupTitle = string.Format(DocString("ReportEmpCountFmt"), group.Key, group.Count());

                                    content.Item().Background(headerColor).Padding(5)
                                        .Text(groupTitle).FontColor(QuestPDF.Helpers.Colors.White).Bold().FontSize(10).AlignCenter();

                                    content.Item().Table(table =>
                                    {
                                        table.ColumnsDefinition(c =>
                                        {
                                            double totalWeight = visibleColumns.Sum(vc => vc.Width);
                                            if (totalWeight <= 0) totalWeight = visibleColumns.Count;
                                            foreach (var vc in visibleColumns)
                                                c.RelativeColumn((float)(vc.Width / totalWeight * 10));
                                        });

                                        table.Header(header =>
                                        {
                                            foreach (var vc in visibleColumns)
                                            {
                                                header.Cell().Background(subHeaderColor).Padding(3)
                                                    .Text(DocString(_reportColumnLayoutService.GetEmployeeColumnHeaderResourceKey(vc.Key)))
                                                    .Bold().FontSize(7);
                                            }
                                        });

                                        foreach (var emp in group)
                                        {
                                            foreach (var vc in visibleColumns)
                                            {
                                                var val = GetEmployeeColumnValue(emp, vc.Key);
                                                var cell = table.Cell().BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(3);

                                                if (emp.IsArchived)
                                                {
                                                    cell = cell.Background("#FFCDCD");
                                                    cell.Text(val ?? "").FontSize(7).Italic().FontColor("#B71C1C");
                                                }
                                                else
                                                {
                                                    var status = GetEmployeeColumnStatus(emp, vc.Key);
                                                    var fontColor = status switch
                                                    {
                                                        "expired" => "#D32F2F",
                                                        "warning" => "#F57F17",
                                                        "ok" => "#2E7D32",
                                                        _ => "#000000"
                                                    };
                                                    cell.Text(val ?? "").FontSize(7).FontColor(fontColor);
                                                }
                                            }
                                        }
                                    });
                                    content.Item().PaddingBottom(8);
                                }
                            }

                            if (IsSheetSelected("archive") && ArchiveHistory.Count > 0)
                            {
                                content.Item().Text(DocString("ReportSheetArchive")).FontSize(10).Bold();
                                content.Item().PaddingTop(4).Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(3);
                                        c.RelativeColumn(2.5f);
                                        c.RelativeColumn(1.5f);
                                        c.RelativeColumn(1.5f);
                                        c.RelativeColumn(1.5f);
                                    });

                                    table.Header(header =>
                                    {
                                        void HeaderCell(string text) => header.Cell()
                                            .Background(agencyHeaderColor).Padding(4)
                                            .Text(text).FontColor(QuestPDF.Helpers.Colors.White).Bold().FontSize(8);

                                        HeaderCell(DocString("ReportColEmployee"));
                                        HeaderCell(DocString("ReportColFirm"));
                                        HeaderCell(DocString("ReportColAction"));
                                        HeaderCell(DocString("ReportColDate"));
                                        HeaderCell(DocString("ReportColTimestamp"));
                                    });

                                    foreach (var entry in ArchiveHistory)
                                    {
                                        var action = entry.Action == "Archived" ? DocString("ReportActionArchived") : DocString("ReportActionRestored");
                                        var bg = entry.Action == "Archived" ? "#FFE4E1" : "#F0FFF0";

                                        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).Text(entry.EmployeeName);
                                        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).Text(entry.FirmName);
                                        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).Text(action);
                                        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).Text(entry.Date);
                                        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).Padding(4).Text(entry.Timestamp);
                                    }
                                });
                            }
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.CurrentPageNumber();
                            text.Span(" / ");
                            text.TotalPages();
                        });
                    });
                }).GeneratePdf(dialog.FileName);

                StatusMessage = GetString("ReportExportedPdf");
                _activityLogService.Log("ExportPdf", "Export", "", "",
                    $"Експортовано звіт → PDF", details: BuildReportExportDetails(dialog.FileName));

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

        private sealed record FilterItemState(string Name, bool IsChecked);

        private sealed record FilterLoadResult(
            List<FilterItemState> CompanyFilters,
            List<FilterItemState> AgencyFilters,
            bool AllCompaniesSelected,
            bool AllAgenciesSelected);

        private sealed record FilterSelectionSnapshot(
            HashSet<string> SelectedFirms,
            HashSet<string> SelectedAgencies,
            bool HasAgencyFilters);

        private sealed record ReportComputationResult(
            List<FirmReportRow> FirmDetails,
            List<AgencyReportRow> AgencyDetails,
            List<ArchiveLogEntry> ArchiveHistory,
            List<EmployeeReportRow> AllEmployees,
            List<ArchiveLogEntry> VisibleArchiveLog,
            List<string> EffectiveFirms,
            int TotalEmployees,
            int ActiveEmployees,
            int NewInPeriod,
            int EndedInPeriod,
            int ArchivedInPeriod,
            int RestoredInPeriod,
            int TotalArchiveActions);
    }
}
