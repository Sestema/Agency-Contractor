using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class ArchiveViewModel : ViewModelBase
    {
        private readonly NavigationService _navigationService;
        private readonly EmployeeService _employeeService;
        private readonly AppSettingsService _appSettingsService;
        private readonly CompanyService _companyService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private readonly ActivityLogService _activityLogService;
        private readonly ObservableCollection<ArchivedEmployeeSummary> _archivedEmployeesSource = new();
        private readonly ICollectionView _archivedEmployeesView;
        private readonly DispatcherTimer _searchDebounce;
        private string? _pendingEmployeeFolder;
        private string _sortField = "EndDate";
        private bool _sortAscending;
        private string _viewMode = "List";
        private double _zoomLevel = 1.0;
        private string _statFilter = "all";
        private int _totalArchivedCount;
        private int _archivedThisMonthCount;
        private int _withoutPhotoCount;
        private int _filteredCount;

        public ICommand GoBackCommand { get; }
        public ICommand RestoreEmployeeCommand { get; }
        public ICommand ConfirmRestoreCommand { get; }
        public ICommand CancelRestoreCommand { get; }
        public ICommand OpenEmployeeFolderCommand { get; }
        public ICommand ViewEmployeeCommand { get; }
        public ICommand SortByCommand { get; }
        public ICommand SetViewModeCommand { get; }
        public ICommand FilterByStatCommand { get; }
        public ICommand ClearFilterCommand { get; }

        // --- List ---
        public ObservableCollection<ArchivedEmployeeSummary> ArchivedEmployees => _archivedEmployeesSource;

        private bool _hasArchivedData;
        public bool HasArchivedData
        {
            get => _hasArchivedData;
            private set
            {
                if (SetProperty(ref _hasArchivedData, value))
                    OnPropertyChanged(nameof(ShowNoMatchesState));
            }
        }

        private bool _hasFilteredResults;
        public bool HasFilteredResults
        {
            get => _hasFilteredResults;
            private set
            {
                if (SetProperty(ref _hasFilteredResults, value))
                    OnPropertyChanged(nameof(ShowNoMatchesState));
            }
        }

        public bool ShowNoMatchesState => HasArchivedData && !HasFilteredResults;

        public int FilteredCount
        {
            get => _filteredCount;
            private set => SetProperty(ref _filteredCount, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public int TotalArchivedCount
        {
            get => _totalArchivedCount;
            set => SetProperty(ref _totalArchivedCount, value);
        }

        public int ArchivedThisMonthCount
        {
            get => _archivedThisMonthCount;
            set => SetProperty(ref _archivedThisMonthCount, value);
        }

        public int WithoutPhotoCount
        {
            get => _withoutPhotoCount;
            set => SetProperty(ref _withoutPhotoCount, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    _searchDebounce.Stop();
                    _searchDebounce.Start();
                }
            }
        }

        public string SortField
        {
            get => _sortField;
            set => SetProperty(ref _sortField, value);
        }

        public bool SortAscending
        {
            get => _sortAscending;
            set => SetProperty(ref _sortAscending, value);
        }

        public string ViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    OnPropertyChanged(nameof(IsListView));
                    OnPropertyChanged(nameof(IsTilesView));
                    SaveArchiveDisplaySettings();
                }
            }
        }

        public bool IsListView => ViewMode == "List";
        public bool IsTilesView => ViewMode == "Tiles";

        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                var clamped = Math.Clamp(value, 0.75, 1.35);
                if (SetProperty(ref _zoomLevel, clamped))
                    SaveArchiveDisplaySettings();
            }
        }

        public string StatFilter
        {
            get => _statFilter;
            set
            {
                if (SetProperty(ref _statFilter, value))
                    RefreshArchiveView();
            }
        }

        // --- Restore dialog ---
        private bool _isRestoreDialogOpen;
        public bool IsRestoreDialogOpen
        {
            get => _isRestoreDialogOpen;
            set => SetProperty(ref _isRestoreDialogOpen, value);
        }

        private ArchivedEmployeeSummary? _employeeToRestore;
        public ArchivedEmployeeSummary? EmployeeToRestore
        {
            get => _employeeToRestore;
            set => SetProperty(ref _employeeToRestore, value);
        }

        public ObservableCollection<EmployerCompany> AvailableCompanies => _companyService.Companies;

        private EmployerCompany? _selectedCompany;
        public EmployerCompany? SelectedCompany
        {
            get => _selectedCompany;
            set
            {
                if (SetProperty(ref _selectedCompany, value))
                    OnSelectedCompanyChanged();
            }
        }

        // Positions and Addresses from selected company
        private ObservableCollection<Position> _companyPositions = new();
        public ObservableCollection<Position> CompanyPositions
        {
            get => _companyPositions;
            set => SetProperty(ref _companyPositions, value);
        }

        private ObservableCollection<WorkAddress> _companyAddresses = new();
        public ObservableCollection<WorkAddress> CompanyAddresses
        {
            get => _companyAddresses;
            set => SetProperty(ref _companyAddresses, value);
        }

        private Position? _selectedPosition;
        public Position? SelectedPosition
        {
            get => _selectedPosition;
            set => SetProperty(ref _selectedPosition, value);
        }

        private WorkAddress? _selectedAddress;
        public WorkAddress? SelectedAddress
        {
            get => _selectedAddress;
            set => SetProperty(ref _selectedAddress, value);
        }

        private string _newStartDate = string.Empty;
        public string NewStartDate
        {
            get => _newStartDate;
            set => SetProperty(ref _newStartDate, value);
        }

        private string _newContractSignDate = string.Empty;
        public string NewContractSignDate
        {
            get => _newContractSignDate;
            set => SetProperty(ref _newContractSignDate, value);
        }

        private string _restoreStatus = string.Empty;
        public string RestoreStatus
        {
            get => _restoreStatus;
            set => SetProperty(ref _restoreStatus, value);
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

        public ArchiveViewModel(
            string? employeeToOpenFolder = null,
            NavigationService? navigationService = null,
            EmployeeService? employeeService = null,
            AppSettingsService? appSettingsService = null,
            CompanyService? companyService = null,
            EmployeeDetailsViewModelFactory? employeeDetailsViewModelFactory = null,
            ActivityLogService? activityLogService = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new InvalidOperationException("EmployeeDetailsViewModelFactory is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _pendingEmployeeFolder = employeeToOpenFolder;
            _sortField = _appSettingsService.Settings.ArchiveSortField ?? "EndDate";
            _sortAscending = _appSettingsService.Settings.ArchiveSortAscending;
            _viewMode = _appSettingsService.Settings.ArchiveViewMode ?? "List";
            _zoomLevel = _appSettingsService.Settings.ArchiveZoomLevel;
            _archivedEmployeesView = CollectionViewSource.GetDefaultView(_archivedEmployeesSource);
            _archivedEmployeesView.Filter = FilterArchived;
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                RefreshArchiveView();
            };

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());

            OpenEmployeeFolderCommand = new RelayCommand(o =>
            {
                if (o is ArchivedEmployeeSummary emp && !string.IsNullOrEmpty(emp.EmployeeFolder))
                {
                    try { Process.Start(new ProcessStartInfo { FileName = emp.EmployeeFolder, UseShellExecute = true }); }
                    catch (Exception ex) { LoggingService.LogWarning("ArchiveViewModel.OpenFolder", ex.Message); }
                }
            });

            RestoreEmployeeCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("відновити працівника з архіву"))
                    return;

                if (o is ArchivedEmployeeSummary emp)
                {
                    EmployeeToRestore = emp;
                    NewStartDate = DateTime.Today.ToString("dd.MM.yyyy");
                    NewContractSignDate = DateTime.Today.ToString("dd.MM.yyyy");
                    RestoreStatus = string.Empty;
                    SelectedCompany = AvailableCompanies.FirstOrDefault();
                    IsRestoreDialogOpen = true;
                }
            });

            ConfirmRestoreCommand = new AsyncRelayCommand(_ => ConfirmRestoreAsync());
            CancelRestoreCommand = new RelayCommand(o => IsRestoreDialogOpen = false);
            SortByCommand = new RelayCommand(o =>
            {
                var field = o as string ?? "EndDate";
                if (SortField == field)
                {
                    SortAscending = !SortAscending;
                }
                else
                {
                    SortField = field;
                    SortAscending = field != "EndDate";
                }

                SaveArchiveDisplaySettings();
                ApplySort();
                RefreshFilteredCount();
                HasFilteredResults = FilteredCount > 0;
            });
            SetViewModeCommand = new RelayCommand(o => ViewMode = o as string ?? "List");
            FilterByStatCommand = new RelayCommand(o => StatFilter = o as string ?? "all");
            ClearFilterCommand = new RelayCommand(_ =>
            {
                var changed = false;
                changed |= SetProperty(ref _searchQuery, string.Empty, nameof(SearchQuery));
                changed |= SetProperty(ref _statFilter, "all", nameof(StatFilter));
                if (changed)
                {
                    _searchDebounce.Stop();
                    RefreshArchiveView();
                }
            });

            ViewEmployeeCommand = new RelayCommand(o =>
            {
                if (o is ArchivedEmployeeSummary emp && !string.IsNullOrEmpty(emp.EmployeeFolder))
                    OpenEmployeeDetails(emp);
            });

            _ = LoadArchiveAsync();
        }

        private void OpenEmployeeDetails(ArchivedEmployeeSummary emp)
        {
            CleanupDetailsVm();
            EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(emp.FirmName, emp.EmployeeFolder, _employeeService, employeeId: emp.UniqueId);
            EmployeeDetailsVm.IsArchiveMode = true;
            EmployeeDetailsVm.RequestClose += OnDetailsClose;
            IsEmployeeDetailsOpen = true;
        }

        private void OnDetailsClose() => IsEmployeeDetailsOpen = false;

        private void CleanupDetailsVm()
        {
            if (EmployeeDetailsVm != null)
                EmployeeDetailsVm.RequestClose -= OnDetailsClose;
        }

        private void OnSelectedCompanyChanged()
        {
            if (SelectedCompany != null)
            {
                CompanyPositions = new ObservableCollection<Position>(SelectedCompany.Positions);
                CompanyAddresses = new ObservableCollection<WorkAddress>(SelectedCompany.Addresses);
                SelectedPosition = CompanyPositions.FirstOrDefault();
                SelectedAddress = CompanyAddresses.FirstOrDefault();
            }
            else
            {
                CompanyPositions = new ObservableCollection<Position>();
                CompanyAddresses = new ObservableCollection<WorkAddress>();
                SelectedPosition = null;
                SelectedAddress = null;
            }
        }

        private async Task LoadArchiveAsync()
        {
            IsLoading = true;
            try
            {
                var loaded = await Task.Run(() => _employeeService.GetArchivedEmployees());

                // Bulk-populate the source. We intentionally do NOT wrap this in
                // _archivedEmployeesView.DeferRefresh(): DeferRefresh is designed
                // to batch direct VIEW mutations (Filter / SortDescriptions /
                // GroupDescriptions), not source mutations. During defer, the
                // default ListCollectionView still processes CollectionChanged
                // from Add/Remove, and its ProcessCollectionChangedWithAdjustedIndex
                // hits get_CurrentPosition -> VerifyRefreshNotDeferred -> throws
                // InvalidOperationException, which previously aborted the whole
                // load and left the archive empty.
                _archivedEmployeesSource.Clear();
                if (loaded != null)
                {
                    foreach (var item in loaded)
                        _archivedEmployeesSource.Add(item);
                }

                // Step 1: commit BASE state first so the UI always shows archived
                // data, even if the optional view pipeline (sort / filtered count)
                // throws below. FilteredCount is pre-seeded from source as a safe
                // fallback in case RefreshFilteredCount() is skipped on error.
                HasArchivedData = _archivedEmployeesSource.Count > 0;
                FilteredCount = _archivedEmployeesSource.Count;
                HasFilteredResults = FilteredCount > 0;
                RefreshStats();

                // Step 2: apply sort + filtered count via ICollectionView. These
                // can throw under edge cases (broken comparer, bad item, WPF
                // quirks around CustomSort). If anything fails, the base state
                // from Step 1 stays, so the archive tab stays usable.
                try
                {
                    ApplySort();
                    RefreshFilteredCount();
                    HasFilteredResults = FilteredCount > 0;
                }
                catch (Exception viewEx)
                {
                    LoggingService.LogError("ArchiveViewModel.LoadArchive.ViewPipeline", viewEx);
                }

                TryOpenPendingEmployee();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ArchiveViewModel.LoadArchive", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool FilterArchived(object obj)
        {
            // Fail-open: if the filter throws on a single bad item, we keep that
            // item visible instead of collapsing the whole archive. The actual
            // exception is logged once per occurrence for later diagnosis.
            try
            {
                if (obj is not ArchivedEmployeeSummary archived)
                    return false;

                switch (StatFilter)
                {
                    case "recent":
                    {
                        var endDate = archived.ParsedEndDate;
                        if (endDate == null)
                            return false;

                        var now = DateTime.Today;
                        if (endDate.Value.Year != now.Year || endDate.Value.Month != now.Month)
                            return false;
                        break;
                    }
                    case "no_photo":
                        if (archived.HasPhoto)
                            return false;
                        break;
                }

                var query = _searchQuery?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(query))
                {
                    return (!string.IsNullOrEmpty(archived.FullName) && archived.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(archived.FirmName) && archived.FirmName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(archived.PositionTitle) && archived.PositionTitle.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(archived.StartDate) && archived.StartDate.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(archived.EndDate) && archived.EndDate.Contains(query, StringComparison.OrdinalIgnoreCase));
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        private void RefreshArchiveView()
        {
            _archivedEmployeesView.Refresh();
            RefreshFilteredCount();
            HasFilteredResults = FilteredCount > 0;
        }

        private void RefreshStats()
        {
            TotalArchivedCount = _archivedEmployeesSource.Count;
            ArchivedThisMonthCount = _archivedEmployeesSource.Count(e => e.ParsedEndDate.HasValue
                && e.ParsedEndDate.Value.Month == DateTime.Today.Month
                && e.ParsedEndDate.Value.Year == DateTime.Today.Year);
            WithoutPhotoCount = _archivedEmployeesSource.Count(e => !e.HasPhoto);
        }

        private void RefreshFilteredCount()
        {
            FilteredCount = _archivedEmployeesView.Cast<object>().Count();
        }

        private void ApplySort()
        {
            if (_archivedEmployeesView is ListCollectionView listCollectionView)
            {
                listCollectionView.CustomSort = new ArchiveComparer(SortField, SortAscending);
                listCollectionView.Refresh();
            }
        }

        private void SaveArchiveDisplaySettings()
        {
            _appSettingsService.Settings.ArchiveSortField = SortField;
            _appSettingsService.Settings.ArchiveSortAscending = SortAscending;
            _appSettingsService.Settings.ArchiveViewMode = ViewMode;
            _appSettingsService.Settings.ArchiveZoomLevel = ZoomLevel;
            _appSettingsService.SaveSettings();
        }

        private void TryOpenPendingEmployee()
        {
            if (string.IsNullOrWhiteSpace(_pendingEmployeeFolder))
                return;

            var pendingFolder = _pendingEmployeeFolder;
            _pendingEmployeeFolder = null;

            var employee = _archivedEmployeesSource.FirstOrDefault(emp =>
                !string.IsNullOrEmpty(emp.EmployeeFolder) &&
                string.Equals(emp.EmployeeFolder, pendingFolder, StringComparison.OrdinalIgnoreCase));

            if (employee != null)
                OpenEmployeeDetails(employee);
        }

        private async Task ConfirmRestoreAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("підтвердити відновлення працівника"))
                return;

            if (EmployeeToRestore == null)
            {
                RestoreStatus = Res("MsgNoEmployeeSelected");
                return;
            }

            if (SelectedCompany == null)
            {
                RestoreStatus = Res("MsgSelectFirmRestore");
                return;
            }

            if (string.IsNullOrWhiteSpace(NewStartDate))
            {
                RestoreStatus = Res("MsgEnterStartDate");
                return;
            }

            try
            {
                var positionTitle = SelectedPosition?.Title ?? string.Empty;
                var positionNumber = SelectedPosition?.PositionNumber ?? string.Empty;
                var workAddress = SelectedAddress != null
                    ? $"{SelectedAddress.Street} {SelectedAddress.Number}, {SelectedAddress.City} {SelectedAddress.ZipCode}".Trim()
                    : string.Empty;

                var result = await _employeeService.RestoreFromArchive(
                    EmployeeToRestore.EmployeeFolder,
                    SelectedCompany.Name,
                    NewStartDate,
                    NewContractSignDate,
                    positionTitle,
                    positionNumber,
                    workAddress
                );

                if (result.Success)
                {
                    await _employeeService.AddHistoryEntry(result.RestoredFolder, EmployeeToRestore.UniqueId, new EmployeeModels.EmployeeHistoryEntry
                    {
                        EventType = "Restored",
                        Action = Res("HistoryActionRestored"),
                        Field = SelectedCompany.Name,
                        Description = string.Format(Res("HistoryDescRestored"), EmployeeToRestore.FullName, SelectedCompany.Name)
                    });

                    _activityLogService.Log("EmployeeRestored", "Archive", SelectedCompany.Name,
                        EmployeeToRestore.FullName,
                        $"Відновлено {EmployeeToRestore.FullName} до {SelectedCompany.Name}, дата початку: {NewStartDate}",
                        EmployeeToRestore.FirmName, SelectedCompany.Name,
                        employeeFolder: result.RestoredFolder,
                        relatedOperationId: result.OperationId);

                    IsRestoreDialogOpen = false;
                    await LoadArchiveAsync();
                }
                else
                {
                    RestoreStatus = Res("MsgRestoreError");
                }
            }
            catch (Exception ex)
            {
                RestoreStatus = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
        }

        private sealed class ArchiveComparer : IComparer
        {
            private readonly string _field;
            private readonly bool _ascending;

            public ArchiveComparer(string field, bool ascending)
            {
                _field = field;
                _ascending = ascending;
            }

            public int Compare(object? x, object? y)
            {
                // Fail-safe comparer: on any unexpected error we treat items as
                // equal (return 0) instead of throwing, which would abort the
                // entire CustomSort / Refresh cycle and hide every archived row.
                try
                {
                    var a = x as ArchivedEmployeeSummary;
                    var b = y as ArchivedEmployeeSummary;
                    if (ReferenceEquals(a, b))
                        return 0;
                    if (a == null)
                        return 1;
                    if (b == null)
                        return -1;

                    var primary = _field switch
                    {
                        "Name" => CompareStrings(a.FullName, b.FullName, _ascending),
                        "Firm" => CompareStrings(a.FirmName, b.FirmName, _ascending),
                        "StartDate" => CompareDatesNullsLast(a.ParsedStartDate, b.ParsedStartDate, _ascending),
                        _ => CompareDatesNullsLast(a.ParsedEndDate, b.ParsedEndDate, _ascending)
                    };

                    if (primary != 0)
                        return primary;

                    return CompareThenBy(a, b, _field);
                }
                catch
                {
                    return 0;
                }
            }

            private static int CompareThenBy(ArchivedEmployeeSummary a, ArchivedEmployeeSummary b, string primaryField)
            {
                return primaryField switch
                {
                    "Name" => string.Compare(a.FirmName, b.FirmName, StringComparison.CurrentCultureIgnoreCase),
                    "Firm" => string.Compare(a.FullName, b.FullName, StringComparison.CurrentCultureIgnoreCase),
                    _ => string.Compare(a.FullName, b.FullName, StringComparison.CurrentCultureIgnoreCase)
                };
            }

            private static int CompareStrings(string? a, string? b, bool ascending)
            {
                var result = string.Compare(a ?? string.Empty, b ?? string.Empty, StringComparison.CurrentCultureIgnoreCase);
                return ascending ? result : -result;
            }

            private static int CompareDatesNullsLast(DateTime? a, DateTime? b, bool ascending)
            {
                if (a == null && b == null)
                    return 0;
                if (a == null)
                    return 1;
                if (b == null)
                    return -1;

                return ascending
                    ? DateTime.Compare(a.Value, b.Value)
                    : DateTime.Compare(b.Value, a.Value);
            }
        }
    }
}
