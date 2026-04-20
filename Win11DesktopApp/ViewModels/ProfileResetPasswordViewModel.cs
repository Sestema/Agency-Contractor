using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public sealed class ProfileResetPasswordViewModel : ViewModelBase
    {
        private readonly LanguageService _languageService;
        private readonly ProfileAuthService _profileAuthService;
        private readonly ClientProfileRecord _profile;

        private string _newPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _errorMessage = string.Empty;
        private string _currentLanguage = "uk";
        private bool _showPasswords;
        private bool _isBusy;

        public event Action<bool, ClientProfileRecord?>? RequestClose;

        public ICommand ResetPasswordCommand { get; }
        public ICommand CancelCommand { get; }

        public string FullName => $"{_profile.FirstName} {_profile.LastName}".Trim();

        public string NewPassword
        {
            get => _newPassword;
            set => SetProperty(ref _newPassword, value);
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set => SetProperty(ref _confirmPassword, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (SetProperty(ref _currentLanguage, value))
                    _languageService.SetLanguage(value);
            }
        }

        public bool ShowPasswords
        {
            get => _showPasswords;
            set => SetProperty(ref _showPasswords, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ProfileResetPasswordViewModel(
            LanguageService languageService,
            ProfileAuthService profileAuthService,
            ClientProfileRecord profile,
            AppSettingsService appSettingsService)
        {
            _languageService = languageService ?? throw new InvalidOperationException("LanguageService is not initialized.");
            var settingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _profileAuthService = profileAuthService;
            _profile = profile;
            _currentLanguage = settingsService.Settings.LanguageCode ?? "uk";

            ResetPasswordCommand = new AsyncRelayCommand(_ => ResetPasswordAsync(), _ => !IsBusy);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false, null), _ => !IsBusy);
        }

        private async Task ResetPasswordAsync()
        {
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                ErrorMessage = Res("ProfileErrNewPasswordRequired");
                return;
            }

            if (NewPassword.Length < 6)
            {
                ErrorMessage = Res("ProfileErrPasswordMinLength");
                return;
            }

            if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
            {
                ErrorMessage = Res("ProfileErrPasswordsMismatch");
                return;
            }

            IsBusy = true;
            try
            {
                var result = await _profileAuthService.CompleteForcedResetAsync(_profile.ClientId, NewPassword);
                if (!result.Success || result.Profile == null)
                {
                    ErrorMessage = result.ErrorMessage;
                    return;
                }

                RequestClose?.Invoke(true, result.Profile);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
