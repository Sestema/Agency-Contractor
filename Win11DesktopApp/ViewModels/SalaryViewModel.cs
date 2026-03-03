using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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
        private readonly FinanceService _financeService;
        private readonly EmployeeService _employeeService;

        // key: folderKey|firmName → note value at load time (for forward propagation)
        private Dictionary<string, string> _originalNotes = new(StringComparer.OrdinalIgnoreCase);

        public event Action? DataLoaded;

        public ObservableCollection<SalaryEntry> Entries { get; } = new();
        public ObservableCollection<FirmSalarySummary> FirmSummaries { get; } = new();
        public ObservableCollection<string> AvailableFirms { get; } = new();
        public ObservableCollection<CustomSalaryField> ActiveCustomFields { get; } = new();
        public ObservableCollection<FirmExpense> FirmExpenses { get; } = new();

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
            set { SetProperty(ref _selectedYear, value); LoadReport(); }
        }

        private int _selectedMonth;
        public int SelectedMonth
        {
            get => _selectedMonth;
            set { SetProperty(ref _selectedMonth, value); LoadReport(); }
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
            get => App.AppSettingsService?.Settings?.ShowStatPaid ?? false;
            set { var s = App.AppSettingsService?.Settings; if (s != null) s.ShowStatPaid = value; App.AppSettingsService?.SaveSettings(); OnPropertyChanged(nameof(ShowStatPaid)); }
        }
        public bool ShowStatRemaining
        {
            get => App.AppSettingsService?.Settings?.ShowStatRemaining ?? false;
            set { var s = App.AppSettingsService?.Settings; if (s != null) s.ShowStatRemaining = value; App.AppSettingsService?.SaveSettings(); OnPropertyChanged(nameof(ShowStatRemaining)); }
        }
        public bool ShowStatAdvances
        {
            get => App.AppSettingsService?.Settings?.ShowStatAdvances ?? false;
            set { var s = App.AppSettingsService?.Settings; if (s != null) s.ShowStatAdvances = value; App.AppSettingsService?.SaveSettings(); OnPropertyChanged(nameof(ShowStatAdvances)); }
        }
        public bool ShowStatCustomAdd
        {
            get => App.AppSettingsService?.Settings?.ShowStatCustomAdd ?? false;
            set { var s = App.AppSettingsService?.Settings; if (s != null) s.ShowStatCustomAdd = value; App.AppSettingsService?.SaveSettings(); OnPropertyChanged(nameof(ShowStatCustomAdd)); }
        }
        public bool ShowStatCustomSub
        {
            get => App.AppSettingsService?.Settings?.ShowStatCustomSub ?? false;
            set { var s = App.AppSettingsService?.Settings; if (s != null) s.ShowStatCustomSub = value; App.AppSettingsService?.SaveSettings(); OnPropertyChanged(nameof(ShowStatCustomSub)); }
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

        public event Action? CustomFieldsChanged;

        public FinanceService Finance => _financeService;

        public SalaryViewModel()
        {
            _financeService = App.FinanceService;
            _employeeService = App.EmployeeService;

            _selectedYear = DateTime.Now.Year;
            _selectedMonth = DateTime.Now.Month;

            GroupedEntries = CollectionViewSource.GetDefaultView(Entries);
            GroupedEntries.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SalaryEntry.FirmName)));

            _selectedFirmFilter = L("FinFilterAll") ?? "All";
            AvailableFirms.Add(_selectedFirmFilter);

            GoBackCommand = new RelayCommand(o => { SaveReport(); App.NavigationService?.NavigateTo(new FinanceTablesViewModel()); });
            SaveCommand = new RelayCommand(o => SaveReport());
            ExportExcelCommand = new RelayCommand(o => ExportToExcel());
            PrevMonthCommand = new RelayCommand(o => ChangeMonth(-1));
            NextMonthCommand = new RelayCommand(o => ChangeMonth(1));
            LoadEmployeesCommand = new RelayCommand(o => { });
            OpenAdvanceDialogCommand = new RelayCommand(o => OpenAdvanceDialog());
            CloseAdvanceDialogCommand = new RelayCommand(o => IsAdvanceDialogOpen = false);
            ConfirmAdvanceCommand = new RelayCommand(o => ConfirmAdvance());
            AddAdvanceCommand = new RelayCommand(o => OpenAdvanceDialog());
            ManageColumnsCommand = new RelayCommand(o => OpenManageColumns());
            AddExpenseCommand = new RelayCommand(o => AddExpense());
            RemoveExpenseCommand = new RelayCommand(o => RemoveExpense(o as string));
            SelectFirmCommand = new RelayCommand(o => SelectFirm(o as string));
            MarkAllPaidCommand = new RelayCommand(o => MarkAllPaid());
            MarkAllUnpaidCommand = new RelayCommand(o => MarkAllUnpaid());
            CreateNextMonthCommand = new RelayCommand(o => CreateNextMonth());
            OpenEmployeeCommand = new RelayCommand(o => OpenEmployee(o as SalaryEntry));
            ToggleStatsSettingsCommand = new RelayCommand(o => IsStatsSettingsOpen = !IsStatsSettingsOpen);
            ClearSearchCommand = new RelayCommand(o => SearchText = string.Empty);

            RefreshActiveFields();
            LoadReport();
        }

        public void RefreshActiveFields()
        {
            var visibleFirms = Entries.Select(e => e.FirmName).Distinct().ToList();
            var fields = visibleFirms.Count > 0
                ? _financeService.GetActiveFields(visibleFirms)
                : _financeService.GetCustomFields();

            ActiveCustomFields.Clear();
            foreach (var f in fields)
                ActiveCustomFields.Add(f);

            var fieldList = ActiveCustomFields.ToList();
            foreach (var entry in Entries)
            {
                entry.FieldDefinitions = fieldList;
                entry.RecalcNet();
            }

            CustomFieldsChanged?.Invoke();
            RecalcTotals();
        }

        private void ChangeMonth(int delta)
        {
            NavigationDirection = delta;
            IsLoading = true;
            var date = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(delta);
            var (testEntries, _) = _financeService.LoadAllFirmPayments(date.Year, date.Month);
            if (delta > 0 && testEntries.Count == 0)
                return;

            if (Entries.Count > 0)
                SaveReport();

            _selectedYear = date.Year;
            _selectedMonth = date.Month;
            OnPropertyChanged(nameof(SelectedYear));
            OnPropertyChanged(nameof(SelectedMonth));
            LoadReport();
        }

        private static string FolderKey(string path) =>
            System.IO.Path.GetFileName(path?.TrimEnd('\\', '/') ?? "");

        private bool ArchivedWorkedInMonth(ArchivedEmployeeSummary arc, int year, int month)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var start = DateParsingHelper.TryParseDate(arc.StartDate);
            if (start == null)
                return false;

            if (start.Value > monthEnd)
                return false;

            if (string.IsNullOrEmpty(arc.EndDate))
                return true;

            var end = DateParsingHelper.TryParseDate(arc.EndDate);
            if (end == null)
                return true;

            return end.Value >= monthStart;
        }

        private async void LoadReport()
        {
            try
            {
            IsLoading = true;
            UpdateMonthDisplay();

            try { await Task.Run(() => _financeService.BuildEmployeeIdIndex()); }
            catch (Exception ex) { LoggingService.LogError("SalaryViewModel.BuildIndex", ex); }

            // Capture UI-bound state before going to background
            var fieldList = ActiveCustomFields.ToList();
            var year = _selectedYear;
            var month = _selectedMonth;
            var monthEnd = new DateTime(year, month, 1).AddMonths(1).AddDays(-1);
            var companiesSnapshot = App.CompanyService?.Companies?.ToList()
                                    ?? new List<EmployerCompany>();

            // Build all entries in background — no UI thread blocking
            var (newEntries, needResave, activeFoldersByFirm) = await Task.Run(() =>
                BuildEntriesBackground(fieldList, year, month, monthEnd, companiesSnapshot));

            // Set IsFinished (fast loop, no I/O)
            foreach (var entry in newEntries)
            {
                var fn = FolderKey(entry.EmployeeFolder);
                entry.IsFinished = !activeFoldersByFirm.TryGetValue(entry.FirmName, out var active)
                                   || !active.Contains(fn);
            }

            // Bulk-update ObservableCollection: DataGrid refreshes exactly once
            foreach (var old in Entries)
                old.PropertyChanged -= OnEntryChanged;

            Entries.Clear();
            _originalNotes.Clear();
            foreach (var entry in newEntries)
            {
                entry.PropertyChanged += OnEntryChanged;
                Entries.Add(entry);
                _originalNotes[FolderKey(entry.EmployeeFolder) + "|" + entry.FirmName] = entry.Note;
            }

            if (needResave)
                SaveReport();

            try { await Task.Run(() => _financeService.CleanupGhostFolders()); }
            catch (Exception ex) { LoggingService.LogError("SalaryViewModel.CleanupGhosts", ex); }

            RebuildFirmFilter();
            RefreshActiveFields();
            RefreshAdvanceSums();
            LoadExpenses();
            CheckNextMonthExists();
            IsLoading = false;
            DataLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("SalaryViewModel.LoadReport", ex);
            }
        }

        private (List<SalaryEntry> entries, bool needResave, Dictionary<string, HashSet<string>> activeFoldersByFirm)
            BuildEntriesBackground(List<CustomSalaryField> fieldList, int year, int month, DateTime monthEnd,
                                   List<EmployerCompany> companies)
        {
            var entries = new List<SalaryEntry>();
            var existingKeys = new HashSet<string>();
            bool needResave = false;
            var activeFoldersByFirm = new Dictionary<string, HashSet<string>>();

            // Build start-date map
            var startDateByFolder = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
            foreach (var company in companies)
                foreach (var emp in _employeeService.GetEmployeesForFirm(company.Name))
                {
                    var fk = FolderKey(emp.EmployeeFolder);
                    if (!startDateByFolder.ContainsKey(fk))
                        startDateByFolder[fk] = DateParsingHelper.TryParseDate(emp.StartDate);
                }

            // Load previous month notes for carry-forward (must be before sharedEntries loop)
            int prevYear = month == 1 ? year - 1 : year;
            int prevMonth = month == 1 ? 12 : month - 1;
            var (prevEntries, _) = _financeService.LoadAllFirmPayments(prevYear, prevMonth);
            var prevNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pe in prevEntries)
                if (!string.IsNullOrEmpty(pe.Note))
                    prevNotes[FolderKey(pe.EmployeeFolder) + "|" + pe.FirmName] = pe.Note;

            // Load saved payments
            var (sharedEntries, _) = _financeService.LoadAllFirmPayments(year, month);
            foreach (var entry in sharedEntries)
            {
                if (string.IsNullOrEmpty(entry.EmployeeId))
                {
                    var id = TryResolveEmployeeIdBackground(entry.EmployeeFolder, entry.FullName, companies);
                    if (!string.IsNullOrEmpty(id)) { entry.EmployeeId = id; needResave = true; }
                }

                var resolved = _financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId);
                if (resolved != entry.EmployeeFolder) { entry.EmployeeFolder = resolved; needResave = true; }

                var fKey = FolderKey(entry.EmployeeFolder);
                if (startDateByFolder.TryGetValue(fKey, out var sd) && sd != null && sd.Value > monthEnd)
                { needResave = true; continue; }

                var key = fKey + "|" + entry.FirmName;
                if (existingKeys.Contains(key)) continue;

                // Carry note from previous month if current entry has none
                if (string.IsNullOrEmpty(entry.Note) && prevNotes.TryGetValue(key, out var inherited))
                    entry.Note = inherited;

                entry.FieldDefinitions = fieldList;
                entry.RecalcNet();
                entries.Add(entry);
                existingKeys.Add(key);
            }

            // Add active employees missing from saved data
            foreach (var company in companies)
            {
                var employees = _employeeService.GetEmployeesForFirm(company.Name);
                var activeNames = new HashSet<string>();
                foreach (var emp in employees)
                {
                    var folderName = FolderKey(emp.EmployeeFolder);
                    if (emp.Status == "Active") activeNames.Add(folderName);
                    if (emp.Status != "Active") continue;

                    var empStart = DateParsingHelper.TryParseDate(emp.StartDate);
                    if (empStart != null && empStart.Value > monthEnd) continue;

                    var key = folderName + "|" + company.Name;
                    if (existingKeys.Contains(key)) continue;

                    prevNotes.TryGetValue(key, out var inheritedNote);
                    var entry = new SalaryEntry
                    {
                        EmployeeId    = emp.UniqueId,
                        EmployeeFolder = emp.EmployeeFolder,
                        FullName      = emp.FullName,
                        FirmName      = company.Name,
                        HourlyRate    = GetDefaultRate(emp.EmployeeFolder),
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

            // Add archived / history employees
            var allHistory = new List<ArchivedEmployeeSummary>();
            allHistory.AddRange(_employeeService.GetArchivedEmployees());
            allHistory.AddRange(_employeeService.GetActiveEmployeeFirmHistory());
            foreach (var arc in allHistory)
            {
                if (!ArchivedWorkedInMonth(arc, year, month)) continue;
                var firmName = arc.FirmName;
                if (string.IsNullOrEmpty(firmName)) continue;
                var folderName = FolderKey(arc.EmployeeFolder);
                var key = folderName + "|" + firmName;
                if (existingKeys.Contains(key)) continue;

                prevNotes.TryGetValue(key, out var inheritedNote);
                var entry = new SalaryEntry
                {
                    EmployeeFolder = arc.EmployeeFolder,
                    FullName       = arc.FullName,
                    FirmName       = firmName,
                    HourlyRate     = GetDefaultRate(arc.EmployeeFolder),
                    HoursWorked    = 0,
                    Note           = inheritedNote ?? string.Empty,
                    FieldDefinitions = fieldList
                };
                entry.RecalcNet();
                entries.Add(entry);
                existingKeys.Add(key);
            }

            return (entries, needResave, activeFoldersByFirm);
        }

        private string? TryResolveEmployeeIdBackground(string employeeFolder, string fullName,
                                                        List<EmployerCompany> companies)
        {
            var folder = _financeService.ResolveEmployeeFolder(employeeFolder);
            if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
            {
                var data = _employeeService.LoadEmployeeData(folder);
                if (data != null && !string.IsNullOrEmpty(data.UniqueId))
                    return data.UniqueId;
            }
            foreach (var company in companies)
            {
                var match = _employeeService.GetEmployeesForFirm(company.Name)
                    .FirstOrDefault(e => e.FullName == fullName && !string.IsNullOrEmpty(e.UniqueId));
                if (match != null) return match.UniqueId;
            }
            return null;
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

            var companiesForResolve = App.CompanyService?.Companies;
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

        private void CheckNextMonthExists()
        {
            var next = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1);
            var (entries, _) = _financeService.LoadAllFirmPayments(next.Year, next.Month);
            NextMonthExists = entries.Count > 0;
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
                PropagateRateForward(entry);
        }

        private void PropagateRateForward(SalaryEntry entry)
        {
            var newRate = entry.HourlyRate;
            decimal oldRate = 0;

            try
            {
                var jsonPath = System.IO.Path.Combine(entry.EmployeeFolder, "employee.json");
                if (System.IO.File.Exists(jsonPath))
                {
                    var json = System.IO.File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                    var data = System.Text.Json.JsonSerializer.Deserialize<EmployeeModels.EmployeeData>(json);
                    if (data != null)
                    {
                        oldRate = data.HourlySalary;
                        data.HourlySalary = newRate;
                        var newJson = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        System.IO.File.WriteAllText(jsonPath, newJson, System.Text.Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogError("SalaryViewModel.PropagateRateForward", ex); }

            if (oldRate != newRate)
            {
                App.ActivityLogService?.Log("RateChanged", "Salary", entry.FirmName, entry.FullName,
                    $"{entry.FullName}: ставка {oldRate} → {newRate} ({entry.FirmName})",
                    oldRate.ToString(), newRate.ToString(),
                    employeeFolder: entry.EmployeeFolder);
            }

            _financeService.UpdateHourlyRateForward(entry.EmployeeFolder, entry.FirmName, newRate, _selectedYear, _selectedMonth);
        }

        private void InitFirstMonth()
        {
            var fieldList = ActiveCustomFields.ToList();
            var initMonthEnd = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1).AddDays(-1);

            var companiesInit = App.CompanyService?.Companies;
            if (companiesInit != null)
            {
                foreach (var company in companiesInit)
                {
                    var employees = _employeeService.GetEmployeesForFirm(company.Name);
                foreach (var emp in employees.Where(e => e.Status == "Active" && !e.FullName.Contains("Archived")))
                {
                    var empStart = DateParsingHelper.TryParseDate(emp.StartDate);
                    if (empStart != null && empStart.Value > initMonthEnd)
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

        private void CreateNextMonth()
        {
            SaveReport();

            var next = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1);
            _selectedYear = next.Year;
            _selectedMonth = next.Month;
            OnPropertyChanged(nameof(SelectedYear));
            OnPropertyChanged(nameof(SelectedMonth));
            UpdateMonthDisplay();

            var previousRates = new Dictionary<string, decimal>();
            foreach (var e in Entries)
                previousRates[FolderKey(e.EmployeeFolder) + "|" + e.FirmName] = e.HourlyRate;

            foreach (var old in Entries)
                old.PropertyChanged -= OnEntryChanged;
            Entries.Clear();

            var fieldList = ActiveCustomFields.ToList();
            var existingKeys = new HashSet<string>();

            var nextMonthEnd = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1).AddDays(-1);
            var companiesCreate = App.CompanyService?.Companies;
            if (companiesCreate != null)
            {
                foreach (var company in companiesCreate)
                {
                    var employees = _employeeService.GetEmployeesForFirm(company.Name);
                foreach (var emp in employees.Where(e => e.Status == "Active"))
                {
                    var empStart = DateParsingHelper.TryParseDate(emp.StartDate);
                    if (empStart != null && empStart.Value > nextMonthEnd)
                        continue;

                    var key = FolderKey(emp.EmployeeFolder) + "|" + company.Name;
                    if (existingKeys.Contains(key)) continue;

                    var entry = new SalaryEntry
                    {
                        EmployeeId = emp.UniqueId,
                        EmployeeFolder = emp.EmployeeFolder,
                        FullName = emp.FullName,
                        FirmName = company.Name,
                        HourlyRate = previousRates.TryGetValue(key, out var prevRate) ? prevRate : GetDefaultRate(emp.EmployeeFolder),
                        HoursWorked = 0,
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

                var folderName = FolderKey(arc.EmployeeFolder);
                var key = folderName + "|" + firmName;
                if (existingKeys.Contains(key)) continue;

                var entry = new SalaryEntry
                {
                    EmployeeFolder = arc.EmployeeFolder,
                    FullName = arc.FullName,
                    FirmName = firmName,
                    HourlyRate = previousRates.TryGetValue(key, out var prevArcRate) ? prevArcRate : GetDefaultRate(arc.EmployeeFolder),
                    HoursWorked = 0,
                    FieldDefinitions = fieldList
                };

                entry.RecalcNet();
                entry.PropertyChanged += OnEntryChanged;
                Entries.Add(entry);
                existingKeys.Add(key);
            }

            RebuildFirmFilter();
            RefreshActiveFields();
            RefreshAdvanceSums();
            LoadExpenses();
            SaveReport();
            CheckNextMonthExists();
        }

        private decimal GetDefaultRate(string employeeFolder)
        {
            var jsonPath = System.IO.Path.Combine(employeeFolder, "employee.json");
            if (System.IO.File.Exists(jsonPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(jsonPath);
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

            AvailableFirms.Clear();
            AvailableFirms.Add(allLabel);
            foreach (var f in firms) AvailableFirms.Add(f);

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
                var searchLower = _searchText?.Trim().ToLowerInvariant() ?? "";
                GroupedEntries.Filter = obj =>
                {
                    if (obj is not SalaryEntry e) return false;
                    if (hasFirmFilter && e.FirmName != _selectedFirmFilter) return false;
                    if (hasSearch && !(e.FullName?.ToLowerInvariant().Contains(searchLower) == true)) return false;
                    return true;
                };
            }

            GroupedEntries.Refresh();
            LoadExpenses();
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
                var searchLower = _searchText.Trim().ToLowerInvariant();
                result = result.Where(e => e.FullName?.ToLowerInvariant().Contains(searchLower) == true);
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

            FirmSummaries.Clear();
            var summarySource = string.IsNullOrWhiteSpace(_searchText) ? Entries : (IEnumerable<SalaryEntry>)visible;
            var allGroups = summarySource.GroupBy(e => e.FirmName);
            foreach (var g in allGroups.OrderByDescending(g => g.Sum(e => e.GrossSalary)))
            {
                FirmSummaries.Add(new FirmSalarySummary
                {
                    FirmName = g.Key,
                    TotalGross = g.Sum(e => e.GrossSalary),
                    TotalNet = g.Sum(e => e.NetSalary),
                    TotalHours = g.Sum(e => e.HoursWorked),
                    EmployeeCount = g.Count(),
                    PaidCount = g.Count(e => e.IsPaid),
                    IsSelected = g.Key == _selectedFirmFilter
                });
            }
        }

        private void SaveReport()
        {
            foreach (var entry in Entries)
                entry.SavedNetSalary = entry.NetSalary;

            _financeService.SaveAllFirmPayments(_selectedYear, _selectedMonth, Entries.ToList(), FirmExpenses.ToList());

            // Propagate note changes forward
            PropagateNoteChangesForward();

            // Update snapshot after save
            _originalNotes.Clear();
            foreach (var entry in Entries)
                _originalNotes[FolderKey(entry.EmployeeFolder) + "|" + entry.FirmName] = entry.Note;

            IsDirty = false;
            StatusMessage = L("FinSalarySaved") is string s && s.Length > 0 ? s : "Saved!";
        }

        private void PropagateNoteChangesForward()
        {
            // Find entries whose note changed since load
            var changed = new Dictionary<string, (string oldNote, string newNote)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Entries)
            {
                var key = FolderKey(entry.EmployeeFolder) + "|" + entry.FirmName;
                var oldNote = _originalNotes.TryGetValue(key, out var o) ? o : string.Empty;
                if (oldNote != entry.Note)
                    changed[key] = (oldNote, entry.Note);
            }
            if (changed.Count == 0) return;

            // Walk forward month by month and propagate
            var date = new DateTime(_selectedYear, _selectedMonth, 1).AddMonths(1);
            for (int i = 0; i < 24; i++) // max 24 months forward
            {
                var (futureEntries, futureExpenses) = _financeService.LoadAllFirmPayments(date.Year, date.Month);
                if (futureEntries.Count == 0) break;

                bool anyUpdated = false;
                foreach (var fe in futureEntries)
                {
                    var key = FolderKey(fe.EmployeeFolder) + "|" + fe.FirmName;
                    if (!changed.TryGetValue(key, out var change)) continue;
                    // Only overwrite if future note == old note OR future note == new note (already propagated)
                    if (fe.Note == change.oldNote || fe.Note == change.newNote)
                    {
                        fe.Note = change.newNote;
                        anyUpdated = true;
                    }
                }

                if (anyUpdated)
                    _financeService.SaveAllFirmPayments(date.Year, date.Month, futureEntries, futureExpenses);

                date = date.AddMonths(1);
            }
        }

        private void UpdateMonthDisplay()
        {
            MonthDisplay = $"{_selectedMonth}.{_selectedYear}";
        }

        private void OpenAdvanceDialog()
        {
            if (SelectedEntry != null)
                AdvanceName = SelectedEntry.FullName;
            AdvanceAmount = "";
            AdvanceNote = "";
            AdvanceDate = DateTime.Today;
            IsAdvanceDialogOpen = true;
        }

        private void ConfirmAdvance()
        {
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

            App.ActivityLogService?.Log("AdvanceAdded", "Advance", target.FirmName, target.FullName,
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
            var monthKey = $"{_selectedYear:D4}-{_selectedMonth:D2}";
            foreach (var entry in Entries)
            {
                var currentAdvances = _financeService.GetTotalAdvancesForEmployeeFirm(entry.EmployeeFolder, entry.FirmName, monthKey);
                var (carriedDebt, _) = _financeService.CalculateCarriedDebtForFirm(entry.EmployeeFolder, entry.FirmName, _selectedYear, _selectedMonth);
                entry.Advance = currentAdvances + carriedDebt;
            }
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
            if (amount >= 1000)
            {
                var msg = $"{L("FinAdvanceDeleteConfirm") ?? "Delete advance"} {amount:N0} Kč?";
                if (MessageBox.Show(msg, "", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            _financeService.RemoveAdvance(advanceId);
            RefreshAdvanceSums();
            if (!string.IsNullOrEmpty(employeeName))
                App.ActivityLogService?.Log("AdvanceDeleted", "Advance", firmName, employeeName,
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

        private void ExportToExcel()
        {
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

            var selectDialog = new Views.ExportFirmSelectWindow(firmData);
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
                App.ActivityLogService?.Log("ExportExcel", "Export", "", "",
                    $"Експортовано зарплату {MonthDisplay} → Excel");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export error: {ex.Message}";
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
                    if (entry.GrossSalary > 0)
                        ws.Cell(row, col).Style.Fill.BackgroundColor = lightGreen;
                    col++;

                    ws.Cell(row, col).Value = entry.HoursWorked;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.0";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    col++;

                    ws.Cell(row, col).Value = entry.HourlyRate;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    col++;

                    ws.Cell(row, col).Value = entry.GrossSalary;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    col++;

                    ws.Cell(row, col).Value = entry.Advance;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (entry.Advance > 0)
                        ws.Cell(row, col).Style.Font.FontColor = XLColor.FromHtml("#C62828");
                    col++;

                    foreach (var f in fields)
                    {
                        var val = entry.CustomValues.TryGetValue(f.Id, out var v) ? v : 0;
                        ws.Cell(row, col).Value = val;
                        ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
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

            ws.Cell(row, 2).Value = DocL("FinColFirm") ?? "Firm";
            ws.Cell(row, 3).Value = DocL("FinColAmount") ?? "Amount";
            ws.Cell(row, 4).Value = colHours;
            var firmHeaderRange2 = ws.Range(row, 2, row, 4);
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
                ws.Cell(row, 2).Value = g.Key;
                ws.Cell(row, 2).Style.Font.FontColor = XLColor.FromHtml("#2F5496");
                ws.Cell(row, 3).Value = g.Sum(e => e.NetSalary);
                ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0 \"Kč\"";
                ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 4).Value = g.Sum(e => e.HoursWorked);
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.0";
                ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                for (int bc = 2; bc <= 4; bc++)
                {
                    ws.Cell(row, bc).Style.Fill.BackgroundColor = firmAltColor;
                    ws.Cell(row, bc).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                    ws.Cell(row, bc).Style.Border.BottomBorderColor = XLColor.FromHtml("#D0D0D0");
                }
                row++;
                firmAlt = !firmAlt;
            }

            var firmTableRange = ws.Range(firmTableHeaderRow, 2, row - 1, 4);
            firmTableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            firmTableRange.Style.Border.OutsideBorderColor = accentBlue;
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
            FirmExpenses.Clear();

            var allLabel = L("FinFilterAll") ?? "All";
            var isAll = string.IsNullOrEmpty(_selectedFirmFilter) || _selectedFirmFilter == allLabel;

            var expenses = isAll
                ? _financeService.GetFirmExpenses(_selectedYear, _selectedMonth)
                : _financeService.GetFirmExpenses(_selectedYear, _selectedMonth, _selectedFirmFilter);

            foreach (var exp in expenses)
            {
                exp.PropertyChanged += OnExpenseChanged;
                FirmExpenses.Add(exp);
            }
            RecalcTotals();
        }

        private void OnExpenseChanged(object? sender, PropertyChangedEventArgs e)
        {
            RecalcTotals();
            if (sender is FirmExpense exp)
                _financeService.UpdateFirmExpense(exp);
        }

        private void AddExpense()
        {
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
            RecalcTotals();
        }

        private void RemoveExpense(string? expenseId)
        {
            if (string.IsNullOrEmpty(expenseId)) return;
            var item = FirmExpenses.FirstOrDefault(e => e.Id == expenseId);
            if (item != null)
            {
                item.PropertyChanged -= OnExpenseChanged;
                FirmExpenses.Remove(item);
                _financeService.RemoveFirmExpense(expenseId);
                RecalcTotals();
            }
        }

        private void MarkAllPaid()
        {
            foreach (var e in VisibleEntries())
            {
                e.IsPaid = true;
                WriteSalaryHistory(e);
            }
            RecalcTotals();
            SaveReport();

            var firmNames = VisibleEntries().Select(e => e.FirmName).Distinct().ToList();
            App.ActivityLogService?.Log("MonthPaid", "Salary", string.Join(", ", firmNames), "",
                $"Позначено оплачено: {MonthDisplay} ({VisibleEntries().Count()} працівників)");
        }

        private void MarkAllUnpaid()
        {
            foreach (var e in VisibleEntries())
            {
                e.IsPaid = false;
                RemoveSalaryHistory(e);
            }
            RecalcTotals();
            SaveReport();
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
            _financeService.SaveFirmExpenses(FirmExpenses.ToList(), _selectedYear, _selectedMonth);
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
            if (EmployeeDetailsVm != null)
                EmployeeDetailsVm.RequestClose -= OnSalaryDetailsClose;
            EmployeeDetailsVm = new EmployeeDetailsViewModel(entry.FirmName, resolvedFolder, _employeeService);
            EmployeeDetailsVm.RequestClose += OnSalaryDetailsClose;
            IsEmployeeDetailsOpen = true;
        }

        private static string? L(string key)
        {
            try { return Application.Current.FindResource(key) as string; }
            catch { return null; }
        }

        private static string? DocL(string key)
        {
            try { return App.DocumentLocalizationService?.Get(key) ?? L(key); }
            catch { return L(key); }
        }
    }
}
