using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
    public class ArchiveViewModel : ViewModelBase, ICleanable
    {
        private readonly NavigationService _navigationService;
        private readonly EmployeeService _employeeService;
        private readonly AppSettingsService _appSettingsService;
        private readonly CompanyService _companyService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private readonly ActivityLogService _activityLogService;
        private readonly AppNotificationService _notificationService;
        private const int ArchivePageSize = 40;

        private readonly ObservableCollection<ArchivedEmployeeSummary> _archivedEmployeesSource = new();
        private readonly ObservableCollection<ArchivedEmployeeSummary> _visibleArchived = new();
        private readonly ICollectionView _archivedEmployeesView;
        private readonly DispatcherTimer _searchDebounce;
        private int _loadArchiveVersion;
        private int _displayLimit = ArchivePageSize;
        private int _displayedCount;
        private string? _pendingEmployeeFolder;
        private string _sortField = "EndDate";
        private bool _sortAscending;
        private string _viewMode = "List";
        private int _tileSizeStep = 4;
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
        public ICommand ShowMoreCommand { get; }

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
            private set
            {
                if (SetProperty(ref _filteredCount, value))
                    NotifyPaginationProperties();
            }
        }

        public int DisplayedCount
        {
            get => _displayedCount;
            private set => SetProperty(ref _displayedCount, value);
        }

        public bool HasMoreToShow =>
            (IsTilesView || IsIconsView) && DisplayedCount < FilteredCount;

        public int ResultsPrimaryCount =>
            IsTilesView || IsIconsView ? DisplayedCount : FilteredCount;

        public int ResultsSecondaryCount =>
            IsTilesView || IsIconsView ? FilteredCount : TotalArchivedCount;

        public string ShowMoreLabel
        {
            get
            {
                var remaining = Math.Max(0, FilteredCount - DisplayedCount);
                var next = Math.Min(ArchivePageSize, remaining);
                var fmt = Res("ArchiveShowMoreFmt");
                if (string.IsNullOrEmpty(fmt))
                    fmt = "Показати ще ({0})";
                return string.Format(fmt, next);
            }
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
                    OnPropertyChanged(nameof(HasActiveFilters));
                    _searchDebounce.Stop();
                    _searchDebounce.Start();
                }
            }
        }

        private string _allFirmsLabel = string.Empty;

        private ObservableCollection<string> _firmOptions = new();
        public ObservableCollection<string> FirmOptions
        {
            get => _firmOptions;
            set => SetProperty(ref _firmOptions, value);
        }

        private string _selectedFirm = string.Empty;
        public string SelectedFirm
        {
            get => _selectedFirm;
            set
            {
                if (SetProperty(ref _selectedFirm, value))
                {
                    OnPropertyChanged(nameof(HasActiveFilters));
                    ResetDisplayLimit();
                    RefreshArchiveView();
                }
            }
        }

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(_searchQuery)
            || _statFilter != "all"
            || (!string.IsNullOrEmpty(_selectedFirm) && _selectedFirm != _allFirmsLabel);

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
                    OnPropertyChanged(nameof(IsIconsView));
                    if (IsTilesView || IsIconsView)
                    {
                        ResetDisplayLimit();
                        RebuildVisibleSliceIfNeeded();
                    }
                    NotifyActiveViewEmployeesChanged();
                    NotifyPaginationProperties();
                    if (IsTilesView && _tilesAvailableWidth > 100)
                        RecalculateTileLayout();
                    if (IsIconsView && _iconsAvailableWidth > 100)
                        RecalculateIconsLayout();
                    SaveArchiveDisplaySettings();
                }
            }
        }

        public bool IsListView => ViewMode == "List";
        public bool IsTilesView => ViewMode == "Tiles";
        public bool IsIconsView => ViewMode == "Icons";

        public IEnumerable<ArchivedEmployeeSummary>? ArchivedForList =>
            IsListView ? _archivedEmployeesSource : null;

        public IEnumerable<ArchivedEmployeeSummary>? ArchivedForTiles =>
            IsTilesView ? _visibleArchived : null;

        public IEnumerable<ArchivedEmployeeSummary>? ArchivedForIcons =>
            IsIconsView ? _visibleArchived : null;

        private void NotifyActiveViewEmployeesChanged()
        {
            OnPropertyChanged(nameof(ArchivedForList));
            OnPropertyChanged(nameof(ArchivedForTiles));
            OnPropertyChanged(nameof(ArchivedForIcons));
        }

        public int TileSizeStep
        {
            get => _tileSizeStep;
            set
            {
                var clamped = Math.Max(1, Math.Min(6, value));
                if (SetProperty(ref _tileSizeStep, clamped))
                {
                    _appSettingsService.Settings.ArchiveTileSizeStep = clamped;
                    _appSettingsService.SaveSettings();
                    RecalculateTileLayout();
                    RecalculateIconsLayout();
                    SaveArchiveDisplaySettings();
                }
            }
        }

        public double ZoomLevel
        {
            get => _zoomLevel;
            private set => SetProperty(ref _zoomLevel, value);
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

        private double _tileCardWidth = TileBaseCardWidth;
        public double TileCardWidth
        {
            get => _tileCardWidth;
            private set => SetProperty(ref _tileCardWidth, value);
        }

        private const double TileBaseCardWidth = 390.0;
        private const double TileBaseHorizontalMargin = 14.0;
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

            var columns = 9 - _tileSizeStep;

            double CardWidthFor(int cols) =>
                (available / cols) * TileBaseCardWidth / (TileBaseCardWidth + TileBaseHorizontalMargin);

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

        private const double IconBaseCardWidth = 240.0;
        private const double IconBaseHorizontalMargin = 12.0;
        private const double IconMinCardWidth = 200.0;

        private void RecalculateIconsLayout()
        {
            var available = _iconsAvailableWidth;
            if (available < 100)
            {
                IconCardWidth = IconBaseCardWidth;
                return;
            }

            var columns = 12 - _tileSizeStep;

            double CardWidthFor(int cols) =>
                (available / cols) * IconBaseCardWidth / (IconBaseCardWidth + IconBaseHorizontalMargin);

            while (columns > 1 && CardWidthFor(columns) < IconMinCardWidth)
                columns--;

            IconCardWidth = Math.Max(IconMinCardWidth, Math.Floor(CardWidthFor(columns)) - 1);
        }

        public double ArchiveListMaxWidth => 1140;

        public string StatFilter
        {
            get => _statFilter;
            set
            {
                if (SetProperty(ref _statFilter, value))
                {
                    OnPropertyChanged(nameof(HasActiveFilters));
                    ResetDisplayLimit();
                    RefreshArchiveView();
                }
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

        private readonly ObservableCollection<EmployerCompany> _availableCompanies = new();
        public ObservableCollection<EmployerCompany> AvailableCompanies => _availableCompanies;

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
            ActivityLogService? activityLogService = null,
            AppNotificationService? notificationService = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new InvalidOperationException("EmployeeDetailsViewModelFactory is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _notificationService = notificationService ?? throw new InvalidOperationException("AppNotificationService is not initialized.");
            _pendingEmployeeFolder = employeeToOpenFolder;
            _sortField = _appSettingsService.Settings.ArchiveSortField ?? "EndDate";
            _sortAscending = _appSettingsService.Settings.ArchiveSortAscending;
            _viewMode = _appSettingsService.Settings.ArchiveViewMode ?? "List";
            _tileSizeStep = Math.Clamp(_appSettingsService.Settings.ArchiveTileSizeStep, 1, 6);
            if (_appSettingsService.Settings.ArchiveTileSizeStep < 1)
            {
                var legacyZoom = _appSettingsService.Settings.ArchiveZoomLevel;
                _tileSizeStep = legacyZoom <= 0.9 ? 2 : legacyZoom >= 1.2 ? 5 : 4;
            }
            _archivedEmployeesView = CollectionViewSource.GetDefaultView(_archivedEmployeesSource);
            _archivedEmployeesView.Filter = FilterArchived;
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDebounce.Tick += OnSearchDebounceTick;
            RefreshAvailableCompanies();
            _companyService.VisibilityChanged += OnCompanyVisibilityChanged;

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
                    RefreshAvailableCompanies();
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
                ResetDisplayLimit();
                ApplySort();
                RebuildAfterViewRefresh();
                NotifyPaginationProperties();
            });
            SetViewModeCommand = new RelayCommand(o => ViewMode = o as string ?? "List");
            FilterByStatCommand = new RelayCommand(o => StatFilter = o as string ?? "all");
            ClearFilterCommand = new RelayCommand(_ =>
            {
                var changed = false;
                changed |= SetProperty(ref _searchQuery, string.Empty, nameof(SearchQuery));
                changed |= SetProperty(ref _statFilter, "all", nameof(StatFilter));
                if (!string.IsNullOrEmpty(_allFirmsLabel) && _selectedFirm != _allFirmsLabel)
                    changed |= SetProperty(ref _selectedFirm, _allFirmsLabel, nameof(SelectedFirm));
                if (changed)
                {
                    OnPropertyChanged(nameof(HasActiveFilters));
                    _searchDebounce.Stop();
                    ResetDisplayLimit();
                    RefreshArchiveView();
                }
            });

            ShowMoreCommand = new RelayCommand(_ =>
            {
                if (!HasMoreToShow)
                    return;

                _displayLimit += ArchivePageSize;
                RebuildVisibleSliceIfNeeded();
                NotifyPaginationProperties();
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
            EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
            IsEmployeeDetailsOpen = true;
        }

        private void OnDetailsClose() => IsEmployeeDetailsOpen = false;

        // Archive profile cannot edit the questionnaire. DataChanged here is mainly
        // "moved to Recently Deleted" — remove that one row instead of reloading everyone.
        private void OnDetailsDataChanged(EmployeeDataChangedEventArgs e)
        {
            try
            {
                var folder = !string.IsNullOrWhiteSpace(e.EmployeeFolder)
                    ? e.EmployeeFolder
                    : EmployeeDetailsVm?.EmployeeFolderPath;
                var uniqueId = !string.IsNullOrWhiteSpace(e.UniqueId)
                    ? e.UniqueId
                    : EmployeeDetailsVm?.Data?.UniqueId;

                if (TryRemoveArchivedEmployee(folder, uniqueId))
                {
                    LoggingService.LogInfo(
                        "ArchiveViewModel.OnDetailsDataChanged",
                        $"Removed single archive row without full reload. folder={folder}; id={uniqueId}.");
                    return;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ArchiveViewModel.OnDetailsDataChanged", ex.Message);
            }

            _ = LoadArchiveAsync();
        }

        private bool TryRemoveArchivedEmployee(string? employeeFolder, string? uniqueId)
        {
            ArchivedEmployeeSummary? match = null;

            if (!string.IsNullOrWhiteSpace(uniqueId))
            {
                match = _archivedEmployeesSource.FirstOrDefault(e =>
                    !string.IsNullOrWhiteSpace(e.UniqueId)
                    && string.Equals(e.UniqueId, uniqueId, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null && !string.IsNullOrWhiteSpace(employeeFolder))
            {
                match = _archivedEmployeesSource.FirstOrDefault(e =>
                    !string.IsNullOrWhiteSpace(e.EmployeeFolder)
                    && string.Equals(e.EmployeeFolder, employeeFolder, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null)
                return false;

            _archivedEmployeesSource.Remove(match);

            var visibleIdx = -1;
            for (var i = 0; i < _visibleArchived.Count; i++)
            {
                if (ReferenceEquals(_visibleArchived[i], match))
                {
                    visibleIdx = i;
                    break;
                }
            }
            if (visibleIdx >= 0)
                _visibleArchived.RemoveAt(visibleIdx);

            HasArchivedData = _archivedEmployeesSource.Count > 0;
            RefreshStats();
            RebuildFirmOptions();

            try
            {
                // Recount + refill first page in one pass (keeps tiles/icons filled after delete).
                RebuildAfterViewRefresh();
            }
            catch (Exception viewEx)
            {
                LoggingService.LogWarning("ArchiveViewModel.TryRemoveArchivedEmployee.View", viewEx.Message);
                HasFilteredResults = _archivedEmployeesSource.Count > 0;
                FilteredCount = _archivedEmployeesSource.Count;
                RebuildVisibleSliceIfNeeded();
            }

            NotifyPaginationProperties();
            NotifyActiveViewEmployeesChanged();
            return true;
        }

        public void Cleanup()
        {
            LoggingService.LogInfo("ArchiveViewModel.Cleanup", "Stopped search timer and cleared details.");
            Interlocked.Increment(ref _loadArchiveVersion);
            _companyService.VisibilityChanged -= OnCompanyVisibilityChanged;
            _searchDebounce.Stop();
            _searchDebounce.Tick -= OnSearchDebounceTick;

            CleanupDetailsVm();
            EmployeeDetailsVm = null;
            IsEmployeeDetailsOpen = false;
        }

        private void OnCompanyVisibilityChanged()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                RefreshAvailableCompanies();
                return;
            }

            _ = dispatcher.BeginInvoke((Action)RefreshAvailableCompanies);
        }

        private void RefreshAvailableCompanies()
        {
            var previouslySelectedId = SelectedCompany?.Id;
            _availableCompanies.Clear();
            foreach (var company in _companyService.VisibleCompanies)
                _availableCompanies.Add(company);

            if (previouslySelectedId is Guid id)
            {
                var stillVisible = _availableCompanies.FirstOrDefault(c => c.Id == id);
                if (stillVisible != null)
                {
                    if (!ReferenceEquals(SelectedCompany, stillVisible))
                        SelectedCompany = stillVisible;
                    return;
                }
            }

            if (SelectedCompany != null && !_availableCompanies.Contains(SelectedCompany))
                SelectedCompany = _availableCompanies.FirstOrDefault();
        }

        private void OnSearchDebounceTick(object? sender, EventArgs e)
        {
            _searchDebounce.Stop();
            ResetDisplayLimit();
            RefreshArchiveView();
        }

        private void CleanupDetailsVm()
        {
            if (EmployeeDetailsVm != null)
            {
                EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
                EmployeeDetailsVm.Cleanup();
            }
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
            var loadVersion = Interlocked.Increment(ref _loadArchiveVersion);
            IsLoading = true;
            try
            {
                var loaded = await Task.Run(() => _employeeService.GetArchivedEmployees());
                if (loadVersion != Volatile.Read(ref _loadArchiveVersion))
                    return;

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
                // fallback in case RebuildAfterViewRefresh() is skipped on error.
                HasArchivedData = _archivedEmployeesSource.Count > 0;
                FilteredCount = _archivedEmployeesSource.Count;
                HasFilteredResults = FilteredCount > 0;
                RefreshStats();
                RebuildFirmOptions();

                // Step 2: apply sort + single-pass count/visible slice via ICollectionView.
                // These can throw under edge cases (broken comparer, bad item, WPF
                // quirks around CustomSort). If anything fails, the base state
                // from Step 1 stays, and we still try to fill the visible page.
                ResetDisplayLimit();
                try
                {
                    ApplySort();
                    RebuildAfterViewRefresh();
                }
                catch (Exception viewEx)
                {
                    LoggingService.LogError("ArchiveViewModel.LoadArchive.ViewPipeline", viewEx);
                    RebuildVisibleSliceIfNeeded();
                }

                TryOpenPendingEmployee();
                NotifyActiveViewEmployeesChanged();
                NotifyPaginationProperties();
            }
            catch (Exception ex)
            {
                if (loadVersion == Volatile.Read(ref _loadArchiveVersion))
                    LoggingService.LogError("ArchiveViewModel.LoadArchive", ex);
            }
            finally
            {
                if (loadVersion == Volatile.Read(ref _loadArchiveVersion))
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

                if (!string.IsNullOrEmpty(_selectedFirm) && _selectedFirm != _allFirmsLabel
                    && !string.Equals(archived.FirmName, _selectedFirm, StringComparison.Ordinal))
                {
                    return false;
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
            RebuildAfterViewRefresh();
            NotifyPaginationProperties();
        }

        private void ResetDisplayLimit() => _displayLimit = ArchivePageSize;

        // After Refresh/ApplySort: count filtered items and fill first tiles/icons page in one pass.
        private void RebuildAfterViewRefresh()
        {
            var count = 0;
            var takeVisible = IsTilesView || IsIconsView;
            if (takeVisible)
                _visibleArchived.Clear();

            foreach (var item in _archivedEmployeesView)
            {
                if (item is not ArchivedEmployeeSummary archived)
                    continue;

                count++;
                if (takeVisible && _visibleArchived.Count < _displayLimit)
                    _visibleArchived.Add(archived);
            }

            FilteredCount = count;
            HasFilteredResults = count > 0;
            if (takeVisible)
                DisplayedCount = _visibleArchived.Count;
        }

        private void RebuildVisibleSliceIfNeeded()
        {
            if (!IsTilesView && !IsIconsView)
                return;

            _visibleArchived.Clear();
            var taken = 0;
            foreach (var item in _archivedEmployeesView)
            {
                if (taken >= _displayLimit)
                    break;

                if (item is ArchivedEmployeeSummary archived)
                {
                    _visibleArchived.Add(archived);
                    taken++;
                }
            }

            DisplayedCount = taken;
        }

        private void NotifyPaginationProperties()
        {
            OnPropertyChanged(nameof(HasMoreToShow));
            OnPropertyChanged(nameof(ShowMoreLabel));
            OnPropertyChanged(nameof(ResultsPrimaryCount));
            OnPropertyChanged(nameof(ResultsSecondaryCount));
        }

        private void RefreshStats()
        {
            TotalArchivedCount = _archivedEmployeesSource.Count;
            ArchivedThisMonthCount = _archivedEmployeesSource.Count(e => e.ParsedEndDate.HasValue
                && e.ParsedEndDate.Value.Month == DateTime.Today.Month
                && e.ParsedEndDate.Value.Year == DateTime.Today.Year);
            WithoutPhotoCount = _archivedEmployeesSource.Count(e => !e.HasPhoto);
        }

        private void RebuildFirmOptions()
        {
            _allFirmsLabel = Res("ProbAllFirms");
            if (string.IsNullOrEmpty(_allFirmsLabel))
                _allFirmsLabel = "Усі фірми";

            var firms = _archivedEmployeesSource
                .Select(e => e.FirmName)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.CurrentCulture)
                .ToList();

            var options = new ObservableCollection<string> { _allFirmsLabel };
            foreach (var f in firms)
                options.Add(f);
            FirmOptions = options;

            if (string.IsNullOrEmpty(_selectedFirm) || !options.Contains(_selectedFirm))
            {
                _selectedFirm = _allFirmsLabel;
                OnPropertyChanged(nameof(SelectedFirm));
                OnPropertyChanged(nameof(HasActiveFilters));
            }
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
            _appSettingsService.Settings.ArchiveTileSizeStep = TileSizeStep;
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

            if (!_companyService.IsCompanyVisible(SelectedCompany))
            {
                RestoreStatus = Res("MsgRestoreFirmHidden");
                if (string.IsNullOrEmpty(RestoreStatus))
                    RestoreStatus = "This firm is hidden. Choose a visible firm.";
                RefreshAvailableCompanies();
                if (SelectedCompany == null || !_companyService.IsCompanyVisible(SelectedCompany))
                    SelectedCompany = AvailableCompanies.FirstOrDefault();
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

                    _notificationService.Success(
                        Res("ArchiveRestoreTitle"),
                        string.Format(Res("HistoryDescRestored"), EmployeeToRestore.FullName, SelectedCompany.Name));

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
