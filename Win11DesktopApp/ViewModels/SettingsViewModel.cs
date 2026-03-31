using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.ViewModels
{
    public class CompanyVisibilityItem : ViewModelBase
    {
        private readonly CompanyService _companyService;

        public EmployerCompany Company { get; }
        public string Name => Company.Name;
        public int ActiveEmployeeCount { get; }
        public bool HasActiveEmployees => ActiveEmployeeCount > 0;

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                if (!value && HasActiveEmployees)
                {
                    // block hiding if active employees exist
                    OnPropertyChanged();
                    return;
                }
                _isVisible = value;
                _companyService.SetCompanyVisible(Company, value);
                OnPropertyChanged();
            }
        }

        public CompanyVisibilityItem(EmployerCompany company, CompanyService companyService)
        {
            Company = company;
            _companyService = companyService;
            ActiveEmployeeCount = companyService.GetActiveEmployeeCount(company);
            _isVisible = companyService.IsCompanyVisible(company);
        }
    }

    public class SettingsViewModel : ViewModelBase
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly ThemeService _themeService;
        private readonly CompanyService? _companyService;
        private readonly ProfileAuthService _profileAuthService;
        private readonly ProfileSessionService _profileSessionService;

        public ICommand GoBackCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand ChangeThemeCommand { get; }
        public ICommand SelectRootFolderCommand { get; }
        public ICommand OpenTagVisibilityCommand { get; }
        public ICommand OpenCompanyVisibilityCommand { get; }
        public ICommand CloseCompanyVisibilityCommand { get; }
        public ICommand ChangeInterfaceSizeCommand { get; }
        public ICommand ChangeTextSizeCommand { get; }
        public ICommand ChangeDocLanguageCommand { get; }
        public ICommand TestGeminiCommand { get; }
        public ICommand OpenGeminiSiteCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand ChangeProfilePasswordCommand { get; }
        public ICommand EditProfileCommand { get; }
        public ICommand CancelProfileEditCommand { get; }

        private bool _isCompanyVisibilityOpen;
        public bool IsCompanyVisibilityOpen
        {
            get => _isCompanyVisibilityOpen;
            set => SetProperty(ref _isCompanyVisibilityOpen, value);
        }

        public ObservableCollection<CompanyVisibilityItem> CompanyVisibilityItems { get; } = new();

        private string _updateStatus = "";
        public string UpdateStatus
        {
            get => _updateStatus;
            set => SetProperty(ref _updateStatus, value);
        }

        private bool _isUpdateChecking;
        public bool IsUpdateChecking
        {
            get => _isUpdateChecking;
            set => SetProperty(ref _isUpdateChecking, value);
        }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        private Velopack.UpdateInfo? _pendingUpdate;
        public ICommand InstallUpdateCommand { get; }
        private string _profileFirstName = string.Empty;
        private string _profileLastName = string.Empty;
        private bool _profileRememberMeEnabled;
        private string _profileCurrentPassword = string.Empty;
        private string _profileNewPassword = string.Empty;
        private string _profileConfirmPassword = string.Empty;
        private string _profileStatusMessage = string.Empty;
        private bool _profileStatusIsError;
        private bool _isInitializingProfileFields;
        private bool _isSyncingRememberMe;
        private bool _isProfileEditMode;
        private bool _isEditingGeminiApiKey;
        private string _geminiApiKeyDraft = string.Empty;

        public string RootFolderPath
        {
            get => _appSettingsService.Settings.RootFolderPath;
            set
            {
                if (_appSettingsService.Settings.RootFolderPath != value)
                {
                    var oldPath = _appSettingsService.Settings.RootFolderPath;
                    _appSettingsService.Settings.RootFolderPath = value;
                    _appSettingsService.SaveSettings();
                    App.ActivityLogService?.Log("RootFolderChanged", "Settings", "", "",
                        $"Змінено кореневу папку", oldPath ?? "", value);
                    OnPropertyChanged();
                }
            }
        }

        public string AppVersion => AppSettingsService.CurrentAppVersion;

        public string AccessStatusTitle => App.AccessStatusService?.Title ?? string.Empty;
        public string AccessStatusDetail => App.AccessStatusService?.Detail ?? string.Empty;
        public string AccessStatusAdminMessage => App.AccessStatusService?.AdminMessage ?? string.Empty;
        public bool HasAccessStatusAdminMessage => App.AccessStatusService?.HasAdminMessage == true;
        public string AccessStatusSeverity => App.AccessStatusService?.Severity ?? "Info";
        public string MachineId => Services.LicenseService.GetMachineId();
        public bool HasProfile => App.CurrentProfile != null;
        public string ProfileClientId => App.CurrentProfile?.ClientId ?? string.Empty;

        public string ProfileFirstName
        {
            get => _profileFirstName;
            set => SetProperty(ref _profileFirstName, value);
        }

        public string ProfileLastName
        {
            get => _profileLastName;
            set => SetProperty(ref _profileLastName, value);
        }

        public bool ProfileRememberMeEnabled
        {
            get => _profileRememberMeEnabled;
            set
            {
                if (!SetProperty(ref _profileRememberMeEnabled, value))
                    return;

                if (_isInitializingProfileFields || _isSyncingRememberMe || App.CurrentProfile == null)
                    return;

                _ = SyncRememberMeAsync(value);
            }
        }

        public string ProfileCurrentPassword
        {
            get => _profileCurrentPassword;
            set => SetProperty(ref _profileCurrentPassword, value);
        }

        public string ProfileNewPassword
        {
            get => _profileNewPassword;
            set => SetProperty(ref _profileNewPassword, value);
        }

        public string ProfileConfirmPassword
        {
            get => _profileConfirmPassword;
            set => SetProperty(ref _profileConfirmPassword, value);
        }

        public string ProfileStatusMessage
        {
            get => _profileStatusMessage;
            set => SetProperty(ref _profileStatusMessage, value);
        }

        public bool ProfileStatusIsError
        {
            get => _profileStatusIsError;
            set => SetProperty(ref _profileStatusIsError, value);
        }

        public bool IsProfileEditMode
        {
            get => _isProfileEditMode;
            set => SetProperty(ref _isProfileEditMode, value);
        }

        public string GeminiApiKey
        {
            get => _appSettingsService.Settings.GeminiApiKey;
            set
            {
                if (_appSettingsService.Settings.GeminiApiKey != value)
                {
                    _appSettingsService.Settings.GeminiApiKey = value;
                    _appSettingsService.SaveSettings();
                    App.GeminiApiService?.SetApiKey(value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasGeminiApiKey));
                    OnPropertyChanged(nameof(GeminiApiKeyMaskedDisplay));
                    OnPropertyChanged(nameof(ShowMaskedGeminiApiKey));
                    OnPropertyChanged(nameof(ShowGeminiApiKeyEditor));
                    OnPropertyChanged(nameof(IsGeminiConfigured));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool HasGeminiApiKey => !string.IsNullOrWhiteSpace(_appSettingsService.Settings.GeminiApiKey);

        public string GeminiApiKeyMaskedDisplay => HasGeminiApiKey ? "************" : string.Empty;

        public bool IsEditingGeminiApiKey
        {
            get => _isEditingGeminiApiKey;
            set
            {
                if (!SetProperty(ref _isEditingGeminiApiKey, value))
                    return;

                OnPropertyChanged(nameof(ShowMaskedGeminiApiKey));
                OnPropertyChanged(nameof(ShowGeminiApiKeyEditor));
            }
        }

        public bool ShowMaskedGeminiApiKey => HasGeminiApiKey && !IsEditingGeminiApiKey;

        public bool ShowGeminiApiKeyEditor => !HasGeminiApiKey || IsEditingGeminiApiKey;

        public string GeminiApiKeyDraft
        {
            get => _geminiApiKeyDraft;
            set => SetProperty(ref _geminiApiKeyDraft, value);
        }

        public bool IsGeminiConfigured => App.GeminiApiService?.IsConfigured ?? false;

        public string[] GeminiModels => Services.GeminiApiService.AvailableModels;

        public string GeminiModel
        {
            get => _appSettingsService.Settings.GeminiModel;
            set
            {
                if (_appSettingsService.Settings.GeminiModel != value)
                {
                    _appSettingsService.Settings.GeminiModel = value;
                    _appSettingsService.SaveSettings();
                    App.GeminiApiService?.SetModel(value);
                    OnPropertyChanged();
                }
            }
        }

        private string _geminiTestResult = "";
        public string GeminiTestResult
        {
            get => _geminiTestResult;
            set => SetProperty(ref _geminiTestResult, value);
        }

        private string _currentLanguage;
        public string CurrentLanguage
        {
            get => _currentLanguage;
            set => SetProperty(ref _currentLanguage, value);
        }

        private string _currentTheme = "Light";
        public string CurrentTheme
        {
            get => _currentTheme;
            set => SetProperty(ref _currentTheme, value);
        }

        private string _currentInterfaceSize = "Medium";
        public string CurrentInterfaceSize
        {
            get => _currentInterfaceSize;
            set => SetProperty(ref _currentInterfaceSize, value);
        }

        private string _currentTextSize = "Medium";
        public string CurrentTextSize
        {
            get => _currentTextSize;
            set => SetProperty(ref _currentTextSize, value);
        }

        private string _currentDocLanguage = "";
        public string CurrentDocLanguage
        {
            get => _currentDocLanguage;
            set => SetProperty(ref _currentDocLanguage, value);
        }

        public SettingsViewModel(
            AppSettingsService? appSettingsService = null,
            ThemeService? themeService = null,
            CompanyService? companyService = null,
            ProfileAuthService? profileAuthService = null,
            ProfileSessionService? profileSessionService = null)
        {
            _appSettingsService = appSettingsService ?? App.AppSettingsService;
            _themeService = themeService ?? App.ThemeService;
            _companyService = companyService ?? App.CompanyService;
            _profileAuthService = profileAuthService ?? App.ProfileAuthService;
            _profileSessionService = profileSessionService ?? App.ProfileSessionService;

            _currentLanguage = _appSettingsService.Settings.LanguageCode;
            _currentTheme = DetectCurrentTheme();
            _currentInterfaceSize = _appSettingsService.Settings.InterfaceSize ?? "Medium";
            _currentTextSize = _appSettingsService.Settings.TextSize ?? "Medium";
            _currentDocLanguage = _appSettingsService.Settings.DocumentLanguage ?? "";
            _isEditingGeminiApiKey = string.IsNullOrWhiteSpace(_appSettingsService.Settings.GeminiApiKey);
            InitializeProfileFields();

            GoBackCommand = new RelayCommand(o =>
            {
                App.NavigationService.NavigateTo(new MainViewModel());
            });

            ChangeLanguageCommand = new RelayCommand(param =>
            {
                if (param is string code)
                {
                    LanguageService.SetLanguage(code);
                    App.AccessStatusService?.RefreshPresentation();
                    CurrentLanguage = code;
                    OnPropertyChanged(nameof(AccessStatusTitle));
                    OnPropertyChanged(nameof(AccessStatusDetail));
                    OnPropertyChanged(nameof(AccessStatusAdminMessage));
                    OnPropertyChanged(nameof(HasAccessStatusAdminMessage));
                    OnPropertyChanged(nameof(AccessStatusSeverity));
                }
            });

            ChangeThemeCommand = new RelayCommand(param =>
            {
                if (param is string theme)
                {
                    _themeService.SetTheme(theme);
                    CurrentTheme = theme;
                }
            });

            SelectRootFolderCommand = new RelayCommand(o =>
            {
                var dialog = new OpenFolderDialog();
                if (dialog.ShowDialog() == true)
                {
                    RootFolderPath = dialog.FolderName;
                }
            });

            OpenTagVisibilityCommand = new RelayCommand(o =>
            {
                var window = new TagVisibilityWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                window.ShowDialog();
            });

            OpenCompanyVisibilityCommand = new RelayCommand(o =>
            {
                CompanyVisibilityItems.Clear();
                if (_companyService != null)
                {
                    foreach (var company in _companyService.Companies)
                        CompanyVisibilityItems.Add(new CompanyVisibilityItem(company, _companyService));
                }
                IsCompanyVisibilityOpen = true;
            });

            CloseCompanyVisibilityCommand = new RelayCommand(o =>
            {
                IsCompanyVisibilityOpen = false;
            });

            ChangeInterfaceSizeCommand = new RelayCommand(param =>
            {
                if (param is string size)
                {
                    CurrentInterfaceSize = size;
                    _appSettingsService.Settings.InterfaceSize = size;
                    _appSettingsService.SaveSettings();
                    ApplyInterfaceSize(size);
                }
            });

            ChangeTextSizeCommand = new RelayCommand(param =>
            {
                if (param is string size)
                {
                    CurrentTextSize = size;
                    _appSettingsService.Settings.TextSize = size;
                    _appSettingsService.SaveSettings();
                    ApplyTextSize(size);
                }
            });

            ChangeDocLanguageCommand = new RelayCommand(param =>
            {
                if (param is string lang)
                {
                    CurrentDocLanguage = lang;
                    _appSettingsService.Settings.DocumentLanguage = lang;
                    _appSettingsService.SaveSettings();
                    App.DocumentLocalizationService.LoadLanguage(lang);
                }
            });

            TestGeminiCommand = new RelayCommand(async o =>
            {
                GeminiTestResult = "Testing...";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (App.GeminiApiService == null) { GeminiTestResult = "Service unavailable"; return; }
                var (success, msg) = await App.GeminiApiService.TestConnectionAsync(cts.Token);
                GeminiTestResult = msg;
            }, o => IsGeminiConfigured);

            OpenGeminiSiteCommand = new RelayCommand(o =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aistudio.google.com/apikey") { UseShellExecute = true }); }
                catch (Exception ex) { LoggingService.LogWarning("Settings.OpenGeminiSite", ex.Message); }
            });

            CheckForUpdatesCommand = new RelayCommand(async o =>
            {
                IsUpdateChecking = true;
                IsUpdateAvailable = false;
                _pendingUpdate = null;
                UpdateStatus = Res("UpdateChecking");
                try
                {
                    var update = await Services.UpdateService.CheckForUpdatesAsync(PolicyService.CurrentPolicy.UpdateChannel);
                    if (update != null)
                    {
                        _pendingUpdate = update;
                        IsUpdateAvailable = true;
                        UpdateStatus = $"{Res("UpdateAvailable")} v{update.TargetFullRelease.Version}";
                    }
                    else
                    {
                        UpdateStatus = Res("UpdateLatest");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus = $"{Res("UpdateError")}: {ex.Message}";
                }
                IsUpdateChecking = false;
            });

            InstallUpdateCommand = new RelayCommand(async o =>
            {
                if (_pendingUpdate == null) return;
                IsUpdateChecking = true;
                UpdateStatus = Res("UpdateDownloading");
                var errorMessage = await Services.UpdateService.DownloadAndApplyAsync(
                    _pendingUpdate,
                    progress =>
                    {
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            UpdateStatus = $"{Res("UpdateDownloading")} {progress}%"));
                    },
                    PolicyService.CurrentPolicy.UpdateChannel);
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    UpdateStatus = $"{Res("UpdateError")}: {errorMessage}";
                    IsUpdateChecking = false;
                }
            });

            SaveProfileCommand = new AsyncRelayCommand(_ => SaveProfileAsync(), _ => HasProfile);
            ChangeProfilePasswordCommand = new AsyncRelayCommand(_ => ChangeProfilePasswordAsync(), _ => HasProfile);
            EditProfileCommand = new RelayCommand(_ =>
            {
                InitializeProfileFields();
                ClearProfilePasswordFields();
                SetProfileStatus(string.Empty, false);
                IsProfileEditMode = true;
            }, _ => HasProfile);
            CancelProfileEditCommand = new RelayCommand(_ =>
            {
                InitializeProfileFields();
                ClearProfilePasswordFields();
                SetProfileStatus(string.Empty, false);
                IsProfileEditMode = false;
            }, _ => HasProfile);
        }

        public static double GetInterfaceSizeMultiplier(string size) => size switch
        {
            "Small" => 0.74,
            "Medium" => 0.87,
            "Large" => 1.0,
            "ExtraLarge" => 1.14,
            _ => 0.87
        };

        public static double GetTextSizeMultiplier(string size) => size switch
        {
            "Small" => 0.82,
            "Medium" => 1.0,
            "Large" => 1.18,
            "ExtraLarge" => 1.36,
            _ => 1.0
        };

        private static readonly int[] FontSizeKeys = { 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 26, 28, 32, 42 };

        private void ApplyInterfaceSize(string size)
        {
            if (Application.Current.MainWindow is MainWindow mw)
                mw.InterfaceSizeMultiplier = GetInterfaceSizeMultiplier(size);
        }

        public static void ApplyTextSize(string size)
        {
            double mult = GetTextSizeMultiplier(size);
            foreach (int baseSize in FontSizeKeys)
            {
                Application.Current.Resources[$"FS{baseSize}"] = Math.Round(baseSize * mult, 1);
            }
        }

        private string DetectCurrentTheme()
        {
            var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
            foreach (var d in dicts)
            {
                if (d.Source != null && d.Source.OriginalString.Contains("Resources/Themes/Theme."))
                {
                    var name = d.Source.OriginalString;
                    if (name.Contains("Light")) return "Light";
                    if (name.Contains("DarkWord")) return "DarkWord";
                    if (name.Contains("Dark2")) return "Dark2";
                    if (name.Contains("Custom")) return "Custom";
                }
            }
            return "Light";
        }

        public void BeginGeminiApiKeyEdit()
        {
            GeminiApiKeyDraft = string.Empty;
            IsEditingGeminiApiKey = true;
        }

        public void CommitGeminiApiKeyEdit()
        {
            var newValue = GeminiApiKeyDraft?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(newValue))
                GeminiApiKey = newValue;

            GeminiApiKeyDraft = string.Empty;
            if (HasGeminiApiKey)
                IsEditingGeminiApiKey = false;
        }

        public void CancelGeminiApiKeyEdit()
        {
            GeminiApiKeyDraft = string.Empty;
            if (HasGeminiApiKey)
                IsEditingGeminiApiKey = false;
        }

        private void InitializeProfileFields()
        {
            _isInitializingProfileFields = true;
            try
            {
                if (App.CurrentProfile == null)
                {
                    ProfileFirstName = string.Empty;
                    ProfileLastName = string.Empty;
                    ProfileRememberMeEnabled = false;
                    return;
                }

                ProfileFirstName = App.CurrentProfile.FirstName;
                ProfileLastName = App.CurrentProfile.LastName;
                ProfileRememberMeEnabled = App.CurrentProfile.RememberMeEnabled;
            }
            finally
            {
                _isInitializingProfileFields = false;
            }
        }

        private async System.Threading.Tasks.Task SaveProfileAsync()
        {
            if (App.CurrentProfile == null)
            {
                SetProfileStatus(Res("SettingsProfileNotLoadedError"), true);
                return;
            }

            if (!ProfileAuthService.IsValidProfileName(ProfileFirstName))
            {
                SetProfileStatus(Res("ProfileErrFirstNameLatin"), true);
                return;
            }

            if (!ProfileAuthService.IsValidProfileName(ProfileLastName))
            {
                SetProfileStatus(Res("ProfileErrLastNameLatin"), true);
                return;
            }

            var nameResult = await _profileAuthService.UpdateProfileNameAsync(
                App.CurrentProfile.ClientId,
                ProfileFirstName,
                ProfileLastName);

            if (!nameResult.Success || nameResult.Profile == null)
            {
                SetProfileStatus(nameResult.ErrorMessage, true);
                return;
            }

            var rememberResult = await _profileAuthService.UpdateRememberMeAsync(
                nameResult.Profile.ClientId,
                ProfileRememberMeEnabled);

            if (!rememberResult.Success || rememberResult.Profile == null)
            {
                SetProfileStatus(rememberResult.ErrorMessage, true);
                return;
            }

            UpdateCurrentProfile(rememberResult.Profile);

            if (ProfileRememberMeEnabled)
                _profileSessionService.SaveRememberedSession(rememberResult.Profile);
            else
                _profileSessionService.ClearRememberedSession();

            SetProfileStatus(Res("SettingsProfileUpdated"), false);
        }

        private async System.Threading.Tasks.Task ChangeProfilePasswordAsync()
        {
            if (App.CurrentProfile == null)
            {
                SetProfileStatus(Res("SettingsProfileNotLoadedError"), true);
                return;
            }

            if (string.IsNullOrWhiteSpace(ProfileCurrentPassword))
            {
                SetProfileStatus(Res("ProfileErrCurrentPasswordRequired"), true);
                return;
            }

            if (string.IsNullOrWhiteSpace(ProfileNewPassword))
            {
                SetProfileStatus(Res("ProfileErrNewPasswordRequired"), true);
                return;
            }

            if (ProfileNewPassword.Length < 6)
            {
                SetProfileStatus(Res("ProfileErrPasswordMinLength"), true);
                return;
            }

            if (!string.Equals(ProfileNewPassword, ProfileConfirmPassword, StringComparison.Ordinal))
            {
                SetProfileStatus(Res("ProfileErrPasswordsMismatch"), true);
                return;
            }

            var result = await _profileAuthService.ChangePasswordAsync(
                App.CurrentProfile.ClientId,
                ProfileCurrentPassword,
                ProfileNewPassword);

            if (!result.Success || result.Profile == null)
            {
                SetProfileStatus(result.ErrorMessage, true);
                return;
            }

            UpdateCurrentProfile(result.Profile);

            if (ProfileRememberMeEnabled)
                _profileSessionService.SaveRememberedSession(result.Profile);
            else
                _profileSessionService.ClearRememberedSession();

            ProfileCurrentPassword = string.Empty;
            ProfileNewPassword = string.Empty;
            ProfileConfirmPassword = string.Empty;
            SetProfileStatus(Res("SettingsProfilePasswordChanged"), false);
        }

        private void UpdateCurrentProfile(ClientProfileRecord profile)
        {
            App.SetCurrentProfile(profile);
            InitializeProfileFields();
            OnPropertyChanged(nameof(HasProfile));
            OnPropertyChanged(nameof(ProfileClientId));
        }

        private async System.Threading.Tasks.Task SyncRememberMeAsync(bool enabled)
        {
            if (App.CurrentProfile == null)
                return;

            var currentProfile = App.CurrentProfile;
            _isSyncingRememberMe = true;

            try
            {
                var result = await _profileAuthService.UpdateRememberMeAsync(currentProfile.ClientId, enabled);
                if (!result.Success || result.Profile == null)
                {
                    _isInitializingProfileFields = true;
                    ProfileRememberMeEnabled = currentProfile.RememberMeEnabled;
                    _isInitializingProfileFields = false;
                    SetProfileStatus(result.ErrorMessage, true);
                    return;
                }

                UpdateCurrentProfile(result.Profile);

                if (enabled)
                    _profileSessionService.SaveRememberedSession(result.Profile);
                else
                    _profileSessionService.ClearRememberedSession();

                SetProfileStatus(Res("SettingsProfileRememberUpdated"), false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.SyncRememberMe", ex.Message);
                _isInitializingProfileFields = true;
                ProfileRememberMeEnabled = currentProfile.RememberMeEnabled;
                _isInitializingProfileFields = false;
                SetProfileStatus(Res("SettingsProfileRememberUpdateFailed"), true);
            }
            finally
            {
                _isSyncingRememberMe = false;
            }
        }

        private void SetProfileStatus(string message, bool isError)
        {
            ProfileStatusMessage = message;
            ProfileStatusIsError = isError;
        }

        private void ClearProfilePasswordFields()
        {
            ProfileCurrentPassword = string.Empty;
            ProfileNewPassword = string.Empty;
            ProfileConfirmPassword = string.Empty;
        }
    }
}
