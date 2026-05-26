using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels;

public sealed class BusinessUserLoginWindowViewModel : ViewModelBase
{
    private readonly LanguageService _languageService;
    private readonly BusinessUserAuthService _businessUserAuthService;
    private readonly AppSettingsService _appSettingsService;

    private string _login = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private string _currentLanguage = "uk";
    private bool _showPassword;
    private bool _isBusy;

    public event Action<bool, AppSettingsService.BusinessUserSetting?>? RequestClose;

    public ICommand LoginCommand { get; }
    public ICommand CancelCommand { get; }

    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

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
                _languageService.SetLanguage(value);
        }
    }

    public bool ShowPassword
    {
        get => _showPassword;
        set => SetProperty(ref _showPassword, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public BusinessUserLoginWindowViewModel(
        LanguageService languageService,
        BusinessUserAuthService businessUserAuthService,
        AppSettingsService appSettingsService)
    {
        _languageService = languageService;
        _businessUserAuthService = businessUserAuthService;
        _appSettingsService = appSettingsService;
        _currentLanguage = _appSettingsService.Settings.LanguageCode ?? "uk";

        LoginCommand = new AsyncRelayCommand(_ => LoginAsync(), _ => !IsBusy && CanLogin);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false, null), _ => !IsBusy);
    }

    private bool CanLogin =>
        !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);

    private Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;

        try
        {
            var result = _businessUserAuthService.TryLoginByLogin(Login, Password);
            if (!result.Success || result.User == null)
            {
                ErrorMessage = result.FailureReason switch
                {
                    "wrong_password" => Res("BusinessUserLoginWrongPassword"),
                    _ => Res("BusinessUserLoginUserNotFound")
                };
                return Task.CompletedTask;
            }

            RequestClose?.Invoke(true, result.User);
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }
}
