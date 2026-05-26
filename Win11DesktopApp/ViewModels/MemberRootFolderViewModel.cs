using System;
using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels;

public sealed class MemberRootFolderViewModel : ViewModelBase
{
    private readonly LanguageService _languageService;
    private readonly AppSettingsService _appSettingsService;
    private readonly FolderService _folderService;

    private string _rootFolderPath = string.Empty;
    private string _errorMessage = string.Empty;
    private string _currentLanguage = "uk";

    public event Action<bool, string?>? RequestClose;

    public ICommand BrowseCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand CancelCommand { get; }

    public string RootFolderPath
    {
        get => _rootFolderPath;
        set => SetProperty(ref _rootFolderPath, value);
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

    public MemberRootFolderViewModel(
        LanguageService languageService,
        AppSettingsService appSettingsService,
        FolderService folderService)
    {
        _languageService = languageService;
        _appSettingsService = appSettingsService;
        _folderService = folderService;
        _currentLanguage = _appSettingsService.Settings.LanguageCode ?? "uk";
        _rootFolderPath = _appSettingsService.Settings.RootFolderPath ?? string.Empty;

        BrowseCommand = new RelayCommand(_ => BrowseFolder());
        ContinueCommand = new RelayCommand(_ => Continue(), _ => !string.IsNullOrWhiteSpace(RootFolderPath));
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false, null));
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrWhiteSpace(RootFolderPath) && System.IO.Directory.Exists(RootFolderPath))
            dialog.InitialDirectory = RootFolderPath;

        if (dialog.ShowDialog() == true)
            RootFolderPath = dialog.FolderName;
    }

    private void Continue()
    {
        ErrorMessage = string.Empty;
        var path = RootFolderPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            ErrorMessage = Res("MemberRootFolderRequired");
            return;
        }

        if (!System.IO.Directory.Exists(path))
        {
            ErrorMessage = Res("MemberRootFolderRequired");
            return;
        }

        _appSettingsService.Settings.RootFolderPath = path;
        _appSettingsService.SaveSettings();
        var passportResult = _folderService.EnsureWorkspacePassport(path, allowCreate: false);
        if (passportResult.HasConflict)
        {
            ErrorMessage = Res("WorkspacePassportConflictError");
            return;
        }

        if (!passportResult.Success)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(passportResult.Message)
                ? Res("WorkspacePassportMissingError")
                : passportResult.Message;
            return;
        }

        RequestClose?.Invoke(true, path);
    }
}
