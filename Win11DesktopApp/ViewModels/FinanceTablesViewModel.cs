using System.Windows.Input;
using Win11DesktopApp.Services;

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
            OpenFinancesCommand = new RelayCommand(o =>
            {
                if (!PolicyService.IsFeatureVisible("finances"))
                {
                    ToastService.Instance.Warning("Модуль фінансів тимчасово недоступний для цього клієнта.");
                    return;
                }

                App.NavigationService.NavigateTo(new SalaryViewModel());
            });
            OpenTablesCommand = new RelayCommand(o =>
            {
                if (!PolicyService.IsFeatureVisible("finances"))
                {
                    ToastService.Instance.Warning("Модуль фінансів тимчасово недоступний для цього клієнта.");
                    return;
                }

                App.NavigationService.NavigateTo(new TablesMenuViewModel());
            });
        }
    }
}
