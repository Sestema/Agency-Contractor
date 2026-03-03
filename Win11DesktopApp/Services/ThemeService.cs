using System;
using System.Linq;
using System.Windows;

namespace Win11DesktopApp.Services
{
    public class ThemeService
    {
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

            if (App.AppSettingsService != null && App.AppSettingsService.Settings.ThemeName != themeName)
            {
                App.AppSettingsService.Settings.ThemeName = themeName;
                App.AppSettingsService.SaveSettings();
            }
        }
    }
}
