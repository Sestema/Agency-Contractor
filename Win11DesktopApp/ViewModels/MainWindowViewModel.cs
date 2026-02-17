using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly NavigationService _navigationService;

        public ViewModelBase? CurrentView => _navigationService.CurrentView;

        public MainWindowViewModel(NavigationService navigationService)
        {
            _navigationService = navigationService;
            _navigationService.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NavigationService.CurrentView))
                {
                    OnPropertyChanged(nameof(CurrentView));
                }
            };
        }
    }
}
