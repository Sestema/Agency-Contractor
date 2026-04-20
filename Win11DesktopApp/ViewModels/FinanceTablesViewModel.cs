using System.Windows.Input;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class FinanceTablesViewModel : ViewModelBase
    {
        private readonly NavigationService _navigationService;
        private readonly FinanceModuleViewModelFactory _financeModuleViewModelFactory;

        public ICommand GoBackCommand { get; }
        public ICommand OpenFinancesCommand { get; }
        public ICommand OpenTablesCommand { get; }

        public FinanceTablesViewModel(
            NavigationService? navigationService = null,
            FinanceModuleViewModelFactory? financeModuleViewModelFactory = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _financeModuleViewModelFactory = financeModuleViewModelFactory ?? throw new InvalidOperationException("FinanceModuleViewModelFactory is not initialized.");

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());
            OpenFinancesCommand = new RelayCommand(o =>
            {
                if (!PolicyService.IsFeatureVisible("finances"))
                {
                    ToastService.Instance.Warning("Модуль фінансів тимчасово недоступний для цього клієнта.");
                    return;
                }

                _navigationService.NavigateTo(_financeModuleViewModelFactory.CreateSalary());
            });
            OpenTablesCommand = new RelayCommand(o =>
            {
                if (!PolicyService.IsFeatureVisible("finances"))
                {
                    ToastService.Instance.Warning("Модуль фінансів тимчасово недоступний для цього клієнта.");
                    return;
                }

                _navigationService.NavigateTo(_financeModuleViewModelFactory.CreateTablesMenu());
            });
        }
    }
}
