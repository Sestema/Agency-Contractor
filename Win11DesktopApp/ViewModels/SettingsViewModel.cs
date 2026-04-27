using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.Telegram;
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

    public class SettingsViewModel : ViewModelBase, ICleanable
    {
        private readonly NavigationService _navigationService;
        private readonly AppSettingsService _appSettingsService;
        private readonly ThemeService _themeService;
        private readonly LanguageService _languageService;
        private readonly CompanyService _companyService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly ProfileAuthService _profileAuthService;
        private readonly ProfileSessionService _profileSessionService;
        private readonly AccessStatusService _accessStatusService;
        private readonly ActivityLogService _activityLogService;
        private readonly GeminiApiService _geminiApiService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly CurrentProfileService _currentProfileService;
        private readonly GeminiApiKeyConfigurationService _geminiApiKeyConfigurationService;
        private readonly TelegramBotService _telegramBotService;
        private readonly TelegramPairingService _telegramPairingService;

        public ICommand GoBackCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand ChangeThemeCommand { get; }
        public ICommand ChangeAccentColorCommand { get; }
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
        public ICommand ConnectTelegramBotCommand { get; }
        public ICommand DisconnectTelegramBotCommand { get; }
        public ICommand GenerateTelegramQrCommand { get; }
        public ICommand RemoveTelegramAuthorizedUserCommand { get; }
        public ICommand OpenBotFatherCommand { get; }

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
        private bool _telegramEnabled;
        private bool _telegramIsBusy;
        private string _telegramBotTokenInput = string.Empty;
        private string _telegramBotStatus = string.Empty;
        private BitmapImage? _telegramQrCodeImage;
        private string _telegramPairingCode = string.Empty;
        private string _telegramPairingExpiryText = string.Empty;

        public ObservableCollection<TelegramAuthorizedUser> TelegramAuthorizedUsers { get; } = new();

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
                    _activityLogService.Log("RootFolderChanged", "Settings", "", "",
                        $"Змінено кореневу папку", oldPath ?? "", value);
                    OnPropertyChanged();
                }
            }
        }

        public string AppVersion => AppSettingsService.CurrentAppVersion;

        public bool TelegramEnabled
        {
            get => _telegramEnabled;
            set
            {
                if (!SetProperty(ref _telegramEnabled, value))
                    return;

                _appSettingsService.Settings.Telegram.Enabled = value;
                _appSettingsService.SaveSettings();

                if (!value)
                {
                    _telegramBotService.Stop();
                    TelegramBotStatus = "Telegram-бот вимкнений.";
                }
                else if (HasTelegramBotToken)
                {
                    _ = RestartTelegramBotAsync();
                }
                else
                {
                    TelegramBotStatus = "Вставте токен від BotFather, щоб активувати бота.";
                }

                OnPropertyChanged(nameof(TelegramNeedsToken));
                OnPropertyChanged(nameof(TelegramCanGenerateQr));
            }
        }

        public bool TelegramNeedsToken => TelegramEnabled && !HasTelegramBotToken;

        public bool TelegramIsBusy
        {
            get => _telegramIsBusy;
            set => SetProperty(ref _telegramIsBusy, value);
        }

        public string TelegramBotTokenInput
        {
            get => _telegramBotTokenInput;
            set => SetProperty(ref _telegramBotTokenInput, value);
        }

        public string TelegramBotStatus
        {
            get => _telegramBotStatus;
            set => SetProperty(ref _telegramBotStatus, value);
        }

        public string TelegramBotUsername => _appSettingsService.Settings.Telegram.BotUsername;

        public bool HasTelegramBotToken => !string.IsNullOrWhiteSpace(_appSettingsService.Settings.Telegram.EncryptedBotToken);

        public bool TelegramIsConnected => TelegramEnabled && HasTelegramBotToken;

        public BitmapImage? TelegramQrCodeImage
        {
            get => _telegramQrCodeImage;
            set => SetProperty(ref _telegramQrCodeImage, value);
        }

        public bool HasTelegramQrCode => TelegramQrCodeImage != null;

        public string TelegramPairingCode
        {
            get => _telegramPairingCode;
            set => SetProperty(ref _telegramPairingCode, value);
        }

        public string TelegramPairingExpiryText
        {
            get => _telegramPairingExpiryText;
            set => SetProperty(ref _telegramPairingExpiryText, value);
        }

        public bool HasTelegramAuthorizedUsers => TelegramAuthorizedUsers.Count > 0;

        public bool TelegramAllowAiQuestions
        {
            get => _appSettingsService.Settings.Telegram.AllowAiQuestions;
            set
            {
                if (_appSettingsService.Settings.Telegram.AllowAiQuestions == value)
                    return;

                _appSettingsService.Settings.Telegram.AllowAiQuestions = value;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        public bool TelegramDailyDigestEnabled
        {
            get => _appSettingsService.Settings.Telegram.DailyDigestEnabled;
            set
            {
                if (_appSettingsService.Settings.Telegram.DailyDigestEnabled == value)
                    return;

                _appSettingsService.Settings.Telegram.DailyDigestEnabled = value;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        public string TelegramDailyDigestTime
        {
            get => _appSettingsService.Settings.Telegram.DailyDigestTime;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "08:00" : value.Trim();
                if (_appSettingsService.Settings.Telegram.DailyDigestTime == normalized)
                    return;

                _appSettingsService.Settings.Telegram.DailyDigestTime = normalized;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        public bool TelegramCanGenerateQr => TelegramIsConnected && !string.IsNullOrWhiteSpace(TelegramBotUsername);

        public string AccessStatusTitle => _accessStatusService.Title ?? string.Empty;
        public string AccessStatusDetail => _accessStatusService.Detail ?? string.Empty;
        public string AccessStatusAdminMessage => _accessStatusService.AdminMessage ?? string.Empty;
        public bool HasAccessStatusAdminMessage => _accessStatusService.HasAdminMessage;
        public string AccessPlanCode => NormalizeAccessPlan(_accessStatusService.Plan);
        public string AccessPlanDisplay => FormatPlanDisplay(AccessPlanCode);
        public bool HasAccessPlan => !string.IsNullOrWhiteSpace(AccessPlanCode);
        public string AccessStatusSeverity => _accessStatusService.Severity ?? "Info";
        public string MachineId => Services.LicenseService.GetMachineId();
        public bool HasProfile => _currentProfileService.CurrentProfile != null;
        public string ProfileClientId => _currentProfileService.CurrentProfile?.ClientId ?? string.Empty;

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

                if (_isInitializingProfileFields || _isSyncingRememberMe || _currentProfileService.CurrentProfile == null)
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
                    _geminiApiKeyConfigurationService.RefreshEffectiveApiKey();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasGeminiApiKey));
                    OnPropertyChanged(nameof(GeminiApiKeyMaskedDisplay));
                    OnPropertyChanged(nameof(ShowMaskedGeminiApiKey));
                    OnPropertyChanged(nameof(ShowGeminiApiKeyEditor));
                    OnPropertyChanged(nameof(IsGeminiConfigured));
                    OnPropertyChanged(nameof(IsManagedGeminiKeyActive));
                    OnPropertyChanged(nameof(ShowGeminiAccessHint));
                    OnPropertyChanged(nameof(GeminiAccessHint));
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

        public bool IsGeminiConfigured => _geminiApiService.IsConfigured;

        public bool IsManagedGeminiKeyActive =>
            !HasGeminiApiKey
            && !PolicyService.IsAIDisabled
            && _geminiApiService.IsConfigured;

        public bool ShowGeminiAccessHint => !HasGeminiApiKey;

        public string GeminiAccessHint => PolicyService.IsAIDisabled
            ? Res("AIGeminiDisabledByPolicy")
            : IsManagedGeminiKeyActive
                ? Res("AIGeminiManagedKeyActive")
                : Res("AIGeminiKeyMissingOrAdmin");

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
                    _geminiApiService.SetModel(value);
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

        public ObservableCollection<AccentPresetItem> AccentPresets { get; } = new();

        public string CurrentAccentColor
        {
            get => _appSettingsService.Settings.AccentColor ?? string.Empty;
            set
            {
                var normalized = value ?? string.Empty;
                if (_appSettingsService.Settings.AccentColor != normalized)
                {
                    _themeService.SetAccentColor(normalized);
                    OnPropertyChanged(nameof(CurrentAccentColor));
                    RefreshAccentSelection();
                }
            }
        }

        private void InitializeAccentPresets()
        {
            AccentPresets.Clear();
            foreach (var preset in ThemeService.AccentPresets)
            {
                AccentPresets.Add(new AccentPresetItem(preset.Name, preset.Hex));
            }
            RefreshAccentSelection();
        }

        private void RefreshAccentSelection()
        {
            var current = CurrentAccentColor ?? string.Empty;
            foreach (var item in AccentPresets)
            {
                item.IsSelected = string.Equals(item.Hex ?? string.Empty, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        public class AccentPresetItem : ViewModelBase
        {
            public AccentPresetItem(string name, string hex)
            {
                Name = name;
                Hex = hex ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(Hex))
                {
                    try
                    {
                        var obj = System.Windows.Media.ColorConverter.ConvertFromString(Hex);
                        if (obj is System.Windows.Media.Color c)
                        {
                            var brush = new System.Windows.Media.SolidColorBrush(c);
                            brush.Freeze();
                            Brush = brush;
                        }
                    }
                    catch { }
                }
                Brush ??= System.Windows.Media.Brushes.Transparent;
            }

            public string Name { get; }
            public string Hex { get; }
            public System.Windows.Media.Brush Brush { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set => SetProperty(ref _isSelected, value);
            }

            public bool IsDefault => string.IsNullOrEmpty(Hex);
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
            NavigationService? navigationService = null,
            AppSettingsService? appSettingsService = null,
            ThemeService? themeService = null,
            LanguageService? languageService = null,
            CompanyService? companyService = null,
            TagCatalogService? tagCatalogService = null,
            ProfileAuthService? profileAuthService = null,
            ProfileSessionService? profileSessionService = null,
            AccessStatusService? accessStatusService = null,
            ActivityLogService? activityLogService = null,
            GeminiApiService? geminiApiService = null,
            DocumentLocalizationService? documentLocalizationService = null,
            CurrentProfileService? currentProfileService = null,
            GeminiApiKeyConfigurationService? geminiApiKeyConfigurationService = null,
            TelegramBotService? telegramBotService = null,
            TelegramPairingService? telegramPairingService = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _themeService = themeService ?? throw new InvalidOperationException("ThemeService is not initialized.");
            _languageService = languageService ?? throw new InvalidOperationException("LanguageService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _profileAuthService = profileAuthService ?? throw new InvalidOperationException("ProfileAuthService is not initialized.");
            _profileSessionService = profileSessionService ?? throw new InvalidOperationException("ProfileSessionService is not initialized.");
            _accessStatusService = accessStatusService ?? throw new InvalidOperationException("AccessStatusService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _geminiApiService = geminiApiService ?? throw new InvalidOperationException("GeminiApiService is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");
            _currentProfileService = currentProfileService ?? throw new InvalidOperationException("CurrentProfileService is not initialized.");
            _geminiApiKeyConfigurationService = geminiApiKeyConfigurationService ?? throw new InvalidOperationException("GeminiApiKeyConfigurationService is not initialized.");
            _telegramBotService = telegramBotService ?? throw new InvalidOperationException("TelegramBotService is not initialized.");
            _telegramPairingService = telegramPairingService ?? throw new InvalidOperationException("TelegramPairingService is not initialized.");

            _currentLanguage = _appSettingsService.Settings.LanguageCode;
            _currentTheme = DetectCurrentTheme();
            _currentInterfaceSize = _appSettingsService.Settings.InterfaceSize ?? "Medium";
            _currentTextSize = _appSettingsService.Settings.TextSize ?? "Medium";
            _currentDocLanguage = _appSettingsService.Settings.DocumentLanguage ?? "";
            _isEditingGeminiApiKey = string.IsNullOrWhiteSpace(_appSettingsService.Settings.GeminiApiKey);
            _telegramEnabled = _appSettingsService.Settings.Telegram.Enabled;
            InitializeProfileFields();
            InitializeAccentPresets();
            RefreshTelegramState();
            _accessStatusService.PropertyChanged += AccessStatusService_PropertyChanged;
            _telegramBotService.StateChanged += TelegramBotService_StateChanged;

            GoBackCommand = new RelayCommand(o =>
            {
                _navigationService.NavigateTo<MainViewModel>();
            });

            ChangeLanguageCommand = new RelayCommand(param =>
            {
                if (param is string code)
                {
                    _languageService.SetLanguage(code);
                    _accessStatusService.RefreshPresentation();
                    CurrentLanguage = code;
                    RaiseAccessStatusPropertiesChanged();
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

            ChangeAccentColorCommand = new RelayCommand(param =>
            {
                var hex = param as string ?? string.Empty;
                CurrentAccentColor = hex;
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
                var window = new TagVisibilityWindow(_appSettingsService, _tagCatalogService)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                window.ShowDialog();
            });

            OpenCompanyVisibilityCommand = new RelayCommand(o =>
            {
                CompanyVisibilityItems.Clear();
                foreach (var company in _companyService.Companies)
                    CompanyVisibilityItems.Add(new CompanyVisibilityItem(company, _companyService));
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
                    _documentLocalizationService.LoadLanguage(lang);
                }
            });

            TestGeminiCommand = new RelayCommand(async o =>
            {
                GeminiTestResult = "Testing...";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var (success, msg) = await _geminiApiService.TestConnectionAsync(cts.Token);
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

            ConnectTelegramBotCommand = new AsyncRelayCommand(_ => ConnectTelegramBotAsync(), _ => !TelegramIsBusy && TelegramEnabled);
            DisconnectTelegramBotCommand = new AsyncRelayCommand(_ => DisconnectTelegramBotAsync(), _ => !TelegramIsBusy && HasTelegramBotToken);
            GenerateTelegramQrCommand = new RelayCommand(_ => GenerateTelegramQr(), _ => TelegramCanGenerateQr);
            RemoveTelegramAuthorizedUserCommand = new RelayCommand(param =>
            {
                if (param is long userId)
                {
                    _telegramBotService.RemoveAuthorizedUser(userId);
                    RefreshTelegramState();
                }
            });
            OpenBotFatherCommand = new RelayCommand(_ =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://t.me/BotFather")
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("Settings.OpenBotFather", ex.Message);
                }
            });
        }

        private void AccessStatusService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AccessStatusService.Current)
                or nameof(AccessStatusService.Title)
                or nameof(AccessStatusService.Detail)
                or nameof(AccessStatusService.AdminMessage)
                or nameof(AccessStatusService.HasAdminMessage)
                or nameof(AccessStatusService.Severity)
                or nameof(AccessStatusService.Plan))
            {
                RaiseAccessStatusPropertiesChanged();
            }
        }

        private void RaiseAccessStatusPropertiesChanged()
        {
            OnPropertyChanged(nameof(AccessStatusTitle));
            OnPropertyChanged(nameof(AccessStatusDetail));
            OnPropertyChanged(nameof(AccessStatusAdminMessage));
            OnPropertyChanged(nameof(HasAccessStatusAdminMessage));
            OnPropertyChanged(nameof(AccessStatusSeverity));
            OnPropertyChanged(nameof(AccessPlanCode));
            OnPropertyChanged(nameof(AccessPlanDisplay));
            OnPropertyChanged(nameof(HasAccessPlan));
        }

        private static string NormalizeAccessPlan(string? plan)
        {
            return (plan ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "standard" => "standard",
                "pro" => "pro",
                _ => "trial"
            };
        }

        private static string FormatPlanDisplay(string? plan)
        {
            return NormalizeAccessPlan(plan) switch
            {
                "standard" => "Standard",
                "pro" => "Pro",
                _ => "Trial"
            };
        }

        private void TelegramBotService_StateChanged(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(RefreshTelegramState));
        }

        private void RefreshTelegramState()
        {
            TelegramAuthorizedUsers.Clear();
            foreach (var user in _appSettingsService.Settings.Telegram.AuthorizedUsers
                         .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                TelegramAuthorizedUsers.Add(user);
            }

            SetProperty(ref _telegramEnabled, _appSettingsService.Settings.Telegram.Enabled, nameof(TelegramEnabled));
            TelegramBotStatus = string.IsNullOrWhiteSpace(_telegramBotService.LastStatus)
                ? (HasTelegramBotToken
                    ? $"Підключено: @{TelegramBotUsername}"
                    : "Telegram-бот ще не налаштований.")
                : _telegramBotService.LastStatus;

            if (!HasTelegramQrCode && TelegramCanGenerateQr)
                GenerateTelegramQr();

            OnPropertyChanged(nameof(TelegramBotUsername));
            OnPropertyChanged(nameof(HasTelegramBotToken));
            OnPropertyChanged(nameof(TelegramIsConnected));
            OnPropertyChanged(nameof(TelegramNeedsToken));
            OnPropertyChanged(nameof(TelegramCanGenerateQr));
            OnPropertyChanged(nameof(HasTelegramAuthorizedUsers));
            OnPropertyChanged(nameof(HasTelegramQrCode));
            CommandManager.InvalidateRequerySuggested();
        }

        private async System.Threading.Tasks.Task ConnectTelegramBotAsync()
        {
            var token = TelegramBotTokenInput?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                TelegramBotStatus = "Вставте токен, який BotFather прислав для вашого бота.";
                return;
            }

            TelegramIsBusy = true;
            TelegramBotStatus = "Перевіряю токен і запускаю бота...";
            try
            {
                var result = await _telegramBotService.ConnectAsync(token);
                TelegramBotStatus = result.message;
                if (result.ok)
                {
                    TelegramBotTokenInput = string.Empty;
                    TelegramEnabled = true;
                    GenerateTelegramQr();
                }
            }
            catch (Exception ex)
            {
                TelegramBotStatus = $"Помилка підключення: {ex.Message}";
                LoggingService.LogWarning("Settings.ConnectTelegramBot", ex.Message);
            }
            finally
            {
                TelegramIsBusy = false;
                RefreshTelegramState();
            }
        }

        private async System.Threading.Tasks.Task DisconnectTelegramBotAsync()
        {
            TelegramIsBusy = true;
            TelegramBotStatus = "Відключаю Telegram-бота...";
            try
            {
                await _telegramBotService.DisconnectAsync();
                TelegramQrCodeImage = null;
                TelegramPairingCode = string.Empty;
                TelegramPairingExpiryText = string.Empty;
            }
            catch (Exception ex)
            {
                TelegramBotStatus = $"Помилка відключення: {ex.Message}";
                LoggingService.LogWarning("Settings.DisconnectTelegramBot", ex.Message);
            }
            finally
            {
                TelegramIsBusy = false;
                RefreshTelegramState();
            }
        }

        private async System.Threading.Tasks.Task RestartTelegramBotAsync()
        {
            try
            {
                await _telegramBotService.RestartAsync();
            }
            catch (Exception ex)
            {
                TelegramBotStatus = $"Не вдалося запустити Telegram-бота: {ex.Message}";
                LoggingService.LogWarning("Settings.RestartTelegramBot", ex.Message);
            }
            finally
            {
                RefreshTelegramState();
            }
        }

        private void GenerateTelegramQr()
        {
            if (!TelegramCanGenerateQr)
            {
                TelegramQrCodeImage = null;
                TelegramPairingCode = string.Empty;
                TelegramPairingExpiryText = string.Empty;
                OnPropertyChanged(nameof(HasTelegramQrCode));
                return;
            }

            TelegramPairingCode = _telegramPairingService.GenerateCode();
            var expiresAt = _telegramPairingService.GetExpiryUtc(TelegramPairingCode).ToLocalTime();
            var deepLink = _telegramPairingService.BuildDeepLink(TelegramBotUsername, TelegramPairingCode);
            TelegramQrCodeImage = _telegramPairingService.BuildQrImage(deepLink);
            TelegramPairingExpiryText = $"Код діє до {expiresAt:HH:mm}";
            OnPropertyChanged(nameof(HasTelegramQrCode));
        }

        public void Cleanup()
        {
            _accessStatusService.PropertyChanged -= AccessStatusService_PropertyChanged;
            _telegramBotService.StateChanged -= TelegramBotService_StateChanged;
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
            // Source of truth is the persisted setting — not a scan of merged
            // dictionaries. The old implementation used String.Contains and did
            // not know about Glass / GlassDark, so those themes always fell
            // through to "Light" after a restart (picker mismatched the window).
            var saved = _appSettingsService.Settings.ThemeName;
            return string.IsNullOrWhiteSpace(saved) ? "Light" : saved;
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
                if (_currentProfileService.CurrentProfile == null)
                {
                    ProfileFirstName = string.Empty;
                    ProfileLastName = string.Empty;
                    ProfileRememberMeEnabled = false;
                    return;
                }

                ProfileFirstName = _currentProfileService.CurrentProfile.FirstName;
                ProfileLastName = _currentProfileService.CurrentProfile.LastName;
                ProfileRememberMeEnabled = _currentProfileService.CurrentProfile.RememberMeEnabled;
            }
            finally
            {
                _isInitializingProfileFields = false;
            }
        }

        private async System.Threading.Tasks.Task SaveProfileAsync()
        {
            if (_currentProfileService.CurrentProfile == null)
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
                _currentProfileService.CurrentProfile.ClientId,
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
            if (_currentProfileService.CurrentProfile == null)
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
                _currentProfileService.CurrentProfile.ClientId,
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
            _currentProfileService.SetCurrentProfile(profile);
            InitializeProfileFields();
            OnPropertyChanged(nameof(HasProfile));
            OnPropertyChanged(nameof(ProfileClientId));
        }

        private async System.Threading.Tasks.Task SyncRememberMeAsync(bool enabled)
        {
            if (_currentProfileService.CurrentProfile == null)
                return;

            var currentProfile = _currentProfileService.CurrentProfile;
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
