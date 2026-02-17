using System;
using System.IO;
using System.Text.Json;

namespace Win11DesktopApp.Services
{
    public class AppSettingsService
    {
        private const string SettingsFileName = "settings.json";
        private const string CurrentAppVersion = "0.0.05";
        private string _settingsPath;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        public class AppSettings
        {
            public string RootFolderPath { get; set; } = string.Empty;
            public string LanguageCode { get; set; } = "uk";
            public string SelectedCompanyId { get; set; } = string.Empty;
            public string AppVersion { get; set; } = CurrentAppVersion;
        }

        public AppSettings Settings { get; private set; } = new AppSettings();

        public AppSettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "AgencyContractor");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, SettingsFileName);
            
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch
                {
                    Settings = new AppSettings();
                }
            }
            else
            {
                Settings = new AppSettings();
            }

            // Ensure version is current
            if (Settings.AppVersion != CurrentAppVersion)
            {
                Settings.AppVersion = CurrentAppVersion;
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(Settings, _jsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
