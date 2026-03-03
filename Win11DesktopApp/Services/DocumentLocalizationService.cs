using System;
using System.Windows;

namespace Win11DesktopApp.Services
{
    public class DocumentLocalizationService
    {
        private ResourceDictionary? _docResources;
        private string _currentLangCode = "";

        public string CurrentLanguageCode => _currentLangCode;

        public void LoadLanguage(string langCode)
        {
            _currentLangCode = langCode;

            if (string.IsNullOrEmpty(langCode))
            {
                _docResources = null;
                return;
            }

            try
            {
                _docResources = LanguageService.LoadDictionary(langCode);
            }
            catch (Exception ex)
            {
                _docResources = null;
                LoggingService.LogError("DocLocalization",
                    $"Cannot load Strings.{langCode}.xaml: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves a localization key. Priority:
        /// 1. Dedicated document language dictionary (_docResources)
        /// 2. Application-wide resources (current UI language)
        /// </summary>
        public string Get(string key)
        {
            if (_docResources != null && _docResources.Contains(key))
                return _docResources[key] as string ?? key;

            return Application.Current?.TryFindResource(key) as string ?? key;
        }

        public void SyncWithUiLanguage(string uiLangCode)
        {
            if (!string.IsNullOrEmpty(_currentLangCode))
                return;

            try
            {
                _docResources = LanguageService.LoadDictionary(uiLangCode);
            }
            catch { }
        }
    }
}
