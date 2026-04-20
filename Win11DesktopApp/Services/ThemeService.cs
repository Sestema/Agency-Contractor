using System;
using System.Linq;
using System.Windows;
using Win11DesktopApp.Helpers;

namespace Win11DesktopApp.Services
{
    public class ThemeService
    {
        private static readonly string[] GlassThemes = { "Glass", "GlassDark" };
        private readonly AppSettingsService _appSettingsService;

        public ThemeService(AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        }

        public void SetTheme(string themeName)
        {
            var dictName = $"Resources/Themes/Theme.{themeName}.xaml";
            var newDict = new ResourceDictionary { Source = new Uri(dictName, UriKind.Relative) };

            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Themes/Theme."));

            if (oldDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);
            }

            Application.Current.Resources.MergedDictionaries.Add(newDict);

            var mainWindow = Application.Current.MainWindow;
            bool isGlass = Array.Exists(GlassThemes, t => t.Equals(themeName, StringComparison.OrdinalIgnoreCase));

            if (isGlass)
            {
                bool isDark = themeName.Equals("GlassDark", StringComparison.OrdinalIgnoreCase);
                AcrylicHelper.EnableAcrylic(mainWindow, isDark);
            }
            else
            {
                AcrylicHelper.DisableAcrylic(mainWindow);
            }

            if (_appSettingsService.Settings.ThemeName != themeName)
            {
                _appSettingsService.Settings.ThemeName = themeName;
                _appSettingsService.SaveSettings();
            }
        }
    }
}
