using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ClosedXML.Excel;
using Win11DesktopApp.Models;
using EmployeeModels = Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.ViewModels
{
    public class BatchAIValidationResultItem : ViewModelBase
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string DocumentName { get; set; } = string.Empty;
        public string DocumentPath { get; set; } = string.Empty;
        public bool CanOpenDocument => !string.IsNullOrWhiteSpace(DocumentPath) && File.Exists(DocumentPath);
        public string FieldKey { get; set; } = string.Empty;
        public string FieldDisplayName { get; set; } = string.Empty;
        private string _currentValue = string.Empty;
        public string CurrentValue
        {
            get => _currentValue;
            set => SetProperty(ref _currentValue, value);
        }

        public string SuggestedValue { get; set; } = string.Empty;
        private string _severity = "ok";
        public string Severity
        {
            get => _severity;
            set => SetProperty(ref _severity, value);
        }

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        private bool _canApply;
        public bool CanApply
        {
            get => _canApply;
            set => SetProperty(ref _canApply, value);
        }

        private bool _isApplied;
        public bool IsApplied
        {
            get => _isApplied;
            set => SetProperty(ref _isApplied, value);
        }
    }

    public class EmployeesViewModel : ViewModelBase
    {
        private readonly NavigationService _navigationService;
        private readonly EmployeeService _employeeService;
        private readonly AddEmployeeWizardViewModelFactory _addEmployeeWizardViewModelFactory;
        private readonly CurrentProfileService _currentProfileService;
        private readonly ProfileAuthService _profileAuthService;
        private readonly RecentlyDeletedService _recentlyDeletedService;
        private readonly AppSettingsService _appSettingsService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private readonly ActivityLogService _activityLogService;
        private readonly TemplateService _templateService;
        private readonly DocumentGenerationService _documentGenerationService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly GeminiApiService _geminiApiService;
        private readonly EmployerCompany? _company;
        private readonly CompanyService? _companyService;
        private readonly SyncEventService? _syncEventService;
        private readonly bool _showAllCompanies;
        private List<EmployeeModels.EmployeeSummary> _allEmployees = new List<EmployeeModels.EmployeeSummary>();
        private string _lastStatus = string.Empty;
        private int _loadGeneration;
        private CancellationTokenSource? _batchAICts;

        private ObservableCollection<EmployeeModels.EmployeeSummary> _employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
        public ObservableCollection<EmployeeModels.EmployeeSummary> Employees
        {
            get => _employees;
            set => SetProperty(ref _employees, value);
        }

        private bool _hasEmployees;
        public bool HasEmployees
        {
            get => _hasEmployees;
            set => SetProperty(ref _hasEmployees, value);
        }

        private bool _hasVisibleEmployees;
        public bool HasVisibleEmployees
        {
            get => _hasVisibleEmployees;
            set => SetProperty(ref _hasVisibleEmployees, value);
        }

        private bool _isCompanySelected;
        public bool IsCompanySelected
        {
            get => _isCompanySelected;
            set => SetProperty(ref _isCompanySelected, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set => SetProperty(ref _isError, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string Title => _showAllCompanies
            ? "Активні працівники"
            : _company == null
            ? GetString("TitleEmployeesGeneric") ?? "Employees"
            : string.Format(GetString("TitleEmployees") ?? "{0}", _company.Name);

        public string LoadingMessage => _showAllCompanies
            ? "Завантажуємо активних працівників з усіх фірм..."
            : GetString("MsgEmployeesLoading") ?? "Loading employees...";

        // Statistics
        private int _totalCount;
        public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

        private int _problemsCount;
        public int ProblemsCount { get => _problemsCount; set => SetProperty(ref _problemsCount, value); }

        private int _newThisMonth;
        public int NewThisMonth { get => _newThisMonth; set => SetProperty(ref _newThisMonth, value); }

        private string _statFilter = "all";
        public string StatFilter
        {
            get => _statFilter;
            set
            {
                if (SetProperty(ref _statFilter, value))
                    ApplyFilter();
            }
        }

        // Selection mode
        private bool _isSelectionMode;
        public bool IsSelectionMode
        {
            get => _isSelectionMode;
            set => SetProperty(ref _isSelectionMode, value);
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        // Batch generate dialog
        private bool _isBatchGenerateOpen;
        public bool IsBatchGenerateOpen
        {
            get => _isBatchGenerateOpen;
            set => SetProperty(ref _isBatchGenerateOpen, value);
        }

        private ObservableCollection<TemplateEntry> _batchTemplates = new();
        public ObservableCollection<TemplateEntry> BatchTemplates
        {
            get => _batchTemplates;
            set => SetProperty(ref _batchTemplates, value);
        }

        private string _batchStatusMessage = string.Empty;
        public string BatchStatusMessage
        {
            get => _batchStatusMessage;
            set => SetProperty(ref _batchStatusMessage, value);
        }

        // Batch AI validation dialog
        private bool _isBatchAIValidationOpen;
        public bool IsBatchAIValidationOpen
        {
            get => _isBatchAIValidationOpen;
            set => SetProperty(ref _isBatchAIValidationOpen, value);
        }

        private bool _isBatchAIValidationRunning;
        public bool IsBatchAIValidationRunning
        {
            get => _isBatchAIValidationRunning;
            set => SetProperty(ref _isBatchAIValidationRunning, value);
        }

        private bool _batchAICheckPassport = true;
        public bool BatchAICheckPassport
        {
            get => _batchAICheckPassport;
            set => SetProperty(ref _batchAICheckPassport, value);
        }

        private bool _batchAICheckVisa = true;
        public bool BatchAICheckVisa
        {
            get => _batchAICheckVisa;
            set => SetProperty(ref _batchAICheckVisa, value);
        }

        private bool _batchAICheckInsurance = true;
        public bool BatchAICheckInsurance
        {
            get => _batchAICheckInsurance;
            set => SetProperty(ref _batchAICheckInsurance, value);
        }

        private bool _batchAICheckPermit = true;
        public bool BatchAICheckPermit
        {
            get => _batchAICheckPermit;
            set => SetProperty(ref _batchAICheckPermit, value);
        }

        private bool _batchAICheckOnlySelected;
        public bool BatchAICheckOnlySelected
        {
            get => _batchAICheckOnlySelected;
            set => SetProperty(ref _batchAICheckOnlySelected, value);
        }

        private bool _showBatchAIOptions = true;
        public bool ShowBatchAIOptions
        {
            get => _showBatchAIOptions;
            set => SetProperty(ref _showBatchAIOptions, value);
        }

        private int _batchAIProgressCurrent;
        public int BatchAIProgressCurrent
        {
            get => _batchAIProgressCurrent;
            set => SetProperty(ref _batchAIProgressCurrent, value);
        }

        private int _batchAIProgressTotal;
        public int BatchAIProgressTotal
        {
            get => _batchAIProgressTotal;
            set => SetProperty(ref _batchAIProgressTotal, value);
        }

        private string _batchAIStatusMessage = string.Empty;
        public string BatchAIStatusMessage
        {
            get => _batchAIStatusMessage;
            set => SetProperty(ref _batchAIStatusMessage, value);
        }

        private string _batchAICurrentEmployee = string.Empty;
        public string BatchAICurrentEmployee
        {
            get => _batchAICurrentEmployee;
            set => SetProperty(ref _batchAICurrentEmployee, value);
        }

        private string _batchAICurrentDocument = string.Empty;
        public string BatchAICurrentDocument
        {
            get => _batchAICurrentDocument;
            set => SetProperty(ref _batchAICurrentDocument, value);
        }

        private string _batchAICurrentField = string.Empty;
        public string BatchAICurrentField
        {
            get => _batchAICurrentField;
            set => SetProperty(ref _batchAICurrentField, value);
        }

        private ObservableCollection<BatchAIValidationResultItem> _batchAIResults = new();
        public ObservableCollection<BatchAIValidationResultItem> BatchAIResults
        {
            get => _batchAIResults;
            set => SetProperty(ref _batchAIResults, value);
        }

        public bool HasBatchAIResults => BatchAIResults.Count > 0;

        // Sorting
        private string _sortField;
        public string SortField
        {
            get => _sortField;
            set => SetProperty(ref _sortField, value);
        }

        private bool _sortAscending;
        public bool SortAscending
        {
            get => _sortAscending;
            set => SetProperty(ref _sortAscending, value);
        }

        public ICommand GoBackCommand { get; }
        public ICommand AddEmployeeCommand { get; }
        public ICommand CloseAddEmployeeDialogCommand { get; }
        public ICommand SelectCompanyCommand { get; }
        public ICommand OpenEmployeeCommand { get; }
        public ICommand EditEmployeeCommand { get; }
        public ICommand DeleteEmployeeCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }
        public ICommand CancelDeleteCommand { get; }
        public ICommand OpenEmployeeFolderCommand { get; }
        public ICommand ExportToExcelCommand { get; }
        public ICommand ToggleSelectionModeCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand BatchGenerateCommand { get; }
        public ICommand CloseBatchGenerateCommand { get; }
        public ICommand BatchGenerateFromTemplateCommand { get; }
        public ICommand BatchGenerateToFolderCommand { get; }
        public ICommand OpenBatchAIValidationCommand { get; }
        public ICommand CloseBatchAIValidationCommand { get; }
        public ICommand StartBatchAIValidationCommand { get; }
        public ICommand CancelBatchAIValidationCommand { get; }
        public ICommand ApplyBatchAISuggestionCommand { get; }
        public ICommand OpenBatchAIDocumentCommand { get; }
        public ICommand ShowBatchAIOptionsCommand { get; }
        public ICommand SortByCommand { get; }
        public ICommand SetViewModeCommand { get; }
        public ICommand FilterByStatCommand { get; }

        private string _viewMode;
        public string ViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    OnPropertyChanged(nameof(IsTableView));
                    OnPropertyChanged(nameof(IsListView));
                    OnPropertyChanged(nameof(IsTilesView));
                    OnPropertyChanged(nameof(IsIconsView));
                    _appSettingsService.Settings.EmployeeViewMode = value;
                    _appSettingsService.SaveSettings();
                }
            }
        }

        public bool IsTableView => ViewMode == "Table";
        public bool IsListView => ViewMode == "List";
        public bool IsTilesView => ViewMode == "Tiles";
        public bool IsIconsView => ViewMode == "Icons";

        private double _zoomLevel;
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (SetProperty(ref _zoomLevel, value))
                {
                    _appSettingsService.Settings.EmployeeZoomLevel = value;
                    _appSettingsService.SaveSettings();
                }
            }
        }

        private bool _isAddEmployeeDialogOpen;
        public bool IsAddEmployeeDialogOpen
        {
            get => _isAddEmployeeDialogOpen;
            set => SetProperty(ref _isAddEmployeeDialogOpen, value);
        }

        private AddEmployeeWizardViewModel? _addEmployeeVm;
        public AddEmployeeWizardViewModel? AddEmployeeVm
        {
            get => _addEmployeeVm;
            set => SetProperty(ref _addEmployeeVm, value);
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

        private bool _isDeleteConfirmOpen;
        public bool IsDeleteConfirmOpen
        {
            get => _isDeleteConfirmOpen;
            set => SetProperty(ref _isDeleteConfirmOpen, value);
        }

        private EmployeeModels.EmployeeSummary? _employeeToDelete;
        public EmployeeModels.EmployeeSummary? EmployeeToDelete
        {
            get => _employeeToDelete;
            set => SetProperty(ref _employeeToDelete, value);
        }

        public EmployeesViewModel(
            EmployerCompany? company,
            EmployeeService? employeeService = null,
            AddEmployeeWizardViewModelFactory? addEmployeeWizardViewModelFactory = null,
            NavigationService? navigationService = null,
            CurrentProfileService? currentProfileService = null,
            ProfileAuthService? profileAuthService = null,
            RecentlyDeletedService? recentlyDeletedService = null,
            AppSettingsService? appSettingsService = null,
            DocumentLocalizationService? documentLocalizationService = null,
            EmployeeDetailsViewModelFactory? employeeDetailsViewModelFactory = null,
            ActivityLogService? activityLogService = null,
            TemplateService? templateService = null,
            DocumentGenerationService? documentGenerationService = null,
            TagCatalogService? tagCatalogService = null,
            GeminiApiService? geminiApiService = null,
            CompanyService? companyService = null,
            SyncEventService? syncEventService = null,
            bool showAllCompanies = false)
        {
            _company = company;
            _companyService = companyService;
            _showAllCompanies = showAllCompanies;
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _addEmployeeWizardViewModelFactory = addEmployeeWizardViewModelFactory ?? throw new InvalidOperationException("AddEmployeeWizardViewModelFactory is not initialized.");
            _currentProfileService = currentProfileService ?? throw new InvalidOperationException("CurrentProfileService is not initialized.");
            _profileAuthService = profileAuthService ?? throw new InvalidOperationException("ProfileAuthService is not initialized.");
            _recentlyDeletedService = recentlyDeletedService ?? throw new InvalidOperationException("RecentlyDeletedService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new InvalidOperationException("EmployeeDetailsViewModelFactory is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _templateService = templateService ?? throw new InvalidOperationException("TemplateService is not initialized.");
            _documentGenerationService = documentGenerationService ?? throw new InvalidOperationException("DocumentGenerationService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _geminiApiService = geminiApiService ?? throw new InvalidOperationException("GeminiApiService is not initialized.");
            _syncEventService = syncEventService;
            if (_syncEventService != null)
                _syncEventService.SyncEventReceived += OnSyncEventReceived;
            _sortField = _appSettingsService.Settings.EmployeeSortField ?? "Name";
            _sortAscending = _appSettingsService.Settings.EmployeeSortAscending;
            _viewMode = _showAllCompanies ? "Tiles" : _appSettingsService.Settings.EmployeeViewMode ?? "List";
            _zoomLevel = _appSettingsService.Settings.EmployeeZoomLevel;
            IsCompanySelected = _company != null;

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());
            AddEmployeeCommand = new RelayCommand(o =>
            {
                try
                {
                    if (_company == null) return;
                    if (!PolicyService.EnsureWriteAllowed("Додати працівника"))
                        return;

                    CleanupAddEmployeeVm();
                    AddEmployeeVm = _addEmployeeWizardViewModelFactory.Create(_company);
                    AddEmployeeVm.RequestClose += OnAddEmployeeClose;
                    IsAddEmployeeDialogOpen = true;
                }
                catch (Exception ex)
                {
                    var errTitle = Application.Current?.TryFindResource("TitleError") as string ?? "Error";
                    var errFmt = Application.Current?.TryFindResource("MsgErrorGeneric") as string ?? "Error: {0}";
                    System.Windows.MessageBox.Show(string.Format(errFmt, ex.Message), errTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            });

            CloseAddEmployeeDialogCommand = new RelayCommand(o =>
            {
                IsAddEmployeeDialogOpen = false;
                CleanupAddEmployeeVm();
            });
            SelectCompanyCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());
            OpenEmployeeCommand = new RelayCommand(o => OpenEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            EditEmployeeCommand = new RelayCommand(o => EditEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            DeleteEmployeeCommand = new RelayCommand(o => AskDeleteEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            ConfirmDeleteCommand = new AsyncRelayCommand(_ => ConfirmDeleteAsync());
            CancelDeleteCommand = new RelayCommand(o => IsDeleteConfirmOpen = false);

            OpenEmployeeFolderCommand = new RelayCommand(o =>
            {
                if (o is EmployeeModels.EmployeeSummary emp && !string.IsNullOrEmpty(emp.EmployeeFolder))
                {
                    try { Process.Start(new ProcessStartInfo { FileName = emp.EmployeeFolder, UseShellExecute = true }); }
                    catch (Exception ex) { LoggingService.LogWarning("EmployeesViewModel.OpenFolder", ex.Message); }
                }
            }, o => o is EmployeeModels.EmployeeSummary);

            ExportToExcelCommand = new RelayCommand(o => ExportToExcel(), o => _allEmployees.Count > 0);

            ToggleSelectionModeCommand = new RelayCommand(o =>
            {
                IsSelectionMode = !IsSelectionMode;
                if (!IsSelectionMode)
                {
                    foreach (var e in Employees) e.IsSelected = false;
                    SelectedCount = 0;
                }
            });

            SelectAllCommand = new RelayCommand(o =>
            {
                foreach (var e in Employees) e.IsSelected = true;
                SelectedCount = Employees.Count;
            });

            DeselectAllCommand = new RelayCommand(o =>
            {
                foreach (var e in Employees) e.IsSelected = false;
                SelectedCount = 0;
                IsSelectionMode = false;
            });

            BatchGenerateCommand = new RelayCommand(o => OpenBatchGenerate(), o => Employees.Any(e => e.IsSelected));
            CloseBatchGenerateCommand = new RelayCommand(o => IsBatchGenerateOpen = false);
            BatchGenerateFromTemplateCommand = new RelayCommand(o => BatchGenerate(o as TemplateEntry));
            BatchGenerateToFolderCommand = new RelayCommand(o => BatchGenerateToFolder(o as TemplateEntry));
            OpenBatchAIValidationCommand = new RelayCommand(o => OpenBatchAIValidation(), o => Employees.Count > 0);
            CloseBatchAIValidationCommand = new RelayCommand(o =>
            {
                if (!IsBatchAIValidationRunning)
                    IsBatchAIValidationOpen = false;
            });
            StartBatchAIValidationCommand = new AsyncRelayCommand(_ => RunBatchAIValidationAsync(), _ => !IsBatchAIValidationRunning);
            CancelBatchAIValidationCommand = new RelayCommand(o => _batchAICts?.Cancel(), _ => IsBatchAIValidationRunning);
            ApplyBatchAISuggestionCommand = new AsyncRelayCommand(
                async o =>
                {
                    if (o is BatchAIValidationResultItem item)
                        await ApplyBatchAISuggestionAsync(item);
                },
                o => o is BatchAIValidationResultItem item && item.CanApply && !item.IsApplied && !IsBatchAIValidationRunning);
            OpenBatchAIDocumentCommand = new RelayCommand(
                o =>
                {
                    if (o is BatchAIValidationResultItem item)
                        OpenBatchAIDocument(item);
                },
                o => o is BatchAIValidationResultItem item && item.CanOpenDocument);
            ShowBatchAIOptionsCommand = new RelayCommand(o => ShowBatchAIOptions = true, _ => !IsBatchAIValidationRunning);

            SortByCommand = new RelayCommand(o =>
            {
                var field = o as string ?? "Name";
                if (SortField == field)
                    SortAscending = !SortAscending;
                else
                {
                    SortField = field;
                    SortAscending = true;
                }
                _appSettingsService.Settings.EmployeeSortField = SortField;
                _appSettingsService.Settings.EmployeeSortAscending = SortAscending;
                _appSettingsService.SaveSettings();
                ApplyFilter();
            });

            SetViewModeCommand = new RelayCommand(o => ViewMode = o as string ?? "List");
            FilterByStatCommand = new RelayCommand(o => StatFilter = o as string ?? "all");

            _ = LoadEmployeesAsync();
        }

        private async Task LoadEmployeesAsync()
        {
            var generation = ++_loadGeneration;
            IsLoading = true;
            StatusMessage = LoadingMessage;
            await Dispatcher.Yield(DispatcherPriority.Render);

            try
            {
                if (_showAllCompanies)
                {
                    var companyNames = _companyService?.VisibleCompanies
                        .Select(company => company.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .ToList() ?? new List<string>();
                    var allResult = await Task.Run(() => LoadAllVisibleCompanyEmployees(companyNames));
                    if (generation != _loadGeneration)
                        return;

                    _allEmployees = allResult.Employees;
                    _lastStatus = allResult.Status;
                    IsError = allResult.Status == "LoadError";
                    if (generation != _loadGeneration)
                        return;

                    IsLoading = false;
                    await ApplyFilterInBatchesAsync(generation);
                    if (HasVisibleEmployees)
                        StatusMessage = string.Empty;

                    RefreshStats();
                    Debug.WriteLine($"EmployeesViewModel.LoadAllEmployees: {Employees.Count} items");
                    return;
                }

                if (_company == null)
                {
                    _allEmployees = new List<EmployeeModels.EmployeeSummary>();
                    Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
                    HasEmployees = false;
                    HasVisibleEmployees = false;
                    IsError = false;
                    StatusMessage = GetString("MsgEmployeesSelectCompany") ?? "Please select a company.";
                    return;
                }

                var companyName = _company.Name;
                var result = await Task.Run(() => _employeeService.GetEmployeesForFirmWithStatus(companyName));
                if (generation != _loadGeneration || !string.Equals(_company?.Name, companyName, StringComparison.OrdinalIgnoreCase))
                    return;

                _allEmployees = result.Employees;
                _lastStatus = result.Status;
                ApplyFilter();
                if (HasVisibleEmployees)
                    StatusMessage = GetStatusMessage(result.Status);
                IsError = result.Status == "LoadError";
                await Dispatcher.Yield(DispatcherPriority.Render);
                if (generation != _loadGeneration || !string.Equals(_company?.Name, companyName, StringComparison.OrdinalIgnoreCase))
                    return;

                RefreshStats();
                Debug.WriteLine($"EmployeesViewModel.LoadEmployees: {Employees.Count} items");
            }
            catch (Exception ex)
            {
                if (generation != _loadGeneration)
                    return;

                LoggingService.LogError("EmployeesViewModel.LoadEmployeesAsync", ex);
                _allEmployees = new List<EmployeeModels.EmployeeSummary>();
                Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
                HasEmployees = false;
                HasVisibleEmployees = false;
                IsError = true;
                StatusMessage = GetString("MsgEmployeesLoadError") ?? "Failed to load employees.";
            }
            finally
            {
                if (generation == _loadGeneration)
                    IsLoading = false;
            }
        }

        private (List<EmployeeModels.EmployeeSummary> Employees, string Status) LoadAllVisibleCompanyEmployees(IReadOnlyList<string> companyNames)
        {
            if (companyNames.Count == 0)
                return (new List<EmployeeModels.EmployeeSummary>(), "NoEmployees");

            var allEmployees = new List<EmployeeModels.EmployeeSummary>();
            var statuses = new List<string>();

            foreach (var companyName in companyNames)
            {
                try
                {
                    var result = _employeeService.GetEmployeesForFirmWithStatus(companyName);
                    statuses.Add(result.Status);

                    foreach (var employee in result.Employees)
                    {
                        if (string.IsNullOrWhiteSpace(employee.FirmName))
                            employee.FirmName = companyName;
                        allEmployees.Add(employee);
                    }
                }
                catch (Exception ex)
                {
                    statuses.Add("LoadError");
                    LoggingService.LogWarning("EmployeesViewModel.LoadAllVisibleCompanyEmployees",
                        $"Failed to load employees for '{companyName}': {ex.Message}");
                }
            }

            var status = allEmployees.Count > 0
                ? "Ok"
                : statuses.Any(status => status == "LoadError")
                    ? "LoadError"
                    : "NoEmployees";

            return (allEmployees, status);
        }

        private string DocRes(string key) =>
            _documentLocalizationService.Get(key);

        private string? GetString(string key)
        {
            return Application.Current?.TryFindResource(key) as string;
        }

        private string GetStatusMessage(string status)
        {
            if (status == "RootFolderNotSet")
                return GetString("MsgEmployeesRootMissing") ?? "Root folder is not configured.";
            if (status == "EmployeesFolderMissing")
                return GetString("MsgEmployeesFolderMissing") ?? "Employees folder not found.";
            if (status == "NoEmployees")
                return GetString("MsgEmployeesEmpty") ?? "No employees yet.";
            if (status == "LoadError")
                return GetString("MsgEmployeesLoadError") ?? "Failed to load employees.";
            return string.Empty;
        }

        private void ApplyFilter()
        {
            HasEmployees = _allEmployees.Count > 0;

            if (_allEmployees.Count == 0)
            {
                Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
                HasVisibleEmployees = false;
                return;
            }

            var list = BuildFilteredEmployees();

            Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>(list);
            UpdateFilteredState(list.Count, SearchQuery?.Trim() ?? string.Empty);
        }

        private async Task ApplyFilterInBatchesAsync(int generation)
        {
            HasEmployees = _allEmployees.Count > 0;

            if (_allEmployees.Count == 0)
            {
                Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
                HasVisibleEmployees = false;
                return;
            }

            var query = SearchQuery?.Trim() ?? string.Empty;
            var list = BuildFilteredEmployees();
            Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
            UpdateFilteredState(list.Count, query);

            const int batchSize = 32;
            for (var index = 0; index < list.Count; index += batchSize)
            {
                if (generation != _loadGeneration)
                    return;

                foreach (var employee in list.Skip(index).Take(batchSize))
                    Employees.Add(employee);

                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }

        private List<EmployeeModels.EmployeeSummary> BuildFilteredEmployees()
        {
            var query = SearchQuery?.Trim() ?? string.Empty;
            List<EmployeeModels.EmployeeSummary> list;

            IEnumerable<EmployeeModels.EmployeeSummary> source = _allEmployees;

            if (_statFilter == "problems")
                source = source.Where(e => HasExpiringDocs(e));
            else if (_statFilter == "new")
                source = source.Where(e => IsThisMonth(e.StartDate));

            if (string.IsNullOrEmpty(query))
            {
                list = source.ToList();
            }
            else
            {
                list = source.Where(e =>
                    (!string.IsNullOrEmpty(e.FullName) && e.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.FirmName) && e.FirmName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.PassportNumber) && e.PassportNumber.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.VisaNumber) && e.VisaNumber.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.InsuranceNumber) && e.InsuranceNumber.Contains(query, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            list = SortField switch
            {
                "Name" => SortAscending
                    ? list.OrderBy(e => e.FullName).ToList()
                    : list.OrderByDescending(e => e.FullName).ToList(),
                "StartDate" => SortAscending
                    ? list.OrderBy(e => DateParsingHelper.TryParseDate(e.StartDate) ?? DateTime.MaxValue).ToList()
                    : list.OrderByDescending(e => DateParsingHelper.TryParseDate(e.StartDate) ?? DateTime.MinValue).ToList(),
                "Status" => SortAscending
                    ? list.OrderBy(e => e.Status ?? string.Empty).ToList()
                    : list.OrderByDescending(e => e.Status ?? string.Empty).ToList(),
                "Problems" => list.OrderByDescending(e => HasExpiringDocs(e) ? 1 : 0)
                                  .ThenBy(e => e.FullName).ToList(),
                _ => list
            };

            return list;
        }

        private void UpdateFilteredState(int visibleCount, string query)
        {
            HasVisibleEmployees = visibleCount > 0;

            if (!HasVisibleEmployees)
            {
                StatusMessage = string.IsNullOrEmpty(query) && StatFilter == "all"
                    ? GetStatusMessage(_lastStatus)
                    : (GetString("MsgEmployeesSearchEmpty") ?? "No employees found.");
            }
            else
            {
                StatusMessage = string.IsNullOrEmpty(query) ? GetStatusMessage(_lastStatus) : string.Empty;
            }
        }

        private void CleanupDetailsVm()
        {
            if (EmployeeDetailsVm != null)
            {
                EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
            }
        }

        private void OnAddEmployeeClose()
        {
            IsAddEmployeeDialogOpen = false;
            CleanupAddEmployeeVm();
            _ = LoadEmployeesAsync();
        }

        private void CleanupAddEmployeeVm()
        {
            if (AddEmployeeVm != null)
                AddEmployeeVm.RequestClose -= OnAddEmployeeClose;
        }

        private void OnDetailsClose() => IsEmployeeDetailsOpen = false;
        private void OnDetailsDataChanged() => _ = LoadEmployeesAsync();

        private void OnSyncEventReceived(object? sender, SyncEventReceivedEventArgs e)
        {
            if (!string.Equals(e.Record.Type, "EmployeeCreated", StringComparison.OrdinalIgnoreCase))
                return;

            var affectsThisView = _showAllCompanies
                || (_company != null && string.Equals(_company.Name, e.Record.FirmName, StringComparison.OrdinalIgnoreCase));
            if (!affectsThisView)
                return;

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                StatusMessage = $"Оновлено: додано {e.Record.EmployeeName}";
                _ = LoadEmployeesAsync();
            }), DispatcherPriority.Background);
        }

        private void OpenEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            var firmName = ResolveEmployeeFirmName(employee);
            if (employee == null || string.IsNullOrWhiteSpace(firmName)) return;
            CleanupDetailsVm();
            EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(
                firmName,
                employee.EmployeeFolder,
                _employeeService,
                employeeId: employee.UniqueId,
                bulkUpdateTargets: BuildBulkUpdateTargets(employee));
            EmployeeDetailsVm.RequestClose += OnDetailsClose;
            EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
            IsEmployeeDetailsOpen = true;
        }

        private void EditEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            if (!PolicyService.EnsureWriteAllowed("Редагувати працівника"))
                return;
            var firmName = ResolveEmployeeFirmName(employee);
            if (employee == null || string.IsNullOrWhiteSpace(firmName)) return;
            CleanupDetailsVm();
            EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(
                firmName,
                employee.EmployeeFolder,
                _employeeService,
                employeeId: employee.UniqueId,
                bulkUpdateTargets: BuildBulkUpdateTargets(employee));
            EmployeeDetailsVm.RequestClose += OnDetailsClose;
            EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
            EmployeeDetailsVm.IsEditMode = true;
            EmployeeDetailsVm.TabIndex = 1;
            IsEmployeeDetailsOpen = true;
        }

        private List<EmployeeBulkUpdateTarget> BuildBulkUpdateTargets(EmployeeModels.EmployeeSummary current)
        {
            if (!IsSelectionMode)
                return new List<EmployeeBulkUpdateTarget>();

            return Employees
                .Where(employee => employee.IsSelected
                    && !string.IsNullOrWhiteSpace(employee.EmployeeFolder)
                    && !string.Equals(employee.UniqueId, current.UniqueId, StringComparison.OrdinalIgnoreCase))
                .Select(employee => new EmployeeBulkUpdateTarget
                {
                    EmployeeFolder = employee.EmployeeFolder,
                    UniqueId = employee.UniqueId,
                    FullName = employee.FullName
                })
                .ToList();
        }

        private string ResolveEmployeeFirmName(EmployeeModels.EmployeeSummary? employee)
        {
            if (_company != null)
                return _company.Name;

            return employee?.FirmName ?? string.Empty;
        }

        private void AskDeleteEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            if (!PolicyService.EnsureWriteAllowed("Видалити працівника"))
                return;
            if (employee == null) return;
            EmployeeToDelete = employee;
            IsDeleteConfirmOpen = true;
        }

        private async System.Threading.Tasks.Task ConfirmDeleteAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Видалити працівника"))
                return;
            if (EmployeeToDelete == null) return;

            var currentProfile = _currentProfileService.CurrentProfile;
            if (currentProfile == null || string.IsNullOrWhiteSpace(currentProfile.ClientId))
            {
                MessageBox.Show(
                    GetString("ConfirmPasswordNoProfile") ?? "User profile was not found. Deletion is blocked.",
                    GetString("ConfirmPasswordTitle") ?? "Confirm password",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var passwordDialog = new ConfirmPasswordWindow
            {
                Owner = Application.Current?.MainWindow
            };

            var confirmed = passwordDialog.ShowDialog() == true && passwordDialog.IsConfirmed;
            if (!confirmed)
                return;

            var authResult = await _profileAuthService.AuthenticateAsync(currentProfile.ClientId, passwordDialog.EnteredPassword);
            if (!authResult.Success)
            {
                MessageBox.Show(
                    GetString("ConfirmPasswordFailed") ?? "Wrong password.",
                    GetString("ConfirmPasswordTitle") ?? "Confirm password",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var employee = EmployeeToDelete;
            Debug.WriteLine($"EmployeesViewModel.ConfirmDelete: Moving employee '{employee.FullName}' to recently deleted from folder '{employee.EmployeeFolder}'");

            var recycleResult = _recentlyDeletedService.MoveEmployeeToRecentlyDeleted(employee);
            if (!recycleResult.Success)
            {
                MessageBox.Show(
                    string.Format(GetString("RecentlyDeletedMoveFailed") ?? "Failed to move employee to Recently Deleted: {0}", recycleResult.Message),
                    GetString("TitleError") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _activityLogService.Log(
                "EmployeeMovedToRecentlyDeleted",
                "Employee",
                employee.FirmName,
                employee.FullName,
                string.Format(GetString("RecentlyDeletedActionMovedDescription") ?? "Employee {0} was moved to Recently Deleted.", employee.FullName),
                employeeFolder: recycleResult.Item?.DeletedEmployeeFolder ?? string.Empty);

            IsDeleteConfirmOpen = false;
            EmployeeToDelete = null;
            await LoadEmployeesAsync();
            ToastService.Instance.Success(string.Format(
                GetString("RecentlyDeletedMoveSuccess") ?? "Employee {0} was moved to Recently Deleted.",
                employee.FullName));
        }

        private void RefreshStats()
        {
            TotalCount = _allEmployees.Count;
            ProblemsCount = _allEmployees.Count(e => HasExpiringDocs(e));
            NewThisMonth = _allEmployees.Count(e => IsThisMonth(e.StartDate));
        }

        private static bool HasExpiringDocs(EmployeeModels.EmployeeSummary emp)
        {
            return IsProblematic(emp.PassportExpiry) || IsProblematic(emp.VisaExpiry) || IsProblematic(emp.InsuranceExpiry);
        }

        private static bool IsProblematic(string dateStr)
        {
            var s = DateParsingHelper.GetSeverity(dateStr);
            return s == "Expired" || s == "Critical" || s == "Warning";
        }

        private static bool IsThisMonth(string dateStr)
        {
            var dt = DateParsingHelper.TryParseDate(dateStr);
            if (dt == null) return false;
            return dt.Value.Year == DateTime.Now.Year && dt.Value.Month == DateTime.Now.Month;
        }

        public void UpdateSelectedCount()
        {
            SelectedCount = Employees.Count(e => e.IsSelected);
        }

        private void ExportToExcel()
        {
            if (!PolicyService.EnsureExportsAllowed("Експорт працівників в Excel"))
                return;
            if (_company == null) return;
            try
            {
                IsLoading = true;
                var dialog = new SaveFileDialog
                {
                    Filter = "Excel|*.xlsx",
                    FileName = $"{DocRes("ExportEmployees")}_{_company.Name}_{DateTime.Now:yyyyMMdd}.xlsx"
                };
                if (dialog.ShowDialog() != true) return;

                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(DocRes("ExportEmployees"));

                string[] headers = { DocRes("ExportColFirstName"), DocRes("ExportColLastName"), DocRes("ExportColPosition"), DocRes("ExportColPhone"), "Email",
                    DocRes("ExportColPassportNum"), DocRes("ExportColPassportExp"), DocRes("ExportColVisaNum"), DocRes("ExportColVisaExp"),
                    DocRes("ExportColInsNum"), DocRes("ExportColInsExp"), DocRes("ExportColContractType"),
                    DocRes("ExportColStartDate"), DocRes("ExportColStatus") };

                for (int i = 0; i < headers.Length; i++)
                    ws.Cell(1, i + 1).Value = headers[i];

                var headerRange = ws.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.CornflowerBlue;
                headerRange.Style.Font.FontColor = XLColor.White;

                int row = 2;
                foreach (var emp in _allEmployees)
                {
                    var data = _employeeService.LoadEmployeeData(emp.EmployeeFolder);
                    if (data == null) continue;

                    ws.Cell(row, 1).Value = data.FirstName;
                    ws.Cell(row, 2).Value = data.LastName;
                    ws.Cell(row, 3).Value = data.PositionTag;
                    ws.Cell(row, 4).Value = data.Phone;
                    ws.Cell(row, 5).Value = data.Email;
                    ws.Cell(row, 6).Value = data.PassportNumber;
                    ws.Cell(row, 7).Value = data.PassportExpiry;
                    ws.Cell(row, 8).Value = data.VisaNumber;
                    ws.Cell(row, 9).Value = data.VisaExpiry;
                    ws.Cell(row, 10).Value = data.InsuranceNumber;
                    ws.Cell(row, 11).Value = data.InsuranceExpiry;
                    ws.Cell(row, 12).Value = data.ContractType;
                    ws.Cell(row, 13).Value = data.StartDate;
                    ws.Cell(row, 14).Value = data.Status;

                    HighlightIfExpired(ws.Cell(row, 7));
                    HighlightIfExpired(ws.Cell(row, 9));
                    HighlightIfExpired(ws.Cell(row, 11));

                    row++;
                }

                ws.Columns().AdjustToContents();
                workbook.SaveAs(dialog.FileName);
                _activityLogService.Log("ExportExcel", "Export", _company?.Name ?? "", "",
                    $"Експортовано список працівників {_company?.Name} → Excel",
                    details: $"Фірма: {_company?.Name}; Працівників: {Employees.Count}; Файл: {Path.GetFileName(dialog.FileName)}");
                Process.Start(new ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Res("MsgExportError"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static void HighlightIfExpired(IXLCell cell)
        {
            var val = cell.GetString();
            var severity = DateParsingHelper.GetSeverity(val);
            if (severity == "Expired" || severity == "Critical")
            {
                cell.Style.Font.FontColor = XLColor.Red;
                cell.Style.Font.Bold = true;
            }
            else if (severity == "Warning")
            {
                cell.Style.Font.FontColor = XLColor.OrangeRed;
            }
        }

        private void OpenBatchGenerate()
        {
            if (!PolicyService.EnsureWriteAllowed("Пакетна генерація документів"))
                return;
            if (_company == null) return;
            var selected = Employees.Where(e => e.IsSelected).ToList();
            if (selected.Count == 0) return;

            BatchStatusMessage = string.Format(Res("MsgSelectedCount"), selected.Count);
            var templates = _templateService.GetTemplates(_company.Name);
            BatchTemplates = new ObservableCollection<TemplateEntry>(templates);
            IsBatchGenerateOpen = true;
        }

        private void OpenBatchAIValidation()
        {
            if (!_geminiApiService.IsConfigured)
            {
                BatchAIStatusMessage = Res("AIChatNoModel");
                IsBatchAIValidationOpen = true;
                return;
            }

            BatchAICheckOnlySelected = IsSelectionMode && Employees.Any(e => e.IsSelected);
            BatchAIProgressCurrent = 0;
            BatchAIProgressTotal = 0;
            ClearBatchAIAction();
            BatchAIResults.Clear();
            OnPropertyChanged(nameof(HasBatchAIResults));
            ShowBatchAIOptions = true;
            BatchAIStatusMessage = "Виберіть, які документи перевірити. Якщо увімкнений режим вибору, можна перевірити тільки позначених працівників.";
            IsBatchAIValidationOpen = true;
        }

        private async Task RunBatchAIValidationAsync()
        {
            if (!_geminiApiService.IsConfigured)
            {
                BatchAIStatusMessage = Res("AIChatNoModel");
                return;
            }

            if (!BatchAICheckPassport && !BatchAICheckVisa && !BatchAICheckInsurance && !BatchAICheckPermit)
            {
                BatchAIStatusMessage = "Виберіть хоча б один тип документа.";
                return;
            }

            var employeesToCheck = BatchAICheckOnlySelected
                ? Employees.Where(e => e.IsSelected).ToList()
                : Employees.ToList();

            if (employeesToCheck.Count == 0)
            {
                BatchAIStatusMessage = "Немає працівників для перевірки.";
                return;
            }

            _batchAICts?.Cancel();
            _batchAICts = new CancellationTokenSource();
            IsBatchAIValidationRunning = true;
            ShowBatchAIOptions = false;
            BatchAIProgressCurrent = 0;
            BatchAIProgressTotal = employeesToCheck.Count;
            ClearBatchAIAction();
            BatchAIResults.Clear();
            OnPropertyChanged(nameof(HasBatchAIResults));

            var checkedDocuments = 0;
            var skippedDocuments = 0;

            try
            {
                foreach (var employee in employeesToCheck)
                {
                    _batchAICts.Token.ThrowIfCancellationRequested();
                    BatchAIProgressCurrent++;
                    BatchAIStatusMessage = $"Перевірка {BatchAIProgressCurrent}/{BatchAIProgressTotal}: {employee.FullName}";
                    SetBatchAIAction(employee.FullName, "Профіль", "Завантажую дані працівника");
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
                    if (data == null)
                    {
                        AddBatchAIResult(employee.FullName, employee.EmployeeFolder, "Профіль", string.Empty, "error", "Не вдалося прочитати employee.json.");
                        continue;
                    }

                    if (BatchAICheckPassport)
                        await ValidateBatchDocumentAsync(employee, data, "passport", _batchAICts.Token, counters => { checkedDocuments += counters.Checked; skippedDocuments += counters.Skipped; });

                    if (BatchAICheckVisa)
                        await ValidateBatchDocumentAsync(employee, data, "visa", _batchAICts.Token, counters => { checkedDocuments += counters.Checked; skippedDocuments += counters.Skipped; });

                    if (BatchAICheckInsurance)
                        await ValidateBatchDocumentAsync(employee, data, "insurance", _batchAICts.Token, counters => { checkedDocuments += counters.Checked; skippedDocuments += counters.Skipped; });

                    if (BatchAICheckPermit)
                        await ValidateBatchDocumentAsync(employee, data, "permit", _batchAICts.Token, counters => { checkedDocuments += counters.Checked; skippedDocuments += counters.Skipped; });
                }

                if (BatchAIResults.Count == 0)
                    AddBatchAIResult("Усі працівники", string.Empty, "AI перевірка", string.Empty, "ok", "Критичних розбіжностей не знайдено.");

                ClearBatchAIAction();
                BatchAIStatusMessage = $"Готово. Працівників: {employeesToCheck.Count}, документів перевірено: {checkedDocuments}, пропущено без файла: {skippedDocuments}, результатів: {BatchAIResults.Count}.";
            }
            catch (OperationCanceledException)
            {
                ClearBatchAIAction();
                BatchAIStatusMessage = $"Скасовано. Перевірено працівників: {Math.Max(0, BatchAIProgressCurrent - 1)}/{BatchAIProgressTotal}.";
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeesViewModel.RunBatchAIValidation", ex);
                BatchAIStatusMessage = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
            finally
            {
                IsBatchAIValidationRunning = false;
                _batchAICts?.Dispose();
                _batchAICts = null;
            }
        }

        private async Task ValidateBatchDocumentAsync(
            EmployeeModels.EmployeeSummary employee,
            EmployeeModels.EmployeeData data,
            string documentType,
            CancellationToken token,
            Action<(int Checked, int Skipped)> updateCounters)
        {
            var (docName, docKey, filePath) = GetBatchDocumentInfo(employee.EmployeeFolder, data, documentType);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                SetBatchAIAction(employee.FullName, docName, "Пропущено: файл не знайдено");
                updateCounters((0, 1));
                return;
            }

            SetBatchAIAction(employee.FullName, docName, "AI читає документ");
            await Dispatcher.Yield(DispatcherPriority.Background);
            using var documentCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            documentCts.CancelAfter(TimeSpan.FromSeconds(90));
            using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(documentCts.Token);
            var stageTask = RunBatchReadingStagesAsync(employee.FullName, docName, documentType, stageCts.Token);
            Dictionary<string, string> extracted;
            try
            {
                extracted = await ScanBatchDocumentAsync(filePath, docKey, documentCts.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                SetBatchAIAction(employee.FullName, docName, "AI не відповів за відведений час");
                AddBatchAIResult(employee.FullName, employee.EmployeeFolder, docName, filePath, "warning", "Пропущено: AI не відповів за 90 секунд. Перевірка продовжилась далі.");
                updateCounters((0, 1));
                return;
            }
            finally
            {
                stageCts.Cancel();
                try { await stageTask; } catch (OperationCanceledException) { }
            }
            updateCounters((1, 0));

            if (extracted.Count == 0)
            {
                SetBatchAIAction(employee.FullName, docName, "AI не зміг прочитати документ");
                AddBatchAIResult(employee.FullName, employee.EmployeeFolder, docName, filePath, "warning", "AI не зміг прочитати документ або відповідь була порожня.");
                return;
            }

            SetBatchAIAction(employee.FullName, docName, $"Знайдено: {FormatFoundBatchFields(extracted)}");
            await Dispatcher.Yield(DispatcherPriority.Background);

            if (!AIScanPrompts.IsDocumentKindCompatible(docKey, extracted))
            {
                var kind = AIScanPrompts.GetDocumentKind(extracted);
                AddBatchAIResult(employee.FullName, employee.EmployeeFolder, docName, filePath, "warning", $"AI розпізнав інший тип документа: {kind}.");
            }

            CheckBatchDocumentOwnership(employee.FullName, employee.EmployeeFolder, docName, filePath, data, extracted);

            switch (documentType)
            {
                case "passport":
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportNumber", data.PassportNumber, "Номер паспорта/ID");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportExpiry", data.PassportExpiry, "Дійсний до");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportAuthority", data.PassportAuthority, "Ким виданий паспорт");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportCity", data.PassportCity, "Місто народження");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportCountry", data.PassportCountry, "Країна народження");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "Citizenship", data.Citizenship, "Громадянство");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "IssuingCountry", data.IssuingCountry, "Країна видачі");
                    break;
                case "visa":
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "VisaNumber", data.VisaNumber, "Номер візи/карти");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "VisaStartDate", data.VisaStartDate, "Початок візи");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "VisaExpiry", data.VisaExpiry, "Кінець візи");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "VisaAuthority", data.VisaAuthority, "Орган візи");
                    break;
                case "insurance":
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "InsuranceCompanyShort", data.InsuranceCompanyShort, "Страхова");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "InsuranceNumber", data.InsuranceNumber, "Номер страховки");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "InsuranceExpiry", data.InsuranceExpiry, "Кінець страховки");
                    break;
                case "permit":
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "WorkPermitNumber", data.WorkPermitNumber, "Номер дозволу");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "WorkPermitIssueDate", data.WorkPermitIssueDate, "Початок дозволу");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "WorkPermitExpiry", data.WorkPermitExpiry, "Кінець дозволу");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "WorkPermitAuthority", data.WorkPermitAuthority, "Орган дозволу");
                    break;
            }
        }

        private void SetBatchAIAction(string employeeName, string documentName, string fieldName)
        {
            BatchAICurrentEmployee = employeeName;
            BatchAICurrentDocument = documentName;
            BatchAICurrentField = fieldName;
        }

        private void ClearBatchAIAction()
        {
            BatchAICurrentEmployee = string.Empty;
            BatchAICurrentDocument = string.Empty;
            BatchAICurrentField = string.Empty;
        }

        private async Task RunBatchReadingStagesAsync(
            string employeeName,
            string docName,
            string documentType,
            CancellationToken token)
        {
            var stages = GetBatchReadingStages(documentType);
            var index = 0;

            while (!token.IsCancellationRequested)
            {
                SetBatchAIAction(employeeName, docName, stages[index % stages.Length]);
                index++;
                await Task.Delay(1200, token);
            }
        }

        private static string[] GetBatchReadingStages(string documentType)
        {
            return documentType switch
            {
                "passport" => new[]
                {
                    "AI шукає ім'я та прізвище",
                    "AI шукає дату народження",
                    "AI шукає номер паспорта / ID",
                    "AI шукає термін дії",
                    "AI перевіряє країну і громадянство"
                },
                "visa" => new[]
                {
                    "AI шукає номер візи / карти",
                    "AI шукає початок візи",
                    "AI шукає кінець візи",
                    "AI шукає орган видачі",
                    "AI перевіряє ім'я на документі"
                },
                "insurance" => new[]
                {
                    "AI шукає страхову компанію",
                    "AI шукає номер страховки",
                    "AI шукає термін дії страховки",
                    "AI перевіряє власника страховки"
                },
                "permit" => new[]
                {
                    "AI шукає номер дозволу",
                    "AI шукає дату видачі дозволу",
                    "AI шукає кінець дозволу",
                    "AI шукає орган видачі",
                    "AI перевіряє ім'я на дозволі"
                },
                _ => new[] { "AI читає документ" }
            };
        }

        private static string FormatFoundBatchFields(Dictionary<string, string> extracted)
        {
            var visible = extracted
                .Where(kv => !kv.Key.StartsWith("__", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                .Take(4)
                .Select(kv => $"{kv.Key}={kv.Value}");

            var text = string.Join("; ", visible);
            return string.IsNullOrWhiteSpace(text) ? "дані не знайдено" : text;
        }

        private async Task<Dictionary<string, string>> ScanBatchDocumentAsync(string filePath, string docKey, CancellationToken token)
        {
            var prompt = AIScanPrompts.GetPrompt(docKey);
            var result = string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase)
                ? await _geminiApiService.ChatWithFileAsync(filePath, prompt, ct: token)
                : await _geminiApiService.ChatWithImageAsync(filePath, prompt, ct: token);

            if (GeminiApiService.IsFailureResponse(result))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return AIScanPrompts.ValidateAndCleanParsedFields(docKey, AIScanPrompts.ParseResponse(result));
        }

        private (string Name, string DocKey, string FilePath) GetBatchDocumentInfo(
            string employeeFolder,
            EmployeeModels.EmployeeData data,
            string documentType)
        {
            return documentType switch
            {
                "passport" => (
                    "Паспорт / ID-карта",
                    string.Equals(data.EmployeeType, "eu_citizen", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(data.EuDocumentType, "id_card", StringComparison.OrdinalIgnoreCase)
                            ? "id_card"
                            : "passport",
                    ResolveBatchDocumentPath(employeeFolder, data.Files?.Passport)),
                "visa" => (
                    "Віза / карта побиту",
                    GetBatchVisaDocKey(data),
                    ResolveBatchDocumentPath(employeeFolder, FirstNonEmpty(data.Files?.Visa, data.Files?.VisaPage2, data.Files?.PassportPage2))),
                "insurance" => (
                    "Страховка",
                    "insurance",
                    ResolveBatchDocumentPath(employeeFolder, data.Files?.Insurance)),
                "permit" => (
                    "Дозвіл на роботу",
                    "permit",
                    ResolveBatchDocumentPath(employeeFolder, data.Files?.WorkPermit)),
                _ => (documentType, documentType, string.Empty)
            };
        }

        private static string GetBatchVisaDocKey(EmployeeModels.EmployeeData data)
        {
            if (string.Equals(data.EmployeeType, "eu_citizen", StringComparison.OrdinalIgnoreCase)
                && string.Equals(data.EuDocumentType, "id_card", StringComparison.OrdinalIgnoreCase))
                return "id_card_back";

            return string.Equals(data.VisaDocType, "id_card", StringComparison.OrdinalIgnoreCase)
                ? "visa2"
                : "visa";
        }

        private static string ResolveBatchDocumentPath(string employeeFolder, string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
                return string.Empty;

            if (Path.IsPathRooted(storedPath) && File.Exists(storedPath))
                return storedPath;

            var combined = Path.Combine(employeeFolder, storedPath);
            return File.Exists(combined) ? combined : storedPath;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        }

        private void CheckBatchDocumentOwnership(
            string employeeName,
            string employeeFolder,
            string docName,
            string documentPath,
            EmployeeModels.EmployeeData data,
            Dictionary<string, string> extracted)
        {
            SetBatchAIAction(employeeName, docName, "Звіряю ім'я, прізвище і дату народження");
            var hasFirst = TryGetBatchValue(extracted, "FirstName", out var firstName);
            var hasLast = TryGetBatchValue(extracted, "LastName", out var lastName);
            var swapped = hasFirst
                && hasLast
                && BatchNamesMatch(firstName, data.LastName)
                && BatchNamesMatch(lastName, data.FirstName);

            if (hasFirst && !BatchNamesMatch(firstName, data.FirstName) && !swapped)
            {
                var isLikelyOcr = IsLikelyNameOcrSlip(data.FirstName, firstName);
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "warning",
                    isLikelyOcr
                        ? $"Ім'я схоже на OCR-помилку, перевірте вручну: профіль '{data.FirstName}', документ '{firstName}'."
                        : $"Ім'я не збігається: профіль '{data.FirstName}', документ '{firstName}'.",
                    "FirstName",
                    "Ім'я",
                    data.FirstName,
                    firstName,
                    canApply: !isLikelyOcr);
            }

            if (hasLast && !BatchNamesMatch(lastName, data.LastName) && !swapped)
            {
                var isLikelyOcr = IsLikelyNameOcrSlip(data.LastName, lastName);
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "warning",
                    isLikelyOcr
                        ? $"Прізвище схоже на OCR-помилку, перевірте вручну: профіль '{data.LastName}', документ '{lastName}'."
                        : $"Прізвище не збігається: профіль '{data.LastName}', документ '{lastName}'.",
                    "LastName",
                    "Прізвище",
                    data.LastName,
                    lastName,
                    canApply: !isLikelyOcr);
            }

            if (TryGetBatchValue(extracted, "BirthDate", out var birthDate) && !BatchValuesMatch("BirthDate", data.BirthDate, birthDate))
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "warning",
                    $"Дата народження не збігається: профіль '{data.BirthDate}', документ '{birthDate}'.",
                    "BirthDate",
                    "Дата народження",
                    data.BirthDate,
                    birthDate,
                    canApply: true);
        }

        private void AddBatchCompare(
            string employeeName,
            string employeeFolder,
            string docName,
            string documentPath,
            Dictionary<string, string> extracted,
            string fieldKey,
            string currentValue,
            string displayName)
        {
            SetBatchAIAction(employeeName, docName, $"Звіряю: {displayName}");

            if (!TryGetBatchValue(extracted, fieldKey, out var suggested))
                return;

            if (AIScanPrompts.IsLowConfidenceField(extracted, fieldKey)
                || AIScanPrompts.IsSuspiciousFieldValue(extracted, fieldKey, suggested, currentValue))
            {
                AddBatchAIResult(employeeName, employeeFolder, docName, documentPath, "warning", $"{displayName}: AI не впевнений у значенні '{suggested}', пропущено.");
                return;
            }

            if (string.IsNullOrWhiteSpace(currentValue))
            {
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "missing",
                    $"{displayName}: поле порожнє у профілі, у документі знайдено '{suggested}'.",
                    fieldKey,
                    displayName,
                    currentValue,
                    suggested,
                    canApply: true);
                return;
            }

            if (!BatchValuesMatch(fieldKey, currentValue, suggested))
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "warning",
                    $"{displayName}: профіль '{currentValue}', документ '{suggested}'.",
                    fieldKey,
                    displayName,
                    currentValue,
                    suggested,
                    canApply: true);
        }

        private async Task ApplyBatchAISuggestionAsync(BatchAIValidationResultItem item)
        {
            if (item == null || !item.CanApply || item.IsApplied || string.IsNullOrWhiteSpace(item.EmployeeFolder))
                return;

            try
            {
                var data = _employeeService.LoadEmployeeData(item.EmployeeFolder);
                if (data == null)
                {
                    item.Message = $"{item.Message} Не вдалося прочитати профіль для заповнення.";
                    return;
                }

                var valueToApply = NormalizeBatchApplyValue(item.FieldKey, item.SuggestedValue);
                if (!SetBatchEmployeeField(data, item.FieldKey, valueToApply))
                {
                    item.Message = $"{item.Message} Це поле поки не підтримує автоматичне заповнення.";
                    return;
                }

                if (!_employeeService.SaveEmployeeData(item.EmployeeFolder, data))
                {
                    item.Message = $"{item.Message} Не вдалося зберегти зміну.";
                    return;
                }

                await _employeeService.AddHistoryEntry(item.EmployeeFolder, data.UniqueId, new EmployeeModels.EmployeeHistoryEntry
                {
                    EventType = "ProfileChanged",
                    Action = "AI масове заповнення",
                    Field = item.FieldDisplayName,
                    OldValue = item.CurrentValue,
                    NewValue = valueToApply,
                    Description = $"AI масово заповнив {item.FieldDisplayName}: {item.CurrentValue} → {valueToApply}"
                });

                item.IsApplied = true;
                item.CanApply = false;
                item.CurrentValue = valueToApply;
                item.Severity = "ok";
                item.Message = $"{item.FieldDisplayName}: заповнено значенням '{valueToApply}'.";
                SetBatchAIAction(item.EmployeeName, item.DocumentName, $"Заповнено: {item.FieldDisplayName}");

                await LoadEmployeesAsync();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeesViewModel.ApplyBatchAISuggestion", ex);
                item.Message = $"{item.Message} Помилка: {ex.Message}";
            }
        }

        private void OpenBatchAIDocument(BatchAIValidationResultItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.DocumentPath) || !File.Exists(item.DocumentPath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.DocumentPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeesViewModel.OpenBatchAIDocument", ex.Message);
                item.Message = $"{item.Message} Не вдалося відкрити документ: {ex.Message}";
            }
        }

        private static string NormalizeBatchApplyValue(string fieldKey, string value)
        {
            if (string.Equals(fieldKey, "FirstName", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "LastName", StringComparison.OrdinalIgnoreCase))
                return FormatPersonName(value);

            return value?.Trim() ?? string.Empty;
        }

        private static string FormatPersonName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var textInfo = CultureInfo.InvariantCulture.TextInfo;
            var normalized = value.Trim().ToLowerInvariant();
            normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            return string.Join(" ", normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => string.Join("-", part
                    .Split('-', StringSplitOptions.RemoveEmptyEntries)
                    .Select(piece => textInfo.ToTitleCase(piece)))));
        }

        private static bool SetBatchEmployeeField(EmployeeModels.EmployeeData data, string fieldKey, string value)
        {
            switch (fieldKey)
            {
                case "FirstName": data.FirstName = value; return true;
                case "LastName": data.LastName = value; return true;
                case "BirthDate": data.BirthDate = value; return true;
                case "PassportNumber": data.PassportNumber = value; return true;
                case "PassportExpiry": data.PassportExpiry = value; return true;
                case "PassportAuthority": data.PassportAuthority = value; return true;
                case "PassportCity": data.PassportCity = value; return true;
                case "PassportCountry": data.PassportCountry = value; return true;
                case "Citizenship": data.Citizenship = value; return true;
                case "IssuingCountry": data.IssuingCountry = value; return true;
                case "VisaNumber": data.VisaNumber = value; return true;
                case "VisaStartDate": data.VisaStartDate = value; return true;
                case "VisaExpiry": data.VisaExpiry = value; return true;
                case "VisaAuthority": data.VisaAuthority = value; return true;
                case "InsuranceCompanyShort": data.InsuranceCompanyShort = value; return true;
                case "InsuranceNumber": data.InsuranceNumber = value; return true;
                case "InsuranceExpiry": data.InsuranceExpiry = value; return true;
                case "WorkPermitNumber": data.WorkPermitNumber = value; return true;
                case "WorkPermitIssueDate": data.WorkPermitIssueDate = value; return true;
                case "WorkPermitExpiry": data.WorkPermitExpiry = value; return true;
                case "WorkPermitAuthority": data.WorkPermitAuthority = value; return true;
                default: return false;
            }
        }

        private void AddBatchAIResult(
            string employeeName,
            string employeeFolder,
            string docName,
            string documentPath,
            string severity,
            string message,
            string fieldKey = "",
            string fieldDisplayName = "",
            string currentValue = "",
            string suggestedValue = "",
            bool canApply = false)
        {
            BatchAIResults.Add(new BatchAIValidationResultItem
            {
                EmployeeName = employeeName,
                EmployeeFolder = employeeFolder,
                DocumentName = docName,
                DocumentPath = documentPath,
                FieldKey = fieldKey,
                FieldDisplayName = fieldDisplayName,
                CurrentValue = currentValue,
                SuggestedValue = suggestedValue,
                Severity = severity,
                Message = message,
                CanApply = canApply
            });
            OnPropertyChanged(nameof(HasBatchAIResults));
        }

        private static bool TryGetBatchValue(Dictionary<string, string> source, string key, out string value)
        {
            if (source.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                value = raw.Trim();
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool BatchValuesMatch(string fieldKey, string current, string suggested)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(suggested))
                return false;

            if (fieldKey.EndsWith("Expiry", StringComparison.OrdinalIgnoreCase)
                || fieldKey.EndsWith("IssueDate", StringComparison.OrdinalIgnoreCase)
                || fieldKey.EndsWith("StartDate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "BirthDate", StringComparison.OrdinalIgnoreCase))
            {
                var currentDate = DateParsingHelper.TryParseDate(current);
                var suggestedDate = DateParsingHelper.TryParseDate(suggested);
                if (currentDate != null && suggestedDate != null)
                    return currentDate.Value.Date == suggestedDate.Value.Date;
            }

            if (fieldKey.Contains("Number", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(NormalizeBatchDocumentNumber(current), NormalizeBatchDocumentNumber(suggested), StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(fieldKey, "FirstName", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "LastName", StringComparison.OrdinalIgnoreCase))
                return BatchNamesMatch(current, suggested);

            return string.Equals(current.Trim(), suggested.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool BatchNamesMatch(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return true;

            return string.Equals(NormalizeBatchName(left), NormalizeBatchName(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBatchName(string value)
        {
            var normalized = value.Trim().ToUpperInvariant()
                .Replace('-', ' ')
                .Replace('’', '\'')
                .Replace('`', '\'')
                .Replace('´', '\'')
                .Replace('\u00A0', ' ');

            return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string NormalizeBatchDocumentNumber(string value)
        {
            return new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static bool IsLikelyNameOcrSlip(string profileValue, string documentValue)
        {
            var left = NormalizeBatchName(profileValue).Replace(" ", string.Empty, StringComparison.Ordinal);
            var right = NormalizeBatchName(documentValue).Replace(" ", string.Empty, StringComparison.Ordinal);
            if (left.Length < 4 || right.Length < 4)
                return false;

            if (Math.Abs(left.Length - right.Length) > 1)
                return false;

            return DamerauLevenshteinDistance(left, right) <= 2;
        }

        private static int DamerauLevenshteinDistance(string source, string target)
        {
            var distances = new int[source.Length + 1, target.Length + 1];

            for (var i = 0; i <= source.Length; i++)
                distances[i, 0] = i;

            for (var j = 0; j <= target.Length; j++)
                distances[0, j] = j;

            for (var i = 1; i <= source.Length; i++)
            {
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distances[i, j] = Math.Min(
                        Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + cost);

                    if (i > 1
                        && j > 1
                        && source[i - 1] == target[j - 2]
                        && source[i - 2] == target[j - 1])
                    {
                        distances[i, j] = Math.Min(distances[i, j], distances[i - 2, j - 2] + 1);
                    }
                }
            }

            return distances[source.Length, target.Length];
        }

        private void BatchGenerateToFolder(TemplateEntry? template)
        {
            if (!PolicyService.EnsureWriteAllowed("Пакетна генерація документів"))
                return;
            if (template == null)
                return;

            var dialog = new OpenFolderDialog
            {
                Title = "Виберіть папку для збереження документів"
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
                return;

            BatchGenerate(template, dialog.FolderName);
        }

        private void BatchGenerate(TemplateEntry? template, string? outputFolder = null)
        {
            if (!PolicyService.EnsureWriteAllowed("Пакетна генерація документів"))
                return;
            if (template == null || _company == null) return;
            try
            {
                IsLoading = true;
                if (!string.IsNullOrWhiteSpace(outputFolder))
                    Directory.CreateDirectory(outputFolder);

                var selected = Employees.Where(e => e.IsSelected).ToList();
                int success = 0;
                int fail = 0;
                var resultLines = new List<string>();

                foreach (var emp in selected)
                {
                    var employeeName = string.IsNullOrWhiteSpace(emp.FullName)
                        ? Path.GetFileName(emp.EmployeeFolder)
                        : emp.FullName;

                    try
                    {
                        var data = _employeeService.LoadEmployeeData(emp.EmployeeFolder);
                        if (data == null)
                        {
                            fail++;
                            resultLines.Add($"[ПОМИЛКА] {employeeName}: анкета не знайдена");
                            continue;
                        }

                        if (!IsBatchEmployeeIdentityMatch(emp, data))
                        {
                            fail++;
                            resultLines.Add($"[ПОМИЛКА] {employeeName}: дані не співпадають з вибраним працівником");
                            LoggingService.LogWarning("EmployeesViewModel.BatchGenerate",
                                $"Skipped batch document generation because selected employee id '{emp.UniqueId}' does not match employee.json id '{data.UniqueId}' in folder '{emp.EmployeeFolder}'.");
                            continue;
                        }

                        var templateFullPath = _templateService.GetTemplateFullPath(_company.Name, template.FilePath) ?? string.Empty;
                        var templateFolder = Path.GetDirectoryName(templateFullPath) ?? string.Empty;
                        var rtfPath = Path.Combine(templateFolder, "content.rtf");
                        bool hasTemplateFile = File.Exists(templateFullPath);
                        bool hasRtfContent = File.Exists(rtfPath);

                        if (!hasTemplateFile && !hasRtfContent)
                        {
                            fail++;
                            resultLines.Add($"[ПОМИЛКА] {employeeName}: шаблон не знайдено");
                            continue;
                        }

                        var tagValues = _tagCatalogService.GetTagValueMapForEmployee(_company.Name, data)
                            ?? new Dictionary<string, string>();
                        var format = template.Format?.ToUpper() ?? Path.GetExtension(templateFullPath).TrimStart('.').ToUpper();
                        var generatedFileName = string.Empty;

                        string SanitizeFn(string n) => string.Join("_", n.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                        string BuildOutputPath(string fileName)
                        {
                            var targetFolder = string.IsNullOrWhiteSpace(outputFolder)
                                ? emp.EmployeeFolder
                                : outputFolder;
                            return EnsureUniqueBatchOutputPath(Path.Combine(targetFolder, fileName));
                        }

                        if (format == "DOCX" || hasRtfContent)
                        {
                            if (hasRtfContent)
                            {
                                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.docx");
                                var outPath = BuildOutputPath(outName);
                                _documentGenerationService.GenerateDocxFromRtf(rtfPath, outPath, tagValues);
                                generatedFileName = Path.GetFileName(outPath);
                            }
                            else if (hasTemplateFile)
                            {
                                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.docx");
                                var outPath = BuildOutputPath(outName);
                                _documentGenerationService.GenerateDocx(templateFullPath, outPath, tagValues);
                                generatedFileName = Path.GetFileName(outPath);
                            }
                        }
                        else if (format == "XLSX" && hasTemplateFile)
                        {
                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.xlsx");
                            var outPath = BuildOutputPath(outName);
                            _documentGenerationService.GenerateXlsx(templateFullPath, outPath, tagValues);
                            generatedFileName = Path.GetFileName(outPath);
                        }
                        else if (format == "PDF" && hasTemplateFile)
                        {
                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.pdf");
                            var outPath = BuildOutputPath(outName);
                            _documentGenerationService.GeneratePdf(templateFullPath, outPath, tagValues);
                            generatedFileName = Path.GetFileName(outPath);
                        }

                        if (string.IsNullOrWhiteSpace(generatedFileName))
                        {
                            fail++;
                            resultLines.Add($"[ПОМИЛКА] {employeeName}: формат не підтримується ({format})");
                            continue;
                        }

                        success++;
                        resultLines.Add($"[OK] {employeeName}: {generatedFileName}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("EmployeesViewModel.BatchGenerate", ex);
                        fail++;
                        resultLines.Add($"[ПОМИЛКА] {employeeName}: {ex.Message}");
                    }
                }

                BatchStatusMessage = string.Join(Environment.NewLine,
                    new[] { string.Format(Res("MsgBatchResult"), success, fail) }.Concat(resultLines));

                LogBatchGeneration(template, selected.Count, success, fail, outputFolder, resultLines);

                if (!string.IsNullOrWhiteSpace(outputFolder) && success > 0)
                    OpenFolderAfterBatchGeneration(outputFolder);
            }
            catch (Exception ex)
            {
                BatchStatusMessage = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static string EnsureUniqueBatchOutputPath(string path)
        {
            if (!File.Exists(path))
                return path;

            var folder = Path.GetDirectoryName(path) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);

            for (var i = 1; i < 1000; i++)
            {
                var candidate = Path.Combine(folder, $"{fileName} ({i}){extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(folder, $"{fileName} ({DateTime.Now:yyyyMMddHHmmss}){extension}");
        }

        private static bool IsBatchEmployeeIdentityMatch(EmployeeModels.EmployeeSummary summary, EmployeeModels.EmployeeData data)
        {
            var expectedId = summary.UniqueId?.Trim() ?? string.Empty;
            var actualId = data.UniqueId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(expectedId) || string.IsNullOrWhiteSpace(actualId))
                return true;

            return string.Equals(expectedId, actualId, StringComparison.OrdinalIgnoreCase);
        }

        private void OpenFolderAfterBatchGeneration(string folder)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeesViewModel.OpenBatchOutputFolder", ex.Message);
            }
        }

        private void LogBatchGeneration(
            TemplateEntry template,
            int selectedCount,
            int success,
            int fail,
            string? outputFolder,
            IReadOnlyList<string> resultLines)
        {
            var target = string.IsNullOrWhiteSpace(outputFolder)
                ? "папки працівників"
                : outputFolder;
            var details = string.Join(Environment.NewLine,
                new[]
                {
                    $"Шаблон: {template.Name}",
                    $"Обрано: {selectedCount}",
                    $"Успішно: {success}",
                    $"Помилки: {fail}",
                    $"Куди: {target}",
                    "Результати:"
                }.Concat(resultLines));

            _activityLogService.Log(
                "BatchDocGenerated",
                "Document",
                _company?.Name ?? string.Empty,
                string.Empty,
                $"Масова генерація «{template.Name}»: успішно {success}, помилки {fail}",
                details: details);
        }
    }
}
