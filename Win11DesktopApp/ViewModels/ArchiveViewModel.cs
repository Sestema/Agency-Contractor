using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
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

        public ICommand GoBackCommand { get; }
        public ICommand RestoreEmployeeCommand { get; }
        public ICommand ConfirmRestoreCommand { get; }
        public ICommand CancelRestoreCommand { get; }
        public ICommand OpenEmployeeFolderCommand { get; }

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

        public ObservableCollection<EmployerCompany> AvailableCompanies => App.CompanyService.Companies;

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

        public ArchiveViewModel()
        {
            _employeeService = App.EmployeeService;

            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));

            OpenEmployeeFolderCommand = new RelayCommand(o =>
            {
                if (o is ArchivedEmployeeSummary emp && !string.IsNullOrEmpty(emp.EmployeeFolder))
                {
                    try { Process.Start(new ProcessStartInfo { FileName = emp.EmployeeFolder, UseShellExecute = true }); }
                    catch { }
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

            ConfirmRestoreCommand = new RelayCommand(o => ConfirmRestore());
            CancelRestoreCommand = new RelayCommand(o => IsRestoreDialogOpen = false);

            LoadArchive();
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

        private void LoadArchive()
        {
            try
            {
                _allArchived = _employeeService.GetArchivedEmployees();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ArchiveViewModel.LoadArchive error: {ex.Message}");
            }
        }

        private void ApplyFilter()
        {
            var query = SearchQuery?.Trim().ToLower() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                ArchivedEmployees = new ObservableCollection<ArchivedEmployeeSummary>(_allArchived);
            }
            else
            {
                var filtered = _allArchived.Where(e =>
                    (!string.IsNullOrEmpty(e.FullName) && e.FullName.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(e.FirmName) && e.FirmName.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(e.PositionTitle) && e.PositionTitle.ToLower().Contains(query))
                ).ToList();
                ArchivedEmployees = new ObservableCollection<ArchivedEmployeeSummary>(filtered);
            }
            HasArchived = ArchivedEmployees.Count > 0;
        }

        private void ConfirmRestore()
        {
            if (EmployeeToRestore == null)
            {
                RestoreStatus = "Не обрано працівника.";
                return;
            }

            if (SelectedCompany == null)
            {
                RestoreStatus = "Оберіть фірму.";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewStartDate))
            {
                RestoreStatus = "Вкажіть дату наступу.";
                return;
            }

            try
            {
                var positionTitle = SelectedPosition?.Title ?? string.Empty;
                var workAddress = SelectedAddress != null
                    ? $"{SelectedAddress.Street} {SelectedAddress.Number}, {SelectedAddress.City} {SelectedAddress.ZipCode}".Trim()
                    : string.Empty;

                var result = _employeeService.RestoreFromArchive(
                    EmployeeToRestore.EmployeeFolder,
                    SelectedCompany.Name,
                    NewStartDate,
                    NewContractSignDate,
                    positionTitle,
                    workAddress
                );

                if (!string.IsNullOrEmpty(result))
                {
                    IsRestoreDialogOpen = false;
                    LoadArchive();
                }
                else
                {
                    RestoreStatus = "Помилка при відновленні.";
                }
            }
            catch (Exception ex)
            {
                RestoreStatus = $"Помилка: {ex.Message}";
            }
        }
    }
}
