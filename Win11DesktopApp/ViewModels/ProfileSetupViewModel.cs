using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public sealed class ProfileSetupViewModel : ViewModelBase
    {
        private readonly ProfileAuthService _profileAuthService;
        private readonly string _clientId;

        private string _firstName = string.Empty;
        private string _lastName = string.Empty;
        private string _password = string.Empty;
        private string _confirmPassword = string.Empty;
        private string _errorMessage = string.Empty;
        private string _currentLanguage = "uk";
        private bool _showPasswords;
        private bool _isBusy;

        public event Action<bool>? RequestClose;

        public ICommand CreateProfileCommand { get; }
        public ICommand CancelCommand { get; }

        public string FirstName
        {
            get => _firstName;
            set => SetProperty(ref _firstName, value);
        }

        public string LastName
        {
            get => _lastName;
            set => SetProperty(ref _lastName, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
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
                    LanguageService.SetLanguage(value);
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

        public ProfileSetupViewModel(ProfileAuthService profileAuthService, string clientId)
        {
            _profileAuthService = profileAuthService;
            _clientId = clientId;
            _currentLanguage = App.AppSettingsService?.Settings.LanguageCode ?? "uk";

            CreateProfileCommand = new AsyncRelayCommand(_ => CreateProfileAsync(), _ => !IsBusy);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false), _ => !IsBusy);
        }

        private async Task CreateProfileAsync()
        {
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(FirstName))
            {
                ErrorMessage = Res("ProfileErrFirstNameRequired");
                return;
            }

            if (string.IsNullOrWhiteSpace(LastName))
            {
                ErrorMessage = Res("ProfileErrLastNameRequired");
                return;
            }

            if (!ProfileAuthService.IsValidProfileName(FirstName))
            {
                ErrorMessage = Res("ProfileErrFirstNameLatin");
                return;
            }

            if (!ProfileAuthService.IsValidProfileName(LastName))
            {
                ErrorMessage = Res("ProfileErrLastNameLatin");
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = Res("ProfileErrPasswordRequired");
                return;
            }

            if (Password.Length < 6)
            {
                ErrorMessage = Res("ProfileErrPasswordMinLength");
                return;
            }

            if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
            {
                ErrorMessage = Res("ProfileErrPasswordsMismatch");
                return;
            }

            IsBusy = true;
            try
            {
                var result = await _profileAuthService.CreateProfileAsync(_clientId, FirstName, LastName, Password);
                if (!result.Success)
                {
                    ErrorMessage = result.ErrorMessage;
                    return;
                }

                RequestClose?.Invoke(true);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
