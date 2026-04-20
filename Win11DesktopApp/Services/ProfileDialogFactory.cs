using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.Services;

public sealed class ProfileDialogFactory
{
    private readonly LanguageService _languageService;
    private readonly ProfileAuthService _profileAuthService;
    private readonly ProfileSessionService _profileSessionService;
    private readonly AppSettingsService _appSettingsService;

    public ProfileDialogFactory(
        LanguageService languageService,
        ProfileAuthService profileAuthService,
        ProfileSessionService profileSessionService,
        AppSettingsService appSettingsService)
    {
        _languageService = languageService;
        _profileAuthService = profileAuthService;
        _profileSessionService = profileSessionService;
        _appSettingsService = appSettingsService;
    }

    public ProfileSetupWindow CreateSetupWindow(string clientId)
    {
        var viewModel = new ProfileSetupViewModel(_languageService, _profileAuthService, clientId, _appSettingsService);
        return new ProfileSetupWindow(viewModel);
    }

    public ProfileResetPasswordWindow CreateResetPasswordWindow(ClientProfileRecord profile)
    {
        var viewModel = new ProfileResetPasswordViewModel(_languageService, _profileAuthService, profile, _appSettingsService);
        return new ProfileResetPasswordWindow(viewModel);
    }

    public ProfileLoginWindow CreateLoginWindow(ClientProfileRecord profile)
    {
        var viewModel = new ProfileLoginViewModel(_languageService, _profileAuthService, _profileSessionService, profile, _appSettingsService);
        return new ProfileLoginWindow(viewModel);
    }
}
