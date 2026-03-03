using System;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services
{
    public class NavigationService : ViewModelBase
    {
        private ViewModelBase? _currentView;

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
    }
}
