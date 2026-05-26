using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Npgsql;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.Telegram;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.ViewModels
{
    public class CompanyVisibilityItem : ViewModelBase
    {
        private readonly CompanyService _companyService;

        public EmployerCompany Company { get; }
        public string Name => Company.Name;
        public int ActiveEmployeeCount { get; }
        public bool HasActiveEmployees => ActiveEmployeeCount > 0;

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                if (!value && HasActiveEmployees)
                {
                    // block hiding if active employees exist
                    OnPropertyChanged();
                    return;
                }
                _isVisible = value;
                _companyService.SetCompanyVisible(Company, value);
                OnPropertyChanged();
            }
        }

        public CompanyVisibilityItem(EmployerCompany company, CompanyService companyService)
        {
            Company = company;
            _companyService = companyService;
            ActiveEmployeeCount = companyService.GetActiveEmployeeCount(company);
            _isVisible = companyService.IsCompanyVisible(company);
        }
    }

    public sealed class ConnectedClientViewItem
    {
        public string MachineName { get; init; } = string.Empty;
        public string WindowsUser { get; init; } = string.Empty;
        public string UserDisplayName { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;
        public string AppVersion { get; init; } = string.Empty;
        public string RootFolderPath { get; init; } = string.Empty;
        public string LastSeenDisplay { get; init; } = string.Empty;
        public string StartedDisplay { get; init; } = string.Empty;
        public string StatusDisplay { get; init; } = string.Empty;
        public bool IsOnline { get; init; }
    }

    public sealed class WorkspaceTeamSessionViewItem
    {
        public string UserDisplayName { get; init; } = string.Empty;
        public string MachineName { get; init; } = string.Empty;
        public string WindowsUser { get; init; } = string.Empty;
        public string LastSeenDisplay { get; init; } = string.Empty;
        public string StatusDisplay { get; init; } = string.Empty;
        public bool IsOnline { get; init; }
    }

    public sealed class UserRolePreviewItem
    {
        public string UserId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string RoleName { get; init; } = string.Empty;
        public string AccessSummary { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool IsCurrentUser { get; init; }
        public bool IsEnabled { get; init; } = true;
        public bool CanEdit { get; init; }
    }

    public sealed class UserPermissionOption
    {
        public string Key { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    public sealed class BusinessUserModuleEditorItem : ViewModelBase
    {
        private string _selectedAccessLevel = "None";

        public string ModuleKey { get; init; } = string.Empty;
        public string ModuleName { get; init; } = string.Empty;
        public ObservableCollection<UserPermissionOption> AccessOptions { get; } = new();

        public string SelectedAccessLevel
        {
            get => _selectedAccessLevel;
            set => SetProperty(ref _selectedAccessLevel, value);
        }
    }

    public sealed class BusinessUserAgencyEditorItem : ViewModelBase
    {
        private bool _isSelected;
        private readonly Action? _selectionChanged;

        public string AgencyName { get; init; } = string.Empty;
        public int EmployerCount { get; init; }
        public string DisplayName => $"{AgencyName} ({EmployerCount})";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                    _selectionChanged?.Invoke();
            }
        }

        public BusinessUserAgencyEditorItem(Action? selectionChanged = null)
        {
            _selectionChanged = selectionChanged;
        }
    }

    public sealed class BusinessUserEmployerEditorItem : ViewModelBase
    {
        private bool _isSelected;
        private string _selectedAccessLevel = "View";

        public string EmployerCompanyId { get; init; } = string.Empty;
        public string EmployerName { get; init; } = string.Empty;
        public string AgencyName { get; init; } = string.Empty;
        public ObservableCollection<UserPermissionOption> AccessOptions { get; } = new();

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string SelectedAccessLevel
        {
            get => _selectedAccessLevel;
            set => SetProperty(ref _selectedAccessLevel, value);
        }
    }

    public class SettingsViewModel : ViewModelBase, ICleanable
    {
        private readonly NavigationService _navigationService;
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;
        private readonly ThemeService _themeService;
        private readonly LanguageService _languageService;
        private readonly CompanyService _companyService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly ProfileAuthService _profileAuthService;
        private readonly ProfileSessionService _profileSessionService;
        private readonly AccessStatusService _accessStatusService;
        private readonly ActivityLogService _activityLogService;
        private readonly GeminiApiService _geminiApiService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly CurrentProfileService _currentProfileService;
        private readonly GeminiApiKeyConfigurationService _geminiApiKeyConfigurationService;
        private readonly TelegramBotService _telegramBotService;
        private readonly TelegramPairingService _telegramPairingService;
        private readonly WebPanelHostService _webPanelHostService;
        private readonly SyncEventService _syncEventService;
        private readonly PostgresConnectionTestService _postgresConnectionTestService;
        private readonly PostgresMigrationService _postgresMigrationService;
        private readonly PostgresToSqliteBackupService _postgresToSqliteBackupService;
        private readonly PostgresResetService _postgresResetService;
        private readonly PostgresNetworkAccessService _postgresNetworkAccessService;
        private readonly AppDataStorageFactory _appDataStorageFactory;
        private readonly ConnectedClientsService _connectedClientsService;
        private readonly BusinessUserAuthService _businessUserAuthService;
        private readonly BusinessUserDirectoryService _businessUserDirectoryService;
        private readonly BusinessUserSessionService _businessUserSessionService;
        private readonly WorkspaceSessionService _workspaceSessionService;
        private readonly List<BusinessUserEmployerEditorItem> _allBusinessUserEmployerEditorItems = new();

        public ICommand GoBackCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand ChangeThemeCommand { get; }
        public ICommand ChangeAccentColorCommand { get; }
        public ICommand SelectRootFolderCommand { get; }
        public ICommand OpenTagVisibilityCommand { get; }
        public ICommand OpenCompanyVisibilityCommand { get; }
        public ICommand CloseCompanyVisibilityCommand { get; }
        public ICommand ChangeInterfaceSizeCommand { get; }
        public ICommand ChangeTextSizeCommand { get; }
        public ICommand ChangeDocLanguageCommand { get; }
        public ICommand TestGeminiCommand { get; }
        public ICommand OpenGeminiSiteCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand LogoutProfileCommand { get; }
        public ICommand ChangeProfilePasswordCommand { get; }
        public ICommand EditProfileCommand { get; }
        public ICommand CancelProfileEditCommand { get; }
        public ICommand ConnectTelegramBotCommand { get; }
        public ICommand DisconnectTelegramBotCommand { get; }
        public ICommand GenerateTelegramQrCommand { get; }
        public ICommand RemoveTelegramAuthorizedUserCommand { get; }
        public ICommand OpenBotFatherCommand { get; }
        public ICommand CopyWebPanelUrlCommand { get; }
        public ICommand CopyWebPanelLanUrlCommand { get; }
        public ICommand OpenWebPanelUrlCommand { get; }
        public ICommand RefreshSyncStatusCommand { get; }
        public ICommand TestPostgresConnectionCommand { get; }
        public ICommand RunPostgresMigrationCommand { get; }
        public ICommand AttachExistingPostgresDatabaseCommand { get; }
        public ICommand ReplacePostgresFromSqliteCommand { get; }
        public ICommand UseSqliteStorageModeCommand { get; }
        public ICommand EnablePostgresStorageModeCommand { get; }
        public ICommand RestartApplicationCommand { get; }
        public ICommand CopySecondPcDatabaseAccessCommand { get; }
        public ICommand RefreshConnectedClientsCommand { get; }
        public ICommand ChangeSettingsSectionCommand { get; }
        public ICommand CreateSqliteBackupFromPostgresCommand { get; }
        public ICommand ResetPostgresDatabaseCommand { get; }
        public ICommand ConfigurePostgresTailscaleAccessCommand { get; }
        public ICommand ConfigurePostgresLanAccessCommand { get; }
        public ICommand SelectPostgresDataDirectoryCommand { get; }
        public ICommand OpenNasPostgresGuideCommand { get; }
        public ICommand OpenBusinessUserEditorCommand { get; }
        public ICommand EditBusinessUserCommand { get; }
        public ICommand CloseBusinessUserEditorCommand { get; }
        public ICommand SaveBusinessUserCommand { get; }
        public ICommand LoginBusinessUserCommand { get; }
        public ICommand LogoutBusinessUserCommand { get; }
        public ICommand ChangeBusinessUserPasswordCommand { get; }
        public ICommand RefreshWorkspaceTeamStatusCommand { get; }

        private bool _isCompanyVisibilityOpen;
        public bool IsCompanyVisibilityOpen
        {
            get => _isCompanyVisibilityOpen;
            set => SetProperty(ref _isCompanyVisibilityOpen, value);
        }

        public ObservableCollection<CompanyVisibilityItem> CompanyVisibilityItems { get; } = new();
        public ObservableCollection<ConnectedClientViewItem> ConnectedClients { get; } = new();
        public ObservableCollection<WorkspaceTeamSessionViewItem> WorkspaceTeamSessions { get; } = new();
        public ObservableCollection<UserRolePreviewItem> UserRolePreviewItems { get; } = new();
        public ObservableCollection<UserPermissionOption> BusinessUserRoleOptions { get; } = new();
        public ObservableCollection<UserPermissionOption> BusinessUserScopeModeOptions { get; } = new();
        public ObservableCollection<UserPermissionOption> BusinessUserLoginOptions { get; } = new();
        public ObservableCollection<BusinessUserModuleEditorItem> BusinessUserModuleEditorItems { get; } = new();
        public ObservableCollection<BusinessUserAgencyEditorItem> BusinessUserAgencyEditorItems { get; } = new();
        public ObservableCollection<BusinessUserEmployerEditorItem> BusinessUserEmployerEditorItems { get; } = new();

        private string _updateStatus = "";
        public string UpdateStatus
        {
            get => _updateStatus;
            set => SetProperty(ref _updateStatus, value);
        }

        private bool _isUpdateChecking;
        public bool IsUpdateChecking
        {
            get => _isUpdateChecking;
            set => SetProperty(ref _isUpdateChecking, value);
        }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        private Velopack.UpdateInfo? _pendingUpdate;
        public ICommand InstallUpdateCommand { get; }
        private string _profileFirstName = string.Empty;
        private string _profileLastName = string.Empty;
        private bool _profileRememberMeEnabled;
        private bool _memberRememberMeEnabled;
        private bool _isSyncingMemberRememberMe;
        private string _profileCurrentPassword = string.Empty;
        private string _profileNewPassword = string.Empty;
        private string _profileConfirmPassword = string.Empty;
        private string _profileStatusMessage = string.Empty;
        private bool _profileStatusIsError;
        private bool _isInitializingProfileFields;
        private bool _isSyncingRememberMe;
        private bool _isProfileEditMode;
        private bool _isInitializingBusinessUserEditor;
        private bool _isBusinessUserEditorOpen;
        private string _editingBusinessUserId = string.Empty;
        private string _businessUserLogin = string.Empty;
        private string _businessUserTemporaryPassword = string.Empty;
        private string _businessUserFirstName = string.Empty;
        private string _businessUserLastName = string.Empty;
        private string _businessUserRoleKey = "manager";
        private bool _businessUserIsActive = true;
        private string _businessUserScopeMode = "AllData";
        private string _businessUserEditorStatus = string.Empty;
        private string _selectedBusinessUserLoginId = string.Empty;
        private string _businessUserLoginPassword = string.Empty;
        private string _businessUserLoginStatus = string.Empty;
        private string _businessUserCurrentPassword = string.Empty;
        private string _businessUserNewPassword = string.Empty;
        private string _businessUserConfirmPassword = string.Empty;
        private string _businessUserPasswordStatus = string.Empty;
        private string _workspaceIdDisplay = string.Empty;
        private string _workspaceTeamStatus = string.Empty;
        private bool _isEditingGeminiApiKey;
        private string _geminiApiKeyDraft = string.Empty;
        private bool _telegramEnabled;
        private bool _telegramIsBusy;
        private string _telegramBotTokenInput = string.Empty;
        private string _telegramBotStatus = string.Empty;
        private BitmapImage? _telegramQrCodeImage;
        private string _telegramPairingCode = string.Empty;
        private string _telegramPairingExpiryText = string.Empty;
        private string _postgresHost = "localhost";
        private string _postgresPort = "5432";
        private string _postgresDatabase = "agency_db";
        private string _postgresUsername = "postgres";
        private string _postgresPassword = string.Empty;
        private string _postgresDataDirectoryPath = string.Empty;
        private string _postgresTestStatus = string.Empty;
        private bool _isPostgresTesting;
        private string _postgresMigrationStatus = Res("SettingsPostgresMigrationNotRun");
        private string _postgresModeStatus = string.Empty;
        private string _secondPcDatabaseAccessStatus = string.Empty;
        private string _connectedClientsStatus = string.Empty;
        private string _sqliteBackupFromPostgresStatus = string.Empty;
        private string _postgresResetStatus = string.Empty;
        private string _postgresNetworkAccessStatus = string.Empty;
        private bool _isPostgresMigrating;
        private bool _isCreatingSqliteBackupFromPostgres;
        private bool _isResettingPostgresDatabase;
        private bool _isConfiguringPostgresNetworkAccess;
        private string _currentSettingsSection = "Program";

        public ObservableCollection<TelegramAuthorizedUser> TelegramAuthorizedUsers { get; } = new();

        public string CurrentSettingsSection
        {
            get => _currentSettingsSection;
            set => SetCurrentSettingsSection(value, force: false);
        }

        public bool IsMemberSettingsSession =>
            _currentProfileService.CurrentBusinessUser != null
            && _currentProfileService.CurrentProfile == null;

        public bool CanShowOwnerSettingsSections => !IsMemberSettingsSession;

        public bool CanShowUsersSettingsTab =>
            CanShowOwnerSettingsSections
            && string.Equals(AccessPlanCode, "business", StringComparison.OrdinalIgnoreCase);

        public bool CanShowUsersAdminPanel => CanShowUsersSettingsTab;

        public bool CanShowWorkspaceTeamPanel => CanShowUsersSettingsTab;

        public string WorkspaceIdDisplay
        {
            get => _workspaceIdDisplay;
            private set => SetProperty(ref _workspaceIdDisplay, value);
        }

        public string WorkspaceTeamStatus
        {
            get => _workspaceTeamStatus;
            private set => SetProperty(ref _workspaceTeamStatus, value);
        }

        public bool HasWorkspaceTeamSessions => WorkspaceTeamSessions.Count > 0;

        public bool CanShowMemberAccountPanel => IsMemberSettingsSession;

        public bool IsMemberProgramAccountSection =>
            IsProgramSettingsSection && CanShowMemberAccountPanel;

        public bool IsUsersAdminSettingsSection =>
            IsUsersSettingsSection && CanShowUsersSettingsTab;

        public bool IsOwnerProgramSettingsSection =>
            IsProgramSettingsSection && CanShowOwnerSettingsSections;

        public bool IsProgramSettingsSection =>
            string.Equals(CurrentSettingsSection, "Program", StringComparison.OrdinalIgnoreCase);

        public bool IsServerSettingsSection =>
            string.Equals(CurrentSettingsSection, "Servers", StringComparison.OrdinalIgnoreCase);

        public bool IsUsersSettingsSection =>
            string.Equals(CurrentSettingsSection, "Users", StringComparison.OrdinalIgnoreCase);

        public string RootFolderPath
        {
            get => _appSettingsService.Settings.RootFolderPath;
            set
            {
                if (_appSettingsService.Settings.RootFolderPath != value)
                {
                    var oldPath = _appSettingsService.Settings.RootFolderPath;
                    _appSettingsService.Settings.RootFolderPath = value;
                    _appSettingsService.SaveSettings();
                    EnsureWorkspacePassportForRoot(value);
                    _activityLogService.Log("RootFolderChanged", "Settings", "", "",
                        $"Змінено кореневу папку", oldPath ?? "", value);
                    OnPropertyChanged();
                    RaiseSecondPcDatabaseAccessPropertiesChanged();
                }
            }
        }

        private void EnsureWorkspacePassportForRoot(string rootFolderPath)
        {
            var allowCreate = !IsMemberSettingsSession;
            var result = _folderService.EnsureWorkspacePassport(rootFolderPath, allowCreate);
            if (result.HasConflict)
            {
                LoggingService.LogWarning(
                    "Settings.RootFolder.WorkspacePassport",
                    $"Workspace passport conflict detected: {result.PassportPath}");
            }
            else if (result.IsInvalid)
            {
                LoggingService.LogWarning(
                    "Settings.RootFolder.WorkspacePassport",
                    $"Workspace passport is invalid and was not replaced: {result.PassportPath}");
            }

            RefreshWorkspaceIdentityDisplay();
        }

        private void RefreshWorkspaceIdentityDisplay()
        {
            if (!_folderService.TryGetWorkspacePassport(RootFolderPath, out var passport)
                || passport == null
                || string.IsNullOrWhiteSpace(passport.WorkspaceId))
            {
                WorkspaceIdDisplay = string.Empty;
                return;
            }

            WorkspaceIdDisplay = passport.WorkspaceId;
        }

        private async System.Threading.Tasks.Task RefreshWorkspaceTeamStatusAsync()
        {
            RefreshWorkspaceIdentityDisplay();
            WorkspaceTeamSessions.Clear();

            if (!CanShowWorkspaceTeamPanel)
            {
                WorkspaceTeamStatus = string.Empty;
                OnPropertyChanged(nameof(HasWorkspaceTeamSessions));
                return;
            }

            WorkspaceTeamStatus = Res("SettingsWorkspaceTeamLoading");
            try
            {
                var ownerClientId = _currentProfileService.CurrentProfile?.ClientId
                    ?? TelemetryService.GetCurrentClientId();
                var status = await _workspaceSessionService.RefreshWorkspaceStatusAsync(ownerClientId);
                if (status == null || !status.Ok)
                {
                    if (!string.IsNullOrWhiteSpace(status?.Error))
                        LoggingService.LogWarning("Settings.RefreshWorkspaceTeamStatus", status.Error);

                    WorkspaceTeamStatus = Res("SettingsWorkspaceTeamUnavailable");
                    return;
                }

                var sessions = status.Sessions
                    .OrderByDescending(item => item.IsOnline)
                    .ThenByDescending(item => item.LastSeenAtUtc ?? DateTime.MinValue)
                    .Select(item => new WorkspaceTeamSessionViewItem
                    {
                        UserDisplayName = string.IsNullOrWhiteSpace(item.ActorDisplayName)
                            ? item.ActorKind
                            : item.ActorDisplayName,
                        MachineName = item.MachineName,
                        WindowsUser = item.WindowsUser,
                        LastSeenDisplay = item.LastSeenAtUtc.HasValue
                            ? FormatLocalDateTime(item.LastSeenAtUtc.Value)
                            : string.Empty,
                        StatusDisplay = item.IsOnline ? Res("SettingsClientOnline") : Res("SettingsClientOffline"),
                        IsOnline = item.IsOnline
                    })
                    .ToList();

                foreach (var session in sessions)
                    WorkspaceTeamSessions.Add(session);

                WorkspaceTeamStatus = string.Format(
                    Res("SettingsWorkspaceTeamSummaryFmt"),
                    status.DevicesOnline,
                    status.MaxDevices,
                    WorkspaceIdDisplay);
            }
            catch (Exception ex)
            {
                WorkspaceTeamStatus = Res("SettingsWorkspaceTeamUnavailable");
                LoggingService.LogWarning("Settings.RefreshWorkspaceTeamStatus", ex.Message);
            }
            finally
            {
                OnPropertyChanged(nameof(HasWorkspaceTeamSessions));
            }
        }

        public string AppVersion => AppSettingsService.CurrentAppVersion;

        public bool WebPanelEnabled
        {
            get => _appSettingsService.Settings.WebPanelEnabled;
            set
            {
                if (_appSettingsService.Settings.WebPanelEnabled == value)
                    return;

                _appSettingsService.Settings.WebPanelEnabled = value;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
                _ = ApplyWebPanelStateAsync();
            }
        }

        public string WebPanelPort
        {
            get => _appSettingsService.Settings.WebPanelPort.ToString();
            set
            {
                if (!int.TryParse(value, out var port))
                    return;

                port = Math.Clamp(port, 1024, 65535);
                if (_appSettingsService.Settings.WebPanelPort == port)
                    return;

                _appSettingsService.Settings.WebPanelPort = port;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(WebPanelUrl));
            }
        }

        public bool WebPanelPreventSleep
        {
            get => _appSettingsService.Settings.WebPanelPreventSleep;
            set
            {
                if (_appSettingsService.Settings.WebPanelPreventSleep == value)
                    return;

                _appSettingsService.Settings.WebPanelPreventSleep = value;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        public string WebPanelUrl => $"http://127.0.0.1:{_appSettingsService.Settings.WebPanelPort}";
        public string WebPanelLanUrl => $"http://{GetLocalNetworkIpAddress()}:{_appSettingsService.Settings.WebPanelPort}";

        public string WebPanelStatus =>
            _webPanelHostService.IsRunning
                ? string.Format(Res("SettingsWebPanelRunningFmt"), WebPanelUrl)
                : Res("SettingsDisabled");

        public string SyncEventsFolderPath => _syncEventService.SyncEventsFolderPath;
        public string SyncStatusSummary => _syncEventService.GetStatusSummary();

        public string DatabaseStorageModeDisplay =>
            string.Equals(_appSettingsService.Settings.DatabaseStorageMode, DatabaseStorageModes.Postgres, StringComparison.OrdinalIgnoreCase)
                ? Res("SettingsPostgresSelectedWorking")
                : Res("SettingsSqliteCurrentOld");

        public string ActiveDatabaseRuntimeDisplay =>
            _appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
                ? Res("SettingsActiveRuntimePostgres")
                : Res("SettingsActiveRuntimeSqlite");

        public string ActiveDatabaseRuntimeDetail =>
            _appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
                ? Res("SettingsActiveRuntimePostgresDetail")
                : Res("SettingsActiveRuntimeSqliteDetail");

        public string SecondPcDatabaseServerDisplay => $"{GetSecondPcPostgresHost()}:{NormalizePostgresPort()}";

        public string SecondPcLocalServerDisplay => $"localhost:{NormalizePostgresPort()}";

        public string SecondPcTailscaleServerDisplay
        {
            get
            {
                var tailscaleIp = _postgresNetworkAccessService.TailscaleIpAddress;
                return string.IsNullOrWhiteSpace(tailscaleIp)
                    ? Res("SettingsTailscaleIpMissing")
                    : $"{tailscaleIp}:{NormalizePostgresPort()}";
            }
        }

        public string SecondPcLanServerDisplay
        {
            get
            {
                var lanIp = _postgresNetworkAccessService.LanIpAddress;
                return string.IsNullOrWhiteSpace(lanIp)
                    ? Res("SettingsLanIpMissing")
                    : $"{lanIp}:{NormalizePostgresPort()}";
            }
        }

        public string SecondPcDatabaseNameDisplay =>
            string.IsNullOrWhiteSpace(PostgresDatabase) ? "agency_db" : PostgresDatabase.Trim();

        public string SecondPcDatabaseUserDisplay =>
            string.IsNullOrWhiteSpace(PostgresUsername) ? "postgres" : PostgresUsername.Trim();

        public string SecondPcSharedFolderDisplay
        {
            get
            {
                var rootFolder = RootFolderPath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rootFolder))
                    return Res("SettingsRootFolderMissing");

                if (rootFolder.StartsWith(@"\\", StringComparison.Ordinal))
                    return rootFolder;

                return string.Format(Res("SettingsSecondPcSharedFolderHintFmt"), rootFolder, Environment.MachineName);
            }
        }

        public string SecondPcDatabaseAccessStatus
        {
            get => _secondPcDatabaseAccessStatus;
            set => SetProperty(ref _secondPcDatabaseAccessStatus, value);
        }

        public string ConnectedClientsStatus
        {
            get => _connectedClientsStatus;
            set => SetProperty(ref _connectedClientsStatus, value);
        }

        public bool HasConnectedClients => ConnectedClients.Count > 0;

        public string SqliteBackupFromPostgresStatus
        {
            get => _sqliteBackupFromPostgresStatus;
            set => SetProperty(ref _sqliteBackupFromPostgresStatus, value);
        }

        public bool IsCreatingSqliteBackupFromPostgres
        {
            get => _isCreatingSqliteBackupFromPostgres;
            set
            {
                if (SetProperty(ref _isCreatingSqliteBackupFromPostgres, value))
                {
                    OnPropertyChanged(nameof(CanCreateSqliteBackupFromPostgres));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanCreateSqliteBackupFromPostgres =>
            _appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
            && !IsCreatingSqliteBackupFromPostgres
            && !IsPostgresMigrating;

        public string PostgresResetStatus
        {
            get => _postgresResetStatus;
            set => SetProperty(ref _postgresResetStatus, value);
        }

        public bool IsResettingPostgresDatabase
        {
            get => _isResettingPostgresDatabase;
            set
            {
                if (SetProperty(ref _isResettingPostgresDatabase, value))
                {
                    OnPropertyChanged(nameof(CanResetPostgresDatabase));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanResetPostgresDatabase =>
            !_appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
            && !IsResettingPostgresDatabase
            && !IsPostgresTesting
            && !IsPostgresMigrating
            && !string.IsNullOrWhiteSpace(_appSettingsService.Settings.PostgresMigrationCompletedAtUtc);

        public string PostgresResetHint =>
            _appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
                ? Res("SettingsPostgresResetHintActive")
                : Res("SettingsPostgresResetHintReady");

        public string PostgresTailscaleIpDisplay =>
            string.IsNullOrWhiteSpace(_postgresNetworkAccessService.TailscaleIpAddress)
                ? Res("SettingsTailscaleIpMissing")
                : _postgresNetworkAccessService.TailscaleIpAddress;

        public string PostgresLanIpDisplay
        {
            get
            {
                var lanIp = _postgresNetworkAccessService.LanIpAddress;
                var lanCidr = _postgresNetworkAccessService.LanCidr;
                if (string.IsNullOrWhiteSpace(lanIp))
                    return Res("SettingsLanIpMissing");

                return string.IsNullOrWhiteSpace(lanCidr) ? lanIp : $"{lanIp} ({lanCidr})";
            }
        }

        public string PostgresNetworkAccessStatus
        {
            get => _postgresNetworkAccessStatus;
            set => SetProperty(ref _postgresNetworkAccessStatus, value);
        }

        public bool IsConfiguringPostgresNetworkAccess
        {
            get => _isConfiguringPostgresNetworkAccess;
            set
            {
                if (SetProperty(ref _isConfiguringPostgresNetworkAccess, value))
                {
                    OnPropertyChanged(nameof(CanConfigurePostgresTailscaleAccess));
                    OnPropertyChanged(nameof(CanConfigurePostgresLanAccess));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanConfigurePostgresTailscaleAccess =>
            !IsConfiguringPostgresNetworkAccess && !IsPostgresTesting && !IsPostgresMigrating;

        public bool CanConfigurePostgresLanAccess =>
            !IsConfiguringPostgresNetworkAccess && !IsPostgresTesting && !IsPostgresMigrating;

        public string CurrentDatabaseStorageMode => _appSettingsService.Settings.DatabaseStorageMode;

        public bool IsSqliteStorageMode =>
            !string.Equals(CurrentDatabaseStorageMode, DatabaseStorageModes.Postgres, StringComparison.OrdinalIgnoreCase);

        public bool IsPostgresStorageMode =>
            DatabaseStorageModes.PostgresRuntimeStorageEnabled
            && string.Equals(CurrentDatabaseStorageMode, DatabaseStorageModes.Postgres, StringComparison.OrdinalIgnoreCase);

        public bool HasDatabaseModeRestartPending =>
            !string.Equals(
                CurrentDatabaseStorageMode,
                _appDataStorageFactory.ActiveRuntimeModeAtStartup,
                StringComparison.OrdinalIgnoreCase);

        public bool CanEnablePostgresStorageMode =>
            DatabaseStorageModes.PostgresRuntimeStorageEnabled
            && !string.IsNullOrWhiteSpace(_appSettingsService.Settings.PostgresMigrationCompletedAtUtc)
            && !string.IsNullOrWhiteSpace(_appSettingsService.Settings.PostgresConnectionString);

        public bool IsPostgresMigrationCompleted =>
            !string.IsNullOrWhiteSpace(_appSettingsService.Settings.PostgresMigrationCompletedAtUtc);

        public bool CanRunPostgresMigration =>
            !IsPostgresMigrationCompleted
            && !IsPostgresTesting
            && !IsPostgresMigrating;

        public bool CanAttachExistingPostgresDatabase =>
            !_appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
            && !IsPostgresTesting
            && !IsPostgresMigrating;

        public bool CanReplacePostgresFromSqlite =>
            IsPostgresMigrationCompleted
            && !_appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
            && !IsPostgresTesting
            && !IsPostgresMigrating;

        public string PostgresMigrationActionText =>
            IsPostgresMigrationCompleted
                ? Res("SettingsMigrationCompletedAction")
                : Res("SettingsMigrationPrepareAction");

        public string ReplacePostgresFromSqliteActionText => Res("SettingsReplacePgFromSqliteAction");

        public string PostgresModeStatus
        {
            get => string.IsNullOrWhiteSpace(_postgresModeStatus)
                ? BuildPostgresModeStatus()
                : _postgresModeStatus;
            set => SetProperty(ref _postgresModeStatus, value);
        }

        public string PostgresHost
        {
            get => _postgresHost;
            set
            {
                if (SetProperty(ref _postgresHost, value))
                {
                    SavePostgresLoginSettings();
                    RaiseSecondPcDatabaseAccessPropertiesChanged();
                }
            }
        }

        public string PostgresPort
        {
            get => _postgresPort;
            set
            {
                if (SetProperty(ref _postgresPort, value))
                {
                    SavePostgresLoginSettings();
                    RaiseSecondPcDatabaseAccessPropertiesChanged();
                }
            }
        }

        public string PostgresDatabase
        {
            get => _postgresDatabase;
            set
            {
                if (SetProperty(ref _postgresDatabase, value))
                {
                    SavePostgresLoginSettings();
                    RaiseSecondPcDatabaseAccessPropertiesChanged();
                }
            }
        }

        public string PostgresUsername
        {
            get => _postgresUsername;
            set
            {
                if (SetProperty(ref _postgresUsername, value))
                {
                    SavePostgresLoginSettings();
                    RaiseSecondPcDatabaseAccessPropertiesChanged();
                }
            }
        }

        public string PostgresPassword
        {
            get => _postgresPassword;
            set
            {
                if (SetProperty(ref _postgresPassword, value))
                    SavePostgresLoginSettings();
            }
        }

        public string PostgresDataDirectoryPath
        {
            get => _postgresDataDirectoryPath;
            set
            {
                if (SetProperty(ref _postgresDataDirectoryPath, value?.Trim() ?? string.Empty))
                {
                    SavePostgresDataDirectoryPath();
                    OnPropertyChanged(nameof(PostgresDataDirectoryStatus));
                }
            }
        }

        public string PostgresDataDirectoryStatus
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PostgresDataDirectoryPath))
                    return Res("SettingsPostgresDataDirectoryAuto");

                return PostgresNetworkAccessService.IsValidPostgresDataDirectory(PostgresDataDirectoryPath)
                    ? Res("SettingsPostgresDataDirectoryValid")
                    : Res("SettingsPostgresDataDirectoryInvalid");
            }
        }

        public string PostgresTestStatus
        {
            get => _postgresTestStatus;
            set => SetProperty(ref _postgresTestStatus, value);
        }

        public bool IsPostgresTesting
        {
            get => _isPostgresTesting;
            set
            {
                if (SetProperty(ref _isPostgresTesting, value))
                    RaiseMigrationActionPropertiesChanged();
            }
        }

        public string PostgresMigrationStatus
        {
            get => _postgresMigrationStatus;
            set => SetProperty(ref _postgresMigrationStatus, value);
        }

        public bool IsPostgresMigrating
        {
            get => _isPostgresMigrating;
            set
            {
                if (SetProperty(ref _isPostgresMigrating, value))
                    RaiseMigrationActionPropertiesChanged();
            }
        }

        private string _webPanelActionStatus = string.Empty;
        public string WebPanelActionStatus
        {
            get => _webPanelActionStatus;
            set => SetProperty(ref _webPanelActionStatus, value);
        }

        public bool TelegramEnabled
        {
            get => _telegramEnabled;
            set
            {
                if (!SetProperty(ref _telegramEnabled, value))
                    return;

                _appSettingsService.Settings.Telegram.Enabled = value;
                _appSettingsService.SaveSettings();

                if (!value)
                {
                    _telegramBotService.Stop();
                    TelegramBotStatus = Res("SettingsTelegramBotDisabled");
                }
                else if (HasTelegramBotToken)
                {
                    _ = RestartTelegramBotAsync();
                }
                else
                {
                    TelegramBotStatus = Res("SettingsTelegramTokenMissing");
                }

                OnPropertyChanged(nameof(TelegramNeedsToken));
                OnPropertyChanged(nameof(TelegramCanGenerateQr));
            }
        }

        public bool TelegramNeedsToken => TelegramEnabled && !HasTelegramBotToken;

        public bool TelegramIsBusy
        {
            get => _telegramIsBusy;
            set => SetProperty(ref _telegramIsBusy, value);
        }

        public string TelegramBotTokenInput
        {
            get => _telegramBotTokenInput;
            set => SetProperty(ref _telegramBotTokenInput, value);
        }

        public string TelegramBotStatus
        {
            get => _telegramBotStatus;
            set => SetProperty(ref _telegramBotStatus, value);
        }

        public string TelegramBotUsername => _appSettingsService.Settings.Telegram.BotUsername;

        public bool HasTelegramBotToken => !string.IsNullOrWhiteSpace(_appSettingsService.Settings.Telegram.EncryptedBotToken);

        public bool TelegramIsConnected => TelegramEnabled && HasTelegramBotToken;

        public BitmapImage? TelegramQrCodeImage
        {
            get => _telegramQrCodeImage;
            set => SetProperty(ref _telegramQrCodeImage, value);
        }

        public bool HasTelegramQrCode => TelegramQrCodeImage != null;

        public string TelegramPairingCode
        {
            get => _telegramPairingCode;
            set => SetProperty(ref _telegramPairingCode, value);
        }

        public string TelegramPairingExpiryText
        {
            get => _telegramPairingExpiryText;
            set => SetProperty(ref _telegramPairingExpiryText, value);
        }

        public bool HasTelegramAuthorizedUsers => TelegramAuthorizedUsers.Count > 0;

        public bool TelegramAllowAiQuestions
        {
            get => _appSettingsService.Settings.Telegram.AllowAiQuestions;
            set
            {
                if (_appSettingsService.Settings.Telegram.AllowAiQuestions == value)
                    return;

                _appSettingsService.Settings.Telegram.AllowAiQuestions = value;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        public bool TelegramDailyDigestEnabled
        {
            get => _appSettingsService.Settings.Telegram.DailyDigestEnabled;
            set
            {
                if (_appSettingsService.Settings.Telegram.DailyDigestEnabled == value)
                    return;

                _appSettingsService.Settings.Telegram.DailyDigestEnabled = value;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        public string TelegramDailyDigestTime
        {
            get => _appSettingsService.Settings.Telegram.DailyDigestTime;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "08:00" : value.Trim();
                if (_appSettingsService.Settings.Telegram.DailyDigestTime == normalized)
                    return;

                _appSettingsService.Settings.Telegram.DailyDigestTime = normalized;
                _appSettingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        public bool TelegramCanGenerateQr => TelegramIsConnected && !string.IsNullOrWhiteSpace(TelegramBotUsername);

        public string AccessStatusTitle => _accessStatusService.Title ?? string.Empty;
        public string AccessStatusDetail => _accessStatusService.Detail ?? string.Empty;
        public string AccessStatusAdminMessage => _accessStatusService.AdminMessage ?? string.Empty;
        public bool HasAccessStatusAdminMessage => _accessStatusService.HasAdminMessage;
        public string AccessPlanCode => NormalizeAccessPlan(_accessStatusService.Plan);
        public string AccessPlanDisplay => FormatPlanDisplay(AccessPlanCode);
        public bool HasAccessPlan => !string.IsNullOrWhiteSpace(AccessPlanCode);
        public string AccessStatusSeverity => _accessStatusService.Severity ?? "Info";
        public string MachineId => Services.LicenseService.GetMachineId();
        public string UsersAccessModeDisplay => _appSettingsService.Settings.PermissionSoftMode
            ? Res("SettingsUsersSoftMode")
            : Res("SettingsUsersHardMode");
        public string UsersBusinessAvailabilityDisplay => AccessPlanCode == "business"
            ? Res("SettingsUsersBusinessActive")
            : Res("SettingsUsersAvailableForTesting");
        public string UsersTenantDisplay => string.IsNullOrWhiteSpace(_currentProfileService.CurrentProfile?.TenantId)
            ? Res("SettingsUsersTenantNotAssigned")
            : _currentProfileService.CurrentProfile!.TenantId;
        public bool HasUserRolePreviewItems => UserRolePreviewItems.Count > 0;
        public bool IsBusinessUserEditorOpen
        {
            get => _isBusinessUserEditorOpen;
            set => SetProperty(ref _isBusinessUserEditorOpen, value);
        }

        public bool IsEditingBusinessUser => !string.IsNullOrWhiteSpace(_editingBusinessUserId);
        public string BusinessUserEditorTitle => IsEditingBusinessUser
            ? Res("SettingsUsersEditorEditTitle")
            : Res("SettingsUsersEditorTitle");
        public string BusinessUserEditorPasswordHint => IsEditingBusinessUser
            ? Res("SettingsUsersPasswordEditHint")
            : Res("SettingsUsersTempPasswordHint");

        public string BusinessUserFirstName
        {
            get => _businessUserFirstName;
            set
            {
                if (SetProperty(ref _businessUserFirstName, value))
                {
                    UpdateBusinessUserGeneratedLogin();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string BusinessUserLastName
        {
            get => _businessUserLastName;
            set
            {
                if (SetProperty(ref _businessUserLastName, value))
                {
                    UpdateBusinessUserGeneratedLogin();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string BusinessUserLogin
        {
            get => _businessUserLogin;
            set
            {
                if (SetProperty(ref _businessUserLogin, NormalizeBusinessUserLogin(value)))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string BusinessUserTemporaryPassword
        {
            get => _businessUserTemporaryPassword;
            set
            {
                if (SetProperty(ref _businessUserTemporaryPassword, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string BusinessUserRoleKey
        {
            get => _businessUserRoleKey;
            set
            {
                if (!SetProperty(ref _businessUserRoleKey, value))
                    return;

                if (!_isInitializingBusinessUserEditor)
                    ApplyBusinessUserRoleDefaults(value);
            }
        }

        public bool BusinessUserIsActive
        {
            get => _businessUserIsActive;
            set => SetProperty(ref _businessUserIsActive, value);
        }

        public string BusinessUserScopeMode
        {
            get => _businessUserScopeMode;
            set
            {
                if (SetProperty(ref _businessUserScopeMode, value))
                {
                    OnPropertyChanged(nameof(ShowBusinessUserAgencySelector));
                    RefreshBusinessUserEmployerEditorItems();
                }
            }
        }

        public bool ShowBusinessUserAgencySelector =>
            BusinessUserScopeMode is "SelectedAgencies" or "SelectedAgenciesAndEmployers";

        public string BusinessUserEditorStatus
        {
            get => _businessUserEditorStatus;
            set => SetProperty(ref _businessUserEditorStatus, value);
        }

        public bool CanSaveBusinessUser =>
            !string.IsNullOrWhiteSpace(BusinessUserFirstName)
            && !string.IsNullOrWhiteSpace(BusinessUserLastName)
            && !string.IsNullOrWhiteSpace(BusinessUserLogin)
            && (IsEditingBusinessUser || !string.IsNullOrWhiteSpace(BusinessUserTemporaryPassword));

        public string SelectedBusinessUserLoginId
        {
            get => _selectedBusinessUserLoginId;
            set
            {
                if (SetProperty(ref _selectedBusinessUserLoginId, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string BusinessUserLoginPassword
        {
            get => _businessUserLoginPassword;
            set
            {
                if (SetProperty(ref _businessUserLoginPassword, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string BusinessUserLoginStatus
        {
            get => _businessUserLoginStatus;
            set => SetProperty(ref _businessUserLoginStatus, value);
        }

        public bool HasBusinessUserLoginOptions => BusinessUserLoginOptions.Count > 0;
        public bool IsBusinessUserSessionActive => _currentProfileService.CurrentBusinessUser != null;
        public string CurrentBusinessUserDisplayName => _currentProfileService.CurrentBusinessUser == null
            ? Res("SettingsUsersNoActiveBusinessUser")
            : $"{_currentProfileService.CurrentBusinessUser.FirstName} {_currentProfileService.CurrentBusinessUser.LastName}".Trim();
        public string CurrentBusinessUserPermissionSummary => BuildCurrentBusinessUserPermissionSummary();
        public bool CanLoginBusinessUser =>
            !string.IsNullOrWhiteSpace(SelectedBusinessUserLoginId)
            && !string.IsNullOrWhiteSpace(BusinessUserLoginPassword);

        public string BusinessUserCurrentPassword
        {
            get => _businessUserCurrentPassword;
            set
            {
                if (SetProperty(ref _businessUserCurrentPassword, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string BusinessUserNewPassword
        {
            get => _businessUserNewPassword;
            set
            {
                if (SetProperty(ref _businessUserNewPassword, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string BusinessUserConfirmPassword
        {
            get => _businessUserConfirmPassword;
            set
            {
                if (SetProperty(ref _businessUserConfirmPassword, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string BusinessUserPasswordStatus
        {
            get => _businessUserPasswordStatus;
            set => SetProperty(ref _businessUserPasswordStatus, value);
        }

        public bool BusinessUserMustChangePassword => _currentProfileService.CurrentBusinessUser?.MustChangePassword == true;
        public string BusinessUserPasswordHint => BusinessUserMustChangePassword
            ? Res("SettingsUsersMustChangePasswordHint")
            : Res("SettingsUsersChangePasswordHint");
        public bool CanChangeBusinessUserPassword =>
            IsBusinessUserSessionActive
            && !string.IsNullOrWhiteSpace(BusinessUserCurrentPassword)
            && !string.IsNullOrWhiteSpace(BusinessUserNewPassword)
            && !string.IsNullOrWhiteSpace(BusinessUserConfirmPassword);

        public bool HasProfile => _currentProfileService.CurrentProfile != null;
        public string ProfileClientId => _currentProfileService.CurrentProfile?.ClientId ?? string.Empty;

        public string ProfileFirstName
        {
            get => _profileFirstName;
            set
            {
                if (SetProperty(ref _profileFirstName, value))
                    OnPropertyChanged(nameof(ProfileInitials));
            }
        }

        public string ProfileLastName
        {
            get => _profileLastName;
            set
            {
                if (SetProperty(ref _profileLastName, value))
                    OnPropertyChanged(nameof(ProfileInitials));
            }
        }

        public string ProfileInitials
        {
            get
            {
                var first = (_profileFirstName ?? string.Empty).Trim();
                var last = (_profileLastName ?? string.Empty).Trim();
                var initials = string.Concat(
                    first.Length > 0 ? char.ToUpperInvariant(first[0]).ToString() : string.Empty,
                    last.Length > 0 ? char.ToUpperInvariant(last[0]).ToString() : string.Empty);
                return string.IsNullOrEmpty(initials) ? "?" : initials;
            }
        }

        public bool ProfileRememberMeEnabled
        {
            get => _profileRememberMeEnabled;
            set
            {
                if (!SetProperty(ref _profileRememberMeEnabled, value))
                    return;

                if (_isInitializingProfileFields || _isSyncingRememberMe || _currentProfileService.CurrentProfile == null)
                    return;

                _ = SyncRememberMeAsync(value);
            }
        }

        public bool MemberRememberMeEnabled
        {
            get => _memberRememberMeEnabled;
            set
            {
                if (!SetProperty(ref _memberRememberMeEnabled, value))
                    return;

                if (_isSyncingMemberRememberMe || _currentProfileService.CurrentBusinessUser == null)
                    return;

                SyncMemberRememberMe(value);
            }
        }

        public string ProfileCurrentPassword
        {
            get => _profileCurrentPassword;
            set => SetProperty(ref _profileCurrentPassword, value);
        }

        public string ProfileNewPassword
        {
            get => _profileNewPassword;
            set => SetProperty(ref _profileNewPassword, value);
        }

        public string ProfileConfirmPassword
        {
            get => _profileConfirmPassword;
            set => SetProperty(ref _profileConfirmPassword, value);
        }

        public string ProfileStatusMessage
        {
            get => _profileStatusMessage;
            set => SetProperty(ref _profileStatusMessage, value);
        }

        public bool ProfileStatusIsError
        {
            get => _profileStatusIsError;
            set => SetProperty(ref _profileStatusIsError, value);
        }

        public bool IsProfileEditMode
        {
            get => _isProfileEditMode;
            set => SetProperty(ref _isProfileEditMode, value);
        }

        public string GeminiApiKey
        {
            get => _appSettingsService.Settings.GeminiApiKey;
            set
            {
                if (_appSettingsService.Settings.GeminiApiKey != value)
                {
                    _appSettingsService.Settings.GeminiApiKey = value;
                    _appSettingsService.SaveSettings();
                    _geminiApiKeyConfigurationService.RefreshEffectiveApiKey();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasGeminiApiKey));
                    OnPropertyChanged(nameof(GeminiApiKeyMaskedDisplay));
                    OnPropertyChanged(nameof(ShowMaskedGeminiApiKey));
                    OnPropertyChanged(nameof(ShowGeminiApiKeyEditor));
                    OnPropertyChanged(nameof(IsGeminiConfigured));
                    OnPropertyChanged(nameof(IsManagedGeminiKeyActive));
                    OnPropertyChanged(nameof(ShowGeminiAccessHint));
                    OnPropertyChanged(nameof(GeminiAccessHint));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool HasGeminiApiKey => !string.IsNullOrWhiteSpace(_appSettingsService.Settings.GeminiApiKey);

        public string GeminiApiKeyMaskedDisplay => HasGeminiApiKey ? "************" : string.Empty;

        public bool IsEditingGeminiApiKey
        {
            get => _isEditingGeminiApiKey;
            set
            {
                if (!SetProperty(ref _isEditingGeminiApiKey, value))
                    return;

                OnPropertyChanged(nameof(ShowMaskedGeminiApiKey));
                OnPropertyChanged(nameof(ShowGeminiApiKeyEditor));
            }
        }

        public bool ShowMaskedGeminiApiKey => HasGeminiApiKey && !IsEditingGeminiApiKey;

        public bool ShowGeminiApiKeyEditor => !HasGeminiApiKey || IsEditingGeminiApiKey;

        public string GeminiApiKeyDraft
        {
            get => _geminiApiKeyDraft;
            set => SetProperty(ref _geminiApiKeyDraft, value);
        }

        public bool IsGeminiConfigured => _geminiApiService.IsConfigured;

        public bool IsManagedGeminiKeyActive =>
            !HasGeminiApiKey
            && !PolicyService.IsAIDisabled
            && _geminiApiService.IsConfigured;

        public bool ShowGeminiAccessHint => !HasGeminiApiKey;

        public string GeminiAccessHint => PolicyService.IsAIDisabled
            ? Res("AIGeminiDisabledByPolicy")
            : IsManagedGeminiKeyActive
                ? Res("AIGeminiManagedKeyActive")
                : Res("AIGeminiKeyMissingOrAdmin");

        public string[] GeminiModels => Services.GeminiApiService.AvailableModels;

        public string GeminiModel
        {
            get => _appSettingsService.Settings.GeminiModel;
            set
            {
                if (_appSettingsService.Settings.GeminiModel != value)
                {
                    _appSettingsService.Settings.GeminiModel = value;
                    _appSettingsService.SaveSettings();
                    _geminiApiService.SetModel(value);
                    OnPropertyChanged();
                }
            }
        }

        private string _geminiTestResult = "";
        public string GeminiTestResult
        {
            get => _geminiTestResult;
            set => SetProperty(ref _geminiTestResult, value);
        }

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

        public ObservableCollection<AccentPresetItem> AccentPresets { get; } = new();

        public string CurrentAccentColor
        {
            get => _appSettingsService.Settings.AccentColor ?? string.Empty;
            set
            {
                var normalized = value ?? string.Empty;
                if (_appSettingsService.Settings.AccentColor != normalized)
                {
                    _themeService.SetAccentColor(normalized);
                    OnPropertyChanged(nameof(CurrentAccentColor));
                    RefreshAccentSelection();
                }
            }
        }

        private void InitializeAccentPresets()
        {
            AccentPresets.Clear();
            foreach (var preset in ThemeService.AccentPresets)
            {
                AccentPresets.Add(new AccentPresetItem(preset.Name, preset.Hex));
            }
            RefreshAccentSelection();
        }

        private void RefreshAccentSelection()
        {
            var current = CurrentAccentColor ?? string.Empty;
            foreach (var item in AccentPresets)
            {
                item.IsSelected = string.Equals(item.Hex ?? string.Empty, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        public class AccentPresetItem : ViewModelBase
        {
            public AccentPresetItem(string name, string hex)
            {
                Name = name;
                Hex = hex ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(Hex))
                {
                    try
                    {
                        var obj = System.Windows.Media.ColorConverter.ConvertFromString(Hex);
                        if (obj is System.Windows.Media.Color c)
                        {
                            var brush = new System.Windows.Media.SolidColorBrush(c);
                            brush.Freeze();
                            Brush = brush;
                        }
                    }
                    catch { }
                }
                Brush ??= System.Windows.Media.Brushes.Transparent;
            }

            public string Name { get; }
            public string Hex { get; }
            public System.Windows.Media.Brush Brush { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set => SetProperty(ref _isSelected, value);
            }

            public bool IsDefault => string.IsNullOrEmpty(Hex);
        }

        private string _currentInterfaceSize = "Medium";
        public string CurrentInterfaceSize
        {
            get => _currentInterfaceSize;
            set => SetProperty(ref _currentInterfaceSize, value);
        }

        private string _currentTextSize = "Medium";
        public string CurrentTextSize
        {
            get => _currentTextSize;
            set => SetProperty(ref _currentTextSize, value);
        }

        private string _currentDocLanguage = "";
        public string CurrentDocLanguage
        {
            get => _currentDocLanguage;
            set => SetProperty(ref _currentDocLanguage, value);
        }

        public SettingsViewModel(
            NavigationService? navigationService = null,
            AppSettingsService? appSettingsService = null,
            FolderService? folderService = null,
            ThemeService? themeService = null,
            LanguageService? languageService = null,
            CompanyService? companyService = null,
            TagCatalogService? tagCatalogService = null,
            ProfileAuthService? profileAuthService = null,
            ProfileSessionService? profileSessionService = null,
            AccessStatusService? accessStatusService = null,
            ActivityLogService? activityLogService = null,
            GeminiApiService? geminiApiService = null,
            DocumentLocalizationService? documentLocalizationService = null,
            CurrentProfileService? currentProfileService = null,
            GeminiApiKeyConfigurationService? geminiApiKeyConfigurationService = null,
            TelegramBotService? telegramBotService = null,
            TelegramPairingService? telegramPairingService = null,
            WebPanelHostService? webPanelHostService = null,
            SyncEventService? syncEventService = null,
            PostgresConnectionTestService? postgresConnectionTestService = null,
            PostgresMigrationService? postgresMigrationService = null,
            PostgresToSqliteBackupService? postgresToSqliteBackupService = null,
            PostgresResetService? postgresResetService = null,
            PostgresNetworkAccessService? postgresNetworkAccessService = null,
            AppDataStorageFactory? appDataStorageFactory = null,
            ConnectedClientsService? connectedClientsService = null,
            BusinessUserAuthService? businessUserAuthService = null,
            BusinessUserDirectoryService? businessUserDirectoryService = null,
            BusinessUserSessionService? businessUserSessionService = null,
            WorkspaceSessionService? workspaceSessionService = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _folderService = folderService ?? throw new InvalidOperationException("FolderService is not initialized.");
            _themeService = themeService ?? throw new InvalidOperationException("ThemeService is not initialized.");
            _languageService = languageService ?? throw new InvalidOperationException("LanguageService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _profileAuthService = profileAuthService ?? throw new InvalidOperationException("ProfileAuthService is not initialized.");
            _profileSessionService = profileSessionService ?? throw new InvalidOperationException("ProfileSessionService is not initialized.");
            _accessStatusService = accessStatusService ?? throw new InvalidOperationException("AccessStatusService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _geminiApiService = geminiApiService ?? throw new InvalidOperationException("GeminiApiService is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");
            _currentProfileService = currentProfileService ?? throw new InvalidOperationException("CurrentProfileService is not initialized.");
            _geminiApiKeyConfigurationService = geminiApiKeyConfigurationService ?? throw new InvalidOperationException("GeminiApiKeyConfigurationService is not initialized.");
            _telegramBotService = telegramBotService ?? throw new InvalidOperationException("TelegramBotService is not initialized.");
            _telegramPairingService = telegramPairingService ?? throw new InvalidOperationException("TelegramPairingService is not initialized.");
            _webPanelHostService = webPanelHostService ?? throw new InvalidOperationException("WebPanelHostService is not initialized.");
            _syncEventService = syncEventService ?? throw new InvalidOperationException("SyncEventService is not initialized.");
            _postgresConnectionTestService = postgresConnectionTestService ?? throw new InvalidOperationException("PostgresConnectionTestService is not initialized.");
            _postgresMigrationService = postgresMigrationService ?? throw new InvalidOperationException("PostgresMigrationService is not initialized.");
            _postgresToSqliteBackupService = postgresToSqliteBackupService ?? throw new InvalidOperationException("PostgresToSqliteBackupService is not initialized.");
            _postgresResetService = postgresResetService ?? throw new InvalidOperationException("PostgresResetService is not initialized.");
            _postgresNetworkAccessService = postgresNetworkAccessService ?? throw new InvalidOperationException("PostgresNetworkAccessService is not initialized.");
            _appDataStorageFactory = appDataStorageFactory ?? throw new InvalidOperationException("AppDataStorageFactory is not initialized.");
            _connectedClientsService = connectedClientsService ?? throw new InvalidOperationException("ConnectedClientsService is not initialized.");
            _businessUserAuthService = businessUserAuthService ?? throw new InvalidOperationException("BusinessUserAuthService is not initialized.");
            _businessUserDirectoryService = businessUserDirectoryService ?? throw new InvalidOperationException("BusinessUserDirectoryService is not initialized.");
            _businessUserSessionService = businessUserSessionService ?? throw new InvalidOperationException("BusinessUserSessionService is not initialized.");
            _workspaceSessionService = workspaceSessionService ?? throw new InvalidOperationException("WorkspaceSessionService is not initialized.");

            _currentLanguage = _appSettingsService.Settings.LanguageCode;
            _currentTheme = DetectCurrentTheme();
            _currentInterfaceSize = _appSettingsService.Settings.InterfaceSize ?? "Medium";
            _currentTextSize = _appSettingsService.Settings.TextSize ?? "Medium";
            _currentDocLanguage = _appSettingsService.Settings.DocumentLanguage ?? "";
            _isEditingGeminiApiKey = string.IsNullOrWhiteSpace(_appSettingsService.Settings.GeminiApiKey);
            _telegramEnabled = _appSettingsService.Settings.Telegram.Enabled;
            LoadPostgresLoginSettings();
            InitializeProfileFields();
            InitializeMemberAccountFields();
            InitializeBusinessUserEditorOptions();
            RefreshBusinessUserLoginOptions();
            RefreshUserRolePreview();
            InitializeAccentPresets();
            RefreshTelegramState();
            EnsureValidSettingsSection();
            RefreshWorkspaceIdentityDisplay();
            _accessStatusService.PropertyChanged += AccessStatusService_PropertyChanged;
            _telegramBotService.StateChanged += TelegramBotService_StateChanged;

            GoBackCommand = new RelayCommand(o =>
            {
                _navigationService.NavigateTo<MainViewModel>();
            });

            ChangeLanguageCommand = new RelayCommand(param =>
            {
                if (param is string code)
                {
                    _languageService.SetLanguage(code);
                    _accessStatusService.RefreshPresentation();
                    CurrentLanguage = code;
                    RaiseAccessStatusPropertiesChanged();
                    RaiseLocalizedSettingsPropertiesChanged();
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

            ChangeAccentColorCommand = new RelayCommand(param =>
            {
                var hex = param as string ?? string.Empty;
                CurrentAccentColor = hex;
            });

            SelectRootFolderCommand = new RelayCommand(o =>
            {
                var dialog = new OpenFolderDialog();
                if (dialog.ShowDialog() == true)
                {
                    RootFolderPath = dialog.FolderName;
                }
            });

            SelectPostgresDataDirectoryCommand = new RelayCommand(o =>
            {
                var dialog = new OpenFolderDialog
                {
                    Title = Res("SettingsPostgresDataDirectorySelectTitle")
                };

                if (!string.IsNullOrWhiteSpace(PostgresDataDirectoryPath)
                    && System.IO.Directory.Exists(PostgresDataDirectoryPath))
                {
                    dialog.InitialDirectory = PostgresDataDirectoryPath;
                }

                if (dialog.ShowDialog() == true)
                {
                    var selectedPath = ResolvePostgresDataDirectorySelection(dialog.FolderName);
                    PostgresDataDirectoryPath = selectedPath;
                    PostgresNetworkAccessStatus = PostgresNetworkAccessService.IsValidPostgresDataDirectory(selectedPath)
                        ? selectedPath.Equals(dialog.FolderName, StringComparison.OrdinalIgnoreCase)
                            ? Res("SettingsPostgresDataDirectorySaved")
                            : string.Format(Res("SettingsPostgresDataDirectoryAutoDetectedFmt"), selectedPath)
                        : Res("SettingsPostgresDataDirectoryInvalid");
                }
            });

            OpenTagVisibilityCommand = new RelayCommand(o =>
            {
                var window = new TagVisibilityWindow(_appSettingsService, _tagCatalogService)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                window.ShowDialog();
            });

            OpenCompanyVisibilityCommand = new RelayCommand(o =>
            {
                CompanyVisibilityItems.Clear();
                foreach (var company in _companyService.Companies)
                    CompanyVisibilityItems.Add(new CompanyVisibilityItem(company, _companyService));
                IsCompanyVisibilityOpen = true;
            });

            CloseCompanyVisibilityCommand = new RelayCommand(o =>
            {
                IsCompanyVisibilityOpen = false;
            });

            ChangeInterfaceSizeCommand = new RelayCommand(param =>
            {
                if (param is string size)
                {
                    CurrentInterfaceSize = size;
                    _appSettingsService.Settings.InterfaceSize = size;
                    _appSettingsService.SaveSettings();
                    ApplyInterfaceSize(size);
                }
            });

            ChangeTextSizeCommand = new RelayCommand(param =>
            {
                if (param is string size)
                {
                    CurrentTextSize = size;
                    _appSettingsService.Settings.TextSize = size;
                    _appSettingsService.SaveSettings();
                    ApplyTextSize(size);
                }
            });

            ChangeDocLanguageCommand = new RelayCommand(param =>
            {
                if (param is string lang)
                {
                    CurrentDocLanguage = lang;
                    _appSettingsService.Settings.DocumentLanguage = lang;
                    _appSettingsService.SaveSettings();
                    _documentLocalizationService.LoadLanguage(lang);
                }
            });

            TestGeminiCommand = new RelayCommand(async o =>
            {
                GeminiTestResult = "Testing...";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var (success, msg) = await _geminiApiService.TestConnectionAsync(cts.Token);
                GeminiTestResult = msg;
            }, o => IsGeminiConfigured);

            OpenGeminiSiteCommand = new RelayCommand(o =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aistudio.google.com/apikey") { UseShellExecute = true }); }
                catch (Exception ex) { LoggingService.LogWarning("Settings.OpenGeminiSite", ex.Message); }
            });

            CheckForUpdatesCommand = new RelayCommand(async o =>
            {
                IsUpdateChecking = true;
                IsUpdateAvailable = false;
                _pendingUpdate = null;
                UpdateStatus = Res("UpdateChecking");
                try
                {
                    var update = await Services.UpdateService.CheckForUpdatesAsync(PolicyService.CurrentPolicy.UpdateChannel);
                    if (update != null)
                    {
                        _pendingUpdate = update;
                        IsUpdateAvailable = true;
                        UpdateStatus = $"{Res("UpdateAvailable")} v{update.TargetFullRelease.Version}";
                    }
                    else
                    {
                        UpdateStatus = Res("UpdateLatest");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus = $"{Res("UpdateError")}: {ex.Message}";
                }
                IsUpdateChecking = false;
            });

            InstallUpdateCommand = new RelayCommand(async o =>
            {
                if (_pendingUpdate == null) return;
                IsUpdateChecking = true;
                UpdateStatus = Res("UpdateDownloading");
                var errorMessage = await Services.UpdateService.DownloadAndApplyAsync(
                    _pendingUpdate,
                    progress =>
                    {
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            UpdateStatus = $"{Res("UpdateDownloading")} {progress}%"));
                    },
                    PolicyService.CurrentPolicy.UpdateChannel);
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    UpdateStatus = $"{Res("UpdateError")}: {errorMessage}";
                    IsUpdateChecking = false;
                }
            });

            SaveProfileCommand = new AsyncRelayCommand(_ => SaveProfileAsync(), _ => HasProfile);
            LogoutProfileCommand = new AsyncRelayCommand(_ => LogoutProfileAsync(), _ => HasProfile);
            ChangeProfilePasswordCommand = new AsyncRelayCommand(_ => ChangeProfilePasswordAsync(), _ => HasProfile);
            EditProfileCommand = new RelayCommand(_ =>
            {
                InitializeProfileFields();
                ClearProfilePasswordFields();
                SetProfileStatus(string.Empty, false);
                IsProfileEditMode = true;
            }, _ => HasProfile);
            CancelProfileEditCommand = new RelayCommand(_ =>
            {
                InitializeProfileFields();
                ClearProfilePasswordFields();
                SetProfileStatus(string.Empty, false);
                IsProfileEditMode = false;
            }, _ => HasProfile);

            ConnectTelegramBotCommand = new AsyncRelayCommand(_ => ConnectTelegramBotAsync(), _ => !TelegramIsBusy && TelegramEnabled);
            DisconnectTelegramBotCommand = new AsyncRelayCommand(_ => DisconnectTelegramBotAsync(), _ => !TelegramIsBusy && HasTelegramBotToken);
            GenerateTelegramQrCommand = new RelayCommand(_ => GenerateTelegramQr(), _ => TelegramCanGenerateQr);
            RemoveTelegramAuthorizedUserCommand = new RelayCommand(param =>
            {
                if (param is long userId)
                {
                    _telegramBotService.RemoveAuthorizedUser(userId);
                    RefreshTelegramState();
                }
            });
            OpenBotFatherCommand = new RelayCommand(_ =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://t.me/BotFather")
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("Settings.OpenBotFather", ex.Message);
                }
            });

            CopyWebPanelUrlCommand = new RelayCommand(_ =>
            {
                try
                {
                    Clipboard.SetText(WebPanelUrl);
                    WebPanelActionStatus = Res("SettingsAddressCopied");
                }
                catch (Exception ex)
                {
                    WebPanelActionStatus = Res("SettingsAddressCopyFailed");
                    LoggingService.LogWarning("Settings.CopyWebPanelUrl", ex.Message);
                }
            });

            CopyWebPanelLanUrlCommand = new RelayCommand(async _ =>
            {
                await PrepareAndCopyWebPanelLanAccessAsync();
            });

            CopySecondPcDatabaseAccessCommand = new RelayCommand(_ =>
            {
                CopySecondPcDatabaseAccess();
            });

            RefreshConnectedClientsCommand = new RelayCommand(_ =>
            {
                RefreshConnectedClients();
            });

            ChangeSettingsSectionCommand = new RelayCommand(param =>
            {
                if (param is not string section)
                    return;

                if (IsMemberSettingsSession)
                    return;

                if (section.Equals("Servers", StringComparison.OrdinalIgnoreCase)
                    && !CanShowOwnerSettingsSections)
                {
                    return;
                }

                if (section.Equals("Users", StringComparison.OrdinalIgnoreCase)
                    && !CanShowUsersSettingsTab)
                {
                    return;
                }

                SetCurrentSettingsSection(section, force: true);
            });

            OpenBusinessUserEditorCommand = new RelayCommand(_ => OpenBusinessUserEditor());
            EditBusinessUserCommand = new RelayCommand(param => EditBusinessUser(param as string), param => param is string id && !string.IsNullOrWhiteSpace(id));
            CloseBusinessUserEditorCommand = new RelayCommand(_ => IsBusinessUserEditorOpen = false);
            SaveBusinessUserCommand = new RelayCommand(_ => SaveBusinessUser(), _ => CanSaveBusinessUser);
            LoginBusinessUserCommand = new RelayCommand(_ => LoginBusinessUser(), _ => CanLoginBusinessUser);
            LogoutBusinessUserCommand = new RelayCommand(_ => LogoutBusinessUser(), _ => IsBusinessUserSessionActive);
            ChangeBusinessUserPasswordCommand = new RelayCommand(_ => ChangeBusinessUserPassword(), _ => CanChangeBusinessUserPassword);
            RefreshWorkspaceTeamStatusCommand = new RelayCommand(async _ => await RefreshWorkspaceTeamStatusAsync());

            CreateSqliteBackupFromPostgresCommand = new RelayCommand(async _ =>
            {
                await CreateSqliteBackupFromPostgresAsync();
            }, _ => CanCreateSqliteBackupFromPostgres);

            ResetPostgresDatabaseCommand = new RelayCommand(async _ =>
            {
                await ResetPostgresDatabaseAsync();
            }, _ => CanResetPostgresDatabase);

            ConfigurePostgresTailscaleAccessCommand = new RelayCommand(async _ =>
            {
                await ConfigurePostgresTailscaleAccessAsync();
            }, _ => CanConfigurePostgresTailscaleAccess);
            ConfigurePostgresLanAccessCommand = new RelayCommand(async _ =>
            {
                await ConfigurePostgresLanAccessAsync();
            }, _ => CanConfigurePostgresLanAccess);
            OpenNasPostgresGuideCommand = new RelayCommand(_ =>
            {
                OpenNasPostgresGuide();
            });

            OpenWebPanelUrlCommand = new RelayCommand(_ =>
            {
                var url = WebPanelUrl;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
                    WebPanelActionStatus = Res("SettingsWebPanelOpened");
                }
                catch (Exception ex)
                {
                    WebPanelActionStatus = Res("SettingsAddressOpenFailed");
                    LoggingService.LogWarning("Settings.OpenWebPanelUrl", ex.Message);
                }
            });

            RefreshSyncStatusCommand = new RelayCommand(_ =>
            {
                OnPropertyChanged(nameof(SyncEventsFolderPath));
                OnPropertyChanged(nameof(SyncStatusSummary));
            });

            TestPostgresConnectionCommand = new RelayCommand(async _ =>
            {
                await TestPostgresConnectionAsync();
            }, _ => !IsPostgresTesting && !IsPostgresMigrating);

            RunPostgresMigrationCommand = new RelayCommand(async _ =>
            {
                await RunPostgresMigrationAsync();
            }, _ => CanRunPostgresMigration);

            AttachExistingPostgresDatabaseCommand = new RelayCommand(async _ =>
            {
                await AttachExistingPostgresDatabaseAsync();
            }, _ => CanAttachExistingPostgresDatabase);

            ReplacePostgresFromSqliteCommand = new RelayCommand(async _ =>
            {
                await ReplacePostgresFromSqliteAsync();
            }, _ => CanReplacePostgresFromSqlite);

            UseSqliteStorageModeCommand = new RelayCommand(_ => UseSqliteStorageMode());
            EnablePostgresStorageModeCommand = new RelayCommand(_ => EnablePostgresStorageMode(), _ => CanEnablePostgresStorageMode);
            RestartApplicationCommand = new RelayCommand(_ => RestartApplication(), _ => HasDatabaseModeRestartPending);

            RefreshConnectedClients();
        }

        private void AccessStatusService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AccessStatusService.Current)
                or nameof(AccessStatusService.Title)
                or nameof(AccessStatusService.Detail)
                or nameof(AccessStatusService.AdminMessage)
                or nameof(AccessStatusService.HasAdminMessage)
                or nameof(AccessStatusService.Severity)
                or nameof(AccessStatusService.Plan))
            {
                RaiseAccessStatusPropertiesChanged();
            }
        }

        private void RaiseAccessStatusPropertiesChanged()
        {
            OnPropertyChanged(nameof(AccessStatusTitle));
            OnPropertyChanged(nameof(AccessStatusDetail));
            OnPropertyChanged(nameof(AccessStatusAdminMessage));
            OnPropertyChanged(nameof(HasAccessStatusAdminMessage));
            OnPropertyChanged(nameof(AccessStatusSeverity));
            OnPropertyChanged(nameof(AccessPlanCode));
            OnPropertyChanged(nameof(AccessPlanDisplay));
            OnPropertyChanged(nameof(HasAccessPlan));
            OnPropertyChanged(nameof(UsersBusinessAvailabilityDisplay));
            OnPropertyChanged(nameof(CanShowUsersSettingsTab));
            OnPropertyChanged(nameof(CanShowUsersAdminPanel));
            OnPropertyChanged(nameof(IsUsersAdminSettingsSection));
            EnsureValidSettingsSection();
            RefreshUserRolePreview();
        }

        private void SetCurrentSettingsSection(string? value, bool force)
        {
            var normalized = NormalizeSettingsSection(value);
            if (!force && string.Equals(_currentSettingsSection, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            if (SetProperty(ref _currentSettingsSection, normalized, nameof(CurrentSettingsSection)))
            {
                OnPropertyChanged(nameof(IsProgramSettingsSection));
                OnPropertyChanged(nameof(IsServerSettingsSection));
                OnPropertyChanged(nameof(IsUsersSettingsSection));
                OnPropertyChanged(nameof(IsUsersAdminSettingsSection));
                OnPropertyChanged(nameof(IsMemberProgramAccountSection));
                OnPropertyChanged(nameof(IsOwnerProgramSettingsSection));

                if (IsUsersSettingsSection && CanShowWorkspaceTeamPanel)
                    _ = RefreshWorkspaceTeamStatusAsync();
            }
        }

        private string NormalizeSettingsSection(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            normalized = normalized.Equals("Servers", StringComparison.OrdinalIgnoreCase)
                ? "Servers"
                : normalized.Equals("Users", StringComparison.OrdinalIgnoreCase)
                    ? "Users"
                    : "Program";

            if (IsMemberSettingsSession)
                return "Program";

            if (normalized.Equals("Users", StringComparison.OrdinalIgnoreCase) && !CanShowUsersSettingsTab)
                return "Program";

            if (normalized.Equals("Servers", StringComparison.OrdinalIgnoreCase) && !CanShowOwnerSettingsSections)
                return "Program";

            return normalized;
        }

        private void EnsureValidSettingsSection()
        {
            var normalized = NormalizeSettingsSection(_currentSettingsSection);
            if (string.Equals(_currentSettingsSection, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            SetCurrentSettingsSection(normalized, force: true);
        }

        private void RaiseLocalizedSettingsPropertiesChanged()
        {
            LoadPostgresLoginSettings();
            InitializeBusinessUserEditorOptions();
            RefreshBusinessUserLoginOptions();
            RefreshTelegramState();
            RefreshUserRolePreview();
            OnPropertyChanged(nameof(WebPanelStatus));
            OnPropertyChanged(nameof(DatabaseStorageModeDisplay));
            OnPropertyChanged(nameof(ActiveDatabaseRuntimeDisplay));
            OnPropertyChanged(nameof(ActiveDatabaseRuntimeDetail));
            OnPropertyChanged(nameof(SecondPcSharedFolderDisplay));
            OnPropertyChanged(nameof(PostgresResetHint));
            OnPropertyChanged(nameof(PostgresTailscaleIpDisplay));
            OnPropertyChanged(nameof(PostgresLanIpDisplay));
            OnPropertyChanged(nameof(SecondPcLocalServerDisplay));
            OnPropertyChanged(nameof(SecondPcTailscaleServerDisplay));
            OnPropertyChanged(nameof(SecondPcLanServerDisplay));
            OnPropertyChanged(nameof(PostgresDataDirectoryStatus));
            OnPropertyChanged(nameof(PostgresMigrationActionText));
            OnPropertyChanged(nameof(ReplacePostgresFromSqliteActionText));
            OnPropertyChanged(nameof(CanAttachExistingPostgresDatabase));
            OnPropertyChanged(nameof(PostgresModeStatus));
            OnPropertyChanged(nameof(PostgresTestStatus));
            OnPropertyChanged(nameof(PostgresMigrationStatus));
            OnPropertyChanged(nameof(SqliteBackupFromPostgresStatus));
            OnPropertyChanged(nameof(PostgresResetStatus));
            OnPropertyChanged(nameof(PostgresNetworkAccessStatus));
            OnPropertyChanged(nameof(SecondPcDatabaseAccessStatus));
            OnPropertyChanged(nameof(ConnectedClientsStatus));
            OnPropertyChanged(nameof(TelegramBotStatus));
            OnPropertyChanged(nameof(TelegramPairingExpiryText));
            RefreshConnectedClients();
        }

        private async System.Threading.Tasks.Task TestPostgresConnectionAsync()
        {
            if (!int.TryParse(PostgresPort, out var port))
            {
                PostgresTestStatus = Res("SettingsPostgresPortMustBeNumber");
                return;
            }

            SavePostgresLoginSettings();
            IsPostgresTesting = true;
            CommandManager.InvalidateRequerySuggested();
            PostgresTestStatus = string.Format(Res("SettingsPostgresCheckingFmt"), ActiveDatabaseRuntimeDisplay);

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await _postgresConnectionTestService.TestAsync(
                    new PostgresConnectionTestRequest
                    {
                        Host = PostgresHost,
                        Port = port,
                        Database = PostgresDatabase,
                        Username = PostgresUsername,
                        Password = PostgresPassword,
                        TimeoutSeconds = 5
                    },
                    cts.Token);

                PostgresTestStatus = result.Success
                    ? string.Format(
                        Res("SettingsPostgresConnectedFmt"),
                        result.ServerVersion,
                        result.Database,
                        result.DatabaseExists ? Res("SettingsExists") : Res("SettingsNotFound"),
                        result.CanCreateDatabase ? Res("SettingsYes") : Res("SettingsNo"),
                        ActiveDatabaseRuntimeDisplay)
                    : string.Format(Res("SettingsPostgresConnectFailedFmt"), result.ErrorMessage, ActiveDatabaseRuntimeDisplay);
            }
            finally
            {
                IsPostgresTesting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void LoadPostgresLoginSettings()
        {
            var settings = _appSettingsService.Settings;
            if (string.Equals(settings.DatabaseStorageMode, DatabaseStorageModes.Postgres, StringComparison.OrdinalIgnoreCase)
                && !DatabaseStorageModes.PostgresRuntimeStorageEnabled)
            {
                settings.DatabaseStorageMode = DatabaseStorageModes.Sqlite;
                settings.PostgresEnabledAtUtc = string.Empty;
                _appSettingsService.SaveSettings();
                _postgresModeStatus = Res("SettingsPostgresModeRollbackNotReady");
            }

            _postgresHost = string.IsNullOrWhiteSpace(settings.PostgresHost) ? "localhost" : settings.PostgresHost;
            _postgresPort = settings.PostgresPort <= 0 ? "5432" : settings.PostgresPort.ToString();
            _postgresDatabase = string.IsNullOrWhiteSpace(settings.PostgresDatabase) ? "agency_db" : settings.PostgresDatabase;
            _postgresUsername = string.IsNullOrWhiteSpace(settings.PostgresUsername) ? "postgres" : settings.PostgresUsername;
            _postgresPassword = LocalSecretProtection.Unprotect(settings.EncryptedPostgresPassword);
            _postgresDataDirectoryPath = settings.PostgresDataDirectoryPath ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(settings.PostgresMigrationCompletedAtUtc))
                _postgresMigrationStatus = string.Format(Res("SettingsMigrationAlreadyCompletedBlockedFmt"), settings.PostgresMigrationCompletedAtUtc);

            _postgresTestStatus = string.Format(Res("SettingsPostgresNotCheckedFmt"), ActiveDatabaseRuntimeDisplay);
            _sqliteBackupFromPostgresStatus = string.IsNullOrWhiteSpace(settings.LastSqliteBackupFromPostgresAtUtc)
                ? Res("SettingsSqliteBackupNever")
                : string.Format(Res("SettingsSqliteBackupLastFmt"), settings.LastSqliteBackupFromPostgresAtUtc);
            _postgresResetStatus = Res("SettingsPostgresResetNotInWindow");
            _postgresNetworkAccessStatus = _postgresNetworkAccessService.IsRunningAsAdministrator()
                ? Res("SettingsPostgresNetworkReady")
                : Res("SettingsPostgresNetworkAdminRequired");
        }

        private void SavePostgresLoginSettings()
        {
            var settings = _appSettingsService.Settings;
            settings.PostgresHost = string.IsNullOrWhiteSpace(PostgresHost) ? "localhost" : PostgresHost.Trim();
            settings.PostgresDatabase = string.IsNullOrWhiteSpace(PostgresDatabase) ? "agency_db" : PostgresDatabase.Trim();
            settings.PostgresUsername = string.IsNullOrWhiteSpace(PostgresUsername) ? "postgres" : PostgresUsername.Trim();
            settings.PostgresPort = int.TryParse(PostgresPort, out var port)
                ? Math.Clamp(port, 1, 65535)
                : 5432;
            settings.EncryptedPostgresPassword = LocalSecretProtection.Protect(PostgresPassword);
            _appSettingsService.SaveSettings();
        }

        private void SavePostgresDataDirectoryPath()
        {
            _appSettingsService.Settings.PostgresDataDirectoryPath = PostgresDataDirectoryPath;
            _appSettingsService.SaveSettings();
        }

        private static string BuildPostgresConnectionString(
            string? host,
            int port,
            string? databaseName,
            string? username,
            string? password,
            bool includePassword)
        {
            return PostgresConnectionFactory.BuildConnectionString(
                host,
                port,
                databaseName,
                username,
                password,
                new PostgresConnectionStringOptions
                {
                    ConnectionTimeoutSeconds = PostgresConnectionFactory.DefaultConnectionTimeoutSeconds,
                    CommandTimeoutSeconds = PostgresConnectionFactory.DefaultConnectionTimeoutSeconds,
                    Pooling = true,
                    IncludePassword = includePassword,
                    UseDefaultUsernameWhenEmpty = false
                });
        }

        private static string ResolvePostgresDataDirectorySelection(string selectedPath)
        {
            if (PostgresNetworkAccessService.IsValidPostgresDataDirectory(selectedPath))
                return selectedPath;

            var nestedDataPath = System.IO.Path.Combine(selectedPath, "data");
            return PostgresNetworkAccessService.IsValidPostgresDataDirectory(nestedDataPath)
                ? nestedDataPath
                : selectedPath;
        }

        private void UseSqliteStorageMode()
        {
            _appSettingsService.Settings.DatabaseStorageMode = DatabaseStorageModes.Sqlite;
            _appSettingsService.Settings.PostgresEnabledAtUtc = string.Empty;
            _appSettingsService.SaveSettings();
            PostgresModeStatus = Res("SettingsSqliteModeSelectedRestart");
            RaiseDatabaseModePropertiesChanged();
        }

        private void EnablePostgresStorageMode()
        {
            if (!CanEnablePostgresStorageMode)
            {
                PostgresModeStatus = Res("SettingsPostgresEnableNeedsMigration");
                RaiseDatabaseModePropertiesChanged();
                return;
            }

            SavePostgresLoginSettings();
            _appSettingsService.Settings.DatabaseStorageMode = DatabaseStorageModes.Postgres;
            _appSettingsService.Settings.PostgresEnabledAtUtc = DateTime.UtcNow.ToString("O");
            _appSettingsService.SaveSettings();
            PostgresModeStatus = Res("SettingsPostgresSelectedRestart");
            RaiseDatabaseModePropertiesChanged();
        }

        private void RaiseDatabaseModePropertiesChanged()
        {
            OnPropertyChanged(nameof(DatabaseStorageModeDisplay));
            OnPropertyChanged(nameof(ActiveDatabaseRuntimeDisplay));
            OnPropertyChanged(nameof(ActiveDatabaseRuntimeDetail));
            OnPropertyChanged(nameof(CurrentDatabaseStorageMode));
            OnPropertyChanged(nameof(IsSqliteStorageMode));
            OnPropertyChanged(nameof(IsPostgresStorageMode));
            OnPropertyChanged(nameof(HasDatabaseModeRestartPending));
            OnPropertyChanged(nameof(CanEnablePostgresStorageMode));
            OnPropertyChanged(nameof(IsPostgresMigrationCompleted));
            OnPropertyChanged(nameof(CanRunPostgresMigration));
            OnPropertyChanged(nameof(CanAttachExistingPostgresDatabase));
            OnPropertyChanged(nameof(CanReplacePostgresFromSqlite));
            OnPropertyChanged(nameof(PostgresMigrationActionText));
            OnPropertyChanged(nameof(ReplacePostgresFromSqliteActionText));
            OnPropertyChanged(nameof(PostgresModeStatus));
            CommandManager.InvalidateRequerySuggested();
        }

        private void RestartApplication()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    PostgresModeStatus = Res("SettingsRestartPathMissing");
                    RaiseDatabaseModePropertiesChanged();
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
                {
                    UseShellExecute = true
                });

                Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.RestartApplication", ex.Message);
                PostgresModeStatus = string.Format(Res("SettingsRestartFailedFmt"), ex.Message);
                RaiseDatabaseModePropertiesChanged();
            }
        }

        private void RaiseMigrationActionPropertiesChanged()
        {
            OnPropertyChanged(nameof(CanRunPostgresMigration));
            OnPropertyChanged(nameof(CanAttachExistingPostgresDatabase));
            OnPropertyChanged(nameof(CanReplacePostgresFromSqlite));
            OnPropertyChanged(nameof(PostgresMigrationActionText));
            OnPropertyChanged(nameof(ReplacePostgresFromSqliteActionText));
            OnPropertyChanged(nameof(CanCreateSqliteBackupFromPostgres));
            OnPropertyChanged(nameof(CanResetPostgresDatabase));
            OnPropertyChanged(nameof(PostgresResetHint));
            OnPropertyChanged(nameof(CanConfigurePostgresTailscaleAccess));
            OnPropertyChanged(nameof(CanConfigurePostgresLanAccess));
            OnPropertyChanged(nameof(PostgresTailscaleIpDisplay));
            OnPropertyChanged(nameof(PostgresLanIpDisplay));
            CommandManager.InvalidateRequerySuggested();
        }

        private void RaiseSecondPcDatabaseAccessPropertiesChanged()
        {
            OnPropertyChanged(nameof(SecondPcDatabaseServerDisplay));
            OnPropertyChanged(nameof(SecondPcLocalServerDisplay));
            OnPropertyChanged(nameof(SecondPcTailscaleServerDisplay));
            OnPropertyChanged(nameof(SecondPcLanServerDisplay));
            OnPropertyChanged(nameof(SecondPcDatabaseNameDisplay));
            OnPropertyChanged(nameof(SecondPcDatabaseUserDisplay));
            OnPropertyChanged(nameof(SecondPcSharedFolderDisplay));
        }

        private void CopySecondPcDatabaseAccess()
        {
            try
            {
                var text = BuildSecondPcDatabaseAccessText();
                Clipboard.SetText(text);
                SecondPcDatabaseAccessStatus = Res("SettingsSecondPcAccessCopied");
            }
            catch (Exception ex)
            {
                SecondPcDatabaseAccessStatus = Res("SettingsSecondPcAccessCopyFailed");
                LoggingService.LogWarning("Settings.CopySecondPcDatabaseAccess", ex.Message);
            }
        }

        private void OpenNasPostgresGuide()
        {
            try
            {
                var window = new NasPostgresGuideWindow
                {
                    Owner = Application.Current?.MainWindow
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.OpenNasPostgresGuide", ex.Message);
                MessageBox.Show(Res("SettingsNasPostgresGuideOpenFailed"), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshConnectedClients()
        {
            ConnectedClients.Clear();

            if (!_connectedClientsService.IsAvailable)
            {
                ConnectedClientsStatus = Res("SettingsConnectedClientsOnlyPostgres");
                OnPropertyChanged(nameof(HasConnectedClients));
                return;
            }

            try
            {
                _connectedClientsService.RefreshNow();
                var nowUtc = DateTime.UtcNow;
                var clients = _connectedClientsService.GetClients()
                    .Select(client =>
                    {
                        var isOnline = client.ClosedAtUtc == null
                            && nowUtc - client.LastSeenAtUtc.ToUniversalTime() <= TimeSpan.FromMinutes(3);

                        return new ConnectedClientViewItem
                        {
                            MachineName = client.MachineName,
                            WindowsUser = client.WindowsUser,
                            UserDisplayName = string.IsNullOrWhiteSpace(client.ProfileName)
                                ? client.WindowsUser
                                : client.ProfileName,
                            IpAddress = client.IpAddress,
                            AppVersion = client.AppVersion,
                            RootFolderPath = client.RootFolderPath,
                            StartedDisplay = FormatLocalDateTime(client.StartedAtUtc),
                            LastSeenDisplay = FormatLastSeen(client.LastSeenAtUtc, nowUtc),
                            StatusDisplay = isOnline ? Res("SettingsClientOnline") : Res("SettingsClientOffline"),
                            IsOnline = isOnline
                        };
                    })
                    .OrderByDescending(client => client.IsOnline)
                    .ThenBy(client => client.MachineName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                foreach (var client in clients)
                    ConnectedClients.Add(client);

                var onlineCount = clients.Count(client => client.IsOnline);
                ConnectedClientsStatus = clients.Count == 0
                    ? Res("SettingsConnectedClientsEmpty")
                    : string.Format(Res("SettingsConnectedClientsSummaryFmt"), clients.Count, onlineCount, DateTime.Now);
            }
            catch (Exception ex)
            {
                ConnectedClientsStatus = Res("SettingsConnectedClientsReadFailed");
                LoggingService.LogWarning("Settings.RefreshConnectedClients", ex.Message);
            }
            finally
            {
                OnPropertyChanged(nameof(HasConnectedClients));
            }
        }

        private async System.Threading.Tasks.Task CreateSqliteBackupFromPostgresAsync()
        {
            if (!CanCreateSqliteBackupFromPostgres)
            {
                SqliteBackupFromPostgresStatus = Res("SettingsSqliteBackupOnlyPostgres");
                return;
            }

            IsCreatingSqliteBackupFromPostgres = true;
            SqliteBackupFromPostgresStatus = Res("SettingsSqliteBackupStarting");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var progress = new Progress<string>(message => SqliteBackupFromPostgresStatus = message);
                var result = await _postgresToSqliteBackupService.CreateBackupAsync(progress, cts.Token);
                SqliteBackupFromPostgresStatus = result.ToDisplayMessage();

                if (result.Success)
                {
                    _appSettingsService.Settings.LastSqliteBackupFromPostgresAtUtc = DateTime.UtcNow.ToString("O");
                    _appSettingsService.SaveSettings();
                }
            }
            finally
            {
                IsCreatingSqliteBackupFromPostgres = false;
            }
        }

        private async System.Threading.Tasks.Task ResetPostgresDatabaseAsync()
        {
            if (!CanResetPostgresDatabase)
            {
                PostgresResetStatus = _appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
                    ? Res("SettingsPostgresResetBlockedActive")
                    : Res("SettingsPostgresResetUnavailable");
                return;
            }

            var confirm = MessageBox.Show(
                string.Format(Res("SettingsPostgresResetConfirmMessageFmt"), PostgresDatabase),
                Res("SettingsPostgresResetConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                PostgresResetStatus = Res("SettingsPostgresResetCancelled");
                return;
            }

            IsResettingPostgresDatabase = true;
            PostgresResetStatus = Res("SettingsPostgresResetDeleting");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = await _postgresResetService.DropApplicationSchemasAsync(cts.Token);
                PostgresResetStatus = result.ToDisplayMessage();

                if (result.Success)
                {
                    var settings = _appSettingsService.Settings;
                    settings.DatabaseStorageMode = DatabaseStorageModes.Sqlite;
                    settings.PostgresConnectionString = string.Empty;
                    settings.PostgresMigrationCompletedAtUtc = string.Empty;
                    settings.PostgresEnabledAtUtc = string.Empty;
                    settings.LastSqliteBackupFromPostgresAtUtc = string.Empty;
                    _appSettingsService.SaveSettings();

                    PostgresMigrationStatus = Res("SettingsPostgresMigrationCleared");
                    RaiseDatabaseModePropertiesChanged();
                }
            }
            finally
            {
                IsResettingPostgresDatabase = false;
            }
        }

        private async System.Threading.Tasks.Task ConfigurePostgresTailscaleAccessAsync()
        {
            if (!_postgresNetworkAccessService.IsRunningAsAdministrator())
            {
                ConfirmAndRestartAsAdministrator();
                return;
            }

            IsConfiguringPostgresNetworkAccess = true;
            PostgresNetworkAccessStatus = Res("SettingsPostgresNetworkConfiguring");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var result = await _postgresNetworkAccessService.ConfigureTailscaleAccessAsync(cts.Token);
                PostgresNetworkAccessStatus = result.Message;

                if (result.Success && !string.IsNullOrWhiteSpace(result.TailscaleIp))
                {
                    PostgresHost = result.TailscaleIp;
                    RaiseSecondPcDatabaseAccessPropertiesChanged();
                }

                OnPropertyChanged(nameof(PostgresTailscaleIpDisplay));
            }
            finally
            {
                IsConfiguringPostgresNetworkAccess = false;
            }
        }

        private async System.Threading.Tasks.Task ConfigurePostgresLanAccessAsync()
        {
            if (!_postgresNetworkAccessService.IsRunningAsAdministrator())
            {
                ConfirmAndRestartAsAdministrator();
                return;
            }

            IsConfiguringPostgresNetworkAccess = true;
            PostgresNetworkAccessStatus = Res("SettingsPostgresLanConfiguring");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var result = await _postgresNetworkAccessService.ConfigureLanAccessAsync(cts.Token);
                PostgresNetworkAccessStatus = result.Message;

                if (result.Success && !string.IsNullOrWhiteSpace(result.LanIp))
                {
                    PostgresHost = result.LanIp;
                    RaiseSecondPcDatabaseAccessPropertiesChanged();
                }

                OnPropertyChanged(nameof(PostgresLanIpDisplay));
            }
            finally
            {
                IsConfiguringPostgresNetworkAccess = false;
            }
        }

        private bool ConfirmAndRestartAsAdministrator()
        {
            var confirm = MessageBox.Show(
                Res("SettingsPostgresAdminRestartConfirmMessage"),
                Res("SettingsPostgresAdminRestartConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (confirm != MessageBoxResult.Yes)
            {
                PostgresNetworkAccessStatus = Res("SettingsPostgresAdminRestartCancelled");
                return false;
            }

            RestartAsAdministrator();
            return true;
        }

        private void RestartAsAdministrator()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    PostgresNetworkAccessStatus = Res("SettingsRestartPathMissing");
                    return;
                }

                PostgresNetworkAccessStatus = Res("SettingsPostgresNetworkRestartingAsAdmin");
                LoggingService.LogInfo("Settings.RestartAsAdministrator", "Restarting as administrator for PostgreSQL network configuration.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });

                Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.RestartAsAdministrator", ex.Message);
                PostgresNetworkAccessStatus = string.Format(Res("SettingsRestartFailedFmt"), ex.Message);
            }
        }

        private static string FormatLocalDateTime(DateTime utcDateTime)
        {
            return utcDateTime.ToUniversalTime().ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        }

        private static string FormatLastSeen(DateTime lastSeenUtc, DateTime nowUtc)
        {
            var elapsed = nowUtc - lastSeenUtc.ToUniversalTime();
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            if (elapsed.TotalSeconds < 60)
                return string.Format(Res("SettingsSecondsAgoFmt"), Math.Max(1, (int)elapsed.TotalSeconds));

            if (elapsed.TotalMinutes < 60)
                return string.Format(Res("SettingsMinutesAgoFmt"), (int)elapsed.TotalMinutes);

            return FormatLocalDateTime(lastSeenUtc);
        }

        private string BuildSecondPcDatabaseAccessText()
        {
            var server = GetSecondPcPostgresHost();
            var tailscaleServer = SecondPcTailscaleServerDisplay;
            var lanServer = SecondPcLanServerDisplay;
            var port = NormalizePostgresPort();
            var database = SecondPcDatabaseNameDisplay;
            var username = SecondPcDatabaseUserDisplay;
            var password = string.IsNullOrEmpty(PostgresPassword) ? Res("SettingsPostgresPasswordPlaceholder") : PostgresPassword;
            var rootFolder = RootFolderPath?.Trim() ?? string.Empty;
            var sharedFolder = string.IsNullOrWhiteSpace(rootFolder)
                ? Res("SettingsRootFolderPlaceholder")
                : rootFolder.StartsWith(@"\\", StringComparison.Ordinal)
                    ? rootFolder
                    : string.Format(Res("SettingsSharedFolderNeedsSharingFmt"), rootFolder, Environment.MachineName);

            return string.Join(Environment.NewLine,
                Res("SettingsSecondPcCopyTitle"),
                "",
                $"{Res("SettingsSecondPcLocalServerLabel")}: localhost:{port}",
                $"{Res("SettingsSecondPcTailscaleServerLabel")}: {tailscaleServer}",
                $"{Res("SettingsSecondPcLanServerLabel")}: {lanServer}",
                $"{Res("SettingsSecondPcServerLabel")}: {server}",
                $"Port: {port}",
                $"Database: {database}",
                $"User: {username}",
                $"Password: {password}",
                "",
                string.Format(Res("SettingsSecondPcFilesFolderFmt"), sharedFolder),
                "",
                Res("SettingsSecondPcMainPcNetworkHint"),
                Res("SettingsSecondPcNetworkHint"));
        }

        private string GetSecondPcPostgresHost()
        {
            var host = PostgresHost?.Trim();
            if (string.IsNullOrWhiteSpace(host)
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
            {
                return GetLocalNetworkIpAddress();
            }

            return host;
        }

        private int NormalizePostgresPort()
        {
            return int.TryParse(PostgresPort, out var port)
                ? Math.Clamp(port, 1, 65535)
                : 5432;
        }

        private string BuildPostgresModeStatus()
        {
            if (!DatabaseStorageModes.PostgresRuntimeStorageEnabled)
            {
                return Res("SettingsPostgresRuntimeDisabledMode");
            }

            if (IsPostgresStorageMode)
                return Res("SettingsPostgresModeSelectedHint");

            return CanEnablePostgresStorageMode
                ? Res("SettingsMigrationReadyModeHint")
                : Res("SettingsSqliteModeMigrationNeeded");
        }

        private async System.Threading.Tasks.Task RunPostgresMigrationAsync()
        {
            if (IsPostgresMigrationCompleted)
            {
                PostgresMigrationStatus = string.Format(Res("SettingsMigrationAlreadyCompletedSafeFmt"), _appSettingsService.Settings.PostgresMigrationCompletedAtUtc);
                RaiseDatabaseModePropertiesChanged();
                return;
            }

            if (!int.TryParse(PostgresPort, out var port))
            {
                PostgresMigrationStatus = Res("SettingsPostgresPortMustBeNumber");
                return;
            }

            if (string.Equals(PostgresDatabase?.Trim(), "postgres", StringComparison.OrdinalIgnoreCase))
            {
                PostgresMigrationStatus = Res("SettingsPostgresReservedDbMigration");
                return;
            }

            SavePostgresLoginSettings();
            IsPostgresMigrating = true;
            CommandManager.InvalidateRequerySuggested();
            PostgresMigrationStatus = Res("SettingsMigrationStarting");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var progress = new Progress<string>(message => PostgresMigrationStatus = message);
                var result = await _postgresMigrationService.MigrateAsync(
                    new PostgresMigrationRequest
                    {
                        Host = PostgresHost,
                        Port = port,
                        Database = PostgresDatabase ?? "agency_db",
                        Username = PostgresUsername,
                        Password = PostgresPassword,
                        TimeoutSeconds = 10
                    },
                    progress,
                    cts.Token);

                PostgresMigrationStatus = string.Format(Res("SettingsMigrationResultBlockedFmt"), result.ToDisplayMessage(), ActiveDatabaseRuntimeDisplay);
                RaiseDatabaseModePropertiesChanged();
            }
            finally
            {
                IsPostgresMigrating = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async System.Threading.Tasks.Task AttachExistingPostgresDatabaseAsync()
        {
            if (!int.TryParse(PostgresPort, out var port))
            {
                PostgresMigrationStatus = Res("SettingsPostgresPortMustBeNumber");
                return;
            }

            var databaseName = string.IsNullOrWhiteSpace(PostgresDatabase)
                ? "agency_db"
                : PostgresDatabase.Trim();

            SavePostgresLoginSettings();
            IsPostgresTesting = true;
            CommandManager.InvalidateRequerySuggested();
            PostgresMigrationStatus = Res("SettingsAttachExistingPgChecking");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await using var connection = new NpgsqlConnection(BuildPostgresConnectionString(
                    PostgresHost,
                    port,
                    databaseName,
                    PostgresUsername,
                    PostgresPassword,
                    includePassword: true));
                await connection.OpenAsync(cts.Token);

                var missingObjects = await GetMissingPostgresApplicationObjectsAsync(connection, cts.Token);
                if (missingObjects.Count > 0)
                {
                    PostgresMigrationStatus = string.Format(
                        Res("SettingsAttachExistingPgMissingSchemaFmt"),
                        string.Join(", ", missingObjects));
                    RaiseDatabaseModePropertiesChanged();
                    return;
                }

                var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                var settings = _appSettingsService.Settings;
                settings.PostgresConnectionString = BuildPostgresConnectionString(
                    PostgresHost,
                    port,
                    databaseName,
                    PostgresUsername,
                    null,
                    includePassword: false);
                settings.PostgresHost = string.IsNullOrWhiteSpace(PostgresHost) ? "localhost" : PostgresHost.Trim();
                settings.PostgresPort = Math.Clamp(port, 1, 65535);
                settings.PostgresDatabase = databaseName;
                settings.PostgresUsername = PostgresUsername?.Trim() ?? string.Empty;
                settings.EncryptedPostgresPassword = LocalSecretProtection.Protect(PostgresPassword);
                settings.PostgresMigrationCompletedAtUtc = now;
                settings.DatabaseStorageMode = DatabaseStorageModes.Postgres;
                settings.PostgresEnabledAtUtc = now;
                _appSettingsService.SaveSettings();

                PostgresMigrationStatus = string.Format(Res("SettingsAttachExistingPgSuccessFmt"), databaseName);
                PostgresModeStatus = Res("SettingsPostgresSelectedRestart");
                RaiseDatabaseModePropertiesChanged();
            }
            catch (OperationCanceledException)
            {
                PostgresMigrationStatus = Res("SettingsAttachExistingPgTimeout");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.AttachExistingPostgresDatabase", ex.Message);
                PostgresMigrationStatus = string.Format(
                    Res("SettingsAttachExistingPgFailedFmt"),
                    PostgresErrorMessageService.ToUserMessage(ex));
            }
            finally
            {
                IsPostgresTesting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static async System.Threading.Tasks.Task<List<string>> GetMissingPostgresApplicationObjectsAsync(
            NpgsqlConnection connection,
            CancellationToken cancellationToken)
        {
            var requiredObjects = new[]
            {
                "core.schema_version",
                "core.app_database",
                "app.schema_version",
                "app.migration_journal",
                "app.employee_index",
                "salary.salary_entries"
            };

            var missingObjects = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass(@object_name)::text;";
            var parameter = command.Parameters.Add("object_name", NpgsqlTypes.NpgsqlDbType.Text);

            foreach (var objectName in requiredObjects)
            {
                parameter.Value = objectName;
                var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (value == null || value == DBNull.Value || string.IsNullOrWhiteSpace(value.ToString()))
                    missingObjects.Add(objectName);
            }

            return missingObjects;
        }

        private async System.Threading.Tasks.Task ReplacePostgresFromSqliteAsync()
        {
            if (!CanReplacePostgresFromSqlite)
            {
                PostgresMigrationStatus = _appDataStorageFactory.IsPostgresRuntimeActiveAtStartup
                    ? Res("SettingsReplaceBlockedActive")
                    : Res("SettingsReplaceUnavailable");
                RaiseDatabaseModePropertiesChanged();
                return;
            }

            var confirm = MessageBox.Show(
                string.Format(Res("SettingsReplaceConfirmMessageFmt"), PostgresDatabase),
                Res("SettingsReplaceConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                PostgresMigrationStatus = Res("SettingsReplaceCancelled");
                return;
            }

            if (!int.TryParse(PostgresPort, out var port))
            {
                PostgresMigrationStatus = Res("SettingsPostgresPortMustBeNumber");
                return;
            }

            if (string.Equals(PostgresDatabase?.Trim(), "postgres", StringComparison.OrdinalIgnoreCase))
            {
                PostgresMigrationStatus = Res("SettingsPostgresReservedDbReplace");
                return;
            }

            SavePostgresLoginSettings();
            IsPostgresMigrating = true;
            CommandManager.InvalidateRequerySuggested();
            PostgresMigrationStatus = Res("SettingsReplaceStarting");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var progress = new Progress<string>(message => PostgresMigrationStatus = Res("SettingsReplaceProgressPrefix") + message);
                var result = await _postgresMigrationService.MigrateAsync(
                    new PostgresMigrationRequest
                    {
                        Host = PostgresHost,
                        Port = port,
                        Database = PostgresDatabase ?? "agency_db",
                        Username = PostgresUsername,
                        Password = PostgresPassword,
                        TimeoutSeconds = 10
                    },
                    progress,
                    cts.Token);

                PostgresMigrationStatus = result.Success
                    ? result.ToDisplayMessage() + " " + Res("SettingsReplaceSuccessSuffix")
                    : result.ToDisplayMessage();
                RaiseDatabaseModePropertiesChanged();
            }
            finally
            {
                IsPostgresMigrating = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static string NormalizeAccessPlan(string? plan)
        {
            return (plan ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "business" => "business",
                "ultimate" => "ultimate",
                "pro" => "ultimate",
                "standard" => "standard",
                _ => "trial"
            };
        }

        private static string FormatPlanDisplay(string? plan)
        {
            return NormalizeAccessPlan(plan) switch
            {
                "business" => "Business",
                "ultimate" => "Ultimate",
                "standard" => "Standard",
                _ => "Trial"
            };
        }

        private void RefreshUserRolePreview()
        {
            UserRolePreviewItems.Clear();

            var profile = _currentProfileService.CurrentProfile;
            var displayName = profile == null
                ? Res("SettingsUsersOwnerFallback")
                : string.Join(" ", new[] { profile.FirstName, profile.LastName }
                    .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = profile?.ClientId ?? Res("SettingsUsersOwnerFallback");

            UserRolePreviewItems.Add(new UserRolePreviewItem
            {
                DisplayName = displayName,
                RoleName = string.IsNullOrWhiteSpace(profile?.RoleKey)
                    ? Res("SettingsUsersRoleOwner")
                    : FormatRoleName(profile.RoleKey),
                AccessSummary = Res("SettingsUsersOwnerAccess"),
                Status = Res("SettingsUsersStatusActive"),
                IsCurrentUser = true,
                IsEnabled = profile?.IsActive != false
            });

            foreach (var user in _appSettingsService.Settings.BusinessUsers
                         .OrderBy(user => user.LastName, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(user => user.FirstName, StringComparer.CurrentCultureIgnoreCase))
            {
                var userName = string.Join(" ", new[] { user.FirstName, user.LastName }
                    .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
                UserRolePreviewItems.Add(new UserRolePreviewItem
                {
                    UserId = user.UserId,
                    DisplayName = string.IsNullOrWhiteSpace(userName) ? user.UserId : userName,
                    RoleName = FormatRoleName(user.RoleKey),
                    AccessSummary = BuildBusinessUserAccessSummary(user),
                    Status = user.IsActive ? Res("SettingsUsersStatusActive") : Res("SettingsUsersStatusDisabled"),
                    IsEnabled = user.IsActive,
                    CanEdit = true
                });
            }

            UserRolePreviewItems.Add(new UserRolePreviewItem
            {
                DisplayName = Res("SettingsUsersAccountantExample"),
                RoleName = Res("SettingsUsersRoleAccountant"),
                AccessSummary = Res("SettingsUsersAccountantAccess"),
                Status = Res("SettingsUsersStatusTemplate"),
                IsEnabled = false
            });

            UserRolePreviewItems.Add(new UserRolePreviewItem
            {
                DisplayName = Res("SettingsUsersManagerExample"),
                RoleName = Res("SettingsUsersRoleManager"),
                AccessSummary = Res("SettingsUsersManagerAccess"),
                Status = Res("SettingsUsersStatusTemplate"),
                IsEnabled = false
            });

            OnPropertyChanged(nameof(HasUserRolePreviewItems));
            OnPropertyChanged(nameof(UsersAccessModeDisplay));
            OnPropertyChanged(nameof(UsersBusinessAvailabilityDisplay));
            OnPropertyChanged(nameof(UsersTenantDisplay));
        }

        private static string FormatRoleName(string roleKey)
        {
            return roleKey.Trim().ToLowerInvariant() switch
            {
                "owner" => Res("SettingsUsersRoleOwner"),
                "admin" => Res("SettingsUsersRoleAdmin"),
                "accountant" => Res("SettingsUsersRoleAccountant"),
                "manager" => Res("SettingsUsersRoleManager"),
                "viewer" => Res("SettingsUsersRoleViewer"),
                _ => roleKey
            };
        }

        private string BuildBusinessUserAccessSummary(AppSettingsService.BusinessUserSetting user)
        {
            var moduleCount = user.ModulePermissions.Count(permission => permission.AccessLevel != "None");
            var employerCount = user.EmployerPermissions.Count(permission => permission.AccessLevel != "None");
            var scope = user.AccessScope.Mode switch
            {
                "SelectedAgencies" => string.Format(Res("SettingsUsersSummaryAgenciesFmt"), user.AccessScope.AgencyNames.Count),
                "SelectedEmployers" => string.Format(Res("SettingsUsersSummaryEmployersFmt"), user.AccessScope.EmployerCompanyIds.Count),
                "SelectedAgenciesAndEmployers" => string.Format(Res("SettingsUsersSummaryMixedFmt"), user.AccessScope.AgencyNames.Count, user.AccessScope.EmployerCompanyIds.Count),
                _ => Res("SettingsUsersSummaryAllData")
            };

            return string.Format(Res("SettingsUsersSummaryFmt"), scope, moduleCount, employerCount);
        }

        private void InitializeBusinessUserEditorOptions()
        {
            BusinessUserRoleOptions.Clear();
            BusinessUserRoleOptions.Add(new UserPermissionOption { Key = "admin", DisplayName = Res("SettingsUsersRoleAdmin") });
            BusinessUserRoleOptions.Add(new UserPermissionOption { Key = "manager", DisplayName = Res("SettingsUsersRoleManager") });
            BusinessUserRoleOptions.Add(new UserPermissionOption { Key = "accountant", DisplayName = Res("SettingsUsersRoleAccountant") });
            BusinessUserRoleOptions.Add(new UserPermissionOption { Key = "viewer", DisplayName = Res("SettingsUsersRoleViewer") });

            BusinessUserScopeModeOptions.Clear();
            BusinessUserScopeModeOptions.Add(new UserPermissionOption { Key = "AllData", DisplayName = Res("SettingsUsersScopeAllTitle") });
            BusinessUserScopeModeOptions.Add(new UserPermissionOption { Key = "SelectedAgencies", DisplayName = Res("SettingsUsersScopeAgenciesTitle") });
            BusinessUserScopeModeOptions.Add(new UserPermissionOption { Key = "SelectedEmployers", DisplayName = Res("SettingsUsersScopeEmployersTitle") });
            BusinessUserScopeModeOptions.Add(new UserPermissionOption { Key = "SelectedAgenciesAndEmployers", DisplayName = Res("SettingsUsersScopeMixedTitle") });
        }

        private void RefreshBusinessUserLoginOptions()
        {
            BusinessUserLoginOptions.Clear();
            foreach (var user in _appSettingsService.Settings.BusinessUsers
                         .Where(user => user.IsActive)
                         .OrderBy(user => user.LastName, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(user => user.FirstName, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = $"{user.FirstName} {user.LastName}".Trim();
                var login = string.IsNullOrWhiteSpace(user.Login) ? user.UserId : user.Login;
                BusinessUserLoginOptions.Add(new UserPermissionOption
                {
                    Key = user.UserId,
                    DisplayName = string.IsNullOrWhiteSpace(name)
                        ? login
                        : $"{name} ({login})"
                });
            }

            if (!BusinessUserLoginOptions.Any(option =>
                    string.Equals(option.Key, SelectedBusinessUserLoginId, StringComparison.OrdinalIgnoreCase)))
                SelectedBusinessUserLoginId = BusinessUserLoginOptions.FirstOrDefault()?.Key ?? string.Empty;

            OnPropertyChanged(nameof(HasBusinessUserLoginOptions));
            CommandManager.InvalidateRequerySuggested();
        }

        private void OpenBusinessUserEditor()
        {
            _isInitializingBusinessUserEditor = true;
            try
            {
                _editingBusinessUserId = string.Empty;
                BusinessUserFirstName = string.Empty;
                BusinessUserLastName = string.Empty;
                BusinessUserLogin = string.Empty;
                BusinessUserTemporaryPassword = GenerateTemporaryPassword();
                BusinessUserRoleKey = "manager";
                BusinessUserIsActive = true;
                BusinessUserScopeMode = "AllData";
                BusinessUserEditorStatus = string.Empty;
                BuildBusinessUserEditorCollections();
                ApplyBusinessUserRoleDefaults(BusinessUserRoleKey);
            }
            finally
            {
                _isInitializingBusinessUserEditor = false;
            }

            RaiseBusinessUserEditorModePropertiesChanged();
            IsBusinessUserEditorOpen = true;
            CommandManager.InvalidateRequerySuggested();
        }

        private void EditBusinessUser(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return;

            var user = _appSettingsService.Settings.BusinessUsers.FirstOrDefault(candidate =>
                string.Equals(candidate.UserId, userId, StringComparison.OrdinalIgnoreCase));
            if (user == null)
                return;

            _isInitializingBusinessUserEditor = true;
            try
            {
                _editingBusinessUserId = user.UserId;
                BusinessUserFirstName = user.FirstName;
                BusinessUserLastName = user.LastName;
                BusinessUserLogin = user.Login;
                BusinessUserTemporaryPassword = string.Empty;
                BusinessUserRoleKey = string.IsNullOrWhiteSpace(user.RoleKey) ? "manager" : user.RoleKey;
                BusinessUserIsActive = user.IsActive;
                BusinessUserScopeMode = string.IsNullOrWhiteSpace(user.AccessScope?.Mode) ? "AllData" : user.AccessScope.Mode;
                BusinessUserEditorStatus = string.Empty;
                BuildBusinessUserEditorCollections();
                ApplyBusinessUserEditorValues(user);
            }
            finally
            {
                _isInitializingBusinessUserEditor = false;
            }

            RaiseBusinessUserEditorModePropertiesChanged();
            IsBusinessUserEditorOpen = true;
            CommandManager.InvalidateRequerySuggested();
        }

        private void RaiseBusinessUserEditorModePropertiesChanged()
        {
            OnPropertyChanged(nameof(IsEditingBusinessUser));
            OnPropertyChanged(nameof(BusinessUserEditorTitle));
            OnPropertyChanged(nameof(BusinessUserEditorPasswordHint));
            OnPropertyChanged(nameof(CanSaveBusinessUser));
        }

        private void BuildBusinessUserEditorCollections()
        {
            BusinessUserModuleEditorItems.Clear();
            foreach (var module in GetBusinessUserModules())
            {
                var item = new BusinessUserModuleEditorItem
                {
                    ModuleKey = module.Key,
                    ModuleName = module.DisplayName
                };
                foreach (var option in GetModuleAccessOptions(module.AllowExport))
                    item.AccessOptions.Add(option);
                BusinessUserModuleEditorItems.Add(item);
            }

            BusinessUserAgencyEditorItems.Clear();
            foreach (var agency in _companyService.Companies
                         .Where(company => !string.IsNullOrWhiteSpace(company.Agency?.Name))
                         .GroupBy(company => company.Agency.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                BusinessUserAgencyEditorItems.Add(new BusinessUserAgencyEditorItem(RefreshBusinessUserEmployerEditorItems)
                {
                    AgencyName = agency.Key,
                    EmployerCount = agency.Count()
                });
            }

            _allBusinessUserEmployerEditorItems.Clear();
            foreach (var company in _companyService.Companies
                         .Where(company => !string.IsNullOrWhiteSpace(company.Name))
                         .OrderBy(company => company.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = new BusinessUserEmployerEditorItem
                {
                    EmployerCompanyId = company.Id.ToString(),
                    EmployerName = company.Name.Trim(),
                    AgencyName = string.IsNullOrWhiteSpace(company.Agency?.Name)
                        ? Res("SettingsUsersEmployerNoAgency")
                        : company.Agency.Name.Trim()
                };
                item.AccessOptions.Add(new UserPermissionOption { Key = "View", DisplayName = Res("SettingsUsersAccessView") });
                item.AccessOptions.Add(new UserPermissionOption { Key = "Edit", DisplayName = Res("SettingsUsersAccessEdit") });
                _allBusinessUserEmployerEditorItems.Add(item);
            }

            RefreshBusinessUserEmployerEditorItems();
        }

        private void RefreshBusinessUserEmployerEditorItems()
        {
            if (_allBusinessUserEmployerEditorItems.Count == 0)
                return;

            var selectedAgencies = BusinessUserAgencyEditorItems
                .Where(item => item.IsSelected)
                .Select(item => item.AgencyName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IEnumerable<BusinessUserEmployerEditorItem> source = _allBusinessUserEmployerEditorItems;

            if (BusinessUserScopeMode is "SelectedAgencies" or "SelectedAgenciesAndEmployers"
                && selectedAgencies.Count > 0)
            {
                source = source.Where(item =>
                    selectedAgencies.Contains(item.AgencyName)
                    || (BusinessUserScopeMode == "SelectedAgenciesAndEmployers" && item.IsSelected));
            }
            else if (BusinessUserScopeMode == "SelectedAgencies" && selectedAgencies.Count == 0)
            {
                source = Enumerable.Empty<BusinessUserEmployerEditorItem>();
            }

            BusinessUserEmployerEditorItems.Clear();
            foreach (var item in source.OrderBy(item => item.EmployerName, StringComparer.CurrentCultureIgnoreCase))
                BusinessUserEmployerEditorItems.Add(item);
        }

        private void ApplyBusinessUserEditorValues(AppSettingsService.BusinessUserSetting user)
        {
            var modulePermissions = user.ModulePermissions
                .GroupBy(permission => permission.ModuleKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().AccessLevel, StringComparer.OrdinalIgnoreCase);
            foreach (var item in BusinessUserModuleEditorItems)
            {
                if (modulePermissions.TryGetValue(item.ModuleKey, out var accessLevel))
                    item.SelectedAccessLevel = accessLevel;
            }

            var selectedAgencies = user.AccessScope?.AgencyNames?
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in BusinessUserAgencyEditorItems)
                item.IsSelected = selectedAgencies.Contains(item.AgencyName);

            var employerPermissions = user.EmployerPermissions
                .GroupBy(permission => permission.EmployerCompanyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().AccessLevel, StringComparer.OrdinalIgnoreCase);
            var scopeEmployerIds = user.AccessScope?.EmployerCompanyIds?
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _allBusinessUserEmployerEditorItems)
            {
                if (employerPermissions.TryGetValue(item.EmployerCompanyId, out var accessLevel)
                    || scopeEmployerIds.Contains(item.EmployerCompanyId))
                {
                    item.IsSelected = true;
                    item.SelectedAccessLevel = string.IsNullOrWhiteSpace(accessLevel) ? "View" : accessLevel;
                }
            }

            RefreshBusinessUserEmployerEditorItems();
        }

        private void ApplyBusinessUserRoleDefaults(string roleKey)
        {
            var normalized = (roleKey ?? string.Empty).Trim().ToLowerInvariant();
            foreach (var item in BusinessUserModuleEditorItems)
            {
                item.SelectedAccessLevel = GetDefaultModuleAccess(normalized, item.ModuleKey);
            }
        }

        private static string GetDefaultModuleAccess(string roleKey, string moduleKey)
        {
            return roleKey switch
            {
                "admin" => moduleKey == "reports" ? "Export" : "Edit",
                "accountant" => moduleKey switch
                {
                    "salary" => "Edit",
                    "reports" => "Export",
                    "settings" => "None",
                    _ => "View"
                },
                "viewer" => moduleKey is "salary" or "settings" ? "None" : "View",
                _ => moduleKey switch
                {
                    "employees" or "documents" => "Edit",
                    "salary" or "companies" or "reports" => "View",
                    _ => "None"
                }
            };
        }

        private void SaveBusinessUser()
        {
            if (!CanSaveBusinessUser)
            {
                BusinessUserEditorStatus = Res("SettingsUsersEditorLoginRequired");
                return;
            }

            var isEditing = IsEditingBusinessUser;
            if (_appSettingsService.Settings.BusinessUsers.Any(user =>
                    !string.Equals(user.UserId, _editingBusinessUserId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(user.Login, BusinessUserLogin, StringComparison.OrdinalIgnoreCase)))
            {
                BusinessUserEditorStatus = Res("SettingsUsersEditorLoginExists");
                return;
            }

            var user = isEditing
                ? _appSettingsService.Settings.BusinessUsers.FirstOrDefault(candidate =>
                    string.Equals(candidate.UserId, _editingBusinessUserId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (isEditing && user == null)
            {
                BusinessUserEditorStatus = Res("SettingsUsersLoginUserNotFound");
                return;
            }

            user ??= new AppSettingsService.BusinessUserSetting
            {
                UserId = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow
            };

            var selectedAgencies = BusinessUserAgencyEditorItems
                .Where(item => item.IsSelected)
                .Select(item => item.AgencyName)
                .ToList();
            var selectedEmployers = BusinessUserEmployerEditorItems
                .Where(item => item.IsSelected)
                .Select(item => item.EmployerCompanyId)
                .ToList();

            user.Login = BusinessUserLogin;
            user.FirstName = BusinessUserFirstName.Trim();
            user.LastName = BusinessUserLastName.Trim();
            user.RoleKey = BusinessUserRoleKey;
            user.IsActive = BusinessUserIsActive;
            user.AccessScope = new AppSettingsService.UserAccessScopeSetting
            {
                Mode = BusinessUserScopeMode,
                AgencyNames = selectedAgencies,
                EmployerCompanyIds = selectedEmployers
            };
            user.ModulePermissions = BusinessUserModuleEditorItems
                .Select(item => new AppSettingsService.ModulePermissionSetting
                {
                    ModuleKey = item.ModuleKey,
                    AccessLevel = item.SelectedAccessLevel
                })
                .ToList();
            user.EmployerPermissions = BusinessUserEmployerEditorItems
                .Where(item => item.IsSelected)
                .Select(item => new AppSettingsService.EmployerPermissionSetting
                {
                    EmployerCompanyId = item.EmployerCompanyId,
                    AccessLevel = item.SelectedAccessLevel
                })
                .ToList();

            if (!string.IsNullOrWhiteSpace(BusinessUserTemporaryPassword) || !isEditing)
            {
                user.PasswordSalt = BusinessUserAuthService.GenerateSalt();
                user.PasswordHash = BusinessUserAuthService.HashPassword(BusinessUserTemporaryPassword, user.PasswordSalt);
                user.MustChangePassword = true;
            }

            if (!isEditing)
                _appSettingsService.Settings.BusinessUsers.Add(user);

            _appSettingsService.SaveSettings();
            _businessUserDirectoryService.SyncToRootFolder();
            _activityLogService.Log(
                isEditing ? "BusinessUserUpdated" : "BusinessUserAdded",
                "Settings",
                "",
                "",
                string.Format(isEditing ? Res("SettingsUsersUpdatedLogFmt") : Res("SettingsUsersAddedLogFmt"), $"{user.FirstName} {user.LastName}".Trim(), FormatRoleName(user.RoleKey)),
                "",
                BuildBusinessUserAccessSummary(user),
                entityType: "BusinessUser",
                entityId: user.UserId);

            if (string.Equals(_currentProfileService.CurrentBusinessUser?.UserId, user.UserId, StringComparison.OrdinalIgnoreCase))
            {
                _currentProfileService.SetCurrentBusinessUser(user.IsActive ? user : null);
                if (!user.IsActive)
                {
                    _appSettingsService.Settings.CurrentBusinessUserId = string.Empty;
                    _appSettingsService.SaveSettings();
                }
                RaiseBusinessUserSessionPropertiesChanged();
            }

            RefreshUserRolePreview();
            RefreshBusinessUserLoginOptions();
            IsBusinessUserEditorOpen = false;
        }

        private void LoginBusinessUser()
        {
            var result = _businessUserAuthService.TryLoginByUserId(SelectedBusinessUserLoginId, BusinessUserLoginPassword);
            if (!result.Success || result.User == null)
            {
                BusinessUserLoginStatus = result.FailureReason switch
                {
                    "wrong_password" => Res("SettingsUsersLoginWrongPassword"),
                    _ => Res("SettingsUsersLoginUserNotFound")
                };
                return;
            }

            var user = result.User;
            BusinessUserLoginPassword = string.Empty;
            BusinessUserLoginStatus = string.Format(Res("SettingsUsersLoginSuccessFmt"), $"{user.FirstName} {user.LastName}".Trim());
            BusinessUserPasswordStatus = user.MustChangePassword ? Res("SettingsUsersMustChangePasswordStatus") : string.Empty;
            RaiseBusinessUserSessionPropertiesChanged();
            _activityLogService.Log(
                "BusinessUserLogin",
                "Settings",
                "",
                "",
                string.Format(Res("SettingsUsersLoginLogFmt"), $"{user.FirstName} {user.LastName}".Trim()),
                entityType: "BusinessUser",
                entityId: user.UserId);
        }

        private void LogoutBusinessUser()
        {
            var previous = _currentProfileService.CurrentBusinessUser;
            var isMemberSession = IsMemberSettingsSession;
            _businessUserAuthService.LogoutSession();
            BusinessUserLoginStatus = Res("SettingsUsersLogoutSuccess");
            ClearBusinessUserPasswordFields();
            BusinessUserPasswordStatus = string.Empty;
            RaiseBusinessUserSessionPropertiesChanged();
            if (previous != null)
            {
                _activityLogService.Log(
                    "BusinessUserLogout",
                    "Settings",
                    "",
                    "",
                    string.Format(Res("SettingsUsersLogoutLogFmt"), $"{previous.FirstName} {previous.LastName}".Trim()),
                    entityType: "BusinessUser",
                    entityId: previous.UserId);
            }

            if (isMemberSession)
            {
                _businessUserSessionService.ClearRememberedSession();
                _memberRememberMeEnabled = false;
                OnPropertyChanged(nameof(MemberRememberMeEnabled));
                _appSettingsService.Settings.PendingMemberRoleSelection = true;
                _appSettingsService.SaveSettings();
                RestartApplicationForLogin();
            }
        }

        private void RaiseBusinessUserSessionPropertiesChanged()
        {
            OnPropertyChanged(nameof(IsBusinessUserSessionActive));
            OnPropertyChanged(nameof(IsMemberSettingsSession));
            OnPropertyChanged(nameof(CanShowOwnerSettingsSections));
            OnPropertyChanged(nameof(CanShowUsersSettingsTab));
            OnPropertyChanged(nameof(CanShowUsersAdminPanel));
            OnPropertyChanged(nameof(CanShowMemberAccountPanel));
            OnPropertyChanged(nameof(IsMemberProgramAccountSection));
            OnPropertyChanged(nameof(IsOwnerProgramSettingsSection));
            OnPropertyChanged(nameof(IsUsersAdminSettingsSection));
            OnPropertyChanged(nameof(CurrentBusinessUserDisplayName));
            OnPropertyChanged(nameof(CurrentBusinessUserPermissionSummary));
            OnPropertyChanged(nameof(BusinessUserMustChangePassword));
            OnPropertyChanged(nameof(BusinessUserPasswordHint));
            OnPropertyChanged(nameof(CanChangeBusinessUserPassword));
            RefreshMemberRememberMeEnabled();
            EnsureValidSettingsSection();
            CommandManager.InvalidateRequerySuggested();
        }

        private void InitializeMemberAccountFields()
        {
            RefreshMemberRememberMeEnabled();
        }

        private void RefreshMemberRememberMeEnabled()
        {
            var enabled = _businessUserSessionService.IsRememberEnabled;
            if (_memberRememberMeEnabled == enabled)
                return;

            _memberRememberMeEnabled = enabled;
            OnPropertyChanged(nameof(MemberRememberMeEnabled));
        }

        private void SyncMemberRememberMe(bool enabled)
        {
            var user = _currentProfileService.CurrentBusinessUser;
            if (user == null)
                return;

            _isSyncingMemberRememberMe = true;
            try
            {
                if (enabled)
                    _businessUserSessionService.SaveRememberedSession(user);
                else
                    _businessUserSessionService.ClearRememberedSession();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.SyncMemberRememberMe", ex.Message);
                RefreshMemberRememberMeEnabled();
            }
            finally
            {
                _isSyncingMemberRememberMe = false;
            }
        }

        private void ChangeBusinessUserPassword()
        {
            var currentUser = _currentProfileService.CurrentBusinessUser;
            if (currentUser == null)
            {
                BusinessUserPasswordStatus = Res("SettingsUsersPasswordNoActiveUser");
                return;
            }

            var user = _appSettingsService.Settings.BusinessUsers.FirstOrDefault(candidate =>
                string.Equals(candidate.UserId, currentUser.UserId, StringComparison.OrdinalIgnoreCase));
            if (user == null)
            {
                BusinessUserPasswordStatus = Res("SettingsUsersLoginUserNotFound");
                return;
            }

            if (!BusinessUserAuthService.VerifyPassword(user, BusinessUserCurrentPassword))
            {
                BusinessUserPasswordStatus = Res("SettingsUsersLoginWrongPassword");
                return;
            }

            if (!string.Equals(BusinessUserNewPassword, BusinessUserConfirmPassword, StringComparison.Ordinal))
            {
                BusinessUserPasswordStatus = Res("SettingsUsersPasswordMismatch");
                return;
            }

            if (BusinessUserNewPassword.Trim().Length < 6)
            {
                BusinessUserPasswordStatus = Res("SettingsUsersPasswordTooShort");
                return;
            }

            user.PasswordSalt = BusinessUserAuthService.GenerateSalt();
            user.PasswordHash = BusinessUserAuthService.HashPassword(BusinessUserNewPassword, user.PasswordSalt);
            user.MustChangePassword = false;
            _appSettingsService.SaveSettings();
            _businessUserDirectoryService.SyncToRootFolder();
            _currentProfileService.SetCurrentBusinessUser(user);
            ClearBusinessUserPasswordFields();
            BusinessUserPasswordStatus = Res("SettingsUsersPasswordChanged");
            RaiseBusinessUserSessionPropertiesChanged();
            _activityLogService.Log(
                "BusinessUserPasswordChanged",
                "Settings",
                "",
                "",
                string.Format(Res("SettingsUsersPasswordChangedLogFmt"), $"{user.FirstName} {user.LastName}".Trim()),
                entityType: "BusinessUser",
                entityId: user.UserId);
        }

        private void ClearBusinessUserPasswordFields()
        {
            BusinessUserCurrentPassword = string.Empty;
            BusinessUserNewPassword = string.Empty;
            BusinessUserConfirmPassword = string.Empty;
        }

        private string BuildCurrentBusinessUserPermissionSummary()
        {
            var user = _currentProfileService.CurrentBusinessUser;
            if (user == null)
                return Res("SettingsUsersOwnerPermissionSummary");

            if (string.Equals(user.RoleKey, "admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(user.RoleKey, "owner", StringComparison.OrdinalIgnoreCase))
            {
                return Res("SettingsUsersAdminPermissionSummary");
            }

            var activeModules = user.ModulePermissions
                .Where(permission => !string.Equals(permission.AccessLevel, "None", StringComparison.OrdinalIgnoreCase))
                .Select(permission => $"{FormatModuleName(permission.ModuleKey)}: {FormatAccessLevel(permission.AccessLevel)}")
                .ToList();

            if (activeModules.Count == 0)
                return Res("SettingsUsersNoModulePermissions");

            return string.Join(" · ", activeModules);
        }

        private string FormatModuleName(string moduleKey)
        {
            return GetBusinessUserModules().FirstOrDefault(module =>
                string.Equals(module.Key, moduleKey, StringComparison.OrdinalIgnoreCase)).DisplayName
                ?? moduleKey;
        }

        private static string FormatAccessLevel(string accessLevel)
        {
            return accessLevel switch
            {
                "Edit" => Res("SettingsUsersAccessEdit"),
                "Export" => Res("SettingsUsersAccessExport"),
                "View" => Res("SettingsUsersAccessView"),
                _ => Res("SettingsUsersAccessNone")
            };
        }

        private IEnumerable<(string Key, string DisplayName, bool AllowExport)> GetBusinessUserModules()
        {
            yield return ("employees", Res("SettingsUsersModuleEmployees"), false);
            yield return ("salary", Res("SettingsUsersModuleSalary"), false);
            yield return ("documents", Res("SettingsUsersModuleDocuments"), false);
            yield return ("companies", Res("SettingsUsersModuleCompanies"), false);
            yield return ("reports", Res("SettingsUsersModuleReports"), true);
            yield return ("settings", Res("SettingsUsersModuleSettings"), false);
        }

        private IEnumerable<UserPermissionOption> GetModuleAccessOptions(bool allowExport)
        {
            yield return new UserPermissionOption { Key = "None", DisplayName = Res("SettingsUsersAccessNone") };
            yield return new UserPermissionOption { Key = "View", DisplayName = Res("SettingsUsersAccessView") };
            yield return new UserPermissionOption { Key = "Edit", DisplayName = Res("SettingsUsersAccessEdit") };
            if (allowExport)
                yield return new UserPermissionOption { Key = "Export", DisplayName = Res("SettingsUsersAccessExport") };
        }

        private void UpdateBusinessUserGeneratedLogin()
        {
            if (_isInitializingBusinessUserEditor)
                return;

            if (!string.IsNullOrWhiteSpace(BusinessUserLogin))
                return;

            var first = NormalizeLoginPart(BusinessUserFirstName);
            var last = NormalizeLoginPart(BusinessUserLastName);
            BusinessUserLogin = string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last)
                ? string.Empty
                : $"{first}.{last}";
        }

        private static string NormalizeBusinessUserLogin(string? value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')
                    builder.Append(ch);
            }

            return builder.ToString();
        }

        private static string NormalizeLoginPart(string? value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            var builder = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
            }

            return builder.ToString();
        }

        private static string GenerateTemporaryPassword()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            Span<byte> bytes = stackalloc byte[10];
            RandomNumberGenerator.Fill(bytes);
            var chars = bytes.ToArray()
                .Select(value => alphabet[value % alphabet.Length])
                .ToArray();
            return new string(chars);
        }

        private void TelegramBotService_StateChanged(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(RefreshTelegramState));
        }

        private void RefreshTelegramState()
        {
            TelegramAuthorizedUsers.Clear();
            foreach (var user in _appSettingsService.Settings.Telegram.AuthorizedUsers
                         .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                TelegramAuthorizedUsers.Add(user);
            }

            SetProperty(ref _telegramEnabled, _appSettingsService.Settings.Telegram.Enabled, nameof(TelegramEnabled));
            TelegramBotStatus = string.IsNullOrWhiteSpace(_telegramBotService.LastStatus)
                ? (HasTelegramBotToken
                    ? string.Format(Res("SettingsTelegramConnectedFmt"), TelegramBotUsername)
                    : Res("SettingsTelegramNotConfigured"))
                : _telegramBotService.LastStatus;

            if (!HasTelegramQrCode && TelegramCanGenerateQr)
                GenerateTelegramQr();

            OnPropertyChanged(nameof(TelegramBotUsername));
            OnPropertyChanged(nameof(HasTelegramBotToken));
            OnPropertyChanged(nameof(TelegramIsConnected));
            OnPropertyChanged(nameof(TelegramNeedsToken));
            OnPropertyChanged(nameof(TelegramCanGenerateQr));
            OnPropertyChanged(nameof(HasTelegramAuthorizedUsers));
            OnPropertyChanged(nameof(HasTelegramQrCode));
            CommandManager.InvalidateRequerySuggested();
        }

        private async System.Threading.Tasks.Task ApplyWebPanelStateAsync()
        {
            try
            {
                if (WebPanelEnabled)
                    await _webPanelHostService.StartAsync();
                else
                    await _webPanelHostService.StopAsync();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.WebPanel", ex.Message);
            }
            finally
            {
                OnPropertyChanged(nameof(WebPanelStatus));
                OnPropertyChanged(nameof(WebPanelUrl));
                OnPropertyChanged(nameof(WebPanelLanUrl));
            }
        }

        private async System.Threading.Tasks.Task PrepareAndCopyWebPanelLanAccessAsync()
        {
            try
            {
                var localIp = GetLocalNetworkIpAddress();
                if (string.IsNullOrWhiteSpace(localIp) || localIp == "127.0.0.1")
                {
                    WebPanelActionStatus = Res("SettingsWebPanelLocalIpMissing");
                    return;
                }

                var settings = _appSettingsService.Settings;
                var needsRestart = _webPanelHostService.IsRunning && !string.Equals(settings.WebPanelBindAddress, "0.0.0.0", StringComparison.OrdinalIgnoreCase);
                settings.WebPanelEnabled = true;
                settings.WebPanelBindAddress = "0.0.0.0";
                _appSettingsService.SaveSettings();

                if (needsRestart)
                    await _webPanelHostService.StopAsync();

                await _webPanelHostService.StartAsync();

                var url = $"http://{localIp}:{settings.WebPanelPort}";
                Clipboard.SetText(url);
                WebPanelActionStatus = string.Format(Res("SettingsWebPanelLanCopiedFmt"), url);
                OnPropertyChanged(nameof(WebPanelEnabled));
                OnPropertyChanged(nameof(WebPanelStatus));
                OnPropertyChanged(nameof(WebPanelUrl));
                OnPropertyChanged(nameof(WebPanelLanUrl));
            }
            catch (Exception ex)
            {
                WebPanelActionStatus = Res("SettingsWebPanelLanPrepareFailed");
                LoggingService.LogWarning("Settings.CopyWebPanelLanUrl", ex.Message);
            }
        }

        private static string GetLocalNetworkIpAddress()
        {
            try
            {
                var candidates = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up
                        && item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address)
                    .Where(address => !IPAddress.IsLoopback(address))
                    .ToList();

                return candidates.FirstOrDefault(IsPrivateIPv4Address)?.ToString()
                    ?? candidates.FirstOrDefault()?.ToString()
                    ?? "127.0.0.1";
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.GetLocalNetworkIpAddress", ex.Message);
                return "127.0.0.1";
            }
        }

        private static bool IsPrivateIPv4Address(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 4
                && (bytes[0] == 10
                    || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
                    || bytes[0] == 192 && bytes[1] == 168);
        }

        private async System.Threading.Tasks.Task ConnectTelegramBotAsync()
        {
            var token = TelegramBotTokenInput?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                TelegramBotStatus = Res("SettingsTelegramPasteBotFatherToken");
                return;
            }

            TelegramIsBusy = true;
            TelegramBotStatus = Res("SettingsTelegramCheckingToken");
            try
            {
                var result = await _telegramBotService.ConnectAsync(token);
                TelegramBotStatus = result.message;
                if (result.ok)
                {
                    TelegramBotTokenInput = string.Empty;
                    TelegramEnabled = true;
                    GenerateTelegramQr();
                }
            }
            catch (Exception ex)
            {
                TelegramBotStatus = string.Format(Res("SettingsTelegramConnectErrorFmt"), ex.Message);
                LoggingService.LogWarning("Settings.ConnectTelegramBot", ex.Message);
            }
            finally
            {
                TelegramIsBusy = false;
                RefreshTelegramState();
            }
        }

        private async System.Threading.Tasks.Task DisconnectTelegramBotAsync()
        {
            TelegramIsBusy = true;
            TelegramBotStatus = Res("SettingsTelegramDisconnecting");
            try
            {
                await _telegramBotService.DisconnectAsync();
                TelegramQrCodeImage = null;
                TelegramPairingCode = string.Empty;
                TelegramPairingExpiryText = string.Empty;
            }
            catch (Exception ex)
            {
                TelegramBotStatus = string.Format(Res("SettingsTelegramDisconnectErrorFmt"), ex.Message);
                LoggingService.LogWarning("Settings.DisconnectTelegramBot", ex.Message);
            }
            finally
            {
                TelegramIsBusy = false;
                RefreshTelegramState();
            }
        }

        private async System.Threading.Tasks.Task RestartTelegramBotAsync()
        {
            try
            {
                await _telegramBotService.RestartAsync();
            }
            catch (Exception ex)
            {
                TelegramBotStatus = string.Format(Res("SettingsTelegramRestartErrorFmt"), ex.Message);
                LoggingService.LogWarning("Settings.RestartTelegramBot", ex.Message);
            }
            finally
            {
                RefreshTelegramState();
            }
        }

        private void GenerateTelegramQr()
        {
            if (!TelegramCanGenerateQr)
            {
                TelegramQrCodeImage = null;
                TelegramPairingCode = string.Empty;
                TelegramPairingExpiryText = string.Empty;
                OnPropertyChanged(nameof(HasTelegramQrCode));
                return;
            }

            TelegramPairingCode = _telegramPairingService.GenerateCode();
            var expiresAt = _telegramPairingService.GetExpiryUtc(TelegramPairingCode).ToLocalTime();
            var deepLink = _telegramPairingService.BuildDeepLink(TelegramBotUsername, TelegramPairingCode);
            TelegramQrCodeImage = _telegramPairingService.BuildQrImage(deepLink);
            TelegramPairingExpiryText = string.Format(Res("SettingsTelegramCodeValidUntilFmt"), expiresAt);
            OnPropertyChanged(nameof(HasTelegramQrCode));
        }

        public void Cleanup()
        {
            _accessStatusService.PropertyChanged -= AccessStatusService_PropertyChanged;
            _telegramBotService.StateChanged -= TelegramBotService_StateChanged;
        }

        public static double GetInterfaceSizeMultiplier(string size) => size switch
        {
            "Small" => 0.74,
            "Medium" => 0.87,
            "Large" => 1.0,
            "ExtraLarge" => 1.14,
            _ => 0.87
        };

        public static double GetTextSizeMultiplier(string size) => size switch
        {
            "Small" => 0.82,
            "Medium" => 1.0,
            "Large" => 1.18,
            "ExtraLarge" => 1.36,
            _ => 1.0
        };

        private static readonly int[] FontSizeKeys = { 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 26, 28, 32, 42 };

        private void ApplyInterfaceSize(string size)
        {
            if (Application.Current.MainWindow is MainWindow mw)
                mw.InterfaceSizeMultiplier = GetInterfaceSizeMultiplier(size);
        }

        public static void ApplyTextSize(string size)
        {
            double mult = GetTextSizeMultiplier(size);
            foreach (int baseSize in FontSizeKeys)
            {
                Application.Current.Resources[$"FS{baseSize}"] = Math.Round(baseSize * mult, 1);
            }
        }

        private string DetectCurrentTheme()
        {
            // Source of truth is the persisted setting — not a scan of merged
            // dictionaries. The old implementation used String.Contains and did
            // not know about Glass / GlassDark, so those themes always fell
            // through to "Light" after a restart (picker mismatched the window).
            var saved = _appSettingsService.Settings.ThemeName;
            return string.IsNullOrWhiteSpace(saved) ? "Light" : saved;
        }

        public void BeginGeminiApiKeyEdit()
        {
            GeminiApiKeyDraft = string.Empty;
            IsEditingGeminiApiKey = true;
        }

        public void CommitGeminiApiKeyEdit()
        {
            var newValue = GeminiApiKeyDraft?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(newValue))
                GeminiApiKey = newValue;

            GeminiApiKeyDraft = string.Empty;
            if (HasGeminiApiKey)
                IsEditingGeminiApiKey = false;
        }

        public void CancelGeminiApiKeyEdit()
        {
            GeminiApiKeyDraft = string.Empty;
            if (HasGeminiApiKey)
                IsEditingGeminiApiKey = false;
        }

        private void InitializeProfileFields()
        {
            _isInitializingProfileFields = true;
            try
            {
                if (_currentProfileService.CurrentProfile == null)
                {
                    ProfileFirstName = string.Empty;
                    ProfileLastName = string.Empty;
                    ProfileRememberMeEnabled = false;
                    return;
                }

                ProfileFirstName = _currentProfileService.CurrentProfile.FirstName;
                ProfileLastName = _currentProfileService.CurrentProfile.LastName;
                ProfileRememberMeEnabled = _currentProfileService.CurrentProfile.RememberMeEnabled;
            }
            finally
            {
                _isInitializingProfileFields = false;
            }
        }

        private async System.Threading.Tasks.Task SaveProfileAsync()
        {
            if (_currentProfileService.CurrentProfile == null)
            {
                SetProfileStatus(Res("SettingsProfileNotLoadedError"), true);
                return;
            }

            if (!ProfileAuthService.IsValidProfileName(ProfileFirstName))
            {
                SetProfileStatus(Res("ProfileErrFirstNameLatin"), true);
                return;
            }

            if (!ProfileAuthService.IsValidProfileName(ProfileLastName))
            {
                SetProfileStatus(Res("ProfileErrLastNameLatin"), true);
                return;
            }

            var nameResult = await _profileAuthService.UpdateProfileNameAsync(
                _currentProfileService.CurrentProfile.ClientId,
                ProfileFirstName,
                ProfileLastName);

            if (!nameResult.Success || nameResult.Profile == null)
            {
                SetProfileStatus(nameResult.ErrorMessage, true);
                return;
            }

            var rememberResult = await _profileAuthService.UpdateRememberMeAsync(
                nameResult.Profile.ClientId,
                ProfileRememberMeEnabled);

            if (!rememberResult.Success || rememberResult.Profile == null)
            {
                SetProfileStatus(rememberResult.ErrorMessage, true);
                return;
            }

            UpdateCurrentProfile(rememberResult.Profile);

            if (ProfileRememberMeEnabled)
                _profileSessionService.SaveRememberedSession(rememberResult.Profile);
            else
                _profileSessionService.ClearRememberedSession();

            SetProfileStatus(Res("SettingsProfileUpdated"), false);
        }

        private async System.Threading.Tasks.Task ChangeProfilePasswordAsync()
        {
            if (_currentProfileService.CurrentProfile == null)
            {
                SetProfileStatus(Res("SettingsProfileNotLoadedError"), true);
                return;
            }

            if (string.IsNullOrWhiteSpace(ProfileCurrentPassword))
            {
                SetProfileStatus(Res("ProfileErrCurrentPasswordRequired"), true);
                return;
            }

            if (string.IsNullOrWhiteSpace(ProfileNewPassword))
            {
                SetProfileStatus(Res("ProfileErrNewPasswordRequired"), true);
                return;
            }

            if (ProfileNewPassword.Length < 6)
            {
                SetProfileStatus(Res("ProfileErrPasswordMinLength"), true);
                return;
            }

            if (!string.Equals(ProfileNewPassword, ProfileConfirmPassword, StringComparison.Ordinal))
            {
                SetProfileStatus(Res("ProfileErrPasswordsMismatch"), true);
                return;
            }

            var result = await _profileAuthService.ChangePasswordAsync(
                _currentProfileService.CurrentProfile.ClientId,
                ProfileCurrentPassword,
                ProfileNewPassword);

            if (!result.Success || result.Profile == null)
            {
                SetProfileStatus(result.ErrorMessage, true);
                return;
            }

            UpdateCurrentProfile(result.Profile);

            if (ProfileRememberMeEnabled)
                _profileSessionService.SaveRememberedSession(result.Profile);
            else
                _profileSessionService.ClearRememberedSession();

            ProfileCurrentPassword = string.Empty;
            ProfileNewPassword = string.Empty;
            ProfileConfirmPassword = string.Empty;
            SetProfileStatus(Res("SettingsProfilePasswordChanged"), false);
        }

        private async System.Threading.Tasks.Task LogoutProfileAsync()
        {
            var profile = _currentProfileService.CurrentProfile;
            if (profile == null)
            {
                SetProfileStatus(Res("SettingsProfileNotLoadedError"), true);
                return;
            }

            var title = Res("SettingsProfileLogoutTitle");
            var message = Res("SettingsProfileLogoutConfirm");
            if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                if (profile.RememberMeEnabled)
                    await _profileAuthService.UpdateRememberMeAsync(profile.ClientId, enabled: false);

                _profileSessionService.ClearRememberedSession();
                _appSettingsService.Settings.CurrentBusinessUserId = string.Empty;
                if (_appSettingsService.Settings.ExperimentalMultiUser)
                    _appSettingsService.Settings.PendingMemberRoleSelection = true;
                _appSettingsService.SaveSettings();
                _currentProfileService.SetCurrentBusinessUser(null);

                _activityLogService.Log(
                    "ProfileLogout",
                    "Settings",
                    "",
                    "",
                    string.Format(Res("SettingsProfileLogoutLogFmt"), $"{profile.FirstName} {profile.LastName}".Trim()));

                RestartApplicationForLogin();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Settings.LogoutProfile", ex);
                SetProfileStatus(Res("SettingsProfileLogoutFailed"), true);
            }
        }

        private static void RestartApplicationForLogin()
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath) && System.IO.File.Exists(executablePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = true
                });
            }

            Application.Current.Shutdown();
        }

        private void UpdateCurrentProfile(ClientProfileRecord profile)
        {
            _currentProfileService.SetCurrentProfile(profile);
            InitializeProfileFields();
            OnPropertyChanged(nameof(HasProfile));
            OnPropertyChanged(nameof(ProfileClientId));
        }

        private async System.Threading.Tasks.Task SyncRememberMeAsync(bool enabled)
        {
            if (_currentProfileService.CurrentProfile == null)
                return;

            var currentProfile = _currentProfileService.CurrentProfile;
            _isSyncingRememberMe = true;

            try
            {
                var result = await _profileAuthService.UpdateRememberMeAsync(currentProfile.ClientId, enabled);
                if (!result.Success || result.Profile == null)
                {
                    _isInitializingProfileFields = true;
                    ProfileRememberMeEnabled = currentProfile.RememberMeEnabled;
                    _isInitializingProfileFields = false;
                    SetProfileStatus(result.ErrorMessage, true);
                    return;
                }

                UpdateCurrentProfile(result.Profile);

                if (enabled)
                    _profileSessionService.SaveRememberedSession(result.Profile);
                else
                    _profileSessionService.ClearRememberedSession();

                SetProfileStatus(Res("SettingsProfileRememberUpdated"), false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Settings.SyncRememberMe", ex.Message);
                _isInitializingProfileFields = true;
                ProfileRememberMeEnabled = currentProfile.RememberMeEnabled;
                _isInitializingProfileFields = false;
                SetProfileStatus(Res("SettingsProfileRememberUpdateFailed"), true);
            }
            finally
            {
                _isSyncingRememberMe = false;
            }
        }

        private void SetProfileStatus(string message, bool isError)
        {
            ProfileStatusMessage = message;
            ProfileStatusIsError = isError;
        }

        private void ClearProfilePasswordFields()
        {
            ProfileCurrentPassword = string.Empty;
            ProfileNewPassword = string.Empty;
            ProfileConfirmPassword = string.Empty;
        }
    }
}
