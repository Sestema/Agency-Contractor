using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public class AppSettingsService
    {
        private const string SettingsFileName = "settings.json";
        private const string BackupFileName = "settings.json.bak";
        public const string CurrentAppVersion = "0.1.05";
        private string _settingsPath;
        private string _backupPath;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private Timer? _debounceTimer;

        public class AppSettings
        {
            public string RootFolderPath { get; set; } = string.Empty;
            public string LanguageCode { get; set; } = "uk";
            public string ThemeName { get; set; } = "Light";
            public List<string> HiddenTags { get; set; } = new List<string>();
            public List<string> HiddenCompanyIds { get; set; } = new List<string>();
            public string SelectedCompanyId { get; set; } = string.Empty;
            public string AppVersion { get; set; } = CurrentAppVersion;
            public string EmployeeSortField { get; set; } = "Name";
            public bool EmployeeSortAscending { get; set; } = true;
            public string EmployeeViewMode { get; set; } = "List";
            public double EmployeeZoomLevel { get; set; } = 1.0;
            public double CandidateZoomLevel { get; set; } = 1.0;
            public string CandidateViewMode { get; set; } = "List";
            public bool ShowStatPaid { get; set; } = false;
            public bool ShowStatRemaining { get; set; } = false;
            public bool ShowStatAdvances { get; set; } = false;
            public bool ShowStatCustomAdd { get; set; } = false;
            public bool ShowStatCustomSub { get; set; } = false;
            public string InterfaceSize { get; set; } = "Medium";
            public string TextSize { get; set; } = "Medium";
            public string DocumentLanguage { get; set; } = "";
            public List<double> SalaryColumnWidths { get; set; } = new List<double>();
            public string ReportDateFrom { get; set; } = "";
            public string ReportDateTo { get; set; } = "";
            public string GeminiApiKey { get; set; } = "";
            public string GeminiModel { get; set; } = "gemini-2.5-flash";
            public double WindowLeft { get; set; } = -1;
            public double WindowTop { get; set; } = -1;
            public double WindowWidth { get; set; } = -1;
            public double WindowHeight { get; set; } = -1;
            public bool WindowMaximized { get; set; } = false;
            public List<string> MenuCardOrder { get; set; } = new List<string>();
            public string DashSlot0 { get; set; } = "expiring";
            public string DashSlot1 { get; set; } = "companies";
            public string DashSlot2 { get; set; } = "salary";
            public double DashColumnRatio { get; set; } = 1.0;
            public double DashRowRatio { get; set; } = 0.4;
        }

        public AppSettings Settings { get; private set; } = new AppSettings();

        public AppSettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "AgencyContractor");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, SettingsFileName);
            _backupPath = Path.Combine(appFolder, BackupFileName);
            
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("AppSettingsService.LoadSettings", ex);
                    if (TryRestoreFromBackup())
                        return;
                    Settings = new AppSettings();
                }
            }
            else
            {
                Settings = new AppSettings();
            }

            if (Settings.AppVersion != CurrentAppVersion)
            {
                Settings.AppVersion = CurrentAppVersion;
                _ = SaveSettingsImmediate();
            }
        }

        private bool TryRestoreFromBackup()
        {
            try
            {
                if (!File.Exists(_backupPath)) return false;
                var json = File.ReadAllText(_backupPath, Encoding.UTF8);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                LoggingService.LogWarning("AppSettingsService", "Restored settings from backup");
                _ = SaveSettingsImmediate();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double SafeDouble(double value, double fallback = -1)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return fallback;
            return value;
        }

        private void SanitizeSettings()
        {
            Settings.WindowLeft = SafeDouble(Settings.WindowLeft);
            Settings.WindowTop = SafeDouble(Settings.WindowTop);
            Settings.WindowWidth = SafeDouble(Settings.WindowWidth);
            Settings.WindowHeight = SafeDouble(Settings.WindowHeight);
            Settings.EmployeeZoomLevel = SafeDouble(Settings.EmployeeZoomLevel, 1.0);
            Settings.CandidateZoomLevel = SafeDouble(Settings.CandidateZoomLevel, 1.0);

            if (Settings.SalaryColumnWidths?.Count > 0)
                Settings.SalaryColumnWidths = Settings.SalaryColumnWidths
                    .Select(w => SafeDouble(w, 100)).ToList();
        }

        public void SaveSettings()
        {
            var newTimer = new Timer(_ => { _ = SaveSettingsImmediate(); }, null, 500, Timeout.Infinite);
            var oldTimer = Interlocked.Exchange(ref _debounceTimer, newTimer);
            oldTimer?.Dispose();
        }

        public async Task SaveSettingsImmediate()
        {
            await _saveLock.WaitAsync();
            try
            {
                SanitizeSettings();
                var tempPath = _settingsPath + ".tmp";
                var json = JsonSerializer.Serialize(Settings, _jsonOptions);
                File.WriteAllText(tempPath, json, Encoding.UTF8);

                if (File.Exists(_settingsPath))
                    File.Copy(_settingsPath, _backupPath, true);

                File.Move(tempPath, _settingsPath, true);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("AppSettingsService.SaveSettings", ex);
                try { if (File.Exists(_settingsPath + ".tmp")) File.Delete(_settingsPath + ".tmp"); } catch { }
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
