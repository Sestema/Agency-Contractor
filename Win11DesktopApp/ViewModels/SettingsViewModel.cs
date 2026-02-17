using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly ThemeService _themeService;

        public ICommand GoBackCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand ChangeThemeCommand { get; }
        public ICommand SelectRootFolderCommand { get; }

        public string RootFolderPath
        {
            get => _appSettingsService.Settings.RootFolderPath;
            set
            {
                if (_appSettingsService.Settings.RootFolderPath != value)
                {
                    _appSettingsService.Settings.RootFolderPath = value;
                    _appSettingsService.SaveSettings();
                    OnPropertyChanged();
                }
            }
        }

        public string AppVersion => _appSettingsService.Settings.AppVersion;

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

        public SettingsViewModel(AppSettingsService? appSettingsService = null, ThemeService? themeService = null)
        {
            _appSettingsService = appSettingsService ?? App.AppSettingsService;
            _themeService = themeService ?? App.ThemeService;

            _currentLanguage = _appSettingsService.Settings.LanguageCode;
            _currentTheme = DetectCurrentTheme();

            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));

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
