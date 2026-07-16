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
using Win11DesktopApp.Converters;
using Win11DesktopApp.Services;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>Row of the company switcher dropdown in the employees header.</summary>
    public sealed class CompanySwitchItem
    {
        public CompanySwitchItem(EmployerCompany company, bool isCurrent)
        {
            Company = company;
            IsCurrent = isCurrent;
        }

        public EmployerCompany Company { get; }
        public bool IsCurrent { get; }
        public string Name => Company.Name;
    }

    public partial class EmployeesViewModel : ViewModelBase, ICleanable
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
        private int _filterGeneration;
        private CancellationTokenSource? _batchAICts;
        private CancellationTokenSource? _thumbnailPreloadCts;
        private readonly DispatcherTimer _searchDebounceTimer;

        private ObservableCollection<EmployeeModels.EmployeeSummary> _employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
        public ObservableCollection<EmployeeModels.EmployeeSummary> Employees
        {
            get => _employees;
            set
            {
                if (SetProperty(ref _employees, value))
                    NotifyActiveViewEmployeesChanged();
            }
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
                    OnPropertyChanged(nameof(HasActiveFilters));

                    // Debounce keystrokes so filtering (and the batched Employees rebuild
                    // it triggers) doesn't run on every character while the user is typing.
                    // Clearing the search should still feel instant, so skip the delay for that.
                    _searchDebounceTimer.Stop();
                    if (string.IsNullOrEmpty(value))
                        ApplyFilter();
                    else
                        _searchDebounceTimer.Start();
                }
            }
        }

        public bool HasActiveFilters =>
            !string.IsNullOrEmpty(_searchQuery) || _statFilter != "all";

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
            ? GetString("TitleEmployeesAllActive") ?? "Активні працівники"
            : _company == null
            ? GetString("TitleEmployeesGeneric") ?? "Employees"
            : string.Format(GetString("TitleEmployees") ?? "{0}", _company.Name);

        // Company switcher dropdown (header pill)
        private bool _isCompanyDropdownOpen;
        public bool IsCompanyDropdownOpen
        {
            get => _isCompanyDropdownOpen;
            set => SetProperty(ref _isCompanyDropdownOpen, value);
        }

        public IReadOnlyList<CompanySwitchItem> CompanyDropdownItems =>
            (_companyService?.VisibleCompanies ?? Enumerable.Empty<EmployerCompany>())
                .Where(PolicyService.CanAccessCompany)
                .Select(c => new CompanySwitchItem(c, _company != null && c.Id == _company.Id))
                .ToList();

        public bool HasCompanyDropdownItems => CompanyDropdownItems.Count > 0;

        public string LoadingMessage => _showAllCompanies
            ? GetString("MsgEmployeesLoadingAll") ?? "Завантажуємо активних працівників з усіх фірм..."
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
                {
                    OnPropertyChanged(nameof(HasActiveFilters));
                    ApplyFilter();
                }
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
        public ICommand ToggleCompanyDropdownCommand { get; }
        public ICommand SwitchCompanyCommand { get; }
        public ICommand OpenEmployeeCommand { get; }
        public ICommand EditEmployeeCommand { get; }
        public ICommand DeleteEmployeeCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }
        public ICommand CancelDeleteCommand { get; }
        public ICommand OpenEmployeeFolderCommand { get; }
        public ICommand OpenEmployeeDocumentCommand { get; }
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
        public ICommand ClearFiltersCommand { get; }

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

                    NotifyActiveViewEmployeesChanged();

                    if (Employees.Count > 0)
                        ScheduleThumbnailPreload(Employees.ToList());
                }
            }
        }

        public bool IsTableView => ViewMode == "Table";
        public bool IsListView => ViewMode == "List";
        public bool IsTilesView => ViewMode == "Tiles";
        public bool IsIconsView => ViewMode == "Icons";

        public IEnumerable<EmployeeModels.EmployeeSummary>? EmployeesForTable =>
            IsTableView ? Employees : null;

        public IEnumerable<EmployeeModels.EmployeeSummary>? EmployeesForList =>
            IsListView ? Employees : null;

        public IEnumerable<EmployeeModels.EmployeeSummary>? EmployeesForTiles =>
            IsTilesView ? Employees : null;

        public IEnumerable<EmployeeModels.EmployeeSummary>? EmployeesForIcons =>
            IsIconsView ? Employees : null;

        private void NotifyActiveViewEmployeesChanged()
        {
            OnPropertyChanged(nameof(EmployeesForTable));
            OnPropertyChanged(nameof(EmployeesForList));
            OnPropertyChanged(nameof(EmployeesForTiles));
            OnPropertyChanged(nameof(EmployeesForIcons));
        }

        private double _zoomLevel = 1.0;
        public double ZoomLevel
        {
            get => _zoomLevel;
            private set => SetProperty(ref _zoomLevel, value);
        }

        private int _tileSizeStep = 4;
        public int TileSizeStep
        {
            get => _tileSizeStep;
            set
            {
                var clamped = Math.Max(1, Math.Min(6, value));
                if (SetProperty(ref _tileSizeStep, clamped))
                {
                    _appSettingsService.Settings.EmployeeTileSizeStep = clamped;
                    _appSettingsService.SaveSettings();
                    RecalculateTileLayout();
                    RecalculateIconsLayout();
                }
            }
        }

        private double _tilesAvailableWidth;
        public double TilesAvailableWidth
        {
            get => _tilesAvailableWidth;
            set
            {
                if (Math.Abs(_tilesAvailableWidth - value) < 0.5)
                    return;
                _tilesAvailableWidth = value;
                RecalculateTileLayout();
            }
        }

        private double _tileCardWidth = 390.0;
        public double TileCardWidth
        {
            get => _tileCardWidth;
            private set => SetProperty(ref _tileCardWidth, value);
        }

        private const double TileBaseCardWidth = 390.0;
        private const double TileBaseHorizontalMargin = 14.0; // card margin '6,14,8,8' => 6 + 8 horizontally
        private const double TileMinCardWidth = 280.0;
        private const double TileMinZoom = 0.6;

        private void RecalculateTileLayout()
        {
            var available = _tilesAvailableWidth;
            if (available < 100)
            {
                TileCardWidth = TileBaseCardWidth;
                ZoomLevel = 1.0;
                return;
            }

            // Step x1..x6 maps to column count: x1 = smallest cards (8 columns), x6 = largest (3 columns).
            var columns = 9 - _tileSizeStep;

            // Card margin scales with zoom (zoom = width / base), so solve slot = width + margin * zoom.
            double CardWidthFor(int cols) =>
                (available / cols) * TileBaseCardWidth / (TileBaseCardWidth + TileBaseHorizontalMargin);

            // Only shrink column count when cards would be too narrow — never bump for max zoom,
            // so x6 always stays at 3 columns even on ultrawide screens.
            while (columns > 1 && CardWidthFor(columns) < TileMinCardWidth)
                columns--;

            var width = Math.Max(160.0, Math.Floor(CardWidthFor(columns)) - 1);
            TileCardWidth = width;
            ZoomLevel = Math.Max(TileMinZoom, width / TileBaseCardWidth);
        }

        private double _iconsAvailableWidth;
        public double IconsAvailableWidth
        {
            get => _iconsAvailableWidth;
            set
            {
                if (Math.Abs(_iconsAvailableWidth - value) < 0.5)
                    return;
                _iconsAvailableWidth = value;
                RecalculateIconsLayout();
            }
        }

        private double _iconCardWidth = IconBaseCardWidth;
        public double IconCardWidth
        {
            get => _iconCardWidth;
            private set => SetProperty(ref _iconCardWidth, value);
        }

        private const double IconBaseCardWidth = 200.0;
        private const double IconBaseHorizontalMargin = 12.0; // card margin '6,14,6,6' => 6 + 6 horizontally
        private const double IconMinCardWidth = 176.0;

        private void RecalculateIconsLayout()
        {
            var available = _iconsAvailableWidth;
            if (available < 100)
            {
                IconCardWidth = IconBaseCardWidth;
                return;
            }

            // Icons cards are much smaller than Tiles, so use a gentler column mapping:
            // x1 = 11 columns, x6 = 6 columns (Tiles' "9 - step" would blow up these compact cards too much).
            var columns = 12 - _tileSizeStep;

            double CardWidthFor(int cols) =>
                (available / cols) * IconBaseCardWidth / (IconBaseCardWidth + IconBaseHorizontalMargin);

            while (columns > 1 && CardWidthFor(columns) < IconMinCardWidth)
                columns--;

            var width = Math.Max(IconMinCardWidth, Math.Floor(CardWidthFor(columns)) - 1);
            IconCardWidth = width;
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
            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                ApplyFilter();
            };
            _sortField = _appSettingsService.Settings.EmployeeSortField ?? "Name";
            _sortAscending = _appSettingsService.Settings.EmployeeSortAscending;
            _viewMode = _showAllCompanies ? "Tiles" : _appSettingsService.Settings.EmployeeViewMode ?? "List";
            _tileSizeStep = Math.Max(1, Math.Min(6, _appSettingsService.Settings.EmployeeTileSizeStep));
            IsCompanySelected = _company != null;

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());
            AddEmployeeCommand = new RelayCommand(o =>
            {
                try
                {
                    if (_company == null) return;
                    if (!PolicyService.CanAccessCompany(_company)) return;
                    if (!PolicyService.RequireCanEditCompany(_company, "Додати працівника")) return;
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
            ToggleCompanyDropdownCommand = new RelayCommand(o =>
            {
                if (!IsCompanyDropdownOpen)
                {
                    OnPropertyChanged(nameof(CompanyDropdownItems));
                    OnPropertyChanged(nameof(HasCompanyDropdownItems));
                }
                IsCompanyDropdownOpen = !IsCompanyDropdownOpen;
            });
            SwitchCompanyCommand = new RelayCommand(o =>
            {
                IsCompanyDropdownOpen = false;
                if (o is not EmployerCompany target)
                    return;
                if (_company != null && target.Id == _company.Id)
                    return;
                if (!PolicyService.CanAccessCompany(target))
                    return;

                if (_companyService != null)
                    _companyService.SelectedCompany = target;

                _navigationService.NavigateTo(new EmployeesViewModel(
                    target,
                    _employeeService,
                    _addEmployeeWizardViewModelFactory,
                    _navigationService,
                    _currentProfileService,
                    _profileAuthService,
                    _recentlyDeletedService,
                    _appSettingsService,
                    _documentLocalizationService,
                    _employeeDetailsViewModelFactory,
                    _activityLogService,
                    _templateService,
                    _documentGenerationService,
                    _tagCatalogService,
                    _geminiApiService,
                    _companyService,
                    _syncEventService));
            }, o => o is EmployerCompany);
            OpenEmployeeCommand = new RelayCommand(o => OpenEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            EditEmployeeCommand = new RelayCommand(o => EditEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            DeleteEmployeeCommand = new RelayCommand(o => AskDeleteEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            ConfirmDeleteCommand = new AsyncRelayCommand(_ => ConfirmDeleteAsync());
            CancelDeleteCommand = new RelayCommand(o => IsDeleteConfirmOpen = false);

            OpenEmployeeFolderCommand = new RelayCommand(o =>
            {
                if (o is EmployeeModels.EmployeeSummary emp && !string.IsNullOrEmpty(emp.EmployeeFolder))
                {
                    if (!CanAccessEmployee(emp)) return;
                    try { Process.Start(new ProcessStartInfo { FileName = emp.EmployeeFolder, UseShellExecute = true }); }
                    catch (Exception ex) { LoggingService.LogWarning("EmployeesViewModel.OpenFolder", ex.Message); }
                }
            }, o => o is EmployeeModels.EmployeeSummary);

            OpenEmployeeDocumentCommand = new RelayCommand(
                o =>
                {
                    if (o is Tuple<EmployeeModels.EmployeeSummary, string> request)
                        OpenEmployeeDocument(request.Item1, request.Item2);
                },
                o => o is Tuple<EmployeeModels.EmployeeSummary, string>);

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
            ClearFiltersCommand = new RelayCommand(o =>
            {
                SearchQuery = string.Empty;
                StatFilter = "all";
            });

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
                        .Where(PolicyService.CanAccessCompany)
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

                if (!PolicyService.CanAccessCompany(_company))
                {
                    _allEmployees = new List<EmployeeModels.EmployeeSummary>();
                    Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
                    HasEmployees = false;
                    HasVisibleEmployees = false;
                    IsError = false;
                    StatusMessage = GetString("MsgEmployeesEmpty") ?? "No employees yet.";
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
            _ = ApplyFilterBatchedAsync();
        }

        // Rebuilds Employees in small batches (yielding to the dispatcher between them) so that
        // large filtered/sorted lists don't block the UI thread with one huge collection rebuild.
        // Small lists (the common single-company case) still complete synchronously in one pass.
        private async Task ApplyFilterBatchedAsync()
        {
            var generation = ++_filterGeneration;
            HasEmployees = _allEmployees.Count > 0;

            if (_allEmployees.Count == 0)
            {
                Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
                HasVisibleEmployees = false;
                return;
            }

            var query = SearchQuery?.Trim() ?? string.Empty;
            var list = BuildFilteredEmployees();
            if (generation != _filterGeneration)
                return;

            Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
            UpdateFilteredState(list.Count, query);

            const int batchSize = 48;
            for (var index = 0; index < list.Count; index += batchSize)
            {
                if (generation != _filterGeneration)
                    return;

                foreach (var employee in list.Skip(index).Take(batchSize))
                    Employees.Add(employee);

                if (index + batchSize < list.Count)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }

            if (generation == _filterGeneration)
            {
                NotifyActiveViewEmployeesChanged();
                ScheduleThumbnailPreload(list);
            }
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

            if (generation == _loadGeneration)
            {
                NotifyActiveViewEmployeesChanged();
                ScheduleThumbnailPreload(list);
            }
        }

        private void ScheduleThumbnailPreload(IReadOnlyList<EmployeeModels.EmployeeSummary> employees)
        {
            _thumbnailPreloadCts?.Cancel();
            _thumbnailPreloadCts?.Dispose();
            _thumbnailPreloadCts = new CancellationTokenSource();
            var token = _thumbnailPreloadCts.Token;

            // Must match the ConverterParameter used by each view's <Image> binding in
            // EmployeesView.xaml, otherwise the preload warms the wrong cache key and the
            // first switch to that view has to decode every photo synchronously instead.
            var decodeWidth = ViewMode switch
            {
                "Icons" => 200,
                "List" => 96,
                "Tiles" => 200,
                _ => 128
            };

            var paths = employees
                .Where(employee => employee.HasPhoto && !string.IsNullOrWhiteSpace(employee.PhotoPath))
                .Select(employee => employee.PhotoPath);

            _ = Task.Run(async () =>
            {
                try
                {
                    await ThumbnailPathConverter.PreloadAsync(paths, decodeWidth, token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("EmployeesViewModel.ScheduleThumbnailPreload", ex.Message);
                }
            }, token);
        }

        private List<EmployeeModels.EmployeeSummary> BuildFilteredEmployees()
        {
            var query = SearchQuery?.Trim() ?? string.Empty;
            List<EmployeeModels.EmployeeSummary> list;

            IEnumerable<EmployeeModels.EmployeeSummary> source = _allEmployees;
            if (PolicyService.IsCompanyDataScopeRestricted)
                source = source.Where(CanAccessEmployee);

            if (_statFilter == "problems")
                source = source.Where(e => HasExpiringDocs(e));
            else if (_statFilter == "new")
                source = source.Where(e => IsThisMonth(e));

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
                    ? list.OrderBy(e => e.FullName, StringComparer.CurrentCultureIgnoreCase).ToList()
                    : list.OrderByDescending(e => e.FullName, StringComparer.CurrentCultureIgnoreCase).ToList(),
                "StartDate" => SortAscending
                    ? list.OrderBy(e => GetParsedStartDate(e) ?? DateTime.MaxValue).ToList()
                    : list.OrderByDescending(e => GetParsedStartDate(e) ?? DateTime.MinValue).ToList(),
                "Status" => SortAscending
                    ? list.OrderBy(e => e.Status ?? string.Empty).ToList()
                    : list.OrderByDescending(e => e.Status ?? string.Empty).ToList(),
                "Problems" => list.OrderByDescending(e => HasExpiringDocs(e) ? 1 : 0)
                                  .ThenBy(e => e.FullName, StringComparer.CurrentCultureIgnoreCase).ToList(),
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
            if (PolicyService.IsCompanyDataScopeRestricted)
            {
                var company = FindCompanyByName(e.Record.FirmName);
                if (company != null && !PolicyService.CanAccessCompany(company))
                    return;
            }

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                StatusMessage = string.Format(GetString("MsgEmployeeSyncAddedFmt") ?? "Оновлено: додано {0}", e.Record.EmployeeName);
                _ = LoadEmployeesAsync();
            }), DispatcherPriority.Background);
        }

        private void OpenEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            var firmName = ResolveEmployeeFirmName(employee);
            if (employee == null || string.IsNullOrWhiteSpace(firmName)) return;
            if (!CanAccessEmployee(employee)) return;
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
            if (!CanAccessEmployee(employee)) return;
            if (!CanEditEmployee(employee)) return;
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

        private bool CanAccessEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            var firmName = ResolveEmployeeFirmName(employee);
            if (employee == null || string.IsNullOrWhiteSpace(firmName))
                return false;

            var company = FindCompanyByName(firmName);
            if (company != null)
                return PolicyService.CanAccessCompany(company);

            return PolicyService.CanAccessEmployer(null, firmName, null);
        }

        private bool CanEditEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            var firmName = ResolveEmployeeFirmName(employee);
            if (employee == null || string.IsNullOrWhiteSpace(firmName))
                return false;

            var company = FindCompanyByName(firmName);
            if (company != null)
                return PolicyService.RequireCanEditCompany(company, "Редагувати працівника");

            return PolicyService.CanEditEmployer(null, firmName, null);
        }

        private void OpenEmployeeDocument(EmployeeModels.EmployeeSummary? employee, string documentType)
        {
            if (employee == null || string.IsNullOrWhiteSpace(employee.EmployeeFolder))
                return;

            if (!CanAccessEmployee(employee))
                return;

            try
            {
                var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
                if (data == null)
                    return;

                var document = GetBatchDocumentInfo(employee.EmployeeFolder, data, documentType);
                if (string.IsNullOrWhiteSpace(document.FilePath) || !File.Exists(document.FilePath))
                {
                    ToastService.Instance.Warning(string.Format(GetString("MsgDocumentFileNotFoundFmt") ?? "{0}: файл не знайдено", document.Name));
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = document.FilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeesViewModel.OpenEmployeeDocument", ex.Message);
                ToastService.Instance.Warning(string.Format(GetString("MsgOpenDocumentFailedFmt") ?? "Не вдалося відкрити документ: {0}", ex.Message));
            }
        }

        private EmployerCompany? FindCompanyByName(string firmName)
        {
            return _companyService?.Companies?.FirstOrDefault(company =>
                string.Equals(company.Name, firmName, StringComparison.OrdinalIgnoreCase));
        }

        private void AskDeleteEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            if (!PolicyService.EnsureWriteAllowed("Видалити працівника"))
                return;
            if (employee == null) return;
            if (!CanAccessEmployee(employee)) return;
            if (!CanEditEmployee(employee)) return;
            EmployeeToDelete = employee;
            IsDeleteConfirmOpen = true;
        }

        private async System.Threading.Tasks.Task ConfirmDeleteAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Видалити працівника"))
                return;
            if (EmployeeToDelete == null) return;
            if (!CanAccessEmployee(EmployeeToDelete)) return;
            if (!CanEditEmployee(EmployeeToDelete)) return;

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
            NewThisMonth = _allEmployees.Count(e => IsThisMonth(e));
        }

        private static bool HasExpiringDocs(EmployeeModels.EmployeeSummary emp)
        {
            // Severity is already computed once by EmployeeService when the employee is loaded
            // (emp.PassportSeverity/VisaSeverity/InsuranceSeverity), so reuse it here instead of
            // re-parsing the expiry date strings on every filter/sort/stats refresh.
            return IsProblemSeverity(emp.PassportSeverity) || IsProblemSeverity(emp.VisaSeverity) || IsProblemSeverity(emp.InsuranceSeverity);
        }

        private static bool IsProblemSeverity(string severity)
        {
            return severity == "Expired" || severity == "Critical" || severity == "Warning";
        }

        private static bool IsThisMonth(EmployeeModels.EmployeeSummary emp)
        {
            var dt = GetParsedStartDate(emp);
            if (dt == null) return false;
            return dt.Value.Year == DateTime.Now.Year && dt.Value.Month == DateTime.Now.Month;
        }

        /// <summary>
        /// Returns the pre-parsed start date cached on the summary (populated once by
        /// EmployeeService), falling back to parsing <see cref="EmployeeModels.EmployeeSummary.StartDate"/>
        /// on the fly if the cache wasn't populated (e.g. objects built outside EmployeeService).
        /// </summary>
        private static DateTime? GetParsedStartDate(EmployeeModels.EmployeeSummary emp)
        {
            return emp.ParsedStartDate ?? DateParsingHelper.TryParseDate(emp.StartDate);
        }

        public void UpdateSelectedCount()
        {
            SelectedCount = Employees.Count(e => e.IsSelected);

            // Keep the "only selected" AI-check option in sync if the user (de)selects
            // employees while the batch AI dialog options are still on screen (i.e. before
            // the check has started). Otherwise the checkbox can stay stale and the batch
            // check silently runs against everyone instead of just the selected employees.
            if (IsBatchAIValidationOpen && ShowBatchAIOptions)
                BatchAICheckOnlySelected = IsSelectionMode && SelectedCount > 0;
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

        public void Cleanup()
        {
            _searchDebounceTimer.Stop();

            CleanupDetailsVm();
            CleanupAddEmployeeVm();

            var batchAiCts = Interlocked.Exchange(ref _batchAICts, null);
            batchAiCts?.Cancel();
            batchAiCts?.Dispose();

            var thumbnailPreloadCts = Interlocked.Exchange(ref _thumbnailPreloadCts, null);
            thumbnailPreloadCts?.Cancel();
            thumbnailPreloadCts?.Dispose();
        }

    }
}
