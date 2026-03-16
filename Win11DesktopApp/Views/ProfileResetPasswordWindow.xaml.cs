using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class ProfileResetPasswordWindow : Window
    {
        private readonly ProfileResetPasswordViewModel _viewModel;

        public bool IsPasswordReset { get; private set; }
        public ClientProfileRecord? ResetProfile { get; private set; }

        public ProfileResetPasswordWindow(ProfileAuthService profileAuthService, ClientProfileRecord profile)
        {
            InitializeComponent();

            _viewModel = new ProfileResetPasswordViewModel(profileAuthService, profile);
            _viewModel.RequestClose += OnRequestClose;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            DataContext = _viewModel;
            SyncPasswordBoxes();
        }

        private void OnRequestClose(bool success, ClientProfileRecord? profile)
        {
            IsPasswordReset = success;
            ResetProfile = profile;
            DialogResult = success;
            Close();
        }

        private void NewPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.NewPassword = ((PasswordBox)sender).Password;
        }

        private void ConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.ConfirmPassword = ((PasswordBox)sender).Password;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProfileResetPasswordViewModel.NewPassword)
                || e.PropertyName == nameof(ProfileResetPasswordViewModel.ConfirmPassword)
                || e.PropertyName == nameof(ProfileResetPasswordViewModel.ShowPasswords))
            {
                SyncPasswordBoxes();
            }
        }

        private void SyncPasswordBoxes()
        {
            if (NewPasswordBox.Password != _viewModel.NewPassword)
                NewPasswordBox.Password = _viewModel.NewPassword ?? string.Empty;

            if (ConfirmPasswordBox.Password != _viewModel.ConfirmPassword)
                ConfirmPasswordBox.Password = _viewModel.ConfirmPassword ?? string.Empty;
        }
    }
}
