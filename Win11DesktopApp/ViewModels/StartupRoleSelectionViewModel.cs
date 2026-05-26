using System;
using System.Windows.Input;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels;

public enum StartupRoleChoice
{
    Owner,
    Member
}

public sealed class StartupRoleSelectionViewModel : ViewModelBase
{
    private readonly LanguageService _languageService;
    private readonly AppSettingsService _appSettingsService;
    private string _currentLanguage = "uk";

    public event Action<StartupRoleChoice?>? RequestClose;

    public ICommand ChooseOwnerCommand { get; }
    public ICommand ChooseMemberCommand { get; }
    public ICommand CancelCommand { get; }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (SetProperty(ref _currentLanguage, value))
                _languageService.SetLanguage(value);
        }
    }

    public StartupRoleSelectionViewModel(
        LanguageService languageService,
        AppSettingsService appSettingsService)
    {
        _languageService = languageService;
        _appSettingsService = appSettingsService;
        _currentLanguage = _appSettingsService.Settings.LanguageCode ?? "uk";

        ChooseOwnerCommand = new RelayCommand(_ => RequestClose?.Invoke(StartupRoleChoice.Owner));
        ChooseMemberCommand = new RelayCommand(_ => RequestClose?.Invoke(StartupRoleChoice.Member));
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(null));
    }
}
