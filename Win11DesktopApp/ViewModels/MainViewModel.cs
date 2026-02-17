using System.Windows;
using System.Windows.Input;
using System.Collections.ObjectModel;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using System.Linq;

namespace Win11DesktopApp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public ICommand GoToSettingsCommand { get; }
        public ICommand ToggleDrawerCommand { get; }
        public ICommand OpenAddCompanyDialogCommand { get; }
        public ICommand CloseAddCompanyDialogCommand { get; }
        public ICommand EditCompanyCommand { get; }
        public ICommand ButtonCommand { get; }
        public ICommand OpenTemplatesCommand { get; }
        public ICommand OpenEmployeesCommand { get; }
        public ICommand OpenProblemsCommand { get; }
        public ICommand OpenArchiveCommand { get; }

        private int _problemsCount;
        public int ProblemsCount
        {
            get => _problemsCount;
            set => SetProperty(ref _problemsCount, value);
        }

        private bool _isDrawerOpen;
        public bool IsDrawerOpen
        {
            get => _isDrawerOpen;
            set => SetProperty(ref _isDrawerOpen, value);
        }

        private bool _isAddCompanyDialogOpen;
        public bool IsAddCompanyDialogOpen
        {
            get => _isAddCompanyDialogOpen;
            set => SetProperty(ref _isAddCompanyDialogOpen, value);
        }

        private AddCompanyViewModel? _addCompanyVm;
        public AddCompanyViewModel? AddCompanyVm
        {
            get => _addCompanyVm;
            set => SetProperty(ref _addCompanyVm, value);
        }

        public ObservableCollection<EmployerCompany> Companies => App.CompanyService.Companies;

        public EmployerCompany? SelectedCompany
        {
            get => App.CompanyService.SelectedCompany;
            set
            {
                App.CompanyService.SelectedCompany = value;
                OnPropertyChanged(nameof(SelectedCompany));
                OnPropertyChanged(nameof(HasSelectedCompany));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasSelectedCompany => SelectedCompany != null;

        public MainViewModel()
        {
            GoToSettingsCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new SettingsViewModel()));
            ButtonCommand = new RelayCommand(o => { });

            OpenTemplatesCommand = new RelayCommand(o =>
            {
                if (SelectedCompany != null)
                    App.NavigationService.NavigateTo(new TemplatesViewModel(SelectedCompany));
            }, o => SelectedCompany != null);

            OpenEmployeesCommand = new RelayCommand(o =>
            {
                App.NavigationService.NavigateTo(new EmployeesViewModel(SelectedCompany));
            });

            OpenProblemsCommand = new RelayCommand(o =>
            {
                App.NavigationService.NavigateTo(new ProblemsViewModel());
            });

            OpenArchiveCommand = new RelayCommand(o =>
            {
                App.NavigationService.NavigateTo(new ArchiveViewModel());
            });

            ToggleDrawerCommand = new RelayCommand(o => IsDrawerOpen = !IsDrawerOpen);

            OpenAddCompanyDialogCommand = new RelayCommand(o =>
            {
                AddCompanyVm = new AddCompanyViewModel();
                AddCompanyVm.RequestClose += () =>
                {
                    IsAddCompanyDialogOpen = false;
                    if (SelectedCompany == null && Companies.Any())
                        SelectedCompany = Companies.Last();
                    OnPropertyChanged(nameof(SelectedCompany));
                    OnPropertyChanged(nameof(HasSelectedCompany));
                };
                IsAddCompanyDialogOpen = true;
                IsDrawerOpen = false;
            });

            EditCompanyCommand = new RelayCommand(o =>
            {
                var company = o as EmployerCompany ?? SelectedCompany;
                if (company == null) return;

                AddCompanyVm = new AddCompanyViewModel(company);
                AddCompanyVm.RequestClose += () =>
                {
                    IsAddCompanyDialogOpen = false;
                    OnPropertyChanged(nameof(SelectedCompany));
                    OnPropertyChanged(nameof(HasSelectedCompany));
                };
                IsAddCompanyDialogOpen = true;
                IsDrawerOpen = false;
            }, o => true);

            CloseAddCompanyDialogCommand = new RelayCommand(o => IsAddCompanyDialogOpen = false);

            CheckRootFolder();

            App.CompanyService.SelectedCompanyChanged += _ =>
            {
                OnPropertyChanged(nameof(SelectedCompany));
                OnPropertyChanged(nameof(HasSelectedCompany));
                RefreshProblemsCount();
            };

            RefreshProblemsCount();
        }

        private void RefreshProblemsCount()
        {
            try
            {
                ProblemsCount = ProblemsViewModel.CountAllProblems();
            }
            catch { ProblemsCount = 0; }
        }

        private static void CheckRootFolder()
        {
            if (string.IsNullOrEmpty(App.AppSettingsService.Settings.RootFolderPath))
            {
                // Root folder check logic
            }
        }
    }
}
