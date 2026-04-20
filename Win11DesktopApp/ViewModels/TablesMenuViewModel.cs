using System.Windows.Input;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class TablesMenuViewModel : ViewModelBase
    {
        private readonly NavigationService _navigationService;
        private readonly FinanceModuleViewModelFactory _financeModuleViewModelFactory;
        public ICommand GoBackCommand { get; }
        public ICommand OpenAdvanceCommand { get; }
        public ICommand OpenPaymentSignCommand { get; }

        public TablesMenuViewModel(
            NavigationService? navigationService = null,
            FinanceModuleViewModelFactory? financeModuleViewModelFactory = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _financeModuleViewModelFactory = financeModuleViewModelFactory ?? throw new InvalidOperationException("FinanceModuleViewModelFactory is not initialized.");

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<FinanceTablesViewModel>());
            OpenAdvanceCommand = new RelayCommand(o => _navigationService.NavigateTo(_financeModuleViewModelFactory.CreateAdvanceTable()));
            OpenPaymentSignCommand = new RelayCommand(o => _navigationService.NavigateTo(_financeModuleViewModelFactory.CreatePaymentSign()));
        }
    }
}
