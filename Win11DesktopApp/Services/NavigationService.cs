using System;
using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services
{
    public class NavigationService : ViewModelBase
    {
        private readonly IServiceProvider _serviceProvider;
        private ViewModelBase? _currentView;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public ViewModelBase? CurrentView
        {
            get => _currentView;
            set
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }

        public void NavigateTo(ViewModelBase viewModel)
        {
            if (_currentView is ICleanable cleanable)
                cleanable.Cleanup();

            CurrentView = viewModel;
        }

        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
        {
            var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
            NavigateTo(viewModel);
        }
    }
}
