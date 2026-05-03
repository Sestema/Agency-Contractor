using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class AddCompanyViewModel : ViewModelBase
    {
        private readonly CompanyService _companyService;
        private readonly EmployeeService _employeeService;
        private readonly FolderService _folderService;
        private readonly ActivityLogService _activityLogService;
        private readonly AresLookupService _aresLookupService;
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
        public ICommand LookupEmployerAresCommand { get; }
        public ICommand LookupAgencyAresCommand { get; }

        public event Action? RequestClose;

        /// <summary>
        /// Constructor for ADD mode.
        /// </summary>
        public AddCompanyViewModel(
            CompanyService? companyService = null,
            EmployeeService? employeeService = null,
            FolderService? folderService = null,
            ActivityLogService? activityLogService = null,
            AresLookupService? aresLookupService = null)
        {
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _folderService = folderService ?? throw new InvalidOperationException("FolderService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _aresLookupService = aresLookupService ?? throw new InvalidOperationException("AresLookupService is not initialized.");

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

            SaveCommand = new AsyncRelayCommand(_ => SaveAsync());
            CancelCommand = new RelayCommand(o => RequestClose?.Invoke());
            DeleteCompanyCommand = new AsyncRelayCommand(_ => DeleteCompanyAsync(), _ => IsEditMode);
            LookupEmployerAresCommand = new AsyncRelayCommand(_ => LookupEmployerAresAsync());
            LookupAgencyAresCommand = new AsyncRelayCommand(_ => LookupAgencyAresAsync());
        }

        /// <summary>
        /// Constructor for EDIT mode — creates a working COPY of the company data.
        /// Changes are applied to the original only on Save.
        /// </summary>
        public AddCompanyViewModel(
            EmployerCompany existingCompany,
            CompanyService? companyService = null,
            EmployeeService? employeeService = null,
            FolderService? folderService = null,
            ActivityLogService? activityLogService = null,
            AresLookupService? aresLookupService = null)
            : this(companyService, employeeService, folderService, activityLogService, aresLookupService)
        {
            IsEditMode = true;
            _originalCompany = existingCompany;
            _originalCompanyName = existingCompany.Name;

            // Create a working copy for editing
            Employer = CloneEmployer(existingCompany);
            Agency = CloneAgency(existingCompany.Agency);
        }

        private async Task LookupEmployerAresAsync()
        {
            var normalizedIco = NormalizeIco(Employer.ICO);
            if (string.IsNullOrWhiteSpace(normalizedIco))
            {
                ToastService.Instance.Warning(Res("InvoicesAresLookupInvalidIco"));
                return;
            }

            var result = await LookupAresAsync(normalizedIco);
            if (result == null)
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Employer.Name = result.Name;
                Employer.ICO = result.Ico;
                Employer.LegalAddress = FormatAresAddress(result);
                ToastService.Instance.Success(string.Format(Res("InvoicesAresLookupSuccess"), normalizedIco));
            });
        }

        private async Task LookupAgencyAresAsync()
        {
            var normalizedIco = NormalizeIco(Agency.ICO);
            if (string.IsNullOrWhiteSpace(normalizedIco))
            {
                ToastService.Instance.Warning(Res("InvoicesAresLookupInvalidIco"));
                return;
            }

            var result = await LookupAresAsync(normalizedIco);
            if (result == null)
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Agency.Name = result.Name;
                Agency.ICO = result.Ico;
                Agency.FullAddress = FormatAresAddress(result);
                ToastService.Instance.Success(string.Format(Res("InvoicesAresLookupSuccess"), normalizedIco));
            });
        }

        private async Task<AresCompanyData?> LookupAresAsync(string normalizedIco)
        {
            try
            {
                var result = await _aresLookupService.LookupByIcoAsync(normalizedIco);
                if (result == null)
                    ToastService.Instance.Warning(string.Format(Res("InvoicesAresLookupNotFound"), normalizedIco));

                return result;
            }
            catch (HttpRequestException)
            {
                ToastService.Instance.Error(Res("InvoicesAresLookupFailed"));
                return null;
            }
            catch (TaskCanceledException)
            {
                ToastService.Instance.Error(Res("InvoicesAresLookupFailed"));
                return null;
            }
            catch (Exception ex)
            {
                ErrorHandler.Report("AddCompanyViewModel.LookupAresAsync", ex, ErrorSeverity.Error, showUser: false);
                ToastService.Instance.Error(Res("InvoicesAresLookupFailed"));
                return null;
            }
        }

        private static string FormatAresAddress(AresCompanyData source)
        {
            return string.Join(", ", new[] { source.Street, source.PostalCode, source.City, source.Country }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static string NormalizeIco(string? ico)
            => new string((ico ?? string.Empty).Where(char.IsDigit).ToArray());

        private async Task SaveAsync()
        {
            if (!PolicyService.EnsureWriteAllowed(IsEditMode ? "Зберегти фірму" : "Додати фірму"))
                return;

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
                _originalCompany.LegalAddress = Employer.LegalAddress;

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

                await _companyService.UpdateCompanyAsync(_originalCompany, _originalCompanyName);
            }
            else
            {
                await _companyService.AddCompanyAsync(Employer, Agency);
                _activityLogService.Log("CompanyAdded", "Company", Employer.Name, "",
                    $"Додано фірму: {Employer.Name}");
                TelemetryService.TrackEvent("firm_created", new Dictionary<string, object>
                {
                    ["firm_name"] = Employer.Name
                });
            }
            RequestClose?.Invoke();
        }

        private async Task DeleteCompanyAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Видалити фірму"))
                return;

            if (_originalCompany == null) return;

            var employees = _employeeService.GetEmployeesForFirm(_originalCompany.Name);
            var employeeFolderCount = _folderService.GetCompanyEmployeeFolderCount(_originalCompany.Name);
            var employeeCount = Math.Max(employees.Count, employeeFolderCount);
            if (employeeCount > 0)
            {
                MessageBox.Show(
                    string.Format(Res("MsgCompanyHasEmployees"), employeeCount),
                    Res("TitleWarning"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                string.Format(Res("MsgConfirmDeleteCompany"), _originalCompany.Name),
                Res("TitleDeleteCompany"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var name = _originalCompany.Name;
                if (await _companyService.DeleteCompanyAsync(_originalCompany))
                {
                    _activityLogService.Log("CompanyDeleted", "Company", name, "",
                        $"Видалено фірму: {name}");
                    RequestClose?.Invoke();
                }
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
                HiddenFromYear = src.HiddenFromYear,
                HiddenFromMonth = src.HiddenFromMonth,
                Name = src.Name,
                ICO = src.ICO,
                LegalAddress = src.LegalAddress,
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
