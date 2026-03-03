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

        private bool _isCompanyVisibilityOpen;
        public bool IsCompanyVisibilityOpen
        {
            get => _isCompanyVisibilityOpen;
            set => SetProperty(ref _isCompanyVisibilityOpen, value);
        }

        public ObservableCollection<CompanyVisibilityItem> CompanyVisibilityItems { get; } = new();


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

        public string LicenseStatus => GetLocalizedLicenseStatus();
        public string MachineId => Services.LicenseService.GetMachineId();

        private string GetLocalizedLicenseStatus()
        {
            try
            {
                var daysLeft = Services.LicenseService.GetDaysLeft();

                if (!Services.LicenseService.IsLicenseValid())
                {
                    if (daysLeft == -1)
                        return Res("LicenseNotActivated");
                    return Res("LicenseExpired");
                }

                if (daysLeft == 99999)
                    return Res("LicenseUnlimited");

                var expires = DateTime.Now.AddDays(daysLeft);
                return $"{Res("LicenseActiveUntil")} {expires:dd.MM.yyyy} ({daysLeft} {Res("LicenseDaysLeft")})";
            }
            catch
            {
                return Res("LicenseCheckError");
            }
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
                    OnPropertyChanged(nameof(IsGeminiConfigured));
                }
            }
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

        public SettingsViewModel(AppSettingsService? appSettingsService = null, ThemeService? themeService = null, CompanyService? companyService = null)
        {
            _appSettingsService = appSettingsService ?? App.AppSettingsService;
            _themeService = themeService ?? App.ThemeService;
            _companyService = companyService ?? App.CompanyService;

            _currentLanguage = _appSettingsService.Settings.LanguageCode;
            _currentTheme = DetectCurrentTheme();
            _currentInterfaceSize = _appSettingsService.Settings.InterfaceSize ?? "Medium";
            _currentTextSize = _appSettingsService.Settings.TextSize ?? "Medium";
            _currentDocLanguage = _appSettingsService.Settings.DocumentLanguage ?? "";

            GoBackCommand = new RelayCommand(o =>
            {
                App.NavigationService.NavigateTo(new MainViewModel());
            });

            ChangeLanguageCommand = new RelayCommand(param =>
            {
                if (param is string code)
                {
                    LanguageService.SetLanguage(code);
                    CurrentLanguage = code;
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
    }
}
