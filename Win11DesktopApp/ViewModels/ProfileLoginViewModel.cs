using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public sealed class ProfileLoginViewModel : ViewModelBase
    {
        private readonly ProfileAuthService _profileAuthService;
        private readonly ProfileSessionService _profileSessionService;
        private readonly ClientProfileRecord _profile;

        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private string _currentLanguage = "uk";
        private bool _showPassword;
        private bool _rememberMe;
        private bool _isBusy;

        public event Action<bool, ClientProfileRecord?>? RequestClose;

        public ICommand LoginCommand { get; }
        public ICommand CancelCommand { get; }

        public string FullName => $"{_profile.FirstName} {_profile.LastName}".Trim();

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
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

        public bool ShowPassword
        {
            get => _showPassword;
            set => SetProperty(ref _showPassword, value);
        }

        public bool RememberMe
        {
            get => _rememberMe;
            set => SetProperty(ref _rememberMe, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ProfileLoginViewModel(
            ProfileAuthService profileAuthService,
            ProfileSessionService profileSessionService,
            ClientProfileRecord profile)
        {
            _profileAuthService = profileAuthService;
            _profileSessionService = profileSessionService;
            _profile = profile;
            _currentLanguage = App.AppSettingsService?.Settings.LanguageCode ?? "uk";
            _rememberMe = profile.RememberMeEnabled;

            LoginCommand = new AsyncRelayCommand(_ => LoginAsync(), _ => !IsBusy);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false, null), _ => !IsBusy);
        }

        private async Task LoginAsync()
        {
            ErrorMessage = string.Empty;
            IsBusy = true;

            try
            {
                var auth = await _profileAuthService.AuthenticateAsync(_profile.ClientId, Password);
                if (!auth.Success || auth.Profile == null)
                {
                    ErrorMessage = auth.ErrorMessage;
                    return;
                }

                var rememberResult = await _profileAuthService.UpdateRememberMeAsync(auth.Profile.ClientId, RememberMe);
                if (!rememberResult.Success || rememberResult.Profile == null)
                {
                    ErrorMessage = rememberResult.ErrorMessage;
                    return;
                }

                if (RememberMe)
                    _profileSessionService.SaveRememberedSession(rememberResult.Profile);
                else
                    _profileSessionService.ClearRememberedSession();

                RequestClose?.Invoke(true, rememberResult.Profile);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
