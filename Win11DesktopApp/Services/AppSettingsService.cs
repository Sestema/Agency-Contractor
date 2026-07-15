using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Win11DesktopApp.Telegram;

namespace Win11DesktopApp.Services
{
    public class AppSettingsService
    {
        private const string SettingsFileName = "settings.json";
        private const string BackupFileName = "settings.json.bak";
        private static readonly Lazy<string> _currentAppVersion = new(ResolveCurrentAppVersion);
        public static string CurrentAppVersion => _currentAppVersion.Value;
        public static string? PendingUpdateFrom { get; set; }
        private readonly bool _suppressStartupNotifications;
        private string _settingsPath;
        private string _backupPath;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private Timer? _debounceTimer;

        public class ReportColumnSetting
        {
            public string Key { get; set; } = string.Empty;
            public bool IsVisible { get; set; } = true;
            public int DisplayIndex { get; set; }
            public double Width { get; set; } = 120;
        }

        public class UserAccessScopeSetting
        {
            public string Mode { get; set; } = "AllData";
            public List<string> AgencyNames { get; set; } = new();
            public List<string> EmployerCompanyIds { get; set; } = new();
        }

        public class ModulePermissionSetting
        {
            public string ModuleKey { get; set; } = string.Empty;
            public string AccessLevel { get; set; } = "None";
        }

        public class EmployerPermissionSetting
        {
            public string EmployerCompanyId { get; set; } = string.Empty;
            public string AccessLevel { get; set; } = "None";
        }

        public class BusinessUserSetting
        {
            public string UserId { get; set; } = string.Empty;
            public string Login { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string RoleKey { get; set; } = "manager";
            public bool IsActive { get; set; } = true;
            public string PasswordHash { get; set; } = string.Empty;
            public string PasswordSalt { get; set; } = string.Empty;
            public bool MustChangePassword { get; set; } = true;
            public DateTime? CreatedAtUtc { get; set; }
            public DateTime? LastLoginAtUtc { get; set; }
            public UserAccessScopeSetting AccessScope { get; set; } = new();
            public List<ModulePermissionSetting> ModulePermissions { get; set; } = new();
            public List<EmployerPermissionSetting> EmployerPermissions { get; set; } = new();
        }

        public class AppSettings
        {
            public string RootFolderPath { get; set; } = string.Empty;
            public string LanguageCode { get; set; } = "uk";
            public string ThemeName { get; set; } = "Light";
            public string AccentColor { get; set; } = string.Empty;
            public List<string> HiddenTags { get; set; } = new List<string>();
            public List<string> HiddenCompanyIds { get; set; } = new List<string>();
            public string SelectedCompanyId { get; set; } = string.Empty;
            public string CompanySortMode { get; set; } = "Default";
            public string AppVersion { get; set; } = CurrentAppVersion;
            public string EmployeeSortField { get; set; } = "Name";
            public bool EmployeeSortAscending { get; set; } = true;
            public string EmployeeViewMode { get; set; } = "List";
            public double EmployeeZoomLevel { get; set; } = 1.0;
            public int EmployeeTileSizeStep { get; set; } = 4;
            public string ArchiveSortField { get; set; } = "EndDate";
            public bool ArchiveSortAscending { get; set; } = false;
            public string ArchiveViewMode { get; set; } = "List";
            public double ArchiveZoomLevel { get; set; } = 1.0;
            public double CandidateZoomLevel { get; set; } = 1.0;
            public string CandidateViewMode { get; set; } = "List";
            public double SalarySidebarTopRatio { get; set; } = 2.0;
            public double SalarySidebarWidth { get; set; } = 230.0;
            public double EmployeeDetailsPanelWidth { get; set; } = 1120.0;
            public double EmployeeDetailsPanelHeight { get; set; } = 760.0;
            public double EmployeeDetailsSidebarWidth { get; set; } = 292.0;
            public int EmployeeDetailsLastTabIndex { get; set; } = 0;
            public double PdfEditorSidebarWidth { get; set; } = 360.0;
            public double PdfEditorFieldsPanelHeight { get; set; } = 260.0;
            public double PdfEditorAiPanelHeight { get; set; } = 280.0;
            public bool PdfEditorAiPanelOpen { get; set; } = false;
            public bool ShowStatPaid { get; set; } = false;
            public bool ShowStatRemaining { get; set; } = false;
            public bool ShowStatAdvances { get; set; } = false;
            public bool ShowStatCustomAdd { get; set; } = false;
            public bool ShowStatCustomSub { get; set; } = false;
            public bool SalaryNameOrderLastFirst { get; set; } = false;
            public bool SalaryHoursCustomPrecision { get; set; } = false;
            public string InterfaceSize { get; set; } = "Medium";
            public string TextSize { get; set; } = "Medium";
            public string DocumentLanguage { get; set; } = "";
            public List<double> SalaryColumnWidths { get; set; } = new List<double>();
            public List<ReportColumnSetting> EmployeeReportColumns { get; set; } = new List<ReportColumnSetting>();
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
            public int DashMovementMonthCount { get; set; } = 1;
            public int ScanDefaultDpi { get; set; } = 300;
            public int ScanDefaultColorMode { get; set; } = 0;
            public int ScanDefaultSource { get; set; } = 0;
            public string ScanDefaultDeviceId { get; set; } = string.Empty;
            public bool RememberProfileLogin { get; set; } = false;
            public string EncryptedProfileSessionToken { get; set; } = string.Empty;
            public int ProfileSessionVersion { get; set; } = 0;
            public string ProfileClientId { get; set; } = string.Empty;
            public string CachedAccessClientId { get; set; } = string.Empty;
            public string CachedAccessExpiresAtUtc { get; set; } = string.Empty;
            public string CachedAccessLastCheckedAtUtc { get; set; } = string.Empty;
            public bool CachedAccessIsBlocked { get; set; } = false;
            public string CachedAccessSource { get; set; } = string.Empty;
            public string CachedAccessPlan { get; set; } = string.Empty;
            public string LegacyLicenseMigratedAtUtc { get; set; } = string.Empty;
            public bool WebPanelEnabled { get; set; } = false;
            public int WebPanelPort { get; set; } = 47831;
            public string WebPanelBindAddress { get; set; } = "127.0.0.1";
            public bool WebPanelPreventSleep { get; set; } = true;
            public bool ExperimentalMultiUser { get; set; } = false;
            public bool PendingMemberRoleSelection { get; set; } = false;
            public bool PermissionSoftMode { get; set; } = true;
            public bool UseApiV2ForWebPanel { get; set; } = false;
            public bool UsePostgresNotify { get; set; } = false;
            public bool MultiUserHardEnforcement { get; set; } = false;
            public List<BusinessUserSetting> BusinessUsers { get; set; } = new();
            public string CurrentBusinessUserId { get; set; } = string.Empty;
            public bool RememberBusinessUserLogin { get; set; } = false;
            public string RememberedBusinessUserId { get; set; } = string.Empty;
            public string EncryptedBusinessUserSessionToken { get; set; } = string.Empty;
            public string DatabaseStorageMode { get; set; } = "Sqlite";
            public string PostgresConnectionString { get; set; } = string.Empty;
            public string PostgresHost { get; set; } = "localhost";
            public int PostgresPort { get; set; } = 5432;
            public string PostgresDatabase { get; set; } = "agency_db";
            public string PostgresUsername { get; set; } = "postgres";
            public string EncryptedPostgresPassword { get; set; } = string.Empty;
            public string PostgresDataDirectoryPath { get; set; } = string.Empty;
            public string PostgresMigrationCompletedAtUtc { get; set; } = string.Empty;
            public string PostgresEnabledAtUtc { get; set; } = string.Empty;
            public string LastSqliteBackupFromPostgresAtUtc { get; set; } = string.Empty;
            public TelegramBotSettings Telegram { get; set; } = new TelegramBotSettings();
        }

        public AppSettings Settings { get; private set; } = new AppSettings();
        public bool WasRecoveredFromBackupOnLoad { get; private set; }
        public bool WasResetToDefaultsOnLoad { get; private set; }

        public AppSettingsService(bool suppressStartupNotifications = false)
        {
            _suppressStartupNotifications = suppressStartupNotifications;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, "AgencyContractor");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, SettingsFileName);
            _backupPath = Path.Combine(appFolder, BackupFileName);
            
            LoadSettings();
        }

        private static string ResolveCurrentAppVersion()
        {
            try
            {
                var assembly = typeof(AppSettingsService).Assembly;
                var assemblyVersion = assembly.GetName().Version;
                if (assemblyVersion != null)
                {
                    return assemblyVersion.Build >= 0
                        ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
                        : $"{assemblyVersion.Major}.{assemblyVersion.Minor}";
                }

                var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
                if (!string.IsNullOrWhiteSpace(fileVersion))
                    return fileVersion;
            }
            catch
            {
            }

            return "0.0.0";
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
                    WasResetToDefaultsOnLoad = true;
                    shouldPersistDefaults = true;
                    NotifyStartupWarning(Res("MsgSettingsResetToDefaults"));
                }
            }
            else
            {
                if (TryRestoreFromBackup())
                    return;

                Settings = new AppSettings();
                shouldPersistDefaults = true;
            }

            if (Settings.AppVersion != CurrentAppVersion)
            {
                PendingUpdateFrom = Settings.AppVersion;
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
                WasRecoveredFromBackupOnLoad = true;
                LoggingService.LogInfo("AppSettingsService", "Restored settings from backup");
                NotifyStartupWarning(Res("MsgSettingsRecoveredFromBackup"));
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
                SafeFileService.MoveFile(path, quarantinePath);
                LoggingService.LogWarning("AppSettingsService.BackupUnreadableFile",
                    $"Moved unreadable {label} file to {quarantinePath}");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppSettingsService.BackupUnreadableFile", ex.Message);
            }
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private void NotifyStartupWarning(string message)
        {
            if (_suppressStartupNotifications)
                return;

            NotifyWarning(message);
        }

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
            Settings.Telegram ??= new TelegramBotSettings();
            Settings.Telegram.AuthorizedUsers ??= new List<TelegramAuthorizedUser>();
            Settings.Telegram.BotUsername ??= string.Empty;
            Settings.Telegram.EncryptedBotToken ??= string.Empty;
            Settings.Telegram.DailyDigestTime = string.IsNullOrWhiteSpace(Settings.Telegram.DailyDigestTime)
                ? "08:00"
                : Settings.Telegram.DailyDigestTime.Trim();
            Settings.BusinessUsers ??= new List<BusinessUserSetting>();
            foreach (var user in Settings.BusinessUsers)
            {
                user.UserId = user.UserId?.Trim() ?? string.Empty;
                user.Login = user.Login?.Trim() ?? string.Empty;
                user.FirstName = user.FirstName?.Trim() ?? string.Empty;
                user.LastName = user.LastName?.Trim() ?? string.Empty;
                user.RoleKey = string.IsNullOrWhiteSpace(user.RoleKey) ? "manager" : user.RoleKey.Trim();
                user.PasswordHash ??= string.Empty;
                user.PasswordSalt ??= string.Empty;
                user.AccessScope ??= new UserAccessScopeSetting();
                user.AccessScope.Mode = NormalizeUserAccessScopeMode(user.AccessScope.Mode);
                user.AccessScope.AgencyNames = user.AccessScope.AgencyNames?
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();
                user.AccessScope.EmployerCompanyIds = user.AccessScope.EmployerCompanyIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();
                user.ModulePermissions = user.ModulePermissions?
                    .Where(permission => !string.IsNullOrWhiteSpace(permission.ModuleKey))
                    .Select(permission => new ModulePermissionSetting
                    {
                        ModuleKey = permission.ModuleKey.Trim(),
                        AccessLevel = NormalizeUserAccessLevel(permission.AccessLevel, allowExport: true)
                    })
                    .GroupBy(permission => permission.ModuleKey, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList() ?? new List<ModulePermissionSetting>();
                user.EmployerPermissions = user.EmployerPermissions?
                    .Where(permission => !string.IsNullOrWhiteSpace(permission.EmployerCompanyId))
                    .Select(permission => new EmployerPermissionSetting
                    {
                        EmployerCompanyId = permission.EmployerCompanyId.Trim(),
                        AccessLevel = NormalizeUserAccessLevel(permission.AccessLevel)
                    })
                    .GroupBy(permission => permission.EmployerCompanyId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList() ?? new List<EmployerPermissionSetting>();
            }
            Settings.CurrentBusinessUserId = Settings.BusinessUsers.Any(user =>
                    string.Equals(user.UserId, Settings.CurrentBusinessUserId, StringComparison.OrdinalIgnoreCase)
                    && user.IsActive)
                ? Settings.CurrentBusinessUserId.Trim()
                : string.Empty;

            Settings.DatabaseStorageMode = string.Equals(Settings.DatabaseStorageMode, "Postgres", StringComparison.OrdinalIgnoreCase)
                ? "Postgres"
                : "Sqlite";
            Settings.PostgresHost = string.IsNullOrWhiteSpace(Settings.PostgresHost)
                ? "localhost"
                : Settings.PostgresHost.Trim();
            Settings.PostgresPort = Math.Clamp(Settings.PostgresPort <= 0 ? 5432 : Settings.PostgresPort, 1, 65535);
            Settings.PostgresDatabase = string.IsNullOrWhiteSpace(Settings.PostgresDatabase)
                ? "agency_db"
                : Settings.PostgresDatabase.Trim();
            Settings.PostgresUsername = string.IsNullOrWhiteSpace(Settings.PostgresUsername)
                ? "postgres"
                : Settings.PostgresUsername.Trim();
            Settings.EncryptedPostgresPassword ??= string.Empty;
            Settings.PostgresDataDirectoryPath = Settings.PostgresDataDirectoryPath?.Trim() ?? string.Empty;

            Settings.WindowLeft = SafeDouble(Settings.WindowLeft);
            Settings.WindowTop = SafeDouble(Settings.WindowTop);
            Settings.WindowWidth = SafeDouble(Settings.WindowWidth);
            Settings.WindowHeight = SafeDouble(Settings.WindowHeight);
            Settings.ExportFirmSelectWindowWidth = SafeDouble(Settings.ExportFirmSelectWindowWidth);
            Settings.ExportFirmSelectWindowHeight = SafeDouble(Settings.ExportFirmSelectWindowHeight);
            Settings.EmployeeZoomLevel = SafeDouble(Settings.EmployeeZoomLevel, 1.0);
            Settings.EmployeeTileSizeStep = Math.Min(6, Math.Max(1, Settings.EmployeeTileSizeStep));
            Settings.ArchiveZoomLevel = SafeDouble(Settings.ArchiveZoomLevel, 1.0);
            Settings.CandidateZoomLevel = SafeDouble(Settings.CandidateZoomLevel, 1.0);
            Settings.SalarySidebarTopRatio = SafeDouble(Settings.SalarySidebarTopRatio, 2.0);
            Settings.SalarySidebarWidth = SafeDouble(Settings.SalarySidebarWidth, 230.0);
            Settings.PdfEditorSidebarWidth = Math.Max(240, SafeDouble(Settings.PdfEditorSidebarWidth, 360.0));
            Settings.PdfEditorFieldsPanelHeight = Math.Max(180, SafeDouble(Settings.PdfEditorFieldsPanelHeight, 260.0));
            Settings.PdfEditorAiPanelHeight = Math.Max(160, SafeDouble(Settings.PdfEditorAiPanelHeight, 280.0));

            if (Settings.SalaryColumnWidths?.Count > 0)
                Settings.SalaryColumnWidths = Settings.SalaryColumnWidths
                    .Select(w => SafeDouble(w, 100)).ToList();

            if (Settings.EmployeeReportColumns?.Count > 0)
            {
                Settings.EmployeeReportColumns = Settings.EmployeeReportColumns
                    .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                    .Select(c => new ReportColumnSetting
                    {
                        Key = c.Key.Trim(),
                        IsVisible = c.IsVisible,
                        DisplayIndex = Math.Max(0, c.DisplayIndex),
                        Width = Math.Max(40, SafeDouble(c.Width, 120))
                    })
                    .ToList();
            }
        }

        private static string NormalizeUserAccessScopeMode(string? mode)
        {
            return (mode ?? string.Empty).Trim() switch
            {
                "SelectedAgencies" => "SelectedAgencies",
                "SelectedEmployers" => "SelectedEmployers",
                "SelectedAgenciesAndEmployers" => "SelectedAgenciesAndEmployers",
                _ => "AllData"
            };
        }

        private static string NormalizeUserAccessLevel(string? accessLevel, bool allowExport = false)
        {
            return (accessLevel ?? string.Empty).Trim() switch
            {
                "View" => "View",
                "Edit" => "Edit",
                "Export" when allowExport => "Export",
                _ => "None"
            };
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
