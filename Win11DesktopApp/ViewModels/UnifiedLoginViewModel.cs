using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels;

public sealed class UnifiedLoginViewModel : ViewModelBase
{
    private readonly LanguageService _languageService;
    private readonly UnifiedLoginService _unifiedLoginService;
    private readonly BusinessUserSessionService _businessUserSessionService;
    private readonly AppSettingsService _appSettingsService;
    private readonly string? _clientId;
    private readonly ClientProfileRecord? _ownerProfile;

    private string _login = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private string _currentLanguage = "uk";
    private bool _showPassword;
    private bool _rememberMe;
    private bool _isBusy;

    public event Action<UnifiedLoginAttemptResult>? RequestClose;

    public ICommand LoginCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ConnectWorkspaceCommand { get; }

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

    public bool CanConnectWorkspace =>
        string.IsNullOrWhiteSpace(_appSettingsService.Settings.RootFolderPath);

    public UnifiedLoginViewModel(
        LanguageService languageService,
        UnifiedLoginService unifiedLoginService,
        BusinessUserSessionService businessUserSessionService,
        AppSettingsService appSettingsService,
        string? clientId,
        ClientProfileRecord? ownerProfile)
    {
        _languageService = languageService;
        _unifiedLoginService = unifiedLoginService;
        _businessUserSessionService = businessUserSessionService;
        _appSettingsService = appSettingsService;
        _clientId = clientId;
        _ownerProfile = ownerProfile;
        _currentLanguage = appSettingsService.Settings.LanguageCode ?? "uk";
        _rememberMe = ownerProfile?.RememberMeEnabled == true || businessUserSessionService.IsRememberEnabled;

        if (businessUserSessionService.TryGetRememberedLogin(out var rememberedLogin))
            _login = rememberedLogin;

        LoginCommand = new AsyncRelayCommand(_ => LoginAsync(), _ => !IsBusy && CanLogin);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(UnifiedLoginAttemptResult.Failed(string.Empty)), _ => !IsBusy);
        ConnectWorkspaceCommand = new RelayCommand(_ => ConnectWorkspace(), _ => !IsBusy && CanConnectWorkspace);
    }

    private bool CanLogin =>
        !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);

    private void ConnectWorkspace()
    {
        ErrorMessage = string.Empty;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Res("UnifiedLoginConnectWorkspaceTitle")
        };

        if (!string.IsNullOrWhiteSpace(_appSettingsService.Settings.RootFolderPath)
            && System.IO.Directory.Exists(_appSettingsService.Settings.RootFolderPath))
        {
            dialog.InitialDirectory = _appSettingsService.Settings.RootFolderPath;
        }

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        _appSettingsService.Settings.RootFolderPath = dialog.FolderName;
        _appSettingsService.SaveSettings();
        _unifiedLoginService.ImportBusinessUsersFromRootIfAvailable();
        OnPropertyChanged(nameof(CanConnectWorkspace));
        ErrorMessage = Res("UnifiedLoginWorkspaceConnected");
    }

    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;

        try
        {
            var attempt = await _unifiedLoginService.AuthenticateAsync(
                Login,
                Password,
                _clientId,
                _ownerProfile,
                RememberMe);

            if (!attempt.Success)
            {
                ErrorMessage = attempt.ErrorMessage;
                return;
            }

            RequestClose?.Invoke(attempt);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
