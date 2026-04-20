using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class ProfileLoginWindow : Window
    {
        private readonly ProfileLoginViewModel _viewModel;

        public bool IsAuthenticated { get; private set; }
        public ClientProfileRecord? AuthenticatedProfile { get; private set; }

        public ProfileLoginWindow(ProfileLoginViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _viewModel.RequestClose += OnRequestClose;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            DataContext = _viewModel;
            SyncPasswordBox();
        }

        private void OnRequestClose(bool success, ClientProfileRecord? profile)
        {
            IsAuthenticated = success;
            AuthenticatedProfile = profile;
            DialogResult = success;
            Close();
        }

        private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.Password = ((PasswordBox)sender).Password;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProfileLoginViewModel.Password)
                || e.PropertyName == nameof(ProfileLoginViewModel.ShowPassword))
            {
                SyncPasswordBox();
            }
        }

        private void SyncPasswordBox()
        {
            if (PasswordBox.Password != _viewModel.Password)
                PasswordBox.Password = _viewModel.Password ?? string.Empty;
        }
    }
}
