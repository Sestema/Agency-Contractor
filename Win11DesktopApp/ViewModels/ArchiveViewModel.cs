using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class ArchiveViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;
        private List<ArchivedEmployeeSummary> _allArchived = new();
        private string? _pendingEmployeeFolder;
        private string _sortField = App.AppSettingsService?.Settings?.ArchiveSortField ?? "EndDate";
        private bool _sortAscending = App.AppSettingsService?.Settings?.ArchiveSortAscending ?? false;
        private string _viewMode = App.AppSettingsService?.Settings?.ArchiveViewMode ?? "List";
        private double _zoomLevel = App.AppSettingsService?.Settings?.ArchiveZoomLevel ?? 1.0;
        private string _statFilter = "all";
        private int _totalArchivedCount;
        private int _archivedThisMonthCount;
        private int _withoutPhotoCount;

        public ICommand GoBackCommand { get; }
        public ICommand RestoreEmployeeCommand { get; }
        public ICommand ConfirmRestoreCommand { get; }
        public ICommand CancelRestoreCommand { get; }
        public ICommand OpenEmployeeFolderCommand { get; }
        public ICommand ViewEmployeeCommand { get; }
        public ICommand SortByCommand { get; }
        public ICommand SetViewModeCommand { get; }
        public ICommand FilterByStatCommand { get; }

        // --- List ---
        private ObservableCollection<ArchivedEmployeeSummary> _archivedEmployees = new();
        public ObservableCollection<ArchivedEmployeeSummary> ArchivedEmployees
        {
            get => _archivedEmployees;
            set => SetProperty(ref _archivedEmployees, value);
        }

        private bool _hasArchived;
        public bool HasArchived
        {
            get => _hasArchived;
            set => SetProperty(ref _hasArchived, value);
        }

        public int FilteredCount => ArchivedEmployees.Count;

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
                    ApplyFilter();
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
                    ApplyFilter();
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

        public ObservableCollection<EmployerCompany> AvailableCompanies => App.CompanyService?.Companies ?? new ObservableCollection<EmployerCompany>();

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

        public ArchiveViewModel(string? employeeToOpenFolder = null)
        {
            _employeeService = App.EmployeeService;
            _pendingEmployeeFolder = employeeToOpenFolder;

            GoBackCommand = new RelayCommand(o => App.NavigationService?.NavigateTo(new MainViewModel()));

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
                ApplyFilter();
            });
            SetViewModeCommand = new RelayCommand(o => ViewMode = o as string ?? "List");
            FilterByStatCommand = new RelayCommand(o => StatFilter = o as string ?? "all");

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
            EmployeeDetailsVm = new EmployeeDetailsViewModel(emp.FirmName, emp.EmployeeFolder, _employeeService);
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
                _allArchived = await Task.Run(() => _employeeService.GetArchivedEmployees());
                RefreshStats();
                ApplyFilter();
                TryOpenPendingEmployee();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ArchiveViewModel.LoadArchive error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<ArchivedEmployeeSummary> source = _allArchived;
            var query = SearchQuery?.Trim() ?? string.Empty;

            source = StatFilter switch
            {
                "recent" => source.Where(e => IsThisMonth(e.EndDate)),
                "no_photo" => source.Where(e => !e.HasPhoto),
                _ => source
            };

            if (!string.IsNullOrEmpty(query))
            {
                source = source.Where(e =>
                    (!string.IsNullOrEmpty(e.FullName) && e.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.FirmName) && e.FirmName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.PositionTitle) && e.PositionTitle.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.StartDate) && e.StartDate.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.EndDate) && e.EndDate.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            var filtered = source.ToList();
            filtered = SortField switch
            {
                "Name" => SortAscending
                    ? filtered.OrderBy(e => e.FullName ?? string.Empty).ThenBy(e => e.FirmName ?? string.Empty).ToList()
                    : filtered.OrderByDescending(e => e.FullName ?? string.Empty).ThenBy(e => e.FirmName ?? string.Empty).ToList(),
                "Firm" => SortAscending
                    ? filtered.OrderBy(e => e.FirmName ?? string.Empty).ThenBy(e => e.FullName ?? string.Empty).ToList()
                    : filtered.OrderByDescending(e => e.FirmName ?? string.Empty).ThenBy(e => e.FullName ?? string.Empty).ToList(),
                "StartDate" => SortAscending
                    ? filtered.OrderBy(e => DateParsingHelper.TryParseDate(e.StartDate) ?? DateTime.MaxValue).ThenBy(e => e.FullName ?? string.Empty).ToList()
                    : filtered.OrderByDescending(e => DateParsingHelper.TryParseDate(e.StartDate) ?? DateTime.MinValue).ThenBy(e => e.FullName ?? string.Empty).ToList(),
                _ => SortAscending
                    ? filtered.OrderBy(e => DateParsingHelper.TryParseDate(e.EndDate) ?? DateTime.MaxValue).ThenBy(e => e.FullName ?? string.Empty).ToList()
                    : filtered.OrderByDescending(e => DateParsingHelper.TryParseDate(e.EndDate) ?? DateTime.MinValue).ThenBy(e => e.FullName ?? string.Empty).ToList()
            };

            ArchivedEmployees = new ObservableCollection<ArchivedEmployeeSummary>(filtered);
            HasArchived = ArchivedEmployees.Count > 0;
            OnPropertyChanged(nameof(FilteredCount));
        }

        private void RefreshStats()
        {
            TotalArchivedCount = _allArchived.Count;
            ArchivedThisMonthCount = _allArchived.Count(e => IsThisMonth(e.EndDate));
            WithoutPhotoCount = _allArchived.Count(e => !e.HasPhoto);
            OnPropertyChanged(nameof(FilteredCount));
        }

        private static bool IsThisMonth(string dateStr)
        {
            var dt = DateParsingHelper.TryParseDate(dateStr);
            return dt.HasValue && dt.Value.Month == DateTime.Today.Month && dt.Value.Year == DateTime.Today.Year;
        }

        private void SaveArchiveDisplaySettings()
        {
            if (App.AppSettingsService == null)
                return;

            App.AppSettingsService.Settings.ArchiveSortField = SortField;
            App.AppSettingsService.Settings.ArchiveSortAscending = SortAscending;
            App.AppSettingsService.Settings.ArchiveViewMode = ViewMode;
            App.AppSettingsService.Settings.ArchiveZoomLevel = ZoomLevel;
            App.AppSettingsService.SaveSettings();
        }

        private void TryOpenPendingEmployee()
        {
            if (string.IsNullOrWhiteSpace(_pendingEmployeeFolder))
                return;

            var pendingFolder = _pendingEmployeeFolder;
            _pendingEmployeeFolder = null;

            var employee = _allArchived.FirstOrDefault(emp =>
                !string.IsNullOrEmpty(emp.EmployeeFolder) &&
                string.Equals(emp.EmployeeFolder, pendingFolder, StringComparison.OrdinalIgnoreCase));

            if (employee != null)
                OpenEmployeeDetails(employee);
        }

        private async Task ConfirmRestoreAsync()
        {
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
                var workAddress = SelectedAddress != null
                    ? $"{SelectedAddress.Street} {SelectedAddress.Number}, {SelectedAddress.City} {SelectedAddress.ZipCode}".Trim()
                    : string.Empty;

                var result = await _employeeService.RestoreFromArchive(
                    EmployeeToRestore.EmployeeFolder,
                    SelectedCompany.Name,
                    NewStartDate,
                    NewContractSignDate,
                    positionTitle,
                    workAddress
                );

                if (!string.IsNullOrEmpty(result))
                {
                    await _employeeService.AddHistoryEntry(result, new EmployeeModels.EmployeeHistoryEntry
                    {
                        EventType = "Restored",
                        Action = Res("HistoryActionRestored"),
                        Field = SelectedCompany.Name,
                        Description = string.Format(Res("HistoryDescRestored"), EmployeeToRestore.FullName, SelectedCompany.Name)
                    });

                    App.ActivityLogService?.Log("EmployeeRestored", "Archive", SelectedCompany.Name,
                        EmployeeToRestore.FullName,
                        $"Відновлено {EmployeeToRestore.FullName} до {SelectedCompany.Name}, дата початку: {NewStartDate}",
                        EmployeeToRestore.FirmName, SelectedCompany.Name,
                        employeeFolder: EmployeeToRestore.EmployeeFolder);

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
    }
}
