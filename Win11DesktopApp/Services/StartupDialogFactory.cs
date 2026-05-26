using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.Services;

public sealed class StartupDialogFactory
{
    private readonly LanguageService _languageService;
    private readonly AppSettingsService _appSettingsService;
    private readonly FolderService _folderService;
    private readonly UnifiedLoginService _unifiedLoginService;
    private readonly BusinessUserSessionService _businessUserSessionService;

    public StartupDialogFactory(
        LanguageService languageService,
        AppSettingsService appSettingsService,
        FolderService folderService,
        UnifiedLoginService unifiedLoginService,
        BusinessUserSessionService businessUserSessionService)
    {
        _languageService = languageService;
        _appSettingsService = appSettingsService;
        _folderService = folderService;
        _unifiedLoginService = unifiedLoginService;
        _businessUserSessionService = businessUserSessionService;
    }
    public StartupRoleSelectionWindow CreateRoleSelectionWindow()
    {
        var viewModel = new StartupRoleSelectionViewModel(_languageService, _appSettingsService);
        return new StartupRoleSelectionWindow(viewModel);
    }

    public MemberRootFolderWindow CreateMemberRootFolderWindow()
    {
        var viewModel = new MemberRootFolderViewModel(_languageService, _appSettingsService, _folderService);
        return new MemberRootFolderWindow(viewModel);
    }

    public UnifiedLoginWindow CreateUnifiedLoginWindow(string? clientId, ClientProfileRecord? ownerProfile)
    {
        var viewModel = new UnifiedLoginViewModel(
            _languageService,
            _unifiedLoginService,
            _businessUserSessionService,
            _appSettingsService,
            clientId,
            ownerProfile);
        return new UnifiedLoginWindow(viewModel);
    }
}
