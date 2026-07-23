using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class CandidatesViewModel : ViewModelBase, ICleanable
    {
        private readonly NavigationService _navigationService;
        private readonly CandidateService _service;
        private readonly AppSettingsService _appSettingsService;
        private readonly CandidateViewModelFactory _candidateViewModelFactory;
        private readonly DispatcherTimer _searchDebounce;
        private int _loadGeneration;
        private List<CandidateSummary> _allCandidates = new();

        public ICommand GoBackCommand { get; }
        public ICommand AddCandidateCommand { get; }
        public ICommand OpenCandidateCommand { get; }
        public ICommand SetViewModeCommand { get; }

        private string _viewMode;
        public string ViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    OnPropertyChanged(nameof(IsListView));
                    OnPropertyChanged(nameof(IsTilesView));
                    _appSettingsService.Settings.CandidateViewMode = value;
                    _appSettingsService.SaveSettings();
                }
            }
        }

        public bool IsListView => ViewMode == "List";
        public bool IsTilesView => ViewMode == "Tiles";

        private double _zoomLevel;
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (SetProperty(ref _zoomLevel, value))
                {
                    _appSettingsService.Settings.CandidateZoomLevel = value;
                    _appSettingsService.SaveSettings();
                }
            }
        }

        public ObservableCollection<CandidateSummary> Candidates { get; } = new();
        public ObservableCollection<string> Positions { get; } = new();

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ScheduleFilterRefresh();
            }
        }

        private string _selectedPosition = "";
        public string SelectedPosition
        {
            get => _selectedPosition;
            set
            {
                if (SetProperty(ref _selectedPosition, value))
                    ApplyFilter();
            }
        }

        private int _totalCount;
        public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

        private bool _hasCandidates;
        public bool HasCandidates { get => _hasCandidates; set => SetProperty(ref _hasCandidates, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private bool _isAddDialogOpen;
        public bool IsAddDialogOpen
        {
            get => _isAddDialogOpen;
            set => SetProperty(ref _isAddDialogOpen, value);
        }

        private AddCandidateViewModel? _addCandidateVm;
        public AddCandidateViewModel? AddCandidateVm
        {
            get => _addCandidateVm;
            set => SetProperty(ref _addCandidateVm, value);
        }

        private bool _isDetailsOpen;
        public bool IsDetailsOpen
        {
            get => _isDetailsOpen;
            set => SetProperty(ref _isDetailsOpen, value);
        }

        private CandidateDetailsViewModel? _detailsVm;
        public CandidateDetailsViewModel? DetailsVm
        {
            get => _detailsVm;
            set => SetProperty(ref _detailsVm, value);
        }

        public CandidatesViewModel(
            CandidateService? candidateService = null,
            AppSettingsService? appSettingsService = null,
            NavigationService? navigationService = null,
            CandidateViewModelFactory? candidateViewModelFactory = null)
        {
            _service = candidateService ?? throw new InvalidOperationException("CandidateService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _candidateViewModelFactory = candidateViewModelFactory ?? throw new InvalidOperationException("CandidateViewModelFactory is not initialized.");
            _viewMode = _appSettingsService.Settings.CandidateViewMode;
            _zoomLevel = _appSettingsService.Settings.CandidateZoomLevel;

            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDebounce.Tick += OnSearchDebounceTick;

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());
            SetViewModeCommand = new RelayCommand(o => ViewMode = o as string ?? "List");
            AddCandidateCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Додати кандидата"))
                    return;

                CleanupAddCandidateVm();
                AddCandidateVm = _candidateViewModelFactory.CreateAddCandidate();
                AddCandidateVm.RequestClose += OnAddCandidateClose;
                IsAddDialogOpen = true;
            });
            OpenCandidateCommand = new RelayCommand(o =>
            {
                if (o is CandidateSummary summary)
                {
                    CleanupDetailsVm();
                    DetailsVm = _candidateViewModelFactory.CreateCandidateDetails(summary.CandidateFolder);
                    DetailsVm.RequestClose += OnDetailsClose;
                    IsDetailsOpen = true;
                }
            });

            _ = LoadCandidatesAsync();
        }

        public void LoadCandidates() => _ = LoadCandidatesAsync();

        public void Cleanup()
        {
            LoggingService.LogInfo("CandidatesViewModel.Cleanup", "Invalidated load and cleared candidate dialogs.");
            Interlocked.Increment(ref _loadGeneration);
            _searchDebounce.Stop();
            _searchDebounce.Tick -= OnSearchDebounceTick;

            CleanupAddCandidateVm();
            AddCandidateVm = null;
            IsAddDialogOpen = false;

            CleanupDetailsVm();
            DetailsVm = null;
            IsDetailsOpen = false;
        }

        private async Task LoadCandidatesAsync()
        {
            var generation = Interlocked.Increment(ref _loadGeneration);
            IsLoading = true;

            try
            {
                var snapshot = await Task.Run(() =>
                {
                    var all = _service.GetAll();
                    var positions = all
                        .Select(c => c.DesiredPosition)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                    return (All: all, Positions: positions);
                }).ConfigureAwait(true);

                if (generation != Volatile.Read(ref _loadGeneration))
                    return;

                _allCandidates = snapshot.All;
                TotalCount = _allCandidates.Count;

                Positions.Clear();
                Positions.Add("");
                foreach (var position in snapshot.Positions)
                    Positions.Add(position);

                ApplyFilter();
            }
            catch (Exception ex)
            {
                if (generation == Volatile.Read(ref _loadGeneration))
                    LoggingService.LogError("CandidatesViewModel.LoadCandidatesAsync", ex);
            }
            finally
            {
                if (generation == Volatile.Read(ref _loadGeneration))
                    IsLoading = false;
            }
        }

        private void ScheduleFilterRefresh()
        {
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private void OnSearchDebounceTick(object? sender, EventArgs e)
        {
            _searchDebounce.Stop();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filtered = BuildFilteredList();
            ReplaceCandidates(filtered);
            HasCandidates = Candidates.Count > 0;
        }

        private List<CandidateSummary> BuildFilteredList()
        {
            var query = _searchText?.Trim() ?? string.Empty;
            var result = new List<CandidateSummary>(_allCandidates.Count);

            foreach (var c in _allCandidates)
            {
                if (!string.IsNullOrEmpty(_selectedPosition) &&
                    !string.Equals(c.DesiredPosition, _selectedPosition, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(query))
                {
                    if (!(c.FullName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(c.DesiredPosition?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(c.LocationDetails?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                        continue;
                }

                result.Add(c);
            }

            return result;
        }

        private void ReplaceCandidates(List<CandidateSummary> items)
        {
            Candidates.Clear();
            foreach (var item in items)
                Candidates.Add(item);
        }

        private void OnAddCandidateClose()
        {
            IsAddDialogOpen = false;
            CleanupAddCandidateVm();
            LoadCandidates();
        }

        private void OnDetailsClose()
        {
            IsDetailsOpen = false;
            CleanupDetailsVm();
            LoadCandidates();
        }

        private void CleanupAddCandidateVm()
        {
            if (AddCandidateVm != null)
                AddCandidateVm.RequestClose -= OnAddCandidateClose;
        }

        private void CleanupDetailsVm()
        {
            if (DetailsVm != null)
                DetailsVm.RequestClose -= OnDetailsClose;
        }
    }
}
