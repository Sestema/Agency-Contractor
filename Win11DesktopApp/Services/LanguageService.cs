using System;
using System.Linq;
using System.Windows;

namespace Win11DesktopApp.Services
{
    public class LanguageService
    {
        public static void SetLanguage(string langCode)
        {
            var dictName = $"Resources/Languages/Strings.{langCode}.xaml";
            var newDict = new ResourceDictionary { Source = new Uri(dictName, UriKind.Relative) };

            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Languages/Strings."));

            if (oldDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);
            }

            Application.Current.Resources.MergedDictionaries.Add(newDict);
            
            // Save settings if AppSettingsService is available (it might be null during very early startup)
            if (App.AppSettingsService != null && App.AppSettingsService.Settings.LanguageCode != langCode)
            {
                App.AppSettingsService.Settings.LanguageCode = langCode;
                App.AppSettingsService.SaveSettings();
            }
        }
    }
}
