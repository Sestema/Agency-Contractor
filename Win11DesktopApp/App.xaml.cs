using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Fonts;
using Win11DesktopApp.DependencyInjection;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.Services.Scanning;
using Win11DesktopApp.Telegram;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp
{
    public partial class App : Application
    {
        private static ServiceProvider? _serviceProvider;
        private static CancellationTokenSource? _backgroundTasksCts;
        private static bool _heartbeatFailureActive;
        private static ClientAccessState _currentGeminiAccessState = new();
        private static string? _recommendedVersionPromptedFor;
        private static Views.SplashWindow? _splashWindow;
        private static int _versionPolicyEnforcementActive;
        private const string AccessPlanTrial = "trial";
        private const string AccessPlanStandard = "standard";
        private const string AccessPlanPro = "pro";
        private static IServiceProvider Services =>
            _serviceProvider ?? throw new InvalidOperationException("Service provider is not initialized.");

        private static CancellationToken BackgroundTaskToken => _backgroundTasksCts?.Token ?? CancellationToken.None;

        private static T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

        private static NavigationService NavigationService => GetRequiredService<NavigationService>();
        private static ThemeService ThemeService => GetRequiredService<ThemeService>();
        private static LanguageService LanguageService => GetRequiredService<LanguageService>();
        private static AppSettingsService AppSettingsService => GetRequiredService<AppSettingsService>();
        private static FolderService FolderService => GetRequiredService<FolderService>();
        private static CompanyService CompanyService => GetRequiredService<CompanyService>();
        private static EmployeeService EmployeeService => GetRequiredService<EmployeeService>();
        private static AdminMirrorSyncService AdminMirrorSyncService => GetRequiredService<AdminMirrorSyncService>();
        private static RecentlyDeletedService RecentlyDeletedService => GetRequiredService<RecentlyDeletedService>();
        private static DocumentLocalizationService DocumentLocalizationService => GetRequiredService<DocumentLocalizationService>();
        private static FinanceService FinanceService => GetRequiredService<FinanceService>();
        private static LocalDbService LocalDbService => GetRequiredService<LocalDbService>();
        private static SalaryDbService SalaryDbService => GetRequiredService<SalaryDbService>();
        private static ActivityLogService ActivityLogService => GetRequiredService<ActivityLogService>();
        private static AppStatisticsService AppStatisticsService => GetRequiredService<AppStatisticsService>();
        private static GeminiApiService GeminiApiService => GetRequiredService<GeminiApiService>();
        private static ProfileDialogFactory ProfileDialogFactory => GetRequiredService<ProfileDialogFactory>();
        private static StartupDialogFactory StartupDialogFactory => GetRequiredService<StartupDialogFactory>();
        private static UnifiedLoginService UnifiedLoginService => GetRequiredService<UnifiedLoginService>();
        private static BusinessUserAuthService BusinessUserAuthService => GetRequiredService<BusinessUserAuthService>();
        private static ProfileAuthService ProfileAuthService => GetRequiredService<ProfileAuthService>();
        private static ProfileSessionService ProfileSessionService => GetRequiredService<ProfileSessionService>();
        private static BusinessUserSessionService BusinessUserSessionService => GetRequiredService<BusinessUserSessionService>();
        private static BusinessUserDirectoryService BusinessUserDirectoryService => GetRequiredService<BusinessUserDirectoryService>();
        private static WorkspaceSessionService WorkspaceSessionService => GetRequiredService<WorkspaceSessionService>();
        private static AccessStatusService AccessStatusService => GetRequiredService<AccessStatusService>();
        private static CurrentProfileService CurrentProfileService => GetRequiredService<CurrentProfileService>();
        private static TelegramBotService TelegramBotService => GetRequiredService<TelegramBotService>();
        private static AppUpdateNotificationService AppUpdateNotificationService => GetRequiredService<AppUpdateNotificationService>();
        private static WebPanelHostService WebPanelHostService => GetRequiredService<WebPanelHostService>();
        private static SyncEventService SyncEventService => GetRequiredService<SyncEventService>();
        private static ConnectedClientsService ConnectedClientsService => GetRequiredService<ConnectedClientsService>();
        private static DailySqliteBackupService DailySqliteBackupService => GetRequiredService<DailySqliteBackupService>();

        private enum MultiUserStartupResult
        {
            Skipped,
            OwnerSelected,
            MemberLoggedIn,
            Cancelled
        }

        private sealed class StartupFlowState
        {
            public bool SkipLicenseGate { get; init; }
            public LocalLicenseStatus LocalLicenseStatus { get; set; } = null!;
            public ClientAccessState StartupAccess { get; set; } = new();
            public string? StartupClientId { get; set; }
            public bool IsRemoteTrialExpired { get; set; }
            public RemotePolicy? StartupPolicy { get; set; }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _backgroundTasksCts = new CancellationTokenSource();

            GlobalFontSettings.FontResolver = new PdfFontResolver();
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            // Gentle fade-in for all windows (dialogs feel lighter, less jarring).
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyWindowLoaded_FadeIn));

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    LoggingService.LogError("AppDomain.UnhandledException", ex);
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LoggingService.LogError("TaskScheduler.UnobservedTaskException", args.Exception);
                args.SetObserved();
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LoggingService.LogError("App.DispatcherUnhandledException", args.Exception);
                var isXamlParseError = args.Exception is XamlParseException;

                ErrorHandler.Report("App.DispatcherUnhandledException", args.Exception, ErrorSeverity.Critical, showUser: true);

                if (isXamlParseError)
                {
                    Shutdown(-1);
                    args.Handled = true;
                    return;
                }

                args.Handled = true;
            };

            StartupIntegrityService startupIntegrityService;
            var startupStopwatch = Stopwatch.StartNew();
            void LogStartupPhase(string phase) =>
                LoggingService.LogInfo("App.Startup", $"{phase} at {startupStopwatch.ElapsedMilliseconds} ms");

            try
            {
                startupIntegrityService = InitializeCoreServices();
#if DEBUG
                Diagnostics.BindingErrorTraceListener.Enable();
#endif
                LogStartupPhase("startup_begin");
                startupIntegrityService.IncludeFinanceStartupState(FinanceService);
                RunBackgroundWarmupTasks();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("App.OnStartup.Init", ex);
                ErrorHandler.Report("App.OnStartup", ex, ErrorSeverity.Critical, showUser: true);
                Shutdown(-1);
                return;
            }

            ApplySavedLanguageAndTheme();
            ShowSplashWindow();
            RunStartupMigrations();
            AppStatisticsService.StartSession();
            LoggingService.LogInfo("App", "All services initialized");
            LogStartupPhase("services_initialized");

            var startupState = CreateStartupFlowState();
            await ResolveStartupAccessAsync(startupState, LogStartupPhase);

            var multiUserStartupResult = await RunMultiUserStartupGateAsync(startupState, LogStartupPhase);
            if (multiUserStartupResult == MultiUserStartupResult.Cancelled)
                return;

            if (multiUserStartupResult != MultiUserStartupResult.MemberLoggedIn)
            {
                if (!await RunProfileGateAsync(startupState, LogStartupPhase))
                    return;

                if (multiUserStartupResult == MultiUserStartupResult.OwnerSelected)
                    ClearBusinessUserSession();
                else
                    RestoreBusinessUserSession();
            }
            await TryMigrateLegacyLicenseAsync(startupState, LogStartupPhase);
            if (!await ApplyStartupPolicyAsync(startupState, LogStartupPhase))
                return;

            ShowMainWindow(LogStartupPhase);
            await FinalizeStartupAsync(startupIntegrityService, startupState);
        }

        private static void ApplySavedLanguageAndTheme()
        {
            if (!string.IsNullOrEmpty(AppSettingsService.Settings.LanguageCode))
            {
                LanguageService.SetLanguage(AppSettingsService.Settings.LanguageCode);
            }

            if (!string.IsNullOrEmpty(AppSettingsService.Settings.DocumentLanguage))
            {
                DocumentLocalizationService.LoadLanguage(AppSettingsService.Settings.DocumentLanguage);
            }
            else if (!string.IsNullOrEmpty(AppSettingsService.Settings.LanguageCode))
            {
                DocumentLocalizationService.LoadLanguage(AppSettingsService.Settings.LanguageCode);
            }

            if (!string.IsNullOrEmpty(AppSettingsService.Settings.ThemeName))
            {
                ThemeService.SetTheme(AppSettingsService.Settings.ThemeName);
            }
        }

        private static void RunStartupMigrations()
        {
            if (GetRequiredService<AppDataStorageFactory>().IsPostgresRuntimeActiveAtStartup)
            {
                LoggingService.LogInfo("App.StartupMigrations", "SQLite startup migrations skipped because PostgreSQL runtime is active.");

                var postgresEmployeeIndexRebuild = EmployeeService.EnsureEmployeeIndexBuilt();
                if (postgresEmployeeIndexRebuild.WasRebuildAttempted)
                {
                    if (postgresEmployeeIndexRebuild.IsSuccessful)
                    {
                        var successMessage = string.Format(
                            Res("MsgEmployeeIndexBuildSuccess", "Employee index was built in {3}. Imported records: {0}. Folders scanned: {1}. Skipped: {2}"),
                            postgresEmployeeIndexRebuild.RecordsImported,
                            postgresEmployeeIndexRebuild.FoldersScanned,
                            postgresEmployeeIndexRebuild.FoldersSkipped,
                            "PostgreSQL");
                        ToastService.Instance.Warning(successMessage);
                        LoggingService.LogInfo("App.EmployeeIndexBuild", successMessage);
                    }
                    else
                    {
                        var failedMessage = string.Format(
                            Res("MsgEmployeeIndexBuildFailed", "Employee index build in {1} failed. The program will keep using the current source. Details: {0}"),
                            postgresEmployeeIndexRebuild.Message,
                            "PostgreSQL");
                        MessageBox.Show(
                            failedMessage,
                            Res("TitleWarning", "Warning"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        LoggingService.LogWarning("App.EmployeeIndexBuild", failedMessage);
                    }
                }

                RemoveDuplicateSalaryHistoryAtStartup("PostgreSQL");
                return;
            }

            var employeeIndexRebuild = EmployeeService.EnsureEmployeeIndexBuilt();
            if (employeeIndexRebuild.WasRebuildAttempted)
            {
                if (employeeIndexRebuild.IsSuccessful)
                {
                    var indexStorageName = AppSettingsService.Settings.DatabaseStorageMode == DatabaseStorageModes.Postgres
                        ? "PostgreSQL"
                        : "SQLite";
                    var successMessage = string.Format(
                        Res("MsgEmployeeIndexBuildSuccess", "Employee index was built in {3}. Imported records: {0}. Folders scanned: {1}. Skipped: {2}"),
                        employeeIndexRebuild.RecordsImported,
                        employeeIndexRebuild.FoldersScanned,
                        employeeIndexRebuild.FoldersSkipped,
                        indexStorageName);
                    ToastService.Instance.Warning(successMessage);
                    LoggingService.LogInfo("App.EmployeeIndexBuild", successMessage);
                }
                else
                {
                    var indexStorageName = AppSettingsService.Settings.DatabaseStorageMode == DatabaseStorageModes.Postgres
                        ? "PostgreSQL"
                        : "SQLite";
                    var failedMessage = string.Format(
                        Res("MsgEmployeeIndexBuildFailed", "Employee index build in {1} failed. The program will keep using the current source. Details: {0}"),
                        employeeIndexRebuild.Message,
                        indexStorageName);
                    MessageBox.Show(
                        failedMessage,
                        Res("TitleWarning", "Warning"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    LoggingService.LogWarning("App.EmployeeIndexBuild", failedMessage);
                }
            }

            RemoveDuplicateSalaryHistoryAtStartup("SQLite");
            RecentlyDeletedService.EnsureStorage();
        }

        private static void RemoveDuplicateSalaryHistoryAtStartup(string storageName)
        {
            try
            {
                var removed = FinanceService.RemoveDuplicateSalaryHistoryRecordsAtStartup();
                if (removed > 0)
                {
                    LoggingService.LogInfo(
                        "App.SalaryHistoryDuplicateCleanup",
                        $"Removed {removed} duplicate salary history row(s) from {storageName}.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("App.SalaryHistoryDuplicateCleanup", ex.Message);
            }
        }

        private static StartupFlowState CreateStartupFlowState()
        {
            return new StartupFlowState
            {
                SkipLicenseGate =
#if DEBUG
                    true,
#else
                    Debugger.IsAttached,
#endif
                LocalLicenseStatus = LicenseService.GetLocalLicenseStatus()
            };
        }

        private static void SetCurrentProfile(ClientProfileRecord? profile)
        {
            CurrentProfileService.SetCurrentProfile(profile);
        }

        private static void RestoreBusinessUserSession()
        {
            var userId = AppSettingsService.Settings.CurrentBusinessUserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                CurrentProfileService.SetCurrentBusinessUser(null);
                return;
            }

            var user = AppSettingsService.Settings.BusinessUsers.FirstOrDefault(candidate =>
                candidate.IsActive
                && string.Equals(candidate.UserId, userId, StringComparison.OrdinalIgnoreCase));
            CurrentProfileService.SetCurrentBusinessUser(user);
        }

        private static void ClearBusinessUserSession()
        {
            BusinessUserAuthService.LogoutSession();
        }

        private async Task<MultiUserStartupResult> RunMultiUserStartupGateAsync(StartupFlowState state, Action<string> logStartupPhase)
        {
            if (!AppSettingsService.Settings.ExperimentalMultiUser)
            {
                logStartupPhase("unified_login_skipped");
                return MultiUserStartupResult.Skipped;
            }

            if (!UnifiedLoginService.ShouldShowUnifiedLogin(AppSettingsService.Settings))
            {
                UnifiedLoginService.ImportBusinessUsersFromRootIfAvailable();
                if (BusinessUserSessionService.TryRestoreRememberedSession())
                {
                    logStartupPhase("unified_login_member_remembered");
                    return MultiUserStartupResult.MemberLoggedIn;
                }

                logStartupPhase("unified_login_skipped");
                return MultiUserStartupResult.Skipped;
            }

            if (AppSettingsService.Settings.PendingMemberRoleSelection)
            {
                AppSettingsService.Settings.PendingMemberRoleSelection = false;
                AppSettingsService.SaveSettings();
            }

            ClearBusinessUserSession();
            UnifiedLoginService.ImportBusinessUsersFromRootIfAvailable();

            ClientProfileRecord? ownerProfile = null;
            if (!string.IsNullOrWhiteSpace(state.StartupClientId))
            {
                var profileCheck = await ProfileAuthService.CheckProfileAsync(state.StartupClientId);
                if (profileCheck.IsFeatureAvailable)
                    ownerProfile = profileCheck.Profile;
            }

            var loginWindow = StartupDialogFactory.CreateUnifiedLoginWindow(state.StartupClientId, ownerProfile);
            MainWindow = loginWindow;
            var loginAccepted = loginWindow.ShowDialog() == true;
            MainWindow = null;

            if (!loginAccepted)
            {
                ClearBusinessUserSession();
                Shutdown();
                logStartupPhase("unified_login_cancelled");
                return MultiUserStartupResult.Cancelled;
            }

            if (loginWindow.LoginKind == UnifiedLoginKind.Member)
            {
                if (string.IsNullOrWhiteSpace(AppSettingsService.Settings.RootFolderPath))
                {
                    var folderWindow = StartupDialogFactory.CreateMemberRootFolderWindow();
                    MainWindow = folderWindow;
                    var folderAccepted = folderWindow.ShowDialog() == true
                        && !string.IsNullOrWhiteSpace(folderWindow.SelectedRootFolderPath);
                    MainWindow = null;

                    if (!folderAccepted)
                    {
                        ClearBusinessUserSession();
                        Shutdown();
                        logStartupPhase("unified_login_member_folder_cancelled");
                        return MultiUserStartupResult.Cancelled;
                    }
                }

                if (!UnifiedLoginService.TryImportBusinessUsersFromRoot(out _)
                    || loginWindow.AuthenticatedMember == null
                    || CurrentProfileService.CurrentBusinessUser == null)
                {
                    MessageBox.Show(
                        Res("BusinessUserLoginUsersNotImported"),
                        "Agency Contractor",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ClearBusinessUserSession();
                    Shutdown();
                    logStartupPhase("unified_login_member_import_failed");
                    return MultiUserStartupResult.Cancelled;
                }

                BusinessUserAuthService.ActivateSession(loginWindow.AuthenticatedMember);
                logStartupPhase("unified_login_member_logged_in");
                return MultiUserStartupResult.MemberLoggedIn;
            }

            if (loginWindow.LoginKind == UnifiedLoginKind.Owner && loginWindow.AuthenticatedProfile != null)
            {
                SetCurrentProfile(loginWindow.AuthenticatedProfile);
                ClearBusinessUserSession();
                logStartupPhase("unified_login_owner_logged_in");
                return MultiUserStartupResult.OwnerSelected;
            }

            ClearBusinessUserSession();
            Shutdown();
            logStartupPhase("unified_login_failed");
            return MultiUserStartupResult.Cancelled;
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private static async Task ResolveStartupAccessAsync(StartupFlowState state, Action<string> logStartupPhase)
        {
            var startupClientTask = TelemetryService.GetStartupAccessStateAsync();
            var startupTelemetryCompleted = await Task.WhenAny(startupClientTask, Task.Delay(3500)) == startupClientTask;
            if (startupTelemetryCompleted)
            {
                state.StartupAccess = await startupClientTask;
                state.StartupClientId = state.StartupAccess.ClientId;
                logStartupPhase("telemetry_completed");
            }
            else
            {
                state.StartupAccess = TelemetryService.GetCachedAccessStateSnapshot();
                if (state.StartupAccess.HasKnownState)
                {
                    state.StartupAccess.IsStale = true;
                    state.StartupAccess.Source = state.StartupAccess.IsOfflineGraceActive ? "cache_offline_grace" : "cache_stale";
                }

                state.StartupClientId = state.StartupAccess.ClientId;
                LoggingService.LogWarning("App.ProfileGate",
                    state.StartupAccess.IsOfflineGraceActive
                        ? "Telemetry startup sync timed out. Continuing with cached offline access state."
                        : "Telemetry startup sync timed out. Continuing without profile gate.");
                logStartupPhase("telemetry_timeout");
            }
        }

        private async Task<bool> RunProfileGateAsync(StartupFlowState state, Action<string> logStartupPhase)
        {
            if (!string.IsNullOrWhiteSpace(state.StartupClientId))
            {
                AdminMirrorSyncService.Start(state.StartupClientId);
                var profileCheckTask = ProfileAuthService.CheckProfileAsync(state.StartupClientId);
                ProfileCheckResult profileCheck;
                if (await Task.WhenAny(profileCheckTask, Task.Delay(2000)) == profileCheckTask)
                {
                    profileCheck = await profileCheckTask;
                }
                else
                {
                    LoggingService.LogWarning("App.ProfileGate",
                        "Profile check timed out after 2000ms. Continuing without profile gate.");
                    profileCheck = new ProfileCheckResult
                    {
                        IsFeatureAvailable = false,
                        ErrorMessage = "Profile check timed out"
                    };
                }

                if (profileCheck.IsFeatureAvailable && profileCheck.RequiresSetup)
                {
                    var profileWindow = ProfileDialogFactory.CreateSetupWindow(state.StartupClientId);
                    MainWindow = profileWindow;
                    var profileAccepted = profileWindow.ShowDialog() == true && profileWindow.IsProfileCreated;
                    MainWindow = null;

                    if (!profileAccepted)
                    {
                        Shutdown();
                        return false;
                    }

                    SetCurrentProfile(await ProfileAuthService.GetProfileByClientIdAsync(state.StartupClientId));
                }
                else if (profileCheck.IsFeatureAvailable && profileCheck.Profile != null)
                {
                    var profile = profileCheck.Profile;
                    var existingProfile = CurrentProfileService.CurrentProfile;
                    if (existingProfile != null
                        && string.Equals(existingProfile.ClientId, profile.ClientId, StringComparison.OrdinalIgnoreCase))
                    {
                        SetCurrentProfile(existingProfile);
                        logStartupPhase("profile_gate_unified_skip");
                        return true;
                    }

                    if (profile.MustResetPassword)
                    {
                        ProfileSessionService.ClearRememberedSession();

                        var resetWindow = ProfileDialogFactory.CreateResetPasswordWindow(profile);
                        MainWindow = resetWindow;
                        var resetAccepted = resetWindow.ShowDialog() == true && resetWindow.IsPasswordReset;
                        MainWindow = null;

                        if (!resetAccepted || resetWindow.ResetProfile == null)
                        {
                            Shutdown();
                            return false;
                        }

                        SetCurrentProfile(resetWindow.ResetProfile);
                    }
                    else if (ProfileSessionService.TryRestoreRememberedSession(profile))
                    {
                        SetCurrentProfile(profile);
                    }
                    else
                    {
                        var loginWindow = ProfileDialogFactory.CreateLoginWindow(profile);
                        MainWindow = loginWindow;
                        var loginAccepted = loginWindow.ShowDialog() == true && loginWindow.IsAuthenticated;
                        MainWindow = null;

                        if (!loginAccepted || loginWindow.AuthenticatedProfile == null)
                        {
                            Shutdown();
                            return false;
                        }

                        SetCurrentProfile(loginWindow.AuthenticatedProfile);
                    }
                }
                else if (!profileCheck.IsFeatureAvailable)
                {
                    LoggingService.LogWarning("App.ProfileGate",
                        $"Profile gate skipped: {profileCheck.ErrorMessage}");
                }

                logStartupPhase("profile_gate_completed");
                return true;
            }

            AdminMirrorSyncService.Start();
            LoggingService.LogWarning("App.ProfileGate",
                "Client ID unavailable during startup. Continuing without profile gate.");
            logStartupPhase("profile_gate_skipped");
            return true;
        }

        private static async Task TryMigrateLegacyLicenseAsync(StartupFlowState state, Action<string> logStartupPhase)
        {
            if (state.StartupAccess.IsLive
                && !state.StartupAccess.IsBlocked
                && state.LocalLicenseStatus.IsValid
                && string.IsNullOrWhiteSpace(AppSettingsService.Settings.LegacyLicenseMigratedAtUtc)
                && LocalLicenseIsStronger(state.LocalLicenseStatus, state.StartupAccess))
            {
                var migrated = await TelemetryService.MigrateLegacyLicenseAsync(
                    state.LocalLicenseStatus.Plan,
                    state.LocalLicenseStatus.ExpiresOn,
                    state.LocalLicenseStatus.ActivatedOn,
                    state.LocalLicenseStatus.IsUnlimited);
                if (migrated != null)
                {
                    state.StartupAccess = migrated;
                    state.StartupClientId = migrated.ClientId;
                    AppSettingsService.Settings.LegacyLicenseMigratedAtUtc = DateTime.UtcNow.ToString("o");
                    AppSettingsService.SaveSettings();
                    state.LocalLicenseStatus = LicenseService.GetLocalLicenseStatus();
                    logStartupPhase("legacy_license_migrated");
                }
            }
        }

        private async Task<bool> ApplyStartupPolicyAsync(StartupFlowState state, Action<string> logStartupPhase)
        {
            if (state.StartupAccess.IsBlocked)
            {
                MessageBox.Show(
                    "Доступ до програми заблоковано адміністратором.",
                    "Agency Contractor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return false;
            }

            state.StartupPolicy = state.StartupAccess.Policy;
            state.IsRemoteTrialExpired = state.StartupAccess.HasKnownState && state.StartupAccess.IsExpired;
            state.StartupPolicy = BuildEffectivePolicy(state.LocalLicenseStatus, state.StartupAccess, state.StartupPolicy);

            await PolicyService.ApplyPolicyAsync(state.StartupPolicy);
            _currentGeminiAccessState = state.StartupAccess;
            ApplyEffectiveGeminiApiKey(state.StartupAccess, state.StartupPolicy);
            if (!await EnforceVersionPolicyAsync(state.StartupPolicy))
                return false;

            if (!state.SkipLicenseGate && !state.LocalLicenseStatus.IsValid && !state.StartupAccess.HasKnownState)
            {
                var licenseWindow = new Views.LicenseWindow(AccessStatusService, shutdownOnCloseWithoutAccess: true, initialAccessState: state.StartupAccess);
                MainWindow = licenseWindow;
                var licenseAccepted = licenseWindow.ShowDialog() == true && licenseWindow.IsActivated;
                MainWindow = null;

                if (!licenseAccepted)
                {
                    Shutdown();
                    return false;
                }

                state.LocalLicenseStatus = LicenseService.GetLocalLicenseStatus();
                state.StartupAccess = licenseWindow.LatestAccessState.HasKnownState ? licenseWindow.LatestAccessState : state.StartupAccess;
                state.StartupPolicy = BuildEffectivePolicy(state.LocalLicenseStatus, state.StartupAccess, state.StartupAccess.Policy ?? state.StartupPolicy);
                await PolicyService.ApplyPolicyAsync(state.StartupPolicy);
                _currentGeminiAccessState = state.StartupAccess;
                ApplyEffectiveGeminiApiKey(state.StartupAccess, state.StartupPolicy);
                if (!await EnforceVersionPolicyAsync(state.StartupPolicy))
                    return false;
            }

            logStartupPhase("license_gate_completed");
            AccessStatusService.Initialize(state.LocalLicenseStatus, state.StartupAccess, state.StartupPolicy);
            return true;
        }

        private void ShowMainWindow(Action<string> logStartupPhase)
        {
            NavigationService.NavigateTo<MainViewModel>();

            var mainWindow = new MainWindow(AppSettingsService)
            {
                DataContext = _serviceProvider!.GetRequiredService<MainWindowViewModel>()
            };
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            logStartupPhase("main_window_shown");
        }

        private static async Task FinalizeStartupAsync(StartupIntegrityService startupIntegrityService, StartupFlowState state)
        {
            if (state.StartupAccess.PendingCommands.Count > 0)
                await CommandService.ExecutePendingCommandsAsync(state.StartupAccess.PendingCommands, state.StartupClientId);

            RunBackgroundTask("App.HeartbeatLoop", StartHeartbeatLoopAsync, BackgroundTaskToken);
            _ = WorkspaceSessionService.ReportSessionAsync(state.StartupClientId);
            if (!string.IsNullOrEmpty(AppSettingsService.PendingUpdateFrom))
            {
                var previousVersion = AppSettingsService.PendingUpdateFrom;
                TelemetryService.TrackEvent("app_updated", new Dictionary<string, object>
                {
                    ["from_version"] = previousVersion,
                    ["to_version"] = AppSettingsService.CurrentAppVersion
                });
                AppUpdateNotificationService.NotifyInstalledUpdate(previousVersion);
                AppSettingsService.PendingUpdateFrom = null;
            }

            if (state.IsRemoteTrialExpired)
            {
                ToastService.Instance.Warning(
                    "Пробний період завершився. Програма працює лише в режимі перегляду до активації в AdminPanel.");
            }

            RunBackgroundTask("StartupIntegrityService.BackgroundCheck", () =>
            {
                startupIntegrityService.RunBackgroundCheck(CompanyService.Companies);
                EmployeeService.RunSessionEmployeeIndexIntegrityCheck(CompanyService.Companies);
            }, BackgroundTaskToken);
            RunBackgroundTask("App.UpdateNotificationCheck", AppUpdateNotificationService.CheckForAvailableUpdateAsync, BackgroundTaskToken);
            RunBackgroundTask("RecentlyDeletedService.PurgeExpired", () => RecentlyDeletedService.PurgeExpired(), BackgroundTaskToken);
            RunBackgroundTask("DailySqliteBackupService", async token =>
            {
                var result = await DailySqliteBackupService.CreateTodayBackupIfNeededAsync(token).ConfigureAwait(false);
                LoggingService.LogInfo("DailySqliteBackupService", result.Message);
            }, BackgroundTaskToken);
            RunBackgroundTask("App.SalaryPrewarm", PrewarmSalaryPath, BackgroundTaskToken);
            RunBackgroundTask("App.TelegramBotStartup", async _ =>
            {
                if (AppSettingsService.Settings.Telegram.Enabled
                    && !string.IsNullOrWhiteSpace(AppSettingsService.Settings.Telegram.EncryptedBotToken))
                {
                    await TelegramBotService.RestartAsync().ConfigureAwait(false);
                }
            }, BackgroundTaskToken);

            RunBackgroundTask("App.WebPanelStartup", async token =>
            {
                await WebPanelHostService.StartAsync(token).ConfigureAwait(false);
            }, BackgroundTaskToken);

            ConnectedClientsService.Start();
        }

        private static StartupIntegrityService InitializeCoreServices()
        {
            _serviceProvider = BuildServiceProvider();

            // Resolve the minimum set first so logging is configured before other services can emit startup diagnostics.
            if (!string.IsNullOrEmpty(FolderService.RootPath))
            {
                LoggingService.Initialize(FolderService.RootPath);
                FolderService.EnsureWorkspacePassport();
            }

            LoggingService.LogInfo("App", $"Application started v{AppSettingsService.CurrentAppVersion}");

            InitializeStartupServices();

            var startupIntegrityService = _serviceProvider.GetRequiredService<StartupIntegrityService>();
            startupIntegrityService.IncludeSettingsStartupState(AppSettingsService);
            startupIntegrityService.RunQuickCheck();

            if (!string.IsNullOrEmpty(AppSettingsService.Settings.GeminiModel))
                GeminiApiService.SetModel(AppSettingsService.Settings.GeminiModel);

            return startupIntegrityService;
        }

        private static ServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddApplicationServices();
            return services.BuildServiceProvider();
        }

        private static void InitializeStartupServices()
        {
            LicenseService.Initialize(AppSettingsService);
            PolicyService.Initialize(AppSettingsService, CurrentProfileService);
            TelemetryService.Initialize(AppSettingsService, CompanyService);
            CompanyService.InitializeAdminMirrorSyncService(AdminMirrorSyncService);
            AdminMirrorSyncService.InitializeEmployeeService(EmployeeService);
            EmployeeService.InitializeFinanceService(FinanceService);
            CommandService.Initialize(AccessStatusService);
            SyncEventService.Start();
            SyncEventService.SyncEventReceived += OnStartupSyncEventReceived;
        }

        private static void OnStartupSyncEventReceived(object? sender, SyncEventReceivedEventArgs e)
        {
            // Keep employee list cache from hiding new/removed profiles after inbound sync.
            // Hot-path load uses a light folder-count index check; session deep check runs at startup.
            if (string.Equals(e.Record.Type, "EmployeeCreated", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(e.Record.FirmName))
                    EmployeeService.InvalidateEmployeesCache(e.Record.FirmName);
                return;
            }

            if (string.Equals(e.Record.Type, "CompanyChanged", StringComparison.OrdinalIgnoreCase))
                EmployeeService.InvalidateEmployeesCache();
        }

        private static void RunBackgroundWarmupTasks()
        {
            RunBackgroundTask("App.PendingCleanupStartup", async _ =>
            {
                await PendingCleanupService.ProcessPendingCleanupAsync(EmployeeService.TryCleanupDeferredDirectory);
            }, BackgroundTaskToken);

            RunBackgroundTask("App.NetPdfWarmUp", () =>
            {
                NetPdfFormHelper.WarmUp();
            }, BackgroundTaskToken);
        }

        private static void RunBackgroundTask(string module, Action action, CancellationToken cancellationToken)
        {
            RunBackgroundTask(module, _ =>
            {
                action();
                return Task.CompletedTask;
            }, cancellationToken);
        }

        private static void RunBackgroundTask(string module, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    await action(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    LoggingService.LogInfo(module, "Cancelled.");
                }
                catch (Exception ex)
                {
                    LoggingService.LogError(module, ex);
                }
            });
        }

        private static void PrewarmSalaryPath()
        {
            try
            {
                var sw = Stopwatch.StartNew();

                if (GetRequiredService<AppDataStorageFactory>().IsPostgresRuntimeActiveAtStartup)
                {
                    LoggingService.LogInfo("App.SalaryPrewarm", "Skipped because PostgreSQL runtime is active.");
                    return;
                }

                var monthDbs = SalaryDbService.EnumerateMonthDatabases()
                    .OrderByDescending(monthDb => monthDb.year)
                    .ThenByDescending(monthDb => monthDb.month)
                    .Take(12)
                    .ToList();

                foreach (var monthDb in monthDbs)
                {
                    using var connection = SalaryDbService.OpenMonthConnection(monthDb.year, monthDb.month);
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM salary_entries;";
                    command.ExecuteScalar();
                }

                LoggingService.LogInfo("App.SalaryPrewarm", $"Completed in {sw.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("App.SalaryPrewarm", ex.Message);
            }
        }

        private static async Task StartHeartbeatLoopAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var heartbeat = await TelemetryService.SendHeartbeatAsync().ConfigureAwait(false);
                        if (_heartbeatFailureActive)
                        {
                            LoggingService.LogInfo("App.HeartbeatLoop", "Heartbeat connection restored.");
                            _heartbeatFailureActive = false;
                        }
                        if (heartbeat.AccessState.HasKnownState || heartbeat.Policy != null)
                        {
                            var effectivePolicy = BuildEffectivePolicy(LicenseService.GetLocalLicenseStatus(), heartbeat.AccessState, heartbeat.Policy);
                            await PolicyService.ApplyPolicyAsync(effectivePolicy).ConfigureAwait(false);
                            _currentGeminiAccessState = heartbeat.AccessState;
                            ApplyEffectiveGeminiApiKey(heartbeat.AccessState, effectivePolicy);
                            AccessStatusService.UpdateRemoteState(heartbeat.AccessState, effectivePolicy);
                            if (!await EnforceVersionPolicyAsync(effectivePolicy).ConfigureAwait(false))
                                return;
                        }
                        else
                        {
                            ApplyEffectiveGeminiApiKey(new ClientAccessState(), PolicyService.CurrentPolicy);
                        }
                        if (heartbeat.PendingCommands.Count > 0)
                            await CommandService.ExecutePendingCommandsAsync(heartbeat.PendingCommands, heartbeat.ClientId).ConfigureAwait(false);

                        _ = WorkspaceSessionService.ReportSessionAsync(heartbeat.ClientId);
                    }
                    catch (Exception ex)
                    {
                        ApplyEffectiveGeminiApiKey(new ClientAccessState(), PolicyService.CurrentPolicy);
                        if (!_heartbeatFailureActive)
                        {
                            LoggingService.LogWarning("App.HeartbeatLoop", $"Heartbeat sync failed: {ex.Message}");
                            _heartbeatFailureActive = true;
                        }
                        else
                        {
                            LoggingService.LogInfo("App.HeartbeatLoop", $"Heartbeat still unavailable: {ex.Message}");
                        }
                    }

                    await Task.Delay(TimeSpan.FromMinutes(3), ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                LoggingService.LogInfo("App.OnExit", $"Application exit started. ExitCode={e.ApplicationExitCode}.");
                _backgroundTasksCts?.Cancel();
                try { AppStatisticsService.StopSession(); }
                catch (Exception ex) { LoggingService.LogWarning("App.OnExit.AppStatisticsService", ex.Message); }

                try { AdminMirrorSyncService.Stop(); }
                catch (Exception ex) { LoggingService.LogWarning("App.OnExit.AdminMirrorSyncService", ex.Message); }

                try { TelegramBotService.Stop(); }
                catch (Exception ex) { LoggingService.LogWarning("App.OnExit.TelegramBotService", ex.Message); }

                try { SyncEventService.Stop(); }
                catch (Exception ex) { LoggingService.LogWarning("App.OnExit.SyncEventService", ex.Message); }

                try { ConnectedClientsService.Stop(); }
                catch (Exception ex) { LoggingService.LogWarning("App.OnExit.ConnectedClientsService", ex.Message); }

                try
                {
                    WorkspaceSessionService.EndSessionAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex) { LoggingService.LogWarning("App.OnExit.WorkspaceSessionService", ex.Message); }

                try
                {
                    using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    WebPanelHostService.StopAsync(shutdownCts.Token).GetAwaiter().GetResult();
                }
                catch (Exception ex) { LoggingService.LogWarning("App.OnExit.WebPanelHostService", ex.Message); }

                try { _serviceProvider?.Dispose(); }
                catch (Exception ex) { LoggingService.LogWarning("App.OnExit.ServiceProvider", ex.Message); }
            }
            finally
            {
                _backgroundTasksCts?.Dispose();
                _backgroundTasksCts = null;
                _serviceProvider = null;
                base.OnExit(e);

                // Some hosted/web/polling libraries can keep background threads alive after WPF closes.
                // The user-visible app is already shut down here, so force the process to end.
                Environment.Exit(e.ApplicationExitCode);
            }
        }

        private static void OnAnyWindowLoaded_FadeIn(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window) return;

            // The first real window (a startup gate dialog or the main window) dismisses the splash.
            if (_splashWindow != null && !ReferenceEquals(window, _splashWindow))
                CloseSplashWindow();

            if (window.AllowsTransparency) return; // layered windows animate poorly
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            window.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private static void ShowSplashWindow()
        {
            try
            {
                _splashWindow = new Views.SplashWindow();
                _splashWindow.Show();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("App.Splash", $"Could not show splash window: {ex.Message}");
                _splashWindow = null;
            }
        }

        private static void CloseSplashWindow()
        {
            var splash = _splashWindow;
            if (splash == null) return;
            _splashWindow = null;
            try { splash.FadeOutAndClose(); }
            catch { /* window may already be closing */ }
        }

        private static RemotePolicy? BuildEffectivePolicy(LocalLicenseStatus localLicenseStatus, ClientAccessState accessState, RemotePolicy? policy)
        {
            var effective = ClonePolicy(policy) ?? (accessState.IsOfflineGraceActive ? BuildPolicyFromSettings() : null);
            if (accessState.HasKnownState && accessState.IsExpired)
            {
                effective ??= new RemotePolicy
                {
                    ClientId = accessState.ClientId ?? string.Empty
                };

                effective.ReadOnlyMode = true;
                effective.DisableAI = true;
                effective.DisableExports = true;
                effective.HideTemplates = true;
                effective.HideFinance = true;

                if (string.IsNullOrWhiteSpace(effective.AdminMessage))
                {
                    effective.AdminMessage = localLicenseStatus.IsValid
                        ? Res("AccessStatusAdminRenewRequired", "Server access needs to be renewed in AdminPanel. The local license no longer restores full access on its own.")
                        : Res("AccessStatusTrialEndedAdminPanel", "The trial period has ended. Activate this client in AdminPanel to restore full access.");
                }
            }

            if (accessState.HasKnownState && !accessState.IsBlocked && !accessState.IsExpired)
            {
                effective ??= new RemotePolicy
                {
                    ClientId = accessState.ClientId ?? string.Empty
                };

                switch (NormalizeAccessPlan(accessState.Plan))
                {
                    case AccessPlanStandard:
                        effective.DisableAI = true;
                        break;
                    case AccessPlanPro:
                    case AccessPlanTrial:
                        effective.DisableAI = false;
                        break;
                }
            }

            return effective;
        }

        internal static void RefreshGeminiApiKeyConfiguration()
        {
            ApplyEffectiveGeminiApiKey(_currentGeminiAccessState, PolicyService.CurrentPolicy);
        }

        private static void ApplyEffectiveGeminiApiKey(ClientAccessState accessState, RemotePolicy? policy)
        {
            // During shutdown the DI container is disposed and nulled; a late heartbeat
            // callback must not touch App.Services (would throw "Service provider is not initialized").
            if (_serviceProvider == null)
                return;

            if (policy?.DisableAI == true || PolicyService.IsAIDisabled)
            {
                GeminiApiService.SetApiKey(null);
                return;
            }

            var userKey = AppSettingsService.Settings.GeminiApiKey;
            var serverKey = accessState.IsLive ? accessState.ManagedGeminiApiKey : string.Empty;
            var effectiveKey = !string.IsNullOrWhiteSpace(userKey) ? userKey : serverKey;
            GeminiApiService.SetApiKey(!string.IsNullOrWhiteSpace(effectiveKey) ? effectiveKey : null);
        }

        private static RemotePolicy? BuildPolicyFromSettings()
        {
            var settings = AppSettingsService?.Settings;
            if (settings == null)
                return null;

            if (!settings.AdminReadOnlyMode
                && !settings.AdminDisableAI
                && !settings.AdminDisableExports
                && !settings.AdminMaintenanceMode
                && !settings.AdminHideTemplates
                && !settings.AdminHideFinance
                && !settings.AdminForceUpdate
                && string.IsNullOrWhiteSpace(settings.AdminMessage)
                && string.IsNullOrWhiteSpace(settings.AdminMinimumSupportedVersion)
                && string.IsNullOrWhiteSpace(settings.AdminRecommendedVersion)
                && string.IsNullOrWhiteSpace(settings.RemotePolicyVersion))
            {
                return null;
            }

            return new RemotePolicy
            {
                ClientId = settings.CachedAccessClientId,
                MinimumSupportedVersion = settings.AdminMinimumSupportedVersion,
                RecommendedVersion = settings.AdminRecommendedVersion,
                UpdateChannel = settings.AdminUpdateChannel,
                ForceUpdate = settings.AdminForceUpdate,
                MaintenanceMode = settings.AdminMaintenanceMode,
                ReadOnlyMode = settings.AdminReadOnlyMode,
                DisableAI = settings.AdminDisableAI,
                DisableExports = settings.AdminDisableExports,
                HideTemplates = settings.AdminHideTemplates,
                HideFinance = settings.AdminHideFinance,
                AdminMessage = settings.AdminMessage,
                PolicyVersion = settings.RemotePolicyVersion
            };
        }

        private static RemotePolicy? ClonePolicy(RemotePolicy? policy)
        {
            if (policy == null)
                return null;

            return new RemotePolicy
            {
                ClientId = policy.ClientId,
                MinimumSupportedVersion = policy.MinimumSupportedVersion,
                RecommendedVersion = policy.RecommendedVersion,
                UpdateChannel = policy.UpdateChannel,
                ForceUpdate = policy.ForceUpdate,
                MaintenanceMode = policy.MaintenanceMode,
                ReadOnlyMode = policy.ReadOnlyMode,
                DisableAI = policy.DisableAI,
                DisableExports = policy.DisableExports,
                HideTemplates = policy.HideTemplates,
                HideFinance = policy.HideFinance,
                RequireOnlineCheck = policy.RequireOnlineCheck,
                AdminMessage = policy.AdminMessage,
                PolicyVersion = policy.PolicyVersion,
                UpdatedAt = policy.UpdatedAt
            };
        }

        private static string NormalizeAccessPlan(string? plan)
        {
            return (plan ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                AccessPlanStandard => AccessPlanStandard,
                AccessPlanPro => AccessPlanPro,
                _ => AccessPlanTrial
            };
        }

        private static bool LocalLicenseIsStronger(LocalLicenseStatus local, ClientAccessState server)
        {
            if (server.IsBlocked)
                return false;

            if (local.IsUnlimited && !server.ExpiresAtUtc.HasValue)
                return true;

            if (!server.ExpiresAtUtc.HasValue)
                return true;

            if (local.IsUnlimited && server.ExpiresAtUtc.Value < new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                return true;

            if (local.ExpiresAtUtc.HasValue && local.ExpiresAtUtc.Value > server.ExpiresAtUtc.Value)
                return true;

            return false;
        }

        private static async Task<bool> EnforceVersionPolicyAsync(RemotePolicy? policy)
        {
            if (policy == null)
                return true;

            var currentVersion = AppSettingsService.CurrentAppVersion;
            var requiresMinimumVersion = PolicyService.IsCurrentVersionBelowMinimum(currentVersion);
            var requiresForcedUpdate = policy.ForceUpdate
                && !string.IsNullOrWhiteSpace(policy.RecommendedVersion)
                && PolicyService.CompareVersions(currentVersion, policy.RecommendedVersion) < 0;

            if (requiresMinimumVersion || requiresForcedUpdate)
            {
                if (Interlocked.Exchange(ref _versionPolicyEnforcementActive, 1) == 1)
                    return false;

                var dispatcher = Current?.Dispatcher;
                if (dispatcher == null)
                    return false;

                await dispatcher.InvokeAsync(() =>
                {
                    var requiredVersion = requiresMinimumVersion
                        ? policy.MinimumSupportedVersion
                        : policy.RecommendedVersion;
                    var reason = requiresMinimumVersion
                        ? "Ця версія програми більше не підтримується сервером."
                        : "Адміністратор вимагає оновити програму перед подальшою роботою.";
                    var adminMessage = string.IsNullOrWhiteSpace(policy.AdminMessage)
                        ? string.Empty
                        : $"\n\n{policy.AdminMessage.Trim()}";

                    MessageBox.Show(
                        $"{reason}\n\nПоточна версія: {currentVersion}\nПотрібна версія: {requiredVersion}{adminMessage}\n\nПрограма буде закрита. Після оновлення її можна відкрити знову.",
                        "Agency Contractor",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    Current?.Shutdown();
                });

                return false;
            }

            if (!string.IsNullOrWhiteSpace(policy.RecommendedVersion)
                && PolicyService.CompareVersions(currentVersion, policy.RecommendedVersion) < 0
                && !string.Equals(_recommendedVersionPromptedFor, policy.RecommendedVersion, StringComparison.OrdinalIgnoreCase))
            {
                _recommendedVersionPromptedFor = policy.RecommendedVersion;
                var dispatcher = Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        ToastService.Instance.Warning(
                            $"Адміністратор рекомендує оновити програму до версії {policy.RecommendedVersion}. Поточна версія: {currentVersion}.");
                    });
                }
            }

            return true;
        }

        private static string Res(string key, string fallback)
        {
            return Current?.TryFindResource(key) as string ?? fallback;
        }
    }
}
