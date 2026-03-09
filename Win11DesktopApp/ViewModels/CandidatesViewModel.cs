using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class CandidatesViewModel : ViewModelBase
    {
        private readonly CandidateService _service;

        public ICommand GoBackCommand { get; }
        public ICommand AddCandidateCommand { get; }
        public ICommand OpenCandidateCommand { get; }
        public ICommand SetViewModeCommand { get; }

        private string _viewMode = App.AppSettingsService.Settings.CandidateViewMode;
        public string ViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    OnPropertyChanged(nameof(IsListView));
                    OnPropertyChanged(nameof(IsTilesView));
                    App.AppSettingsService.Settings.CandidateViewMode = value;
                    App.AppSettingsService.SaveSettings();
                }
            }
        }

        public bool IsListView => ViewMode == "List";
        public bool IsTilesView => ViewMode == "Tiles";

        private double _zoomLevel = App.AppSettingsService.Settings.CandidateZoomLevel;
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (SetProperty(ref _zoomLevel, value))
                {
                    App.AppSettingsService.Settings.CandidateZoomLevel = value;
                    App.AppSettingsService.SaveSettings();
                }
            }
        }

        public ObservableCollection<CandidateSummary> Candidates { get; } = new();
        public ObservableCollection<string> Positions { get; } = new();

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
        }

        private string _selectedPosition = "";
        public string SelectedPosition
        {
            get => _selectedPosition;
            set { if (SetProperty(ref _selectedPosition, value)) ApplyFilter(); }
        }

        private int _totalCount;
        public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

        private bool _hasCandidates;
        public bool HasCandidates { get => _hasCandidates; set => SetProperty(ref _hasCandidates, value); }

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

        private System.Collections.Generic.List<CandidateSummary> _allCandidates = new();

        public CandidatesViewModel()
        {
            _service = App.CandidateService;

            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));
            SetViewModeCommand = new RelayCommand(o => ViewMode = o as string ?? "List");
            AddCandidateCommand = new RelayCommand(o =>
            {
                AddCandidateVm = new AddCandidateViewModel();
                AddCandidateVm.RequestClose += () =>
                {
                    IsAddDialogOpen = false;
                    LoadCandidates();
                };
                IsAddDialogOpen = true;
            });
            OpenCandidateCommand = new RelayCommand(o =>
            {
                if (o is CandidateSummary summary)
                {
                    DetailsVm = new CandidateDetailsViewModel(summary.CandidateFolder);
                    DetailsVm.RequestClose += () =>
                    {
                        IsDetailsOpen = false;
                        LoadCandidates();
                    };
                    IsDetailsOpen = true;
                }
            });

            LoadCandidates();
        }

        public void LoadCandidates()
        {
            _allCandidates = _service.GetAll();
            TotalCount = _allCandidates.Count;

            Positions.Clear();
            Positions.Add("");
            foreach (var p in _allCandidates
                .Select(c => c.DesiredPosition)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct().OrderBy(p => p))
            {
                Positions.Add(p);
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            Candidates.Clear();
            var query = _searchText?.Trim() ?? string.Empty;

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

                Candidates.Add(c);
            }

            HasCandidates = Candidates.Count > 0;
        }
    }
}
