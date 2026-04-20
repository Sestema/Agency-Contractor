using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace Win11DesktopApp.Services
{
    public class LanguageService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private static bool _languageMetadataOverridden;
        private static ResourceDictionary? _activeLangDict;

        public LanguageService(
            AppSettingsService appSettingsService,
            DocumentLocalizationService documentLocalizationService)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _documentLocalizationService = documentLocalizationService ?? throw new ArgumentNullException(nameof(documentLocalizationService));
        }

        public static ResourceDictionary LoadDictionary(string langCode)
        {
            var xamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "Languages", $"Strings.{langCode}.xaml");

            if (File.Exists(xamlPath))
            {
                using var stream = File.OpenRead(xamlPath);
                return (ResourceDictionary)XamlReader.Load(stream);
            }

            return new ResourceDictionary
            {
                Source = new Uri($"Resources/Languages/Strings.{langCode}.xaml", UriKind.Relative)
            };
        }

        public void SetLanguage(string langCode)
        {
            var newDict = LoadDictionary(langCode);
            var merged = Application.Current.Resources.MergedDictionaries;

            if (_activeLangDict != null && merged.Contains(_activeLangDict))
            {
                merged.Remove(_activeLangDict);
            }
            else
            {
                var oldDict = merged.FirstOrDefault(d =>
                    d.Source?.OriginalString.Contains("Resources/Languages/Strings.") == true);
                if (oldDict != null)
                    merged.Remove(oldDict);
            }

            merged.Add(newDict);
            _activeLangDict = newDict;

            var culture = langCode switch
            {
                "uk" => new CultureInfo("uk-UA"),
                "cs" => new CultureInfo("cs-CZ"),
                "ru" => new CultureInfo("ru-RU"),
                _ => new CultureInfo("en-US")
            };

            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            if (!_languageMetadataOverridden)
            {
                FrameworkElement.LanguageProperty.OverrideMetadata(
                    typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
                _languageMetadataOverridden = true;
            }

            if (_appSettingsService.Settings.LanguageCode != langCode)
            {
                _appSettingsService.Settings.LanguageCode = langCode;
                _appSettingsService.SaveSettings();
            }

            _documentLocalizationService.SyncWithUiLanguage(langCode);
        }
    }
}
