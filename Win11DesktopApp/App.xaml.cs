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
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
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
        private static ProfileAuthService ProfileAuthService => GetRequiredService<ProfileAuthService>();
        private static ProfileSessionService ProfileSessionService => GetRequiredService<ProfileSessionService>();
        private static AccessStatusService AccessStatusService => GetRequiredService<AccessStatusService>();
        private static CurrentProfileService CurrentProfileService => GetRequiredService<CurrentProfileService>();
        private static TelegramBotService TelegramBotService => GetRequiredService<TelegramBotService>();
        private static AppUpdateNotificationService AppUpdateNotificationService => GetRequiredService<AppUpdateNotificationService>();

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
            RunStartupMigrations();
            AppStatisticsService.StartSession();
            LoggingService.LogInfo("App", "All services initialized");
            LogStartupPhase("services_initialized");

            var startupState = CreateStartupFlowState();
            await ResolveStartupAccessAsync(startupState, LogStartupPhase);
            if (!await RunProfileGateAsync(startupState, LogStartupPhase))
                return;
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
            var activityLogMigration = ActivityLogService.EnsureMigratedToLocalDb();
            if (activityLogMigration.WasMigrationAttempted)
            {
                if (activityLogMigration.IsSuccessful)
                {
                    var successMessage = string.Format(
                        Res("MsgActivityLogMigrationSuccess", "Activity log was migrated to SQLite. Imported records: {0}. Backup: {1}"),
                        activityLogMigration.RecordsImported,
                        string.IsNullOrWhiteSpace(activityLogMigration.BackupPath)
                            ? Res("MsgActivityLogMigrationNoBackup", "not created")
                            : activityLogMigration.BackupPath);
                    ToastService.Instance.Warning(successMessage);
                    LoggingService.LogInfo("App.ActivityLogMigration", successMessage);
                }
                else
                {
                    var failedMessage = string.Format(
                        Res("MsgActivityLogMigrationFailed", "Activity log migration to SQLite failed. The program will keep using the previous source. Details: {0}"),
                        activityLogMigration.Message);
                    MessageBox.Show(
                        failedMessage,
                        Res("TitleWarning", "Warning"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    LoggingService.LogWarning("App.ActivityLogMigration", failedMessage);
                }
            }

            var employeeHistoryMigration = EmployeeService.EnsureEmployeeHistoryMigratedToLocalDb();
            if (employeeHistoryMigration.WasMigrationAttempted)
            {
                if (employeeHistoryMigration.IsSuccessful)
                {
                    var successMessage = string.Format(
                        Res("MsgEmployeeHistoryMigrationSuccess", "Employee history was migrated to SQLite. Imported records: {0}. Folders scanned: {1}. Skipped: {2}"),
                        employeeHistoryMigration.RecordsImported,
                        employeeHistoryMigration.FoldersScanned,
                        employeeHistoryMigration.FoldersSkipped);
                    ToastService.Instance.Warning(successMessage);
                    LoggingService.LogInfo("App.EmployeeHistoryMigration", successMessage);
                }
                else
                {
                    var failedMessage = string.Format(
                        Res("MsgEmployeeHistoryMigrationFailed", "Employee history migration to SQLite failed. The program will keep using the previous source. Details: {0}"),
                        employeeHistoryMigration.Message);
                    MessageBox.Show(
                        failedMessage,
                        Res("TitleWarning", "Warning"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    LoggingService.LogWarning("App.EmployeeHistoryMigration", failedMessage);
                }
            }

            var archiveLogMigration = EmployeeService.EnsureArchiveLogMigratedToLocalDb();
            if (archiveLogMigration.WasMigrationAttempted)
            {
                if (archiveLogMigration.IsSuccessful)
                {
                    var successMessage = string.Format(
                        Res("MsgArchiveLogMigrationSuccess", "Archive log was migrated to SQLite. Imported records: {0}. Backup: {1}"),
                        archiveLogMigration.RecordsImported,
                        string.IsNullOrWhiteSpace(archiveLogMigration.BackupPath)
                            ? Res("MsgArchiveLogMigrationNoBackup", "not created")
                            : archiveLogMigration.BackupPath);
                    ToastService.Instance.Warning(successMessage);
                    LoggingService.LogInfo("App.ArchiveLogMigration", successMessage);
                }
                else
                {
                    var failedMessage = string.Format(
                        Res("MsgArchiveLogMigrationFailed", "Archive log migration to SQLite failed. The program will keep using the previous source. Details: {0}"),
                        archiveLogMigration.Message);
                    MessageBox.Show(
                        failedMessage,
                        Res("TitleWarning", "Warning"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    LoggingService.LogWarning("App.ArchiveLogMigration", failedMessage);
                }
            }

            var employeeIndexRebuild = EmployeeService.EnsureEmployeeIndexBuilt();
            if (employeeIndexRebuild.WasRebuildAttempted)
            {
                if (employeeIndexRebuild.IsSuccessful)
                {
                    var successMessage = string.Format(
                        Res("MsgEmployeeIndexBuildSuccess", "Employee index was built in SQLite. Imported records: {0}. Folders scanned: {1}. Skipped: {2}"),
                        employeeIndexRebuild.RecordsImported,
                        employeeIndexRebuild.FoldersScanned,
                        employeeIndexRebuild.FoldersSkipped);
                    ToastService.Instance.Warning(successMessage);
                    LoggingService.LogInfo("App.EmployeeIndexBuild", successMessage);
                }
                else
                {
                    var failedMessage = string.Format(
                        Res("MsgEmployeeIndexBuildFailed", "Employee index build in SQLite failed. The program will keep using the current source. Details: {0}"),
                        employeeIndexRebuild.Message);
                    MessageBox.Show(
                        failedMessage,
                        Res("TitleWarning", "Warning"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    LoggingService.LogWarning("App.EmployeeIndexBuild", failedMessage);
                }
            }

            var salaryMigration = FinanceService.EnsureSalaryMigratedToLocalDb();
            if (salaryMigration.WasMigrationAttempted)
            {
                if (salaryMigration.IsSuccessful)
                {
                    var successMessage = string.Format(
                        Res("MsgSalaryMigrationSuccess", "Salary month databases were built in SQLite. Files: {0}. Entries: {1}. Expenses: {2}."),
                        salaryMigration.FilesScanned,
                        salaryMigration.RecordsImported,
                        salaryMigration.ExpensesImported);
                    ToastService.Instance.Warning(successMessage);
                    LoggingService.LogInfo("App.SalaryMigration", successMessage);
                }
                else
                {
                    var failedMessage = string.Format(
                        Res("MsgSalaryMigrationFailed", "Salary migration to SQLite failed. The program will keep using JSON. Details: {0}"),
                        salaryMigration.Message);
                    MessageBox.Show(
                        failedMessage,
                        Res("TitleWarning", "Warning"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    LoggingService.LogWarning("App.SalaryMigration", failedMessage);
                }
            }

            var salaryHistoryMigration = FinanceService.EnsureSalaryHistoryMigratedToLocalDb();
            if (salaryHistoryMigration.WasMigrationAttempted)
            {
                if (salaryHistoryMigration.IsSuccessful)
                {
                    var successMessage = $"Salary history was migrated to SQLite. Folders: {salaryHistoryMigration.FoldersScanned}. Records: {salaryHistoryMigration.RecordsImported}.";
                    LoggingService.LogInfo("App.SalaryHistoryMigration", successMessage);
                }
                else
                {
                    var failedMessage = $"Salary history migration to SQLite failed. The program will keep using JSON fallback. Details: {salaryHistoryMigration.Message}";
                    LoggingService.LogWarning("App.SalaryHistoryMigration", failedMessage);
                }
            }

            var customFieldsMigration = FinanceService.EnsureCustomFieldsMigratedToLocalDb();
            if (customFieldsMigration.WasMigrationAttempted)
            {
                if (customFieldsMigration.IsSuccessful)
                {
                    LoggingService.LogInfo("App.CustomFieldsMigration", customFieldsMigration.Message);
                }
                else
                {
                    var failedMessage = $"Custom fields migration to SQLite failed. Details: {customFieldsMigration.Message}";
                    LoggingService.LogWarning("App.CustomFieldsMigration", failedMessage);
                }
            }

            var advancesMigration = FinanceService.EnsureAdvancesMigratedToLocalDb();
            if (advancesMigration.WasMigrationAttempted)
            {
                if (advancesMigration.IsSuccessful)
                {
                    LoggingService.LogInfo("App.AdvancesMigration", advancesMigration.Message);
                }
                else
                {
                    var failedMessage = $"Advances migration to SQLite failed. Details: {advancesMigration.Message}";
                    LoggingService.LogWarning("App.AdvancesMigration", failedMessage);
                }
            }

            var reportsMigration = FinanceService.EnsureReportsMigratedToLocalDb();
            if (reportsMigration.WasMigrationAttempted)
            {
                if (reportsMigration.IsSuccessful)
                {
                    LoggingService.LogInfo("App.ReportsMigration", reportsMigration.Message);
                }
                else
                {
                    var failedMessage = $"Reports migration to SQLite failed. Details: {reportsMigration.Message}";
                    LoggingService.LogWarning("App.ReportsMigration", failedMessage);
                }
            }

            var accommodationsMigration = FinanceService.EnsureAccommodationsMigratedToLocalDb();
            if (accommodationsMigration.WasMigrationAttempted)
            {
                if (accommodationsMigration.IsSuccessful)
                {
                    LoggingService.LogInfo("App.AccommodationsMigration", accommodationsMigration.Message);
                }
                else
                {
                    var failedMessage = $"Accommodations migration to SQLite failed. Details: {accommodationsMigration.Message}";
                    LoggingService.LogWarning("App.AccommodationsMigration", failedMessage);
                }
            }

            if (FinanceService.CloseMigratedFinanceDataIfSafe())
                LoggingService.LogInfo("App.FinanceDataMigration", "Renamed finance_data.json to finance_data.json.migrated after confirmed SQLite migration.");

            try
            {
                var deletedActivityLogBackup = LocalDbService.CleanupMigratedActivityLogBackup();
                if (deletedActivityLogBackup)
                    LoggingService.LogInfo("App.MigratedBackupCleanup", "Deleted activity_log.json.migrated after confirmed SQLite migration.");

                var deletedArchiveLogBackup = LocalDbService.CleanupMigratedArchiveLogBackup();
                if (deletedArchiveLogBackup)
                    LoggingService.LogInfo("App.MigratedBackupCleanup", "Deleted archive_log.json.migrated after confirmed SQLite migration.");

                var deletedEmployeeHistoryBackups = EmployeeService.CleanupMigratedEmployeeHistoryBackups();
                if (deletedEmployeeHistoryBackups > 0)
                {
                    LoggingService.LogInfo("App.MigratedBackupCleanup",
                        $"Deleted history.json.migrated backups after confirmed SQLite migration. Files: {deletedEmployeeHistoryBackups}.");
                }

                var deletedSalaryHistoryBackups = FinanceService.CleanupMigratedSalaryHistoryBackups();
                if (deletedSalaryHistoryBackups > 0)
                {
                    LoggingService.LogInfo("App.MigratedBackupCleanup",
                        $"Deleted salary_history.json.migrated backups after confirmed SQLite migration. Files: {deletedSalaryHistoryBackups}.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("App.MigratedBackupCleanup", ex.Message);
            }

            RecentlyDeletedService.EnsureStorage();
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

            RunBackgroundTask("StartupIntegrityService.BackgroundCheck", () => startupIntegrityService.RunBackgroundCheck(CompanyService.Companies), BackgroundTaskToken);
            RunBackgroundTask("App.UpdateNotificationCheck", AppUpdateNotificationService.CheckForAvailableUpdateAsync, BackgroundTaskToken);
            RunBackgroundTask("RecentlyDeletedService.PurgeExpired", () => RecentlyDeletedService.PurgeExpired(), BackgroundTaskToken);
            RunBackgroundTask("App.SalaryPrewarm", PrewarmSalaryPath, BackgroundTaskToken);
            RunBackgroundTask("App.TelegramBotStartup", async _ =>
            {
                if (AppSettingsService.Settings.Telegram.Enabled
                    && !string.IsNullOrWhiteSpace(AppSettingsService.Settings.Telegram.EncryptedBotToken))
                {
                    await TelegramBotService.RestartAsync().ConfigureAwait(false);
                }
            }, BackgroundTaskToken);
        }

        private static StartupIntegrityService InitializeCoreServices()
        {
            _serviceProvider = BuildServiceProvider();

            // Resolve the minimum set first so logging is configured before other services can emit startup diagnostics.
            if (!string.IsNullOrEmpty(FolderService.RootPath))
                LoggingService.Initialize(FolderService.RootPath);

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
            ConfigureServices(services);
            return services.BuildServiceProvider();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(sp => new NavigationService(sp));
            services.AddSingleton<ThemeService>();
            services.AddSingleton<LanguageService>();
            services.AddSingleton(_ => new AppSettingsService(suppressStartupNotifications: true));
            services.AddSingleton<AccessStatusService>();
            services.AddSingleton(sp => new FolderService(sp.GetRequiredService<AppSettingsService>()));
            services.AddSingleton(sp => new CoreDbService(sp.GetRequiredService<FolderService>()));
            services.AddSingleton(sp => new PersistenceService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<CoreDbService>()));
            services.AddSingleton(sp => new StartupIntegrityService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<PersistenceService>()));
            services.AddSingleton<TagCatalogService>();
            services.AddSingleton(sp => new CompanyService(
                sp.GetRequiredService<TagCatalogService>(),
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<PersistenceService>(),
                sp.GetRequiredService<FolderService>()));
            services.AddSingleton(sp => new TemplateService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<TagCatalogService>()));
            services.AddSingleton<StarterTemplateCatalogService>();
            services.AddSingleton(sp => new LocalDbService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<SalaryDbService>()));
            services.AddSingleton(sp => new EmployeeIndexDbService(sp.GetRequiredService<FolderService>()));
            services.AddSingleton(sp => new SalaryDbService(sp.GetRequiredService<FolderService>()));
            services.AddSingleton(sp => new AdminMirrorSyncService(
                sp.GetRequiredService<CompanyService>()));
            services.AddSingleton(sp => new EmployeeService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<TagCatalogService>(),
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<EmployeeIndexDbService>(),
                sp.GetRequiredService<CurrentProfileService>(),
                sp.GetRequiredService<AdminMirrorSyncService>(),
                companyService: sp.GetRequiredService<CompanyService>()));
            services.AddSingleton(sp => new RecentlyDeletedService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<EmployeeService>(),
                sp.GetRequiredService<CurrentProfileService>(),
                sp.GetRequiredService<FinanceService>(),
                sp.GetRequiredService<ActivityLogService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<EmployeeIndexDbService>()));
            services.AddSingleton(sp => new FinanceService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<SalaryDbService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<CompanyService>(),
                sp.GetRequiredService<EmployeeIndexDbService>(),
                suppressStartupNotifications: true));
            services.AddSingleton(sp => new InvoiceStorageService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<AppSettingsService>()));
            services.AddSingleton(sp => new AresLookupService(sp.GetRequiredService<InvoiceStorageService>()));
            services.AddSingleton<InvoiceQrPaymentService>();
            services.AddSingleton(sp => new InvoicePdfRenderService(
                sp.GetRequiredService<InvoiceStorageService>(),
                sp.GetRequiredService<InvoiceQrPaymentService>()));
            services.AddSingleton(sp => new ActivityLogService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<CurrentProfileService>()));
            services.AddSingleton<AppNotificationService>();
            services.AddSingleton<AppUpdateNotificationService>();
            services.AddSingleton(sp => new AppStatisticsService(sp.GetRequiredService<FolderService>()));
            services.AddSingleton(sp => new CandidateService(sp.GetRequiredService<FolderService>()));
            services.AddSingleton<ChatPersistenceService>();
            services.AddSingleton<GeminiApiService>();
            services.AddSingleton<NewsService>();
            services.AddSingleton<EmployeeDetailsViewModelFactory>();
            services.AddSingleton<AddEmployeeWizardViewModelFactory>();
            services.AddSingleton<AddCompanyViewModelFactory>();
            services.AddSingleton<CandidateViewModelFactory>();
            services.AddSingleton<TemplateViewModelFactory>();
            services.AddSingleton<InvoiceViewModelFactory>();
            services.AddSingleton<MainModuleViewModelFactory>();
            services.AddSingleton<FinanceModuleViewModelFactory>();
            services.AddSingleton<AiWindowFactory>();
            services.AddSingleton<ProfileDialogFactory>();
            services.AddSingleton<ProfileAuthService>();
            services.AddSingleton(sp => new ProfileSessionService(sp.GetRequiredService<AppSettingsService>()));
            services.AddSingleton<DocumentGenerationService>();
            services.AddSingleton<DocumentLocalizationService>();
            services.AddSingleton<ReportColumnLayoutService>();
            services.AddSingleton<CurrentProfileService>();
            services.AddSingleton<GeminiApiKeyConfigurationService>();
            services.AddSingleton<TelegramPairingService>();
            services.AddSingleton<TelegramBotService>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<ReportViewModel>();
            services.AddTransient<ActivityLogViewModel>();
            services.AddTransient<FinanceTablesViewModel>();
            services.AddTransient<CandidatesViewModel>();
            services.AddTransient<NewsViewModel>();
        }

        private static void InitializeStartupServices()
        {
            LicenseService.Initialize(AppSettingsService);
            PolicyService.Initialize(AppSettingsService);
            TelemetryService.Initialize(AppSettingsService, CompanyService);
            CompanyService.InitializeAdminMirrorSyncService(AdminMirrorSyncService);
            AdminMirrorSyncService.InitializeEmployeeService(EmployeeService);
            EmployeeService.InitializeFinanceService(FinanceService);
            CommandService.Initialize(AccessStatusService);
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

                LocalDbService.IsSalaryHistoryMigrationCompleted();

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
            _backgroundTasksCts?.Cancel();
            _backgroundTasksCts?.Dispose();
            _backgroundTasksCts = null;
            try { AppStatisticsService.StopSession(); } catch { }
            try { AdminMirrorSyncService.Stop(); } catch { }
            try { TelegramBotService.Stop(); } catch { }
            base.OnExit(e);
        }

        private static void OnAnyWindowLoaded_FadeIn(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window) return;
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
