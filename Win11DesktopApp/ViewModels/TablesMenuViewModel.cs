using System.Windows.Input;

namespace Win11DesktopApp.ViewModels
{
    public class TablesMenuViewModel : ViewModelBase
    {
        public ICommand GoBackCommand { get; }
        public ICommand OpenAdvanceCommand { get; }
        public ICommand OpenPaymentSignCommand { get; }

        public TablesMenuViewModel()
        {
            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new FinanceTablesViewModel()));
            OpenAdvanceCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new AdvanceTableViewModel()));
            OpenPaymentSignCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new PaymentSignViewModel()));
        }
    }
}
