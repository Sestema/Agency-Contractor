using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Threading.Tasks;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class SalaryViewModel : ViewModelBase
    {
        private readonly NavigationService _navigationService;
        private readonly FinanceService _financeService;
        private readonly EmployeeService _employeeService;
        private readonly AppSettingsService _appSettingsService;
        private readonly ActivityLogService _activityLogService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly CompanyService _companyService;
        private readonly object _ratePropagationGate = new();
        private readonly Dictionary<string, CancellationTokenSource> _ratePropagationCtsByKey = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _notePropagationCts;
        private readonly SemaphoreSlim _saveReportGate = new(1, 1);
        private int _advanceRefreshVersion;
        // key: folderKey|firmName → note value at load time (for forward propagation)
        private Dictionary<string, string> _originalNotes = new(StringComparer.OrdinalIgnoreCase);

        internal AppSettingsService AppSettingsService => _appSettingsService;

        public event Action? DataLoaded;

        public ObservableCollection<SalaryEntry> Entries { get; } = new();
        private ObservableCollection<FirmSalarySummary> _firmSummaries = new();
        public ObservableCollection<FirmSalarySummary> FirmSummaries
        {
            get => _firmSummaries;
            set => SetProperty(ref _firmSummaries, value);
        }

        private ObservableCollection<string> _availableFirms = new();
        public ObservableCollection<string> AvailableFirms
        {
            get => _availableFirms;
            set => SetProperty(ref _availableFirms, value);
        }

        private ObservableCollection<CustomSalaryField> _activeCustomFields = new();
        public ObservableCollection<CustomSalaryField> ActiveCustomFields
        {
            get => _activeCustomFields;
            set => SetProperty(ref _activeCustomFields, value);
        }

        private ObservableCollection<FirmExpense> _firmExpenses = new();
        public ObservableCollection<FirmExpense> FirmExpenses
        {
            get => _firmExpenses;
            set => SetProperty(ref _firmExpenses, value);
        }

        public ICollectionView GroupedEntries { get; }

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand PrevMonthCommand { get; }
        public ICommand NextMonthCommand { get; }
        public ICommand LoadEmployeesCommand { get; }
        public ICommand AddAdvanceCommand { get; }
        public ICommand OpenAdvanceDialogCommand { get; }
        public ICommand CloseAdvanceDialogCommand { get; }
        public ICommand ConfirmAdvanceCommand { get; }
        public ICommand ManageColumnsCommand { get; }
        public ICommand AddExpenseCommand { get; }
        public ICommand RemoveExpenseCommand { get; }
        public ICommand SelectFirmCommand { get; }
        public ICommand MarkAllPaidCommand { get; }
        public ICommand MarkAllUnpaidCommand { get; }
        public ICommand CreateNextMonthCommand { get; }
        public ICommand OpenEmployeeCommand { get; }
        public ICommand ToggleStatsSettingsCommand { get; }
        public ICommand ClearSearchCommand { get; }

        private bool _nextMonthExists;
        public bool NextMonthExists { get => _nextMonthExists; set => SetProperty(ref _nextMonthExists, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private bool _isEmployeeDetailsOpen;
        public bool IsEmployeeDetailsOpen { get => _isEmployeeDetailsOpen; set => SetProperty(ref _isEmployeeDetailsOpen, value); }

        private EmployeeDetailsViewModel? _employeeDetailsVm;
        public EmployeeDetailsViewModel? EmployeeDetailsVm { get => _employeeDetailsVm; set => SetProperty(ref _employeeDetailsVm, value); }

        private int _selectedYear;
        public int SelectedYear
        {
            get => _selectedYear;
            set { SetProperty(ref _selectedYear, value); _ = LoadReportAsync(); }
        }

        private int _selectedMonth;
        public int SelectedMonth
        {
            get => _selectedMonth;
            set { SetProperty(ref _selectedMonth, value); _ = LoadReportAsync(); }
        }

        private string _monthDisplay = string.Empty;
        public string MonthDisplay
        {
            get => _monthDisplay;
            set => SetProperty(ref _monthDisplay, value);
        }

        public int NavigationDirection { get; private set; } = 0;

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set => SetProperty(ref _isDirty, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private decimal _totalGross;
        public decimal TotalGross { get => _totalGross; set => SetProperty(ref _totalGross, value); }

        private decimal _totalNet;
        public decimal TotalNet { get => _totalNet; set { SetProperty(ref _totalNet, value); OnPropertyChanged(nameof(IsNetNegative)); } }
        public bool IsNetNegative => _totalNet < 0;

        private decimal _totalHours;
        public decimal TotalHours { get => _totalHours; set => SetProperty(ref _totalHours, value); }

        private int _totalEmployees;
        public int TotalEmployees { get => _totalEmployees; set => SetProperty(ref _totalEmployees, value); }

        private decimal _grandTotal;
        public decimal GrandTotal { get => _grandTotal; set => SetProperty(ref _grandTotal, value); }

        private decimal _totalExpenses;
        public decimal TotalExpenses { get => _totalExpenses; set => SetProperty(ref _totalExpenses, value); }

        private int _paidCount;
        public int PaidCount { get => _paidCount; set => SetProperty(ref _paidCount, value); }

        private string _paidDisplay = "0/0";
        public string PaidDisplay { get => _paidDisplay; set => SetProperty(ref _paidDisplay, value); }

        private bool _allPaid;
        public bool AllPaid { get => _allPaid; set => SetProperty(ref _allPaid, value); }

        // ===== Extra stats (optional) =====
        private decimal _statPaid;
        public decimal StatPaid { get => _statPaid; set => SetProperty(ref _statPaid, value); }

        private decimal _statRemaining;
        public decimal StatRemaining { get => _statRemaining; set => SetProperty(ref _statRemaining, value); }

        private decimal _statAdvances;
        public decimal StatAdvances { get => _statAdvances; set => SetProperty(ref _statAdvances, value); }

        private decimal _statCustomAdd;
        public decimal StatCustomAdd { get => _statCustomAdd; set => SetProperty(ref _statCustomAdd, value); }

        private decimal _statCustomSub;
        public decimal StatCustomSub { get => _statCustomSub; set => SetProperty(ref _statCustomSub, value); }

        public bool ShowStatPaid
        {
            get => _appSettingsService.Settings.ShowStatPaid;
            set { _appSettingsService.Settings.ShowStatPaid = value; _appSettingsService.SaveSettings(); OnPropertyChanged(nameof(ShowStatPaid)); }
        }
        public bool ShowStatRemaining
        {
            get => _appSettingsService.Settings.ShowStatRemaining;
            set { _appSettingsService.Settings.ShowStatRemaining = value; _appSettingsService.SaveSettings(); OnPropertyChanged(nameof(ShowStatRemaining)); }
        }
        public bool ShowStatAdvances
        {
            get => _appSettingsService.Settings.ShowStatAdvances;
            set { _appSettingsService.Settings.ShowStatAdvances = value; _appSettingsService.SaveSettings(); OnPropertyChanged(nameof(ShowStatAdvances)); }
        }
        public bool ShowStatCustomAdd
        {
            get => _appSettingsService.Settings.ShowStatCustomAdd;
            set { _appSettingsService.Settings.ShowStatCustomAdd = value; _appSettingsService.SaveSettings(); OnPropertyChanged(nameof(ShowStatCustomAdd)); }
        }
        public bool ShowStatCustomSub
        {
            get => _appSettingsService.Settings.ShowStatCustomSub;
            set { _appSettingsService.Settings.ShowStatCustomSub = value; _appSettingsService.SaveSettings(); OnPropertyChanged(nameof(ShowStatCustomSub)); }
        }

        private bool _isStatsSettingsOpen;
        public bool IsStatsSettingsOpen { get => _isStatsSettingsOpen; set => SetProperty(ref _isStatsSettingsOpen, value); }

        private bool _isAdvanceDialogOpen;
        public bool IsAdvanceDialogOpen { get => _isAdvanceDialogOpen; set => SetProperty(ref _isAdvanceDialogOpen, value); }

        private string _advanceName = string.Empty;
        public string AdvanceName { get => _advanceName; set => SetProperty(ref _advanceName, value); }

        private string _advanceAmount = string.Empty;
        public string AdvanceAmount { get => _advanceAmount; set => SetProperty(ref _advanceAmount, value); }

        private string _advanceNote = string.Empty;
        public string AdvanceNote { get => _advanceNote; set => SetProperty(ref _advanceNote, value); }

        private DateTime _advanceDate = DateTime.Today;
        public DateTime AdvanceDate { get => _advanceDate; set => SetProperty(ref _advanceDate, value); }

        private SalaryEntry? _selectedEntry;
        public SalaryEntry? SelectedEntry { get => _selectedEntry; set => SetProperty(ref _selectedEntry, value); }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        private string _selectedFirmFilter;
        public string SelectedFirmFilter
        {
            get => _selectedFirmFilter;
            set
            {
                if (SetProperty(ref _selectedFirmFilter, value))
                {
                    ApplyFilter();
                    LoadExpenses();
                    OnPropertyChanged(nameof(IsFirmFiltered));
                }
            }
        }

        public bool IsFirmFiltered
        {
            get
            {
                var allLabel = L("FinFilterAll") ?? "All";
                return !string.IsNullOrEmpty(_selectedFirmFilter) && _selectedFirmFilter != allLabel;
            }
        }

        public string ExpenseHeaderText
        {
            get
            {
                var title = L("FinFirmExpenses") ?? "Firm Expenses";
                return FirmExpenses.Count > 0 ? $"{title} ({FirmExpenses.Count})" : title;
            }
        }

        public event Action? CustomFieldsChanged;

        public FinanceService Finance => _financeService;

        private sealed record RatePropagationRequest(
            string Key,
            string EmployeeId,
            string EmployeeFolder,
            string FirmName,
            string FullName,
            decimal NewRate,
            int FromYear,
            int FromMonth);

        public SalaryViewModel(
            NavigationService? navigationService = null,
            FinanceService? financeService = null,
            EmployeeService? employeeService = null,
            AppSettingsService? appSettingsService = null,
            ActivityLogService? activityLogService = null,
            EmployeeDetailsViewModelFactory? employeeDetailsViewModelFactory = null,
            DocumentLocalizationService? documentLocalizationService = null,
            CompanyService? companyService = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _financeService = financeService ?? throw new InvalidOperationException("FinanceService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new InvalidOperationException("EmployeeDetailsViewModelFactory is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");

            _selectedYear = DateTime.Now.Year;
            _selectedMonth = DateTime.Now.Month;

            GroupedEntries = CollectionViewSource.GetDefaultView(Entries);
            GroupedEntries.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SalaryEntry.FirmName)));

            _selectedFirmFilter = L("FinFilterAll") ?? "All";
            AvailableFirms.Add(_selectedFirmFilter);

            GoBackCommand = new AsyncRelayCommand(async o =>
            {
                if (await SaveReportAsync())
                    _navigationService.NavigateTo<FinanceTablesViewModel>();
            });
            SaveCommand = new AsyncRelayCommand(async o => await SaveReportAsync());
            ExportExcelCommand = new RelayCommand(o => ExportToExcel());
            PrevMonthCommand = new AsyncRelayCommand(_ => ChangeMonthAsync(-1), _ => !IsLoading);
            NextMonthCommand = new AsyncRelayCommand(_ => ChangeMonthAsync(1), _ => !IsLoading);
            LoadEmployeesCommand = new RelayCommand(o => { });
            OpenAdvanceDialogCommand = new RelayCommand(o => OpenAdvanceDialog());
            CloseAdvanceDialogCommand = new RelayCommand(o => IsAdvanceDialogOpen = false);
            ConfirmAdvanceCommand = new RelayCommand(o => ConfirmAdvance());
            AddAdvanceCommand = new RelayCommand(o => OpenAdvanceDialog());
            ManageColumnsCommand = new RelayCommand(o => OpenManageColumns());
            AddExpenseCommand = new RelayCommand(o => AddExpense());
            RemoveExpenseCommand = new RelayCommand(o => RemoveExpense(o as string));
            SelectFirmCommand = new RelayCommand(o => SelectFirm(o as string));
            MarkAllPaidCommand = new AsyncRelayCommand(async o => await MarkAllPaidAsync());
            MarkAllUnpaidCommand = new AsyncRelayCommand(async o => await MarkAllUnpaidAsync());
            CreateNextMonthCommand = new AsyncRelayCommand(async o => await CreateNextMonthAsync());
            OpenEmployeeCommand = new RelayCommand(o => OpenEmployee(o as SalaryEntry));
            ToggleStatsSettingsCommand = new RelayCommand(o => IsStatsSettingsOpen = !IsStatsSettingsOpen);
            ClearSearchCommand = new RelayCommand(o => SearchText = string.Empty);

            RefreshActiveFields();
            _ = LoadReportAsync();
        }

        public void RefreshActiveFields()
        {
            var visibleFirms = Entries.Select(e => e.FirmName).Distinct().ToList();
            var fields = visibleFirms.Count > 0
                ? _financeService.GetActiveFields(visibleFirms)
                : _financeService.GetCustomFields();

            ActiveCustomFields = new ObservableCollection<CustomSalaryField>(fields);

            var fieldList = ActiveCustomFields.ToList();
            foreach (var entry in Entries)
            {
                entry.FieldDefinitions = fieldList;
                entry.RecalcNet();
            }

            CustomFieldsChanged?.Invoke();
            RecalcTotals();
        }

        private async Task ChangeMonthAsync(int delta)
        {
            NavigationDirection = delta;
            IsLoading = true;
            var date = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(delta);
            try
            {
                var monthResult = await Task.Run(() =>
                    _financeService.TryLoadAllFirmPayments(date.Year, date.Month)).ConfigureAwait(true);

                if (!monthResult.success)
                    return;

                var testEntries = monthResult.entries;

                if (delta > 0 && testEntries.Count == 0)
                    return;

                if (Entries.Count > 0 && !await SaveReportAsync())
                    return;

                _selectedYear = date.Year;
                _selectedMonth = date.Month;
                OnPropertyChanged(nameof(SelectedYear));
                OnPropertyChanged(nameof(SelectedMonth));
                await LoadReportAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static string FolderKey(string path) =>
            System.IO.Path.GetFileName(path?.TrimEnd('\\', '/') ?? "");

        private static string NormalizeEmployeePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('/', '\\').Trim().TrimEnd('\\');
        }

        private static string BuildEmployeeFirmKey(string? employeeId, string? employeeFolder, string? firmName)
        {
            var identity = !string.IsNullOrWhiteSpace(employeeId)
                ? employeeId.Trim()
                : NormalizeEmployeePath(employeeFolder);

            if (string.IsNullOrWhiteSpace(identity))
                identity = FolderKey(employeeFolder ?? string.Empty);

            return identity + "|" + (firmName ?? string.Empty);
        }

        internal static bool ShouldResaveWhenCanonicalSavedEntryDuplicates(HashSet<string> existingKeys, string key)
        {
            return existingKeys.Contains(key);
        }

        internal static bool ShouldReplaceFirmExpenseForSelectedFirm(string? expenseFirmName, string? selectedFirmFilter)
        {
            return string.Equals(expenseFirmName, selectedFirmFilter, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesSalaryEntry(SalaryEntry existingEntry, string? employeeId, string employeeFolder, string firmName)
        {
            if (!string.Equals(existingEntry.FirmName, firmName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(employeeId)
                && !string.IsNullOrWhiteSpace(existingEntry.EmployeeId)
                && string.Equals(existingEntry.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var existingFolder = NormalizeEmployeePath(_financeService.ResolveEmployeeFolder(existingEntry.EmployeeFolder, existingEntry.EmployeeId));
            var currentFolder = NormalizeEmployeePath(_financeService.ResolveEmployeeFolder(employeeFolder, employeeId));
            return string.Equals(existingFolder, currentFolder, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetHourlyRateFromEntries(IReadOnlyList<SalaryEntry> sourceEntries, string? employeeId, string employeeFolder, string firmName, out decimal hourlyRate)
        {
            for (int i = sourceEntries.Count - 1; i >= 0; i--)
            {
                var entry = sourceEntries[i];
                if (!MatchesSalaryEntry(entry, employeeId, employeeFolder, firmName))
                    continue;

                hourlyRate = entry.HourlyRate;
                return true;
            }

            hourlyRate = 0;
            return false;
        }

        private static bool WorkedInMonth(string? startDate, string? endDate, int year, int month)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var start = DateParsingHelper.TryParseDate(startDate ?? string.Empty);
            if (start == null)
                return false;

            if (start.Value > monthEnd)
                return false;

            if (string.IsNullOrWhiteSpace(endDate))
                return true;

            var end = DateParsingHelper.TryParseDate(endDate ?? string.Empty);
            if (end == null)
                return true;

            return end.Value >= monthStart;
        }

        private static bool EmployeeWorkedInMonth(EmployeeSummary emp, int year, int month) =>
            WorkedInMonth(emp.StartDate, emp.EndDate, year, month);

        private static bool ArchivedWorkedInMonth(ArchivedEmployeeSummary arc, int year, int month) =>
            WorkedInMonth(arc.StartDate, arc.EndDate, year, month);

        internal static void AddEmploymentPeriod(
            Dictionary<string, List<(string StartDate, string EndDate)>> employmentByKey,
            string key,
            string? startDate,
            string? endDate)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(startDate))
                return;

            if (!employmentByKey.TryGetValue(key, out var periods))
            {
                periods = new List<(string StartDate, string EndDate)>();
                employmentByKey[key] = periods;
            }

            if (!periods.Any(period =>
                    string.Equals(period.StartDate, startDate, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(period.EndDate, endDate ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
            {
                periods.Add((startDate, endDate ?? string.Empty));
            }
        }

        internal static bool WorkedInAnyEmploymentPeriod(
            IReadOnlyList<(string StartDate, string EndDate)> periods,
            int year,
            int month)
        {
            return periods.Any(period => WorkedInMonth(period.StartDate, period.EndDate, year, month));
        }

        private sealed class SalaryEmployeesSnapshot
        {
            public Dictionary<string, List<EmployeeSummary>> EmployeesByFirm { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, string> FirstEmployeeIdByFullName { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class BuildEntriesTimingMetrics
        {
            public long TotalMs { get; set; }
            public long ArchivedMs { get; set; }
            public long FirmHistoryMs { get; set; }
            public long PeriodMapMs { get; set; }
            public long PrevMonthMs { get; set; }
            public long CurrentMonthMs { get; set; }
            public long CanonicalizeMs { get; set; }
            public long CanonicalizeResolveMs { get; set; }
            public long CanonicalizeIdLookupMs { get; set; }
            public long ActiveMissingMs { get; set; }
            public long ArchivedLoopMs { get; set; }
            public int CompaniesCount { get; set; }
            public int SharedEntriesCount { get; set; }
            public int PrevEntriesCount { get; set; }
            public int HistoryEntriesCount { get; set; }
            public int ActiveEmployeesCount { get; set; }
        }

        private SalaryEmployeesSnapshot BuildEmployeesSnapshot(List<EmployerCompany> companies)
        {
            var snapshot = new SalaryEmployeesSnapshot();

            foreach (var company in companies)
            {
                var employees = _employeeService.GetEmployeesForFirm(company.Name).ToList();
                snapshot.EmployeesByFirm[company.Name] = employees;

                foreach (var employee in employees)
                {
                    if (string.IsNullOrWhiteSpace(employee.FullName) || string.IsNullOrWhiteSpace(employee.UniqueId))
                        continue;

                    snapshot.FirstEmployeeIdByFullName.TryAdd(employee.FullName, employee.UniqueId);
                }
            }

            return snapshot;
        }

        private async Task LoadReportAsync()
        {
            try
            {
            var totalSw = Stopwatch.StartNew();
            long snapshotMs = 0;
            long buildEntriesMs = 0;
            long saveMs = 0;
            long ghostsMs = 0;
            long uiRecalcMs = 0;
            long rebuildFirmFilterMs = 0;
            long refreshActiveFieldsMs = 0;
            long refreshAdvanceSumsMs = 0;
            long expensesMs = 0;
            long nextMonthMs = 0;

            IsLoading = true;
            UpdateMonthDisplay();

            // Capture UI-bound state before going to background
            var fieldList = ActiveCustomFields.ToList();
            var year = _selectedYear;
            var month = _selectedMonth;
            var monthEnd = new DateTime(year, month, 1).AddMonths(1).AddDays(-1);
            var companyService = _companyService;
            var companiesSnapshot = companyService?.Companies?
                .Where(c => companyService.IsCompanyVisibleForPeriod(c, year, month))
                .ToList()
                ?? new List<EmployerCompany>();

            // Build all entries in background — no UI thread blocking
            var snapshotSw = Stopwatch.StartNew();
            var employeesSnapshot = await Task.Run(() => BuildEmployeesSnapshot(companiesSnapshot));
            snapshotMs = snapshotSw.ElapsedMilliseconds;

            BuildEntriesTimingMetrics buildTiming;
            var buildEntriesSw = Stopwatch.StartNew();
            var (newEntries, needResave, activeFoldersByFirm, timedBuildMetrics) = await Task.Run(() =>
                BuildEntriesBackground(fieldList, year, month, monthEnd, companiesSnapshot, employeesSnapshot));
            buildEntriesMs = buildEntriesSw.ElapsedMilliseconds;
            buildTiming = timedBuildMetrics;

            // Set IsFinished (fast loop, no I/O)
            foreach (var entry in newEntries)
            {
                var fn = NormalizeEmployeePath(entry.EmployeeFolder);
                entry.IsFinished = !activeFoldersByFirm.TryGetValue(entry.FirmName, out var active)
                                   || !active.Contains(fn);
            }

            var refreshAdvanceSumsSw = Stopwatch.StartNew();
            await ApplyAdvanceSumsToEntriesAsync(newEntries, year, month);
            refreshAdvanceSumsMs = refreshAdvanceSumsSw.ElapsedMilliseconds;

            // Bulk-update ObservableCollection: DataGrid refreshes exactly once
            foreach (var old in Entries)
                old.PropertyChanged -= OnEntryChanged;

            Entries.Clear();
            _originalNotes.Clear();
            foreach (var entry in newEntries)
            {
                entry.PropertyChanged += OnEntryChanged;
                Entries.Add(entry);
                _originalNotes[BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName)] = entry.Note;
            }

            var uiRecalcSw = Stopwatch.StartNew();
            var rebuildFirmFilterSw = Stopwatch.StartNew();
            RebuildFirmFilter();
            rebuildFirmFilterMs = rebuildFirmFilterSw.ElapsedMilliseconds;

            var refreshActiveFieldsSw = Stopwatch.StartNew();
            RefreshActiveFields();
            refreshActiveFieldsMs = refreshActiveFieldsSw.ElapsedMilliseconds;
            uiRecalcMs = uiRecalcSw.ElapsedMilliseconds;

            var expensesSw = Stopwatch.StartNew();
            LoadExpenses();
            expensesMs = expensesSw.ElapsedMilliseconds;

            if (needResave)
            {
                var saveSw = Stopwatch.StartNew();
                await SaveReportAsync();
                saveMs = saveSw.ElapsedMilliseconds;
            }

            var nextMonthSw = Stopwatch.StartNew();
            await CheckNextMonthExistsAsync();
            nextMonthMs = nextMonthSw.ElapsedMilliseconds;

            totalSw.Stop();
            LoggingService.LogInfo(
                "Timing.BuildEntries",
                $"BuildEntriesBackground {year:D4}-{month:D2} total={buildTiming.TotalMs}ms | " +
                $"archived={buildTiming.ArchivedMs}ms | firmHistory={buildTiming.FirmHistoryMs}ms | " +
                $"periodMap={buildTiming.PeriodMapMs}ms | prev={buildTiming.PrevMonthMs}ms | " +
                $"current={buildTiming.CurrentMonthMs}ms | canonicalize={buildTiming.CanonicalizeMs}ms" +
                $"(resolve={buildTiming.CanonicalizeResolveMs}ms,idLookup={buildTiming.CanonicalizeIdLookupMs}ms) | " +
                $"activeMissing={buildTiming.ActiveMissingMs}ms | archivedLoop={buildTiming.ArchivedLoopMs}ms | " +
                $"companies={buildTiming.CompaniesCount} | sharedEntries={buildTiming.SharedEntriesCount} | " +
                $"prevEntries={buildTiming.PrevEntriesCount} | historyEntries={buildTiming.HistoryEntriesCount} | " +
                $"activeEmployees={buildTiming.ActiveEmployeesCount}");
            LoggingService.LogInfo(
                "Timing.LoadReport",
                $"LoadReportAsync {year:D4}-{month:D2} total={totalSw.ElapsedMilliseconds}ms | " +
                $"Snapshot={snapshotMs}ms | BuildEntries={buildEntriesMs}ms | Save={saveMs}ms | " +
                $"Ghosts={ghostsMs}ms | UIRecalc={uiRecalcMs}ms(rebuild={rebuildFirmFilterMs}ms,activeFields={refreshActiveFieldsMs}ms,advanceSums={refreshAdvanceSumsMs}ms) | Expenses={expensesMs}ms | " +
                $"NextMonth={nextMonthMs}ms");

            IsLoading = false;
            DataLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("SalaryViewModel.LoadReport", ex);
                IsLoading = false;
            }
        }

        private (List<SalaryEntry> entries, bool needResave, Dictionary<string, HashSet<string>> activeFoldersByFirm, BuildEntriesTimingMetrics timing)
            BuildEntriesBackground(List<CustomSalaryField> fieldList, int year, int month, DateTime monthEnd,
                                   List<EmployerCompany> companies, SalaryEmployeesSnapshot employeesSnapshot)
        {
            var totalSw = Stopwatch.StartNew();
            var timing = new BuildEntriesTimingMetrics
            {
                CompaniesCount = companies.Count,
                ActiveEmployeesCount = employeesSnapshot.EmployeesByFirm.Values.Sum(v => v.Count)
            };
            var entries = new List<SalaryEntry>();
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool needResave = false;
            var activeFoldersByFirm = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            var allHistory = new List<ArchivedEmployeeSummary>();
            var archivedSw = Stopwatch.StartNew();
            allHistory.AddRange(_employeeService.GetArchivedEmployees());
            timing.ArchivedMs = archivedSw.ElapsedMilliseconds;

            var firmHistorySw = Stopwatch.StartNew();
            allHistory.AddRange(_employeeService.GetActiveEmployeeFirmHistory());
            timing.FirmHistoryMs = firmHistorySw.ElapsedMilliseconds;
            timing.HistoryEntriesCount = allHistory.Count;

            // Build employment period map for both active and archived/history employees.
            var periodMapSw = Stopwatch.StartNew();
            var employmentByKey = new Dictionary<string, List<(string StartDate, string EndDate)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var company in companies)
                foreach (var emp in GetEmployeesForFirmSnapshot(company.Name, employeesSnapshot))
                {
                    var key = BuildEmployeeFirmKey(emp.UniqueId, emp.EmployeeFolder, company.Name);
                    AddEmploymentPeriod(employmentByKey, key, emp.StartDate, emp.EndDate);
                }

            foreach (var arc in allHistory)
            {
                if (string.IsNullOrEmpty(arc.FirmName))
                    continue;

                var key = BuildEmployeeFirmKey(arc.UniqueId, arc.EmployeeFolder, arc.FirmName);
                AddEmploymentPeriod(employmentByKey, key, arc.StartDate, arc.EndDate);
            }
            timing.PeriodMapMs = periodMapSw.ElapsedMilliseconds;

            // Load previous month notes for carry-forward (must be before sharedEntries loop)
            int prevYear = month == 1 ? year - 1 : year;
            int prevMonth = month == 1 ? 12 : month - 1;
            var prevMonthSw = Stopwatch.StartNew();
            var prevMonthResult = _financeService.TryLoadAllFirmPayments(prevYear, prevMonth);
            var prevEntries = prevMonthResult.success ? prevMonthResult.entries : new List<SalaryEntry>();
            timing.PrevMonthMs = prevMonthSw.ElapsedMilliseconds;
            timing.PrevEntriesCount = prevEntries.Count;
            var prevNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pe in prevEntries)
                if (!string.IsNullOrEmpty(pe.Note))
                    prevNotes[BuildEmployeeFirmKey(pe.EmployeeId, pe.EmployeeFolder, pe.FirmName)] = pe.Note;

            // Load saved payments
            var currentMonthSw = Stopwatch.StartNew();
            var currentMonthResult = _financeService.TryLoadAllFirmPayments(year, month);
            var sharedEntries = currentMonthResult.success ? currentMonthResult.entries : new List<SalaryEntry>();
            timing.CurrentMonthMs = currentMonthSw.ElapsedMilliseconds;
            timing.SharedEntriesCount = sharedEntries.Count;

            var canonicalizeSw = Stopwatch.StartNew();
            foreach (var entry in sharedEntries)
            {
                var idLookupSw = Stopwatch.StartNew();
                var canonicalId = TryResolveEmployeeIdBackground(entry.EmployeeFolder, entry.FullName, employeesSnapshot, out var resolveInsideLookupMs);
                timing.CanonicalizeResolveMs += resolveInsideLookupMs;
                timing.CanonicalizeIdLookupMs += Math.Max(0, idLookupSw.ElapsedMilliseconds - resolveInsideLookupMs);
                if (!string.IsNullOrEmpty(canonicalId)
                    && !string.Equals(entry.EmployeeId, canonicalId, StringComparison.OrdinalIgnoreCase))
                {
                    entry.EmployeeId = canonicalId;
                    needResave = true;
                }

                var resolveSw = Stopwatch.StartNew();
                var resolved = _financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId);
                timing.CanonicalizeResolveMs += resolveSw.ElapsedMilliseconds;
                if (resolved != entry.EmployeeFolder) { entry.EmployeeFolder = resolved; needResave = true; }

                var key = BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName);
                if (!employmentByKey.TryGetValue(key, out var employmentPeriods)
                    || !WorkedInAnyEmploymentPeriod(employmentPeriods, year, month))
                {
                    needResave = true;
                    continue;
                }

                if (ShouldResaveWhenCanonicalSavedEntryDuplicates(existingKeys, key))
                {
                    needResave = true;
                    continue;
                }

                entry.FieldDefinitions = fieldList;
                entry.RecalcNet();
                entries.Add(entry);
                existingKeys.Add(key);
            }
            timing.CanonicalizeMs = canonicalizeSw.ElapsedMilliseconds;

            // Add active employees missing from saved data
            var activeMissingSw = Stopwatch.StartNew();
            foreach (var company in companies)
            {
                var employees = GetEmployeesForFirmSnapshot(company.Name, employeesSnapshot);
                var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var emp in employees)
                {
                    var normalizedFolder = NormalizeEmployeePath(emp.EmployeeFolder);
                    if (emp.Status == "Active") activeNames.Add(normalizedFolder);
                    if (emp.Status != "Active") continue;
                    if (!EmployeeWorkedInMonth(emp, year, month)) continue;

                    var key = BuildEmployeeFirmKey(emp.UniqueId, emp.EmployeeFolder, company.Name);
                    if (existingKeys.Contains(key)) continue;

                    prevNotes.TryGetValue(key, out var inheritedNote);
                    var entry = new SalaryEntry
                    {
                        EmployeeId    = emp.UniqueId,
                        EmployeeFolder = emp.EmployeeFolder,
                        FullName      = emp.FullName,
                        FirmName      = company.Name,
                        HourlyRate    = TryGetHourlyRateFromEntries(prevEntries, emp.UniqueId, emp.EmployeeFolder, company.Name, out var previousRate)
                            ? previousRate
                            : GetDefaultRate(emp.EmployeeFolder),
                        HoursWorked   = 0,
                        Note          = inheritedNote ?? string.Empty,
                        FieldDefinitions = fieldList
                    };
                    entry.RecalcNet();
                    entries.Add(entry);
                    existingKeys.Add(key);
                }
                activeFoldersByFirm[company.Name] = activeNames;
            }
            timing.ActiveMissingMs = activeMissingSw.ElapsedMilliseconds;

            var archivedLoopSw = Stopwatch.StartNew();
            foreach (var arc in allHistory)
            {
                if (!ArchivedWorkedInMonth(arc, year, month)) continue;
                var firmName = arc.FirmName;
                if (string.IsNullOrEmpty(firmName)) continue;
                var key = BuildEmployeeFirmKey(arc.UniqueId, arc.EmployeeFolder, firmName);
                if (existingKeys.Contains(key)) continue;

                prevNotes.TryGetValue(key, out var inheritedNote);
                var historyRecord = TryGetSalaryHistoryRecord(arc.EmployeeFolder, arc.UniqueId, firmName, year, month);
                var entry = historyRecord != null
                    ? CreateSalaryEntryFromHistory(historyRecord, arc.UniqueId, arc.EmployeeFolder, arc.FullName, firmName, fieldList)
                    : new SalaryEntry
                    {
                        EmployeeId     = arc.UniqueId,
                        EmployeeFolder = arc.EmployeeFolder,
                        FullName       = arc.FullName,
                        FirmName       = firmName,
                        HourlyRate     = TryGetHourlyRateFromEntries(prevEntries, arc.UniqueId, arc.EmployeeFolder, firmName, out var previousRate)
                            ? previousRate
                            : GetDefaultRate(arc.EmployeeFolder),
                        HoursWorked    = 0,
                        Note           = inheritedNote ?? string.Empty,
                        FieldDefinitions = fieldList
                    };
                if (historyRecord == null)
                    entry.RecalcNet();
                entries.Add(entry);
                existingKeys.Add(key);
            }
            timing.ArchivedLoopMs = archivedLoopSw.ElapsedMilliseconds;
            timing.TotalMs = totalSw.ElapsedMilliseconds;

            return (entries, needResave, activeFoldersByFirm, timing);
        }

        private static List<EmployeeSummary> GetEmployeesForFirmSnapshot(string companyName, SalaryEmployeesSnapshot snapshot)
        {
            return snapshot.EmployeesByFirm.TryGetValue(companyName, out var employees)
                ? employees
                : new List<EmployeeSummary>();
        }

        private string? TryResolveEmployeeIdBackground(string employeeFolder, string fullName,
                                                        SalaryEmployeesSnapshot employeesSnapshot, out long resolveMs)
        {
            var resolveSw = Stopwatch.StartNew();
            var folder = _financeService.ResolveEmployeeFolder(employeeFolder);
            resolveMs = resolveSw.ElapsedMilliseconds;
            if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
            {
                var data = _employeeService.LoadEmployeeData(folder);
                if (data != null && !string.IsNullOrEmpty(data.UniqueId))
                    return data.UniqueId;
            }

            return employeesSnapshot.FirstEmployeeIdByFullName.TryGetValue(fullName, out var employeeId)
                ? employeeId
                : null;
        }

        private string? TryResolveEmployeeId(string employeeFolder, string fullName)
        {
            var folder = _financeService.ResolveEmployeeFolder(employeeFolder);
            if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
            {
                var data = _employeeService.LoadEmployeeData(folder);
                if (data != null && !string.IsNullOrEmpty(data.UniqueId))
                    return data.UniqueId;
            }

            var companiesForResolve = _companyService.Companies;
            if (companiesForResolve != null)
            {
                foreach (var company in companiesForResolve)
                {
                    var employees = _employeeService.GetEmployeesForFirm(company.Name);
                    var match = employees.FirstOrDefault(e => e.FullName == fullName && !string.IsNullOrEmpty(e.UniqueId));
                    if (match != null) return match.UniqueId;
                }
            }

            return null;
        }

        private SalaryHistoryRecord? TryGetSalaryHistoryRecord(string employeeFolder, string? employeeId, string firmName, int year, int month)
        {
            try
            {
                var resolvedFolder = _financeService.ResolveEmployeeFolder(employeeFolder, employeeId);
                var history = _financeService.SalaryHistoryService.LoadSalaryHistoryFromResolvedFolder(resolvedFolder, employeeId);
                return history.FirstOrDefault(record =>
                    record.Year == year
                    && record.Month == month
                    && string.Equals(record.FirmName, firmName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("SalaryViewModel.TryGetSalaryHistoryRecord", ex);
                return null;
            }
        }

        private static SalaryEntry CreateSalaryEntryFromHistory(
            SalaryHistoryRecord record,
            string employeeId,
            string employeeFolder,
            string fullName,
            string firmName,
            List<CustomSalaryField> fieldList)
        {
            var entry = new SalaryEntry
            {
                EmployeeId = employeeId,
                EmployeeFolder = employeeFolder,
                FullName = string.IsNullOrWhiteSpace(record.FullName) ? fullName : record.FullName,
                FirmName = firmName,
                HoursWorked = record.HoursWorked,
                HourlyRate = record.HourlyRate,
                Advance = record.Advance,
                SavedNetSalary = record.NetSalary,
                Status = "paid",
                Note = record.Note ?? string.Empty,
                CustomValues = new Dictionary<string, decimal>(record.CustomValues, StringComparer.OrdinalIgnoreCase),
                FieldDefinitions = fieldList
            };
            entry.RecalcNet();
            return entry;
        }

        private async Task CheckNextMonthExistsAsync()
        {
            try
            {
                var year = _selectedYear;
                var month = _selectedMonth;
                var next = new DateTime(year, month, 1).AddMonths(1);
                var hasEntries = await Task.Run(() => _financeService.MonthDataExists(next.Year, next.Month));

                if (_selectedYear != year || _selectedMonth != month)
                    return;

                NextMonthExists = hasEntries;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("SalaryViewModel.CheckNextMonthExistsAsync", ex);
            }
        }

        private static readonly HashSet<string> _recalcProperties = new()
        {
            nameof(SalaryEntry.HoursWorked), nameof(SalaryEntry.HourlyRate),
            nameof(SalaryEntry.GrossSalary), nameof(SalaryEntry.NetSalary),
            nameof(SalaryEntry.Advance), nameof(SalaryEntry.IsPaid), "Item[]"
        };

        private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _recalcProperties.Contains(e.PropertyName))
                RecalcTotals();

            IsDirty = true;

            if (e.PropertyName == nameof(SalaryEntry.HourlyRate) && sender is SalaryEntry entry)
                ScheduleRatePropagation(entry);
        }

        private void ScheduleRatePropagation(SalaryEntry entry)
        {
            var key = BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName);
            var request = new RatePropagationRequest(
                key,
                entry.EmployeeId,
                entry.EmployeeFolder,
                entry.FirmName,
                entry.FullName,
                entry.HourlyRate,
                _selectedYear,
                _selectedMonth);
            var cts = new CancellationTokenSource();
            CancellationTokenSource? previous = null;

            lock (_ratePropagationGate)
            {
                if (_ratePropagationCtsByKey.TryGetValue(key, out var existing))
                    previous = existing;

                _ratePropagationCtsByKey[key] = cts;
            }

            previous?.Cancel();
            previous?.Dispose();
            _ = RunRatePropagationAsync(request, cts);
        }

        private async Task RunRatePropagationAsync(RatePropagationRequest request, CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(500, cts.Token);
                await Task.Run(() => PropagateRateForwardCore(request, cts.Token), cts.Token);
            }
            catch (OperationCanceledException)
            {
                // A newer rate edit superseded this propagation.
            }
            catch (Exception ex)
            {
                LoggingService.LogError("SalaryViewModel.PropagateRateForwardAsync", ex);
            }
            finally
            {
                lock (_ratePropagationGate)
                {
                    if (_ratePropagationCtsByKey.TryGetValue(request.Key, out var current) && ReferenceEquals(current, cts))
                        _ratePropagationCtsByKey.Remove(request.Key);
                }

                cts.Dispose();
            }
        }

        private void PropagateRateForwardCore(RatePropagationRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _financeService.UpdateHourlyRateForward(request.EmployeeId, request.EmployeeFolder, request.FirmName, request.NewRate, request.FromYear, request.FromMonth, token);
        }

        private void InitFirstMonth()
        {
            var fieldList = ActiveCustomFields.ToList();
            var initMonthEnd = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1).AddDays(-1);

            var companyService = _companyService;
            var companiesInit = companyService?.Companies?
                .Where(c => companyService.IsCompanyVisibleForPeriod(c, _selectedYear, _selectedMonth))
                .ToList();
            if (companiesInit != null)
            {
                foreach (var company in companiesInit)
                {
                    var employees = _employeeService.GetEmployeesForFirm(company.Name);
                foreach (var emp in employees.Where(e => e.Status == "Active" && !e.FullName.Contains("Archived")))
                {
                    if (!EmployeeWorkedInMonth(emp, _selectedYear, _selectedMonth))
                        continue;

                    var entry = new SalaryEntry
                    {
                        EmployeeId = emp.UniqueId,
                        EmployeeFolder = emp.EmployeeFolder,
                        FullName = emp.FullName,
                        FirmName = company.Name,
                        HourlyRate = GetDefaultRate(emp.EmployeeFolder),
                        HoursWorked = 0,
                        FieldDefinitions = fieldList
                    };

                    entry.RecalcNet();
                    entry.PropertyChanged += OnEntryChanged;
                    Entries.Add(entry);
                }
                }
            }
        }

        private async Task CreateNextMonthAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("створити наступний місяць зарплат"))
                return;

            if (!await SaveReportAsync())
                return;

            var next = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1);
            _selectedYear = next.Year;
            _selectedMonth = next.Month;
            OnPropertyChanged(nameof(SelectedYear));
            OnPropertyChanged(nameof(SelectedMonth));
            UpdateMonthDisplay();

            var previousEntries = Entries.ToList();

            foreach (var old in previousEntries)
                old.PropertyChanged -= OnEntryChanged;

            foreach (var e in previousEntries)
            {
                var canonicalId = TryResolveEmployeeId(e.EmployeeFolder, e.FullName);
                if (!string.IsNullOrEmpty(canonicalId))
                    e.EmployeeId = canonicalId;

                var resolvedFolder = _financeService.ResolveEmployeeFolder(e.EmployeeFolder, e.EmployeeId);
                if (!string.IsNullOrEmpty(resolvedFolder))
                    e.EmployeeFolder = resolvedFolder;
            }

            var prevNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pe in previousEntries)
            {
                if (!string.IsNullOrEmpty(pe.Note))
                    prevNotes[BuildEmployeeFirmKey(pe.EmployeeId, pe.EmployeeFolder, pe.FirmName)] = pe.Note;
            }

            Entries.Clear();

            var fieldList = ActiveCustomFields.ToList();
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var nextMonthEnd = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1).AddDays(-1);
            var companyService = _companyService;
            var companiesCreate = companyService?.Companies?
                .Where(c => companyService.IsCompanyVisibleForPeriod(c, _selectedYear, _selectedMonth))
                .ToList();
            if (companiesCreate != null)
            {
                foreach (var company in companiesCreate)
                {
                    var employees = _employeeService.GetEmployeesForFirm(company.Name);
                foreach (var emp in employees.Where(e => e.Status == "Active"))
                {
                    if (!EmployeeWorkedInMonth(emp, _selectedYear, _selectedMonth))
                        continue;

                    var key = BuildEmployeeFirmKey(emp.UniqueId, emp.EmployeeFolder, company.Name);
                    if (existingKeys.Contains(key)) continue;

                    prevNotes.TryGetValue(key, out var inheritedNote);

                    var entry = new SalaryEntry
                    {
                        EmployeeId = emp.UniqueId,
                        EmployeeFolder = emp.EmployeeFolder,
                        FullName = emp.FullName,
                        FirmName = company.Name,
                        HourlyRate = TryGetHourlyRateFromEntries(previousEntries, emp.UniqueId, emp.EmployeeFolder, company.Name, out var prevRate)
                            ? prevRate
                            : GetDefaultRate(emp.EmployeeFolder),
                        HoursWorked = 0,
                        Note = inheritedNote ?? string.Empty,
                        FieldDefinitions = fieldList
                    };

                    entry.RecalcNet();
                    entry.PropertyChanged += OnEntryChanged;
                    Entries.Add(entry);
                    existingKeys.Add(key);
                }
                }
            }

            var allHistoryEntries = new List<ArchivedEmployeeSummary>();
            allHistoryEntries.AddRange(_employeeService.GetArchivedEmployees());
            allHistoryEntries.AddRange(_employeeService.GetActiveEmployeeFirmHistory());

            foreach (var arc in allHistoryEntries)
            {
                if (!ArchivedWorkedInMonth(arc, _selectedYear, _selectedMonth))
                    continue;

                var firmName = arc.FirmName;
                if (string.IsNullOrEmpty(firmName)) continue;

                    var key = BuildEmployeeFirmKey(arc.UniqueId, arc.EmployeeFolder, firmName);
                if (existingKeys.Contains(key)) continue;

                prevNotes.TryGetValue(key, out var inheritedNote);

                var entry = new SalaryEntry
                {
                        EmployeeId = arc.UniqueId,
                    EmployeeFolder = arc.EmployeeFolder,
                    FullName = arc.FullName,
                    FirmName = firmName,
                    HourlyRate = TryGetHourlyRateFromEntries(previousEntries, arc.UniqueId, arc.EmployeeFolder, firmName, out var prevArcRate)
                        ? prevArcRate
                        : GetDefaultRate(arc.EmployeeFolder),
                    HoursWorked = 0,
                    Note = inheritedNote ?? string.Empty,
                    FieldDefinitions = fieldList
                };

                entry.RecalcNet();
                entry.PropertyChanged += OnEntryChanged;
                Entries.Add(entry);
                existingKeys.Add(key);
            }

            RebuildFirmFilter();
            RefreshActiveFields();
            await RefreshAdvanceSumsAsync();
            LoadExpenses();
            await SaveReportAsync();
            await CheckNextMonthExistsAsync();
        }

        private decimal GetDefaultRate(string employeeFolder)
        {
            var jsonPath = System.IO.Path.Combine(employeeFolder, "employee.json");
            if (System.IO.File.Exists(jsonPath))
            {
                try
                {
                    var json = SafeFileService.ReadAllText(jsonPath);
                    var data = System.Text.Json.JsonSerializer.Deserialize<EmployeeModels.EmployeeData>(json);
                    if (data != null && data.HourlySalary > 0)
                        return data.HourlySalary;
                }
                catch (Exception ex) { LoggingService.LogError("SalaryViewModel.GetDefaultRate", ex); }
            }
            return 160;
        }

        private void RebuildFirmFilter()
        {
            var allLabel = L("FinFilterAll") ?? "All";
            var firms = Entries.Select(e => e.FirmName).Distinct().OrderBy(f => f).ToList();

            AvailableFirms = new ObservableCollection<string>(new[] { allLabel }.Concat(firms));

            if (_selectedFirmFilter != allLabel && !firms.Contains(_selectedFirmFilter))
                SelectedFirmFilter = allLabel;
        }

        private void ApplyFilter()
        {
            var allLabel = L("FinFilterAll") ?? "All";
            bool hasFirmFilter = !string.IsNullOrEmpty(_selectedFirmFilter) && _selectedFirmFilter != allLabel;
            bool hasSearch = !string.IsNullOrWhiteSpace(_searchText);

            if (!hasFirmFilter && !hasSearch)
                GroupedEntries.Filter = null;
            else
            {
                var search = _searchText?.Trim() ?? "";
                GroupedEntries.Filter = obj =>
                {
                    if (obj is not SalaryEntry e) return false;
                    if (hasFirmFilter && e.FirmName != _selectedFirmFilter) return false;
                    if (hasSearch && !(e.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                        || e.FirmName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)) return false;
                    return true;
                };
            }

            GroupedEntries.Refresh();
            RecalcTotals();
        }

        private IEnumerable<SalaryEntry> VisibleEntries()
        {
            var allLabel = L("FinFilterAll") ?? "All";
            IEnumerable<SalaryEntry> result = Entries;
            if (!string.IsNullOrEmpty(_selectedFirmFilter) && _selectedFirmFilter != allLabel)
                result = result.Where(e => e.FirmName == _selectedFirmFilter);
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var search = _searchText.Trim();
                result = result.Where(e => e.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
            }
            return result;
        }

        private void RecalcTotals()
        {
            var visible = VisibleEntries().ToList();
            TotalGross = visible.Sum(e => e.GrossSalary);
            TotalNet = visible.Sum(e => e.NetSalary);
            TotalHours = visible.Sum(e => e.HoursWorked);
            TotalEmployees = visible.Count;
            TotalExpenses = FirmExpenses.Sum(ex => ex.Amount);
            GrandTotal = TotalNet + TotalExpenses;

            PaidCount = visible.Count(e => e.IsPaid);
            PaidDisplay = $"{PaidCount}/{visible.Count}";
            AllPaid = visible.Count > 0 && PaidCount == visible.Count;

            StatPaid = visible.Where(e => e.IsPaid).Sum(e => e.NetSalary);
            StatRemaining = visible.Where(e => !e.IsPaid).Sum(e => e.NetSalary);
            StatAdvances = visible.Sum(e => e.Advance);

            decimal addSum = 0, subSum = 0;
            foreach (var entry in visible)
            {
                if (entry.FieldDefinitions == null) continue;
                foreach (var f in entry.FieldDefinitions)
                {
                    if (!entry.CustomValues.TryGetValue(f.Id, out var val) || val == 0) continue;
                    if (f.Operation == FieldOperation.Add) addSum += val;
                    else if (f.Operation == FieldOperation.Subtract) subSum += val;
                }
            }
            StatCustomAdd = addSum;
            StatCustomSub = subSum;

            var summarySource = string.IsNullOrWhiteSpace(_searchText) ? Entries : (IEnumerable<SalaryEntry>)visible;
            var allGroups = summarySource.GroupBy(e => e.FirmName);
            FirmSummaries = new ObservableCollection<FirmSalarySummary>(
                allGroups
                    .OrderByDescending(g => g.Sum(e => e.GrossSalary))
                    .Select(g => new FirmSalarySummary
                {
                    FirmName = g.Key,
                    TotalGross = g.Sum(e => e.GrossSalary),
                    TotalNet = g.Sum(e => e.NetSalary),
                    TotalHours = g.Sum(e => e.HoursWorked),
                    EmployeeCount = g.Count(),
                    PaidCount = g.Count(e => e.IsPaid),
                    IsSelected = g.Key == _selectedFirmFilter
                }));
        }

        private void SaveReport()
            => _ = SaveReportAsync();

        private async Task<bool> SaveReportAsync()
        {
            await _saveReportGate.WaitAsync();
            try
            {
                if (!PolicyService.EnsureWriteAllowed("зберегти зарплатний звіт"))
                    return false;

                var saveYear = _selectedYear;
                var saveMonth = _selectedMonth;
                var expensesForSave = BuildExpensesForSave(saveYear, saveMonth);

                if (!_financeService.SaveAllFirmPayments(saveYear, saveMonth, Entries.ToList(), expensesForSave))
                {
                    StatusMessage = !string.IsNullOrWhiteSpace(_financeService.LastSalaryConflictMessage)
                        ? _financeService.LastSalaryConflictMessage
                        : !string.IsNullOrWhiteSpace(_financeService.LastSaveRecoveryPath)
                            ? (L("FinSalarySaveRecoveryCreated") ?? "Could not update the main file, but new data was saved to a recovery file.")
                            : (L("FinSalarySaveGenericError") ?? "Failed to save salary file.");
                    return false;
                }

                foreach (var entry in Entries)
                    entry.SavedNetSalary = entry.NetSalary;

                var changedNotes = CaptureNotePropagationChanges();

                // Update snapshot after save
                _originalNotes.Clear();
                foreach (var entry in Entries)
                    _originalNotes[BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName)] = entry.Note;

                IsDirty = false;
                StatusMessage = L("FinSalarySaved") is string s && s.Length > 0 ? s : "Saved!";

                if (changedNotes.Count > 0)
                    await PropagateNoteChangesForwardAsync(changedNotes, saveYear, saveMonth);

                return true;
            }
            finally
            {
                _saveReportGate.Release();
            }
        }

        private List<NotePropagationChange> CaptureNotePropagationChanges()
        {
            var changed = new List<NotePropagationChange>();
            foreach (var entry in Entries)
            {
                var key = BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName);
                var oldNote = _originalNotes.TryGetValue(key, out var o) ? o : string.Empty;
                if (oldNote != entry.Note)
                {
                    changed.Add(new NotePropagationChange
                    {
                        EmployeeId = entry.EmployeeId ?? string.Empty,
                        EmployeeFolder = entry.EmployeeFolder ?? string.Empty,
                        FirmName = entry.FirmName ?? string.Empty,
                        OldNote = oldNote,
                        NewNote = entry.Note ?? string.Empty
                    });
                }
            }

            return changed;
        }

        private void ScheduleNotePropagation(List<NotePropagationChange> changed, int fromYear, int fromMonth)
        {
            if (changed.Count == 0)
                return;

            var cts = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _notePropagationCts, cts);
            previous?.Cancel();
            previous?.Dispose();
            _ = RunNotePropagationAsync(changed, fromYear, fromMonth, cts);
        }

        private Task PropagateNoteChangesForwardAsync(
            List<NotePropagationChange> changed,
            int fromYear,
            int fromMonth)
        {
            return Task.Run(() => PropagateNoteChangesForwardCore(changed, fromYear, fromMonth, CancellationToken.None));
        }

        private async Task RunNotePropagationAsync(
            List<NotePropagationChange> changed,
            int fromYear,
            int fromMonth,
            CancellationTokenSource cts)
        {
            try
            {
                await Task.Run(() => PropagateNoteChangesForwardCore(changed, fromYear, fromMonth, cts.Token), cts.Token);
            }
            catch (OperationCanceledException)
            {
                // A newer save superseded this propagation.
            }
            catch (Exception ex)
            {
                LoggingService.LogError("SalaryViewModel.PropagateNoteChangesForwardAsync", ex);
            }
            finally
            {
                if (ReferenceEquals(_notePropagationCts, cts))
                    _notePropagationCts = null;

                cts.Dispose();
            }
        }

        private void PropagateNoteChangesForwardCore(
            List<NotePropagationChange> changed,
            int fromYear,
            int fromMonth,
            CancellationToken token)
        {
            if (changed.Count == 0)
                return;

            // Walk forward month by month and propagate
            var date = new DateTime(fromYear, fromMonth, 1).AddMonths(1);
            for (int i = 0; i < 24; i++) // max 24 months forward
            {
                token.ThrowIfCancellationRequested();
                var futureMonthResult = _financeService.TryLoadAllFirmPayments(date.Year, date.Month);
                if (!futureMonthResult.success)
                    break;

                var futureEntries = futureMonthResult.entries;
                var futureExpenses = futureMonthResult.expenses;
                if (futureEntries.Count == 0) break;

                CanonicalizeSalaryEntriesForPropagation(futureEntries);

                bool anyUpdated = false;
                foreach (var fe in futureEntries)
                {
                    var matched = FindChangedNoteForEntry(changed, fe);
                    if (matched == null)
                        continue;

                    var change = matched;

                    // Only overwrite if future note == old note OR future note == new note (already propagated)
                    if (fe.Note == change.OldNote || fe.Note == change.NewNote || string.IsNullOrEmpty(fe.Note))
                    {
                        fe.Note = change.NewNote;
                        anyUpdated = true;
                    }
                }

                token.ThrowIfCancellationRequested();
                if (anyUpdated && !_financeService.SaveAllFirmPayments(date.Year, date.Month, futureEntries, futureExpenses))
                {
                    LoggingService.LogWarning(
                        "SalaryViewModel.PropagateNoteChangesForward",
                        $"Failed to save propagated notes for {date.Year:D4}-{date.Month:D2}.");
                    break;
                }

                date = date.AddMonths(1);
            }
        }

        private NotePropagationChange? FindChangedNoteForEntry(
            List<NotePropagationChange> changed,
            SalaryEntry futureEntry)
        {
            foreach (var changedEntry in changed)
            {
                if (!MatchesSalaryEntry(futureEntry, changedEntry.EmployeeId, changedEntry.EmployeeFolder, changedEntry.FirmName))
                    continue;

                return changedEntry;
            }

            return null;
        }

        private sealed class NotePropagationChange
        {
            public string EmployeeId { get; init; } = string.Empty;
            public string EmployeeFolder { get; init; } = string.Empty;
            public string FirmName { get; init; } = string.Empty;
            public string OldNote { get; init; } = string.Empty;
            public string NewNote { get; init; } = string.Empty;
        }

        private void CanonicalizeSalaryEntriesForPropagation(List<SalaryEntry> entries)
        {
            var companyService = _companyService;
            var companies = companyService?.Companies?.ToList() ?? new List<EmployerCompany>();
            var employeesSnapshot = BuildEmployeesSnapshot(companies);

            foreach (var entry in entries)
            {
                var canonicalId = TryResolveEmployeeIdBackground(entry.EmployeeFolder, entry.FullName, employeesSnapshot, out _);
                if (!string.IsNullOrEmpty(canonicalId)
                    && !string.Equals(entry.EmployeeId, canonicalId, StringComparison.OrdinalIgnoreCase))
                {
                    entry.EmployeeId = canonicalId;
                }

                var resolvedFolder = _financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId);
                if (!string.Equals(resolvedFolder, entry.EmployeeFolder, StringComparison.OrdinalIgnoreCase))
                    entry.EmployeeFolder = resolvedFolder;
            }
        }

        private void UpdateMonthDisplay()
        {
            MonthDisplay = $"{_selectedMonth}.{_selectedYear}";
        }

        private void OpenAdvanceDialog()
        {
            if (!PolicyService.EnsureWriteAllowed("додати аванс"))
                return;

            if (SelectedEntry != null)
                AdvanceName = SelectedEntry.FullName;
            AdvanceAmount = "";
            AdvanceNote = "";
            AdvanceDate = DateTime.Today;
            IsAdvanceDialogOpen = true;
        }

        private void ConfirmAdvance()
        {
            if (!PolicyService.EnsureWriteAllowed("підтвердити аванс"))
                return;

            if (!decimal.TryParse(AdvanceAmount.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                StatusMessage = L("FinAdvanceInvalid") ?? "Invalid amount";
                return;
            }

            var target = SelectedEntry ?? Entries.FirstOrDefault(e => e.FullName == AdvanceName);
            if (target == null)
            {
                StatusMessage = L("FinAdvanceNotFound") ?? "Employee not found";
                return;
            }

            var advance = new AdvancePayment
            {
                EmployeeFolder = target.EmployeeFolder,
                EmployeeName = target.FullName,
                CompanyId = target.FirmName,
                Amount = amount,
                Date = AdvanceDate,
                Month = $"{_selectedYear:D4}-{_selectedMonth:D2}",
                Note = AdvanceNote
            };
            _financeService.AddAdvance(advance);

            _activityLogService.Log("AdvanceAdded", "Advance", target.FirmName, target.FullName,
                $"Аванс {amount:N0} Kč → {target.FullName} ({target.FirmName})",
                "", amount.ToString("N0"),
                employeeFolder: target.EmployeeFolder);

            RefreshAdvanceSums();

            IsAdvanceDialogOpen = false;
            var msg = L("FinAdvanceAdded") ?? "Advance {0} Kč → {1}";
            StatusMessage = string.Format(msg, amount.ToString("N0"), target.FullName);
        }

        private void RefreshAdvanceSums()
        {
            _ = RefreshAdvanceSumsAsync();
        }

        private async Task ApplyAdvanceSumsToEntriesAsync(IReadOnlyList<SalaryEntry> entries, int year, int month)
        {
            var monthKey = $"{year:D4}-{month:D2}";
            var requests = entries
                .Select(entry => (
                    requestKey: BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName),
                    employeeId: entry.EmployeeId,
                    employeeFolder: entry.EmployeeFolder,
                    firmName: entry.FirmName))
                .Distinct()
                .ToList();

            var advanceRequests = requests
                .Select(request => (request.requestKey, request.employeeId, request.employeeFolder, request.firmName))
                .ToList();

            var advancesTask = Task.Run(() =>
                _financeService.GetTotalAdvancesForEmployeeFirms(advanceRequests, monthKey));
            var debtTask = Task.Run(() =>
                _financeService.CalculateCarriedDebtForEntries(requests, year, month));

            await Task.WhenAll(advancesTask, debtTask);
            var currentAdvancesByRequest = advancesTask.Result;
            var carriedDebtByRequest = debtTask.Result;

            foreach (var entry in entries)
            {
                var requestKey = BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName);
                currentAdvancesByRequest.TryGetValue(requestKey, out var currentAdvances);
                carriedDebtByRequest.TryGetValue(requestKey, out var carriedDebt);
                entry.Advance = currentAdvances + carriedDebt;
            }
        }

        private async Task RefreshAdvanceSumsAsync()
        {
            var totalSw = Stopwatch.StartNew();
            var monthKey = $"{_selectedYear:D4}-{_selectedMonth:D2}";
            var year = _selectedYear;
            var month = _selectedMonth;
            var version = Interlocked.Increment(ref _advanceRefreshVersion);
            long advancesTaskMs = 0;
            long debtTaskMs = 0;
            long applyMs = 0;
            var requests = Entries
                .Select(entry => (
                    requestKey: BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName),
                    employeeId: entry.EmployeeId,
                    employeeFolder: entry.EmployeeFolder,
                    firmName: entry.FirmName))
                .Distinct()
                .ToList();

            var advanceRequests = requests
                .Select(request => (request.requestKey, request.employeeId, request.employeeFolder, request.firmName))
                .ToList();

            var advancesTask = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var result = _financeService.GetTotalAdvancesForEmployeeFirms(advanceRequests, monthKey);
                advancesTaskMs = sw.ElapsedMilliseconds;
                return result;
            });
            var debtTask = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var result = _financeService.CalculateCarriedDebtForEntries(requests, year, month);
                debtTaskMs = sw.ElapsedMilliseconds;
                return result;
            });

            await Task.WhenAll(advancesTask, debtTask);
            var currentAdvancesByRequest = advancesTask.Result;
            var carriedDebtByRequest = debtTask.Result;

            if (version != Volatile.Read(ref _advanceRefreshVersion)
                || year != _selectedYear
                || month != _selectedMonth)
            {
                return;
            }

            var applySw = Stopwatch.StartNew();
            foreach (var entry in Entries)
            {
                var requestKey = BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName);
                currentAdvancesByRequest.TryGetValue(requestKey, out var currentAdvances);
                carriedDebtByRequest.TryGetValue(requestKey, out var carriedDebt);
                entry.Advance = currentAdvances + carriedDebt;
            }
            applyMs = applySw.ElapsedMilliseconds;

            totalSw.Stop();
            LoggingService.LogInfo(
                "Timing.RefreshAdvanceSums",
                $"RefreshAdvanceSumsAsync {year:D4}-{month:D2} total={totalSw.ElapsedMilliseconds}ms | " +
                $"advancesTask={advancesTaskMs}ms | debtTask={debtTaskMs}ms | apply={applyMs}ms | " +
                $"requests={requests.Count} | entries={Entries.Count}");
        }

        public List<AdvancePayment> GetAdvancesForEmployeeFirm(string employeeFolder, string firmName)
        {
            var monthKey = $"{_selectedYear:D4}-{_selectedMonth:D2}";
            return _financeService.GetAdvancesForEmployeeFirmMonth(employeeFolder, firmName, monthKey);
        }

        public List<DebtInfoItem> GetDebtInfoForEmployeeFirm(string employeeFolder, string firmName)
        {
            var (_, details) = _financeService.CalculateCarriedDebtForFirm(employeeFolder, firmName, _selectedYear, _selectedMonth);
            return details;
        }

        public void DeleteAdvance(string advanceId, string employeeName = "", string firmName = "", decimal amount = 0)
        {
            if (!PolicyService.EnsureWriteAllowed("видалити аванс"))
                return;

            if (amount >= 1000)
            {
                var msg = $"{L("FinAdvanceDeleteConfirm") ?? "Delete advance"} {amount:N0} Kč?";
                var title = L("TitleWarning") ?? "Warning";
                if (MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            _financeService.RemoveAdvance(advanceId);
            RefreshAdvanceSums();
            if (!string.IsNullOrEmpty(employeeName))
                _activityLogService.Log("AdvanceDeleted", "Advance", firmName, employeeName,
                    $"Видалено аванс {amount:N0} Kč ← {employeeName} ({firmName})");
        }

        private void OpenManageColumns()
        {
            var firmNames = Entries.Select(e => e.FirmName).Distinct().OrderBy(n => n).ToList();
            var dialog = new Views.ManageColumnsWindow(_financeService, firmNames);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
                RefreshActiveFields();
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

        private string BuildSalaryExportDetails(IEnumerable<string> selectedFirms, List<SalaryEntry> exportEntries, List<CustomSalaryField> fields, string outputPath)
        {
            var fieldNames = fields.Select(f => f.Name);
            var paidCount = exportEntries.Count(e => e.IsPaid);

            return $"Місяць: {MonthDisplay}; " +
                   $"Фірми: {SummarizeForLog(selectedFirms, "не вибрано")}; " +
                   $"Працівників: {exportEntries.Count}; " +
                   $"Оплачено: {paidCount}; " +
                   $"Кастомні колонки: {SummarizeForLog(fieldNames, "немає")}; " +
                   $"Файл: {Path.GetFileName(outputPath)}";
        }

        private void ExportToExcel()
        {
            if (!PolicyService.EnsureExportsAllowed("експортувати зарплатний звіт"))
                return;

            if (Entries.Count == 0)
            {
                StatusMessage = L("FinSalaryNoData") is string n && n.Length > 0 ? n : "No data to export";
                return;
            }

            var firmData = Entries
                .GroupBy(e => e.FirmName)
                .OrderBy(g => g.Key)
                .Select(g => (firmName: g.Key, count: g.Count()))
                .ToList();

            var selectDialog = new Views.ExportFirmSelectWindow(firmData, _appSettingsService);
            selectDialog.Owner = Application.Current.MainWindow;
            if (selectDialog.ShowDialog() != true) return;

            var selectedFirms = selectDialog.SelectedFirms;
            var exportEntries = Entries.Where(e => selectedFirms.Contains(e.FirmName)).ToList();

            if (exportEntries.Count == 0) return;

            var dlg = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"Salary_{_selectedYear}-{_selectedMonth:D2}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet(MonthDisplay);
                var fields = ActiveCustomFields.ToList();
                int fixedBefore = 6;
                int dynamicCount = fields.Count;
                int totalCols = fixedBefore + dynamicCount + 3;
                var accentBlue = XLColor.FromHtml("#4472C4");

                ws.Style.Font.FontSize = 13;

                int row = ExcelWriteTitle(ws, totalCols);
                int headerRow = row;
                row = ExcelWriteHeader(ws, row, fields, totalCols, accentBlue);
                row = ExcelWriteDataRows(ws, row, exportEntries, fields, totalCols, accentBlue);
                row = ExcelWriteTotals(ws, row, exportEntries, fields, fixedBefore, dynamicCount, totalCols, accentBlue);

                var dataRange = ws.Range(headerRow, 1, row - 1, totalCols);
                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                dataRange.Style.Border.OutsideBorderColor = accentBlue;
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#B0B0B0");

                int statsAnchorRow = row + 1;
                row = ExcelWriteExpenses(ws, row, selectedFirms, accentBlue, out statsAnchorRow);
                row = ExcelWriteGrandTotal(ws, row, exportEntries, selectedFirms);
                row = ExcelWriteFirmSummary(ws, row, exportEntries, accentBlue);
                ExcelWriteStats(ws, exportEntries, fields, statsAnchorRow);

                ws.Columns().AdjustToContents();
                if (ws.Column(1).Width < 6) ws.Column(1).Width = 6;
                ws.Column(2).Width = 28;
                for (int c = 3; c <= totalCols; c++)
                    if (ws.Column(c).Width < 14) ws.Column(c).Width = 14;

                ws.SheetView.FreezeRows(headerRow);
                wb.SaveAs(dlg.FileName);
                StatusMessage = L("FinSalaryExported") is string ex && ex.Length > 0 ? ex : "Exported!";
                ToastService.Instance.Success(StatusMessage);
                _activityLogService.Log("ExportExcel", "Export", "", "",
                    $"Експортовано виплату {MonthDisplay} → Excel",
                    details: BuildSalaryExportDetails(selectedFirms, exportEntries, fields, dlg.FileName));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export error: {ex.Message}";
                ToastService.Instance.Error(StatusMessage);
            }
        }

        private int ExcelWriteTitle(IXLWorksheet ws, int totalCols)
        {
            var titleRange = ws.Range(1, 1, 2, totalCols);
            titleRange.Merge();
            ws.Cell(1, 1).Value = $"{_selectedMonth}.{_selectedYear}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 24;
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            titleRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            titleRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#2F5496");
            titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E2F3");
            ws.Row(1).Height = 22;
            ws.Row(2).Height = 22;
            return 3;
        }

        private int ExcelWriteHeader(IXLWorksheet ws, int row, List<CustomSalaryField> fields, int totalCols, XLColor accentBlue)
        {
            var headerCols = new List<string>
            {
                DocL("FinColFirm") ?? "Firm", DocL("FinColName") ?? "Name",
                DocL("FinColHours") ?? "Hours", DocL("FinColRate") ?? "Rate",
                DocL("FinColGross") ?? "Gross", DocL("FinColAdvance") ?? "Advance"
            };
            foreach (var f in fields)
            {
                string prefix = f.Operation switch
                {
                    FieldOperation.Add => "+", FieldOperation.Subtract => "−",
                    FieldOperation.Multiply => "×", FieldOperation.Divide => "÷", _ => ""
                };
                headerCols.Add($"{prefix}{f.Name}");
            }
            headerCols.Add(DocL("FinColNet") ?? "Net Pay");
            headerCols.Add(DocL("FinColNote") ?? "Note");
            headerCols.Add(DocL("FinColPaid") ?? "Paid");

            for (int c = 0; c < headerCols.Count; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = headerCols[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 13;
                cell.Style.Fill.BackgroundColor = accentBlue;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#2F5496");
            }
            ws.Row(row).Height = 24;
            return row + 1;
        }

        private int ExcelWriteDataRows(IXLWorksheet ws, int row, List<SalaryEntry> exportEntries,
            List<CustomSalaryField> fields, int totalCols, XLColor accentBlue)
        {
            var lightBlue = XLColor.FromHtml("#D9E2F3");
            var lightGreen = XLColor.FromHtml("#E2EFDA");
            var firmGroups = exportEntries.GroupBy(e => e.FirmName).OrderBy(g => g.Key);
            bool isAlternate = false;

            foreach (var group in firmGroups)
            {
                var firmHeaderRange = ws.Range(row, 1, row, totalCols);
                firmHeaderRange.Merge();
                ws.Cell(row, 1).Value = group.Key;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 15;
                ws.Cell(row, 1).Style.Fill.BackgroundColor = lightBlue;
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#2F5496");
                ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                firmHeaderRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                firmHeaderRange.Style.Border.OutsideBorderColor = accentBlue;
                ws.Row(row).Height = 26;
                row++;
                isAlternate = false;

                foreach (var entry in group.OrderBy(e => e.FullName))
                {
                    int col = 1;
                    var altColor = isAlternate ? XLColor.FromHtml("#F7F9FC") : XLColor.White;

                    ws.Cell(row, col).Value = entry.FirmName;
                    ws.Cell(row, col).Style.Font.FontColor = XLColor.FromHtml("#2F5496");
                    ws.Cell(row, col).Style.Font.FontSize = 12;
                    col++;

                    ws.Cell(row, col).Value = entry.FullName;
                    ws.Cell(row, col).Style.Font.Bold = true;
                    if (entry.GrossSalary > 0)
                        ws.Cell(row, col).Style.Fill.BackgroundColor = lightGreen;
                    col++;

                    ws.Cell(row, col).Value = entry.HoursWorked;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.0";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, col).Style.Font.Bold = true;
                    col++;

                    ws.Cell(row, col).Value = entry.HourlyRate;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, col).Style.Font.Bold = true;
                    col++;

                    ws.Cell(row, col).Value = entry.GrossSalary;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, col).Style.Font.Bold = true;
                    col++;

                    ws.Cell(row, col).Value = entry.Advance;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, col).Style.Font.Bold = true;
                    if (entry.Advance > 0)
                        ws.Cell(row, col).Style.Font.FontColor = XLColor.FromHtml("#C62828");
                    col++;

                    foreach (var f in fields)
                    {
                        var val = entry.CustomValues.TryGetValue(f.Id, out var v) ? v : 0;
                        ws.Cell(row, col).Value = val;
                        ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ws.Cell(row, col).Style.Font.Bold = true;
                        col++;
                    }

                    ws.Cell(row, col).Value = entry.NetSalary;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, col).Style.Font.Bold = true;
                    ws.Cell(row, col).Style.Font.FontColor = XLColor.FromHtml("#1565C0");
                    col++;

                    ws.Cell(row, col).Value = entry.Note;
                    ws.Cell(row, col).Style.Font.FontColor = XLColor.FromHtml("#888888");
                    ws.Cell(row, col).Style.Font.Bold = true;
                    ws.Cell(row, col).Style.Font.Italic = true;
                    col++;

                    ws.Cell(row, col).Value = entry.IsPaid ? "✓" : "";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (entry.IsPaid)
                    {
                        ws.Cell(row, col).Style.Font.FontColor = XLColor.FromHtml("#2E7D32");
                        ws.Cell(row, col).Style.Font.Bold = true;
                    }

                    var rowBg = entry.IsPaid ? XLColor.FromHtml("#E8F5E9") : altColor;
                    for (int bc = 1; bc <= totalCols; bc++)
                    {
                        if (entry.IsPaid || ws.Cell(row, bc).Style.Fill.BackgroundColor == XLColor.NoColor)
                            ws.Cell(row, bc).Style.Fill.BackgroundColor = rowBg;
                    }

                    row++;
                    isAlternate = !isAlternate;
                }
            }
            return row;
        }

        private int ExcelWriteTotals(IXLWorksheet ws, int row, List<SalaryEntry> exportEntries,
            List<CustomSalaryField> fields, int fixedBefore, int dynamicCount, int totalCols, XLColor accentBlue)
        {
            var expTotalHours = exportEntries.Sum(e => e.HoursWorked);
            var expTotalGross = exportEntries.Sum(e => e.GrossSalary);
            var expTotalNet = exportEntries.Sum(e => e.NetSalary);
            int totalsRow = row;

            var totalsYellow = XLColor.FromHtml("#FFF9C4");
            for (int bc = 1; bc <= totalCols; bc++)
                ws.Cell(totalsRow, bc).Style.Fill.BackgroundColor = totalsYellow;
            var totalsRange = ws.Range(totalsRow, 1, totalsRow, totalCols);
            totalsRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            totalsRange.Style.Border.OutsideBorderColor = accentBlue;
            ws.Row(totalsRow).Style.Font.Bold = true;
            ws.Row(totalsRow).Height = 24;

            ws.Cell(totalsRow, 3).Value = expTotalHours;
            ws.Cell(totalsRow, 3).Style.NumberFormat.Format = "#,##0.0";
            ws.Cell(totalsRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(totalsRow, fixedBefore - 1).Value = expTotalGross;
            ws.Cell(totalsRow, fixedBefore - 1).Style.NumberFormat.Format = "#,##0.00 \"Kč\"";
            ws.Cell(totalsRow, fixedBefore - 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var expTotalAdvance = exportEntries.Sum(e => e.Advance);
            ws.Cell(totalsRow, fixedBefore).Value = expTotalAdvance;
            ws.Cell(totalsRow, fixedBefore).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(totalsRow, fixedBefore).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (expTotalAdvance > 0)
                ws.Cell(totalsRow, fixedBefore).Style.Font.FontColor = XLColor.FromHtml("#C62828");

            for (int fi = 0; fi < fields.Count; fi++)
            {
                int dynCol = fixedBefore + 1 + fi;
                var fieldTotal = exportEntries.Sum(e => e.CustomValues.TryGetValue(fields[fi].Id, out var v) ? v : 0);
                ws.Cell(totalsRow, dynCol).Value = fieldTotal;
                ws.Cell(totalsRow, dynCol).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(totalsRow, dynCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int netCol = fixedBefore + dynamicCount + 1;
            ws.Cell(totalsRow, netCol).Value = expTotalNet;
            ws.Cell(totalsRow, netCol).Style.NumberFormat.Format = "#,##0.00 \"Kč\"";
            ws.Cell(totalsRow, netCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(totalsRow, netCol).Style.Font.FontColor = XLColor.FromHtml("#1565C0");

            return totalsRow + 1;
        }

        private int ExcelWriteExpenses(IXLWorksheet ws, int row, HashSet<string> selectedFirms, XLColor accentBlue, out int statsAnchorRow)
        {
            var exportExpenses = _financeService.GetFirmExpensesForFirms(_selectedYear, _selectedMonth, selectedFirms);
            var lightOrange = XLColor.FromHtml("#FFF2CC");
            var warmOrange = XLColor.FromHtml("#F4B183");
            var thinBorder = XLBorderStyleValues.Thin;
            statsAnchorRow = row + 1;

            if (exportExpenses.Count == 0) return row;

            row += 1;
            statsAnchorRow = row;
            int expHeaderRow = row;

            var lExpenses = DocL("FinFirmExpenses") ?? "Firm Expenses";
            var expTitleRange = ws.Range(row, 1, row, 4);
            expTitleRange.Merge();
            ws.Cell(row, 1).Value = lExpenses;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 14;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#E65100");
            expTitleRange.Style.Fill.BackgroundColor = lightOrange;
            expTitleRange.Style.Border.BottomBorder = thinBorder;
            expTitleRange.Style.Border.BottomBorderColor = warmOrange;
            ws.Row(row).Height = 24;
            row++;

            foreach (var expense in exportExpenses)
            {
                ws.Cell(row, 1).Value = expense.FirmName;
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#2F5496");
                ws.Cell(row, 1).Style.Font.FontSize = 12;
                ws.Cell(row, 2).Value = expense.Name;
                ws.Cell(row, 2).Style.Font.FontColor = XLColor.FromHtml("#4E342E");
                ws.Cell(row, 3).Value = expense.Amount;
                ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0 \"Kč\"";
                ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Style.Font.FontColor = XLColor.FromHtml("#E65100");
                for (int bc = 1; bc <= 4; bc++)
                {
                    ws.Cell(row, bc).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                    ws.Cell(row, bc).Style.Border.BottomBorderColor = XLColor.FromHtml("#E0D0C0");
                }
                row++;
            }

            var lExpTotal = DocL("FinExpenseTotal") ?? "Total expenses";
            ws.Cell(row, 2).Value = lExpTotal;
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.FromHtml("#BF360C");
            ws.Cell(row, 3).Value = exportExpenses.Sum(e => e.Amount);
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0 \"Kč\"";
            ws.Cell(row, 3).Style.Font.Bold = true;
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Style.Font.FontColor = XLColor.FromHtml("#BF360C");
            var expTotalRange = ws.Range(row, 1, row, 4);
            expTotalRange.Style.Fill.BackgroundColor = lightOrange;
            expTotalRange.Style.Border.TopBorder = thinBorder;
            expTotalRange.Style.Border.TopBorderColor = warmOrange;
            expTotalRange.Style.Border.BottomBorder = thinBorder;
            expTotalRange.Style.Border.BottomBorderColor = warmOrange;
            row++;

            var expFullRange = ws.Range(expHeaderRow, 1, row - 1, 4);
            expFullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            expFullRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E65100");

            return row;
        }

        private int ExcelWriteGrandTotal(IXLWorksheet ws, int row, List<SalaryEntry> exportEntries, HashSet<string> selectedFirms)
        {
            row += 1;
            var gold = XLColor.FromHtml("#FFD966");
            var lGrandTotal = DocL("FinStatGrandTotal") ?? "Grand Total";
            var exportExpenses = _financeService.GetFirmExpensesForFirms(_selectedYear, _selectedMonth, selectedFirms);
            var expensesTotal = exportExpenses?.Sum(e => e.Amount) ?? 0m;
            var expTotalNet = exportEntries.Sum(e => e.NetSalary);
            var grandTotalValue = expTotalNet + expensesTotal;

            var gtRange = ws.Range(row, 1, row, 4);
            ws.Cell(row, 1).Value = lGrandTotal;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 15;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#3E2723");
            ws.Cell(row, 2).Value = grandTotalValue;
            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00 \"Kč\"";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.FontSize = 15;
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.FromHtml("#3E2723");
            ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            gtRange.Style.Fill.BackgroundColor = gold;
            gtRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            gtRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#F9A825");
            ws.Row(row).Height = 28;
            return row + 1;
        }

        private int ExcelWriteFirmSummary(IXLWorksheet ws, int row, List<SalaryEntry> exportEntries, XLColor accentBlue)
        {
            row += 1;
            var colHours = DocL("FinColHours") ?? "Hours";
            int firmTableHeaderRow = row;

            var firmHeaderCell = ws.Range(row, 2, row, 3);
            firmHeaderCell.Merge();
            firmHeaderCell.Value = DocL("FinColFirm") ?? "Firm";
            ws.Cell(row, 4).Value = DocL("FinColAmount") ?? "Amount";
            ws.Cell(row, 5).Value = colHours;
            var firmHeaderRange2 = ws.Range(row, 2, row, 5);
            firmHeaderRange2.Style.Font.Bold = true;
            firmHeaderRange2.Style.Font.FontSize = 13;
            firmHeaderRange2.Style.Fill.BackgroundColor = accentBlue;
            firmHeaderRange2.Style.Font.FontColor = XLColor.White;
            firmHeaderRange2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            firmHeaderRange2.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            firmHeaderRange2.Style.Border.BottomBorderColor = XLColor.FromHtml("#2F5496");
            row++;

            var expFirmSummaries = exportEntries
                .GroupBy(e => e.FirmName)
                .OrderByDescending(g => g.Sum(e => e.GrossSalary));
            bool firmAlt = false;

            foreach (var g in expFirmSummaries)
            {
                var firmAltColor = firmAlt ? XLColor.FromHtml("#F7F9FC") : XLColor.White;
                var firmNameCell = ws.Range(row, 2, row, 3);
                firmNameCell.Merge();
                firmNameCell.Value = g.Key;
                firmNameCell.Style.Font.FontColor = XLColor.FromHtml("#2F5496");
                firmNameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                firmNameCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                ws.Cell(row, 4).Value = g.Sum(e => e.NetSalary);
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0 \"Kč\"";
                ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 5).Value = g.Sum(e => e.HoursWorked);
                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.0";
                ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                for (int bc = 2; bc <= 5; bc++)
                {
                    ws.Cell(row, bc).Style.Fill.BackgroundColor = firmAltColor;
                    ws.Cell(row, bc).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                    ws.Cell(row, bc).Style.Border.BottomBorderColor = XLColor.FromHtml("#D0D0D0");
                }
                row++;
                firmAlt = !firmAlt;
            }

            var firmTableRange = ws.Range(firmTableHeaderRow, 2, row - 1, 5);
            firmTableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            firmTableRange.Style.Border.OutsideBorderColor = accentBlue;

            var firmNameBlock = ws.Range(firmTableHeaderRow, 2, row - 1, 3);
            firmNameBlock.Style.Border.RightBorder = XLBorderStyleValues.Medium;
            firmNameBlock.Style.Border.RightBorderColor = accentBlue;

            var amountBlock = ws.Range(firmTableHeaderRow, 4, row - 1, 4);
            amountBlock.Style.Border.RightBorder = XLBorderStyleValues.Medium;
            amountBlock.Style.Border.RightBorderColor = accentBlue;
            return row;
        }

        private void ExcelWriteStats(IXLWorksheet ws, List<SalaryEntry> exportEntries,
            List<CustomSalaryField> fields, int statsAnchorRow)
        {
            var anyStats = ShowStatPaid || ShowStatRemaining || ShowStatAdvances || ShowStatCustomAdd || ShowStatCustomSub;
            if (!anyStats) return;

            var sPaid = exportEntries.Where(e => e.IsPaid).Sum(e => e.NetSalary);
            var sRemaining = exportEntries.Where(e => !e.IsPaid).Sum(e => e.NetSalary);
            var sAdvances = exportEntries.Sum(e => e.Advance);
            decimal sAdd = 0, sSub = 0;
            foreach (var entry in exportEntries)
            {
                if (entry.FieldDefinitions == null) continue;
                foreach (var f in entry.FieldDefinitions)
                {
                    if (!entry.CustomValues.TryGetValue(f.Id, out var val) || val == 0) continue;
                    if (f.Operation == FieldOperation.Add) sAdd += val;
                    else if (f.Operation == FieldOperation.Subtract) sSub += val;
                }
            }

            int sCol1 = 6, sCol2 = 7;
            int sRow = statsAnchorRow;

            var statItems = new List<(string label, decimal value, string bgColor, string borderColor)>();
            if (ShowStatPaid) statItems.Add((DocL("FinStatPaid") ?? "Paid", sPaid, "#E8F5E9", "#4CAF50"));
            if (ShowStatRemaining) statItems.Add((DocL("FinStatRemaining") ?? "Remaining", sRemaining, "#FBE9E7", "#E53935"));
            if (ShowStatAdvances) statItems.Add((DocL("FinStatAdvances") ?? "Advances", sAdvances, "#FFF3E0", "#FB8C00"));
            if (ShowStatCustomAdd) statItems.Add((DocL("FinStatCustomAdd") ?? "Surcharges", sAdd, "#E3F2FD", "#42A5F5"));
            if (ShowStatCustomSub) statItems.Add((DocL("FinStatCustomSub") ?? "Deductions", sSub, "#F3E5F5", "#AB47BC"));

            foreach (var (label, value, bgColor, borderColor) in statItems)
            {
                var labelCell = ws.Cell(sRow, sCol1);
                labelCell.Value = label;
                labelCell.Style.Font.Bold = true;
                labelCell.Style.Font.FontSize = 12;
                labelCell.Style.Font.FontColor = XLColor.FromHtml(borderColor);
                labelCell.Style.Fill.BackgroundColor = XLColor.FromHtml(bgColor);
                labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                var valCell = ws.Cell(sRow, sCol2);
                valCell.Value = value;
                valCell.Style.NumberFormat.Format = "#,##0 \"Kč\"";
                valCell.Style.Font.Bold = true;
                valCell.Style.Font.FontSize = 13;
                valCell.Style.Font.FontColor = XLColor.FromHtml(borderColor);
                valCell.Style.Fill.BackgroundColor = XLColor.FromHtml(bgColor);
                valCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                valCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                var statRowRange = ws.Range(sRow, sCol1, sRow, sCol2);
                statRowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                statRowRange.Style.Border.OutsideBorderColor = XLColor.FromHtml(borderColor);
                sRow++;
            }

            ws.Column(sCol1).Width = 24;
            ws.Column(sCol2).Width = 18;
        }

        private void LoadExpenses()
        {
            foreach (var old in FirmExpenses)
                old.PropertyChanged -= OnExpenseChanged;

            var allLabel = L("FinFilterAll") ?? "All";
            var isAll = string.IsNullOrEmpty(_selectedFirmFilter) || _selectedFirmFilter == allLabel;

            var expenses = isAll
                ? _financeService.GetFirmExpenses(_selectedYear, _selectedMonth)
                : _financeService.GetFirmExpenses(_selectedYear, _selectedMonth, _selectedFirmFilter);

            foreach (var exp in expenses)
                exp.PropertyChanged += OnExpenseChanged;

            FirmExpenses = new ObservableCollection<FirmExpense>(expenses);
            OnPropertyChanged(nameof(ExpenseHeaderText));
            RecalcTotals();
        }

        private void OnExpenseChanged(object? sender, PropertyChangedEventArgs e)
        {
            RecalcTotals();
            OnPropertyChanged(nameof(ExpenseHeaderText));
            if (sender is FirmExpense exp)
                _financeService.UpdateFirmExpense(exp);
        }

        private void AddExpense()
        {
            if (!PolicyService.EnsureWriteAllowed("додати витрату"))
                return;

            var allLabel = L("FinFilterAll") ?? "All";
            var isAll = string.IsNullOrEmpty(_selectedFirmFilter) || _selectedFirmFilter == allLabel;

            if (isAll)
            {
                StatusMessage = L("FinExpenseSelectFirm") ?? "Select a firm first to add expenses";
                return;
            }

            var exp = new FirmExpense
            {
                Year = _selectedYear,
                Month = _selectedMonth,
                FirmName = _selectedFirmFilter,
                Name = L("FinExpenseNew") ?? "New expense",
                Amount = 0
            };
            _financeService.AddFirmExpense(exp);
            exp.PropertyChanged += OnExpenseChanged;
            FirmExpenses.Add(exp);
            OnPropertyChanged(nameof(ExpenseHeaderText));
            RecalcTotals();
        }

        private void RemoveExpense(string? expenseId)
        {
            if (!PolicyService.EnsureWriteAllowed("видалити витрату"))
                return;

            if (string.IsNullOrEmpty(expenseId)) return;
            var item = FirmExpenses.FirstOrDefault(e => e.Id == expenseId);
            if (item != null)
            {
                item.PropertyChanged -= OnExpenseChanged;
                FirmExpenses.Remove(item);
                _financeService.RemoveFirmExpense(expenseId, item.Year, item.Month);
                OnPropertyChanged(nameof(ExpenseHeaderText));
                RecalcTotals();
            }
        }

        private async Task MarkAllPaidAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("позначити зарплати як оплачені"))
                return;

            foreach (var e in VisibleEntries())
            {
                e.IsPaid = true;
                WriteSalaryHistory(e);
            }
            RecalcTotals();
            await SaveReportAsync();

            var firmNames = VisibleEntries().Select(e => e.FirmName).Distinct().ToList();
            _activityLogService.Log("MonthPaid", "Salary", string.Join(", ", firmNames), "",
                $"Позначено оплачено: {MonthDisplay} ({VisibleEntries().Count()} працівників)");
        }

        private async Task MarkAllUnpaidAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("зняти позначку оплати зарплат"))
                return;

            foreach (var e in VisibleEntries())
            {
                e.IsPaid = false;
                RemoveSalaryHistory(e);
            }
            RecalcTotals();
            await SaveReportAsync();
        }

        internal void OnEntryPaidChanged(SalaryEntry? entry)
        {
            if (entry != null)
            {
                if (entry.IsPaid)
                    WriteSalaryHistory(entry);
                else
                    RemoveSalaryHistory(entry);
            }
            RecalcTotals();
            SaveReport();
        }

        private void WriteSalaryHistory(SalaryEntry entry)
        {
            if (string.IsNullOrEmpty(entry.EmployeeFolder)) return;
            var folder = _financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId);
            var record = _financeService.BuildHistoryRecord(entry, _selectedYear, _selectedMonth, ActiveCustomFields.ToList());
            _financeService.SaveSalaryHistoryRecord(folder, record);
        }

        private void RemoveSalaryHistory(SalaryEntry entry)
        {
            if (string.IsNullOrEmpty(entry.EmployeeFolder)) return;
            var folder = _financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId);
            _financeService.RemoveSalaryHistoryRecord(folder, _selectedYear, _selectedMonth, entry.FirmName);
        }

        private void SelectFirm(string? firmName)
        {
            var allLabel = L("FinFilterAll") ?? "All";
            if (string.IsNullOrEmpty(firmName))
            {
                SelectedFirmFilter = allLabel;
                return;
            }
            SelectedFirmFilter = (_selectedFirmFilter == firmName) ? allLabel : firmName;
        }

        public void SaveExpensesNow()
        {
            if (!PolicyService.EnsureWriteAllowed("зберегти витрати"))
                return;

            var allLabel = L("FinFilterAll") ?? "All";
            var isAll = string.IsNullOrEmpty(_selectedFirmFilter) || _selectedFirmFilter == allLabel;
            _financeService.SaveFirmExpenses(FirmExpenses.ToList(), _selectedYear, _selectedMonth, isAll ? null : _selectedFirmFilter);
        }

        private List<FirmExpense> BuildExpensesForSave(int year, int month)
        {
            var allLabel = L("FinFilterAll") ?? "All";
            var isAll = string.IsNullOrEmpty(_selectedFirmFilter) || _selectedFirmFilter == allLabel;
            if (isAll)
                return FirmExpenses.ToList();

            var monthExpenses = _financeService.GetFirmExpenses(year, month)
                .Where(expense => !ShouldReplaceFirmExpenseForSelectedFirm(expense.FirmName, _selectedFirmFilter))
                .ToList();
            monthExpenses.AddRange(FirmExpenses.ToList());
            return monthExpenses;
        }

        private void OnSalaryDetailsClose()
        {
            IsEmployeeDetailsOpen = false;
            if (EmployeeDetailsVm != null)
            {
                EmployeeDetailsVm.RequestClose -= OnSalaryDetailsClose;
                EmployeeDetailsVm = null;
            }
        }

        private void OpenEmployee(SalaryEntry? entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.EmployeeFolder)) return;
            var resolvedFolder = _financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId);
            if (resolvedFolder != entry.EmployeeFolder)
                entry.EmployeeFolder = resolvedFolder;
            if (!Directory.Exists(resolvedFolder))
            {
                MessageBox.Show(
                    $"{L("MsgOpenFolderFail") ?? "Could not open folder."}\n\n{resolvedFolder}",
                    L("TitleWarning") ?? "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (EmployeeDetailsVm != null)
                EmployeeDetailsVm.RequestClose -= OnSalaryDetailsClose;
            EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(entry.FirmName, resolvedFolder, _employeeService);
            EmployeeDetailsVm.RequestClose += OnSalaryDetailsClose;
            IsEmployeeDetailsOpen = true;
        }

        private static string? L(string key)
        {
            try { return Application.Current.FindResource(key) as string; }
            catch { return null; }
        }

        private string? DocL(string key)
        {
            try { return _documentLocalizationService.Get(key) ?? L(key); }
            catch { return L(key); }
        }
    }
}
