using System.Windows.Input;

namespace Win11DesktopApp.ViewModels
{
    public class FinanceTablesViewModel : ViewModelBase
    {
        public ICommand GoBackCommand { get; }
        public ICommand OpenFinancesCommand { get; }
        public ICommand OpenTablesCommand { get; }

        public FinanceTablesViewModel()
        {
            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));
            OpenFinancesCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new SalaryViewModel()));
            OpenTablesCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new TablesMenuViewModel()));
        }
    }
}
