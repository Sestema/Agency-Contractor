using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.ViewModels
{
    public class AddCompanyViewModel : ViewModelBase
    {
        private EmployerCompany _employer = new();
        public EmployerCompany Employer
        {
            get => _employer;
            set => SetProperty(ref _employer, value);
        }

        private AgencyCompany _agency = new();
        public AgencyCompany Agency
        {
            get => _agency;
            set => SetProperty(ref _agency, value);
        }

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (SetProperty(ref _isEditMode, value))
                    OnPropertyChanged(nameof(DialogTitle));
            }
        }

        public string DialogTitle => IsEditMode ? Res("CompanyDialogTitleEdit") : Res("CompanyDialogTitleAdd");

        private string _originalCompanyName = string.Empty;
        private EmployerCompany? _originalCompany;

        public ICommand AddAddressCommand { get; }
        public ICommand RemoveAddressCommand { get; }
        public ICommand AddPositionCommand { get; }
        public ICommand RemovePositionCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DeleteCompanyCommand { get; }

        public event Action? RequestClose;

        /// <summary>
        /// Constructor for ADD mode.
        /// </summary>
        public AddCompanyViewModel()
        {
            Employer.Addresses.Add(new WorkAddress());
            Employer.Positions.Add(new Position());

            AddAddressCommand = new RelayCommand(o =>
            {
                if (Employer.Addresses.Count < 4)
                    Employer.Addresses.Add(new WorkAddress());
            });

            RemoveAddressCommand = new RelayCommand(o =>
            {
                if (o is WorkAddress addr && Employer.Addresses.Count > 1)
                    Employer.Addresses.Remove(addr);
            }, o => Employer.Addresses.Count > 1);

            AddPositionCommand = new RelayCommand(o => Employer.Positions.Add(new Position()));

            RemovePositionCommand = new RelayCommand(o =>
            {
                if (o is Position pos && Employer.Positions.Count > 1)
                    Employer.Positions.Remove(pos);
            }, o => Employer.Positions.Count > 1);

            SaveCommand = new RelayCommand(o => Save());
            CancelCommand = new RelayCommand(o => RequestClose?.Invoke());
            DeleteCompanyCommand = new RelayCommand(o => DeleteCompany(), o => IsEditMode);
        }

        /// <summary>
        /// Constructor for EDIT mode — creates a working COPY of the company data.
        /// Changes are applied to the original only on Save.
        /// </summary>
        public AddCompanyViewModel(EmployerCompany existingCompany) : this()
        {
            IsEditMode = true;
            _originalCompany = existingCompany;
            _originalCompanyName = existingCompany.Name;

            // Create a working copy for editing
            Employer = CloneEmployer(existingCompany);
            Agency = CloneAgency(existingCompany.Agency);
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(Employer.Name))
            {
                MessageBox.Show(
                    Application.Current?.TryFindResource("MsgEnterCompanyName") as string ?? "Enter company name.",
                    Application.Current?.TryFindResource("TitleError") as string ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsEditMode && _originalCompany != null)
            {
                // Apply changes from the working copy to the original
                _originalCompany.Name = Employer.Name;
                _originalCompany.ICO = Employer.ICO;

                _originalCompany.Addresses.Clear();
                foreach (var addr in Employer.Addresses)
                    _originalCompany.Addresses.Add(addr);

                _originalCompany.Positions.Clear();
                foreach (var pos in Employer.Positions)
                    _originalCompany.Positions.Add(pos);

                _originalCompany.WeeklyWorkHours = Employer.WeeklyWorkHours;
                _originalCompany.DailyWorkHours = Employer.DailyWorkHours;
                _originalCompany.ShiftCount = Employer.ShiftCount;

                _originalCompany.Agency ??= new AgencyCompany();
                _originalCompany.Agency.Name = Agency.Name;
                _originalCompany.Agency.ICO = Agency.ICO;
                _originalCompany.Agency.FullAddress = Agency.FullAddress;

                App.CompanyService.UpdateCompany(_originalCompany, _originalCompanyName);
            }
            else
            {
                App.CompanyService.AddCompany(Employer, Agency);
                App.ActivityLogService.Log("CompanyAdded", "Company", Employer.Name, "",
                    $"Додано фірму: {Employer.Name}");
            }
            RequestClose?.Invoke();
        }

        private void DeleteCompany()
        {
            if (_originalCompany == null) return;

            var employees = App.EmployeeService.GetEmployeesForFirm(_originalCompany.Name);
            if (employees.Count > 0)
            {
                var empResult = MessageBox.Show(
                    string.Format(Res("MsgCompanyHasEmployees"), employees.Count),
                    Res("TitleWarning"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (empResult != MessageBoxResult.Yes) return;
            }

            var result = MessageBox.Show(
                string.Format(Res("MsgConfirmDeleteCompany"), _originalCompany.Name),
                Res("TitleDeleteCompany"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var name = _originalCompany.Name;
                App.CompanyService.DeleteCompany(_originalCompany);
                App.ActivityLogService.Log("CompanyDeleted", "Company", name, "",
                    $"Видалено фірму: {name}");
                RequestClose?.Invoke();
            }
        }

        // ============ CLONING HELPERS ============

        private static EmployerCompany CloneEmployer(EmployerCompany src)
        {
            var clone = new EmployerCompany
            {
                Id = src.Id,
                CreatedAt = src.CreatedAt,
                LastModified = src.LastModified,
                Name = src.Name,
                ICO = src.ICO,
                WeeklyWorkHours = src.WeeklyWorkHours,
                DailyWorkHours = src.DailyWorkHours,
                ShiftCount = src.ShiftCount
            };

            clone.Addresses.Clear();
            foreach (var addr in src.Addresses)
            {
                clone.Addresses.Add(new WorkAddress
                {
                    Street = addr.Street,
                    Number = addr.Number,
                    City = addr.City,
                    ZipCode = addr.ZipCode
                });
            }

            clone.Positions.Clear();
            foreach (var pos in src.Positions)
            {
                clone.Positions.Add(new Position
                {
                    Title = pos.Title,
                    PositionNumber = pos.PositionNumber,
                    MonthlySalaryBrutto = pos.MonthlySalaryBrutto,
                    HourlySalary = pos.HourlySalary
                });
            }

            return clone;
        }

        private static AgencyCompany CloneAgency(AgencyCompany? src)
        {
            if (src == null) return new AgencyCompany();
            return new AgencyCompany
            {
                Name = src.Name,
                ICO = src.ICO,
                FullAddress = src.FullAddress
            };
        }
    }
}
