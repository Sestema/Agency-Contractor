using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Win11DesktopApp.Services
{
    public class AppSettingsService
    {
        private const string SettingsFileName = "settings.json";
        private const string BackupFileName = "settings.json.bak";
        public const string CurrentAppVersion = "0.1.22";
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
            public string ArchiveSortField { get; set; } = "EndDate";
            public bool ArchiveSortAscending { get; set; } = false;
            public string ArchiveViewMode { get; set; } = "List";
            public double ArchiveZoomLevel { get; set; } = 1.0;
            public double CandidateZoomLevel { get; set; } = 1.0;
            public string CandidateViewMode { get; set; } = "List";
            public double SalarySidebarTopRatio { get; set; } = 2.0;
            public double SalarySidebarWidth { get; set; } = 230.0;
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
            public bool AdminReadOnlyMode { get; set; } = false;
            public bool AdminDisableAI { get; set; } = false;
            public bool AdminDisableExports { get; set; } = false;
            public bool AdminMaintenanceMode { get; set; } = false;
            public bool AdminHideTemplates { get; set; } = false;
            public bool AdminHideFinance { get; set; } = false;
            public bool AdminForceUpdate { get; set; } = false;
            public string AdminMessage { get; set; } = "";
            public string AdminUpdateChannel { get; set; } = "stable";
            public string AdminMinimumSupportedVersion { get; set; } = "";
            public string AdminRecommendedVersion { get; set; } = "";
            public string RemotePolicyVersion { get; set; } = "";
            public double WindowLeft { get; set; } = -1;
            public double WindowTop { get; set; } = -1;
            public double WindowWidth { get; set; } = -1;
            public double WindowHeight { get; set; } = -1;
            public bool WindowMaximized { get; set; } = false;
            public double ExportFirmSelectWindowWidth { get; set; } = -1;
            public double ExportFirmSelectWindowHeight { get; set; } = -1;
            public List<string> MenuCardOrder { get; set; } = new List<string>();
            public string DashSlot0 { get; set; } = "expiring";
            public string DashSlot1 { get; set; } = "companies";
            public string DashSlot2 { get; set; } = "salary";
            public double DashColumnRatio { get; set; } = 1.0;
            public double DashRowRatio { get; set; } = 0.4;
            public bool RememberProfileLogin { get; set; } = false;
            public string EncryptedProfileSessionToken { get; set; } = string.Empty;
            public int ProfileSessionVersion { get; set; } = 0;
            public string ProfileClientId { get; set; } = string.Empty;
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
            var shouldPersistDefaults = false;

            if (File.Exists(_settingsPath))
            {
                try
                {
                    Settings = SafeFileService.ReadJsonOrDefault(_settingsPath, new AppSettings(), _jsonOptions, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("AppSettingsService.LoadSettings", ex);
                    BackupUnreadableFile(_settingsPath, "settings");
                    if (TryRestoreFromBackup())
                        return;

                    Settings = new AppSettings();
                    shouldPersistDefaults = true;
                    NotifyWarning(Res("MsgSettingsResetToDefaults"));
                }
            }
            else
            {
                if (TryRestoreFromBackup())
                    return;

                Settings = new AppSettings();
            }

            if (Settings.AppVersion != CurrentAppVersion)
            {
                Settings.AppVersion = CurrentAppVersion;
                _ = SaveSettingsImmediate();
            }
            else if (shouldPersistDefaults)
            {
                _ = SaveSettingsImmediate();
            }
        }

        private bool TryRestoreFromBackup()
        {
            try
            {
                if (!File.Exists(_backupPath)) return false;
                Settings = SafeFileService.ReadJsonOrDefault(_backupPath, new AppSettings(), _jsonOptions, Encoding.UTF8);
                LoggingService.LogWarning("AppSettingsService", "Restored settings from backup");
                NotifyWarning(Res("MsgSettingsRecoveredFromBackup"));
                _ = SaveSettingsImmediate();
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppSettingsService.TryRestoreFromBackup", ex.Message);
                BackupUnreadableFile(_backupPath, "settings-backup");
                return false;
            }
        }

        private static void BackupUnreadableFile(string path, string label)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                var directory = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path);
                var quarantineName = $"{fileName}.corrupt.{DateTime.Now:yyyyMMdd_HHmmss}";
                var quarantinePath = string.IsNullOrWhiteSpace(directory)
                    ? quarantineName
                    : Path.Combine(directory, quarantineName);
                File.Move(path, quarantinePath, true);
                LoggingService.LogWarning("AppSettingsService.BackupUnreadableFile",
                    $"Moved unreadable {label} file to {quarantinePath}");
            }
            catch
            {
            }
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private static void NotifyWarning(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (Application.Current?.MainWindow?.IsVisible == true)
                {
                    ToastService.Instance.Warning(message);
                    return;
                }

                MessageBox.Show(message, Res("TitleWarning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            });
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
            Settings.ExportFirmSelectWindowWidth = SafeDouble(Settings.ExportFirmSelectWindowWidth);
            Settings.ExportFirmSelectWindowHeight = SafeDouble(Settings.ExportFirmSelectWindowHeight);
            Settings.EmployeeZoomLevel = SafeDouble(Settings.EmployeeZoomLevel, 1.0);
            Settings.ArchiveZoomLevel = SafeDouble(Settings.ArchiveZoomLevel, 1.0);
            Settings.CandidateZoomLevel = SafeDouble(Settings.CandidateZoomLevel, 1.0);
            Settings.SalarySidebarTopRatio = SafeDouble(Settings.SalarySidebarTopRatio, 2.0);
            Settings.SalarySidebarWidth = SafeDouble(Settings.SalarySidebarWidth, 230.0);

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

                if (File.Exists(_settingsPath))
                {
                    var existingJson = SafeFileService.ReadAllText(_settingsPath, Encoding.UTF8);
                    SafeFileService.WriteTextAtomic(_backupPath, existingJson, Encoding.UTF8);
                }

                SafeFileService.WriteJsonAtomic(_settingsPath, Settings, _jsonOptions, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("AppSettingsService.SaveSettings", ex);
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
