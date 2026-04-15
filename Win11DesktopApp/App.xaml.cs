using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using PdfSharp.Fonts;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp
{
    public partial class App : Application
    {
        private static CancellationTokenSource? _heartbeatCts;
        private static bool _heartbeatFailureActive;
        private static ClientAccessState _currentGeminiAccessState = new();
        private static string? _recommendedVersionPromptedFor;
        private static int _versionPolicyEnforcementActive;
        private const string AccessPlanTrial = "trial";
        private const string AccessPlanStandard = "standard";
        private const string AccessPlanPro = "pro";
        public static NavigationService NavigationService { get; private set; } = null!;
        public static ThemeService ThemeService { get; private set; } = null!;
        public static LanguageService LanguageService { get; private set; } = null!;
        public static AppSettingsService AppSettingsService { get; private set; } = null!;
        public static FolderService FolderService { get; private set; } = null!;
        public static TagCatalogService TagCatalogService { get; private set; } = null!;
        public static CompanyService CompanyService { get; private set; } = null!;
        public static TemplateService TemplateService { get; private set; } = null!;
        public static StarterTemplateCatalogService StarterTemplateCatalogService { get; private set; } = null!;
        public static PersistenceService PersistenceService { get; private set; } = null!;
        public static EmployeeService EmployeeService { get; private set; } = null!;
        public static AdminMirrorSyncService AdminMirrorSyncService { get; private set; } = null!;
        public static RecentlyDeletedService RecentlyDeletedService { get; private set; } = null!;
        public static DocumentGenerationService DocumentGenerationService { get; private set; } = null!;
        public static DocumentLocalizationService DocumentLocalizationService { get; private set; } = null!;
        public static InvoiceStorageService InvoiceStorageService { get; private set; } = null!;
        public static AresLookupService AresLookupService { get; private set; } = null!;
        public static InvoiceQrPaymentService InvoiceQrPaymentService { get; private set; } = null!;
        public static InvoicePdfRenderService InvoicePdfRenderService { get; private set; } = null!;

        public static FinanceService FinanceService { get; private set; } = null!;
        public static LocalDbService LocalDbService { get; private set; } = null!;
        public static EmployeeIndexDbService EmployeeIndexDbService { get; private set; } = null!;
        public static SalaryDbService SalaryDbService { get; private set; } = null!;
        public static ActivityLogService ActivityLogService { get; private set; } = null!;
        public static CandidateService CandidateService { get; private set; } = null!;
        public static GeminiApiService GeminiApiService { get; private set; } = null!;
        public static NewsService NewsService { get; private set; } = null!;
        public static ProfileAuthService ProfileAuthService { get; private set; } = null!;
        public static ProfileSessionService ProfileSessionService { get; private set; } = null!;
        public static AccessStatusService AccessStatusService { get; private set; } = null!;
        public static ClientProfileRecord? CurrentProfile { get; private set; }

        public static void SetCurrentProfile(ClientProfileRecord? profile)
        {
            CurrentProfile = profile;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            GlobalFontSettings.FontResolver = new PdfFontResolver();

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

            NavigationService = new NavigationService();
            ThemeService = new ThemeService();
            LanguageService = new LanguageService();
            AppSettingsService = new AppSettingsService(suppressStartupNotifications: true);
            AccessStatusService = new AccessStatusService();
            FolderService = new FolderService(AppSettingsService);

            if (!string.IsNullOrEmpty(FolderService.RootPath))
                LoggingService.Initialize(FolderService.RootPath);

            LoggingService.LogInfo("App", $"Application started v{AppSettingsService.CurrentAppVersion}");
            var startupStopwatch = Stopwatch.StartNew();
            void LogStartupPhase(string phase) =>
                LoggingService.LogInfo("App.Startup", $"{phase} at {startupStopwatch.ElapsedMilliseconds} ms");
            LogStartupPhase("startup_begin");

            PersistenceService = new PersistenceService(AppSettingsService, FolderService);
            var startupIntegrityService = new StartupIntegrityService(FolderService, PersistenceService);
            startupIntegrityService.IncludeSettingsStartupState(AppSettingsService);
            startupIntegrityService.RunQuickCheck();
            TagCatalogService = new TagCatalogService();
            CompanyService = new CompanyService(TagCatalogService, AppSettingsService, PersistenceService, FolderService);
            TemplateService = new TemplateService(AppSettingsService, FolderService, TagCatalogService);
            StarterTemplateCatalogService = new StarterTemplateCatalogService();
            LocalDbService = new LocalDbService(FolderService);
            EmployeeIndexDbService = new EmployeeIndexDbService(FolderService);
            SalaryDbService = new SalaryDbService(FolderService);
            EmployeeService = new EmployeeService(AppSettingsService, TagCatalogService, FolderService, LocalDbService, EmployeeIndexDbService);
            AdminMirrorSyncService = new AdminMirrorSyncService();
            RecentlyDeletedService = new RecentlyDeletedService(FolderService, EmployeeService);
            FinanceService = new FinanceService(FolderService, SalaryDbService, LocalDbService, suppressStartupNotifications: true);
            InvoiceStorageService = new InvoiceStorageService(FolderService);
            AresLookupService = new AresLookupService(InvoiceStorageService);
            InvoiceQrPaymentService = new InvoiceQrPaymentService();
            InvoicePdfRenderService = new InvoicePdfRenderService(InvoiceStorageService, InvoiceQrPaymentService);
            startupIntegrityService.IncludeFinanceStartupState(FinanceService);
            ActivityLogService = new ActivityLogService(FolderService, LocalDbService);
            CandidateService = new CandidateService(FolderService);
            GeminiApiService = new GeminiApiService();
            NewsService = new NewsService();
            ProfileAuthService = new ProfileAuthService();
            ProfileSessionService = new ProfileSessionService(AppSettingsService);
            if (!string.IsNullOrEmpty(AppSettingsService.Settings.GeminiModel))
                GeminiApiService.SetModel(AppSettingsService.Settings.GeminiModel);
            DocumentGenerationService = new DocumentGenerationService();
            DocumentLocalizationService = new DocumentLocalizationService();
            _ = Task.Run(async () =>
            {
                try
                {
                    await PendingCleanupService.ProcessPendingCleanupAsync(EmployeeService.TryCleanupDeferredDirectory);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("App.PendingCleanupStartup", ex.Message);
                }
            });

            _ = Task.Run(() =>
            {
                try
                {
                    NetPdfFormHelper.WarmUp();
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("App.NetPdfWarmUp", ex.Message);
                }
            });

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

            try
            {
                if (LocalDbService != null)
                {
                    var deletedActivityLogBackup = LocalDbService.CleanupMigratedActivityLogBackup();
                    if (deletedActivityLogBackup)
                        LoggingService.LogInfo("App.MigratedBackupCleanup", "Deleted activity_log.json.migrated after confirmed SQLite migration.");

                    var deletedArchiveLogBackup = LocalDbService.CleanupMigratedArchiveLogBackup();
                    if (deletedArchiveLogBackup)
                        LoggingService.LogInfo("App.MigratedBackupCleanup", "Deleted archive_log.json.migrated after confirmed SQLite migration.");
                }

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

            LoggingService.LogInfo("App", "All services initialized");
            LogStartupPhase("services_initialized");

            var skipLicenseGate =
#if DEBUG
                true;
#else
                Debugger.IsAttached;
#endif

            var localLicenseStatus = LicenseService.GetLocalLicenseStatus();
            var startupClientTask = TelemetryService.GetStartupAccessStateAsync();
            var startupAccess = new ClientAccessState();
            string? startupClientId = null;
            var startupTelemetryCompleted = await Task.WhenAny(startupClientTask, Task.Delay(3500)) == startupClientTask;
            if (startupTelemetryCompleted)
            {
                startupAccess = await startupClientTask;
                startupClientId = startupAccess.ClientId;
                LogStartupPhase("telemetry_completed");
            }
            else
            {
                startupAccess = TelemetryService.GetCachedAccessStateSnapshot();
                if (startupAccess.HasKnownState)
                {
                    startupAccess.IsStale = true;
                    startupAccess.Source = startupAccess.IsOfflineGraceActive ? "cache_offline_grace" : "cache_stale";
                }
                startupClientId = startupAccess.ClientId;
                LoggingService.LogWarning("App.ProfileGate",
                    startupAccess.IsOfflineGraceActive
                        ? "Telemetry startup sync timed out. Continuing with cached offline access state."
                        : "Telemetry startup sync timed out. Continuing without profile gate.");
                LogStartupPhase("telemetry_timeout");
            }

            if (!string.IsNullOrWhiteSpace(startupClientId))
            {
                AdminMirrorSyncService.Start(startupClientId);
                var profileCheckTask = ProfileAuthService.CheckProfileAsync(startupClientId);
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
                    var profileWindow = new Views.ProfileSetupWindow(ProfileAuthService, startupClientId);
                    MainWindow = profileWindow;
                    var profileAccepted = profileWindow.ShowDialog() == true && profileWindow.IsProfileCreated;
                    MainWindow = null;

                    if (!profileAccepted)
                    {
                        Shutdown();
                        return;
                    }

                    SetCurrentProfile(await ProfileAuthService.GetProfileByClientIdAsync(startupClientId));
                }
                else if (profileCheck.IsFeatureAvailable && profileCheck.Profile != null)
                {
                    var profile = profileCheck.Profile;
                    if (profile.MustResetPassword)
                    {
                        ProfileSessionService.ClearRememberedSession();

                        var resetWindow = new Views.ProfileResetPasswordWindow(ProfileAuthService, profile);
                        MainWindow = resetWindow;
                        var resetAccepted = resetWindow.ShowDialog() == true && resetWindow.IsPasswordReset;
                        MainWindow = null;

                        if (!resetAccepted || resetWindow.ResetProfile == null)
                        {
                            Shutdown();
                            return;
                        }

                        SetCurrentProfile(resetWindow.ResetProfile);
                    }
                    else if (ProfileSessionService.TryRestoreRememberedSession(profile))
                    {
                        SetCurrentProfile(profile);
                    }
                    else
                    {
                        var loginWindow = new Views.ProfileLoginWindow(ProfileAuthService, ProfileSessionService, profile);
                        MainWindow = loginWindow;
                        var loginAccepted = loginWindow.ShowDialog() == true && loginWindow.IsAuthenticated;
                        MainWindow = null;

                        if (!loginAccepted || loginWindow.AuthenticatedProfile == null)
                        {
                            Shutdown();
                            return;
                        }

                        SetCurrentProfile(loginWindow.AuthenticatedProfile);
                    }
                }
                else if (!profileCheck.IsFeatureAvailable)
                {
                    LoggingService.LogWarning("App.ProfileGate",
                        $"Profile gate skipped: {profileCheck.ErrorMessage}");
                }
            LogStartupPhase("profile_gate_completed");
            }
            else
            {
                AdminMirrorSyncService.Start();
                LoggingService.LogWarning("App.ProfileGate",
                    "Client ID unavailable during startup. Continuing without profile gate.");
            LogStartupPhase("profile_gate_skipped");
            }

            if (startupAccess.IsLive
                && !startupAccess.IsBlocked
                && localLicenseStatus.IsValid
                && string.IsNullOrWhiteSpace(AppSettingsService.Settings.LegacyLicenseMigratedAtUtc)
                && LocalLicenseIsStronger(localLicenseStatus, startupAccess))
            {
                var migrated = await TelemetryService.MigrateLegacyLicenseAsync(
                    localLicenseStatus.Plan,
                    localLicenseStatus.ExpiresOn,
                    localLicenseStatus.ActivatedOn,
                    localLicenseStatus.IsUnlimited);
                if (migrated != null)
                {
                    startupAccess = migrated;
                    startupClientId = migrated.ClientId;
                    AppSettingsService.Settings.LegacyLicenseMigratedAtUtc = DateTime.UtcNow.ToString("o");
                    AppSettingsService.SaveSettings();
                    LogStartupPhase("legacy_license_migrated");
                }
            }

            if (startupAccess.IsBlocked)
            {
                MessageBox.Show(
                    "Доступ до програми заблоковано адміністратором.",
                    "Agency Contractor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            var startupPolicy = startupAccess.Policy;

            var isRemoteTrialExpired = startupAccess.HasKnownState && startupAccess.IsExpired;
            startupPolicy = BuildEffectivePolicy(localLicenseStatus, startupAccess, startupPolicy);

            await PolicyService.ApplyPolicyAsync(startupPolicy);
            _currentGeminiAccessState = startupAccess;
            ApplyEffectiveGeminiApiKey(startupAccess, startupPolicy);
            if (!await EnforceVersionPolicyAsync(startupPolicy))
                return;

            if (!skipLicenseGate && !localLicenseStatus.IsValid && !startupAccess.HasKnownState)
            {
                var licenseWindow = new Views.LicenseWindow(shutdownOnCloseWithoutAccess: true, initialAccessState: startupAccess);
                MainWindow = licenseWindow;
                var licenseAccepted = licenseWindow.ShowDialog() == true && licenseWindow.IsActivated;
                MainWindow = null;

                if (!licenseAccepted)
                {
                    Shutdown();
                    return;
                }

                localLicenseStatus = LicenseService.GetLocalLicenseStatus();
                startupAccess = licenseWindow.LatestAccessState.HasKnownState ? licenseWindow.LatestAccessState : startupAccess;
                startupPolicy = BuildEffectivePolicy(localLicenseStatus, startupAccess, startupAccess.Policy ?? startupPolicy);
                await PolicyService.ApplyPolicyAsync(startupPolicy);
                _currentGeminiAccessState = startupAccess;
                ApplyEffectiveGeminiApiKey(startupAccess, startupPolicy);
                if (!await EnforceVersionPolicyAsync(startupPolicy))
                    return;
            }
        LogStartupPhase("license_gate_completed");

            AccessStatusService.Initialize(localLicenseStatus, startupAccess, startupPolicy);

            NavigationService.NavigateTo(new MainViewModel());
            
            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(NavigationService)
            };
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        LogStartupPhase("main_window_shown");

            if (startupAccess.PendingCommands.Count > 0)
                await CommandService.ExecutePendingCommandsAsync(startupAccess.PendingCommands, startupClientId);

            _heartbeatCts = new CancellationTokenSource();
            _ = StartHeartbeatLoopAsync(_heartbeatCts.Token);
            if (!string.IsNullOrEmpty(AppSettingsService.PendingUpdateFrom))
            {
                TelemetryService.TrackEvent("app_updated", new Dictionary<string, object>
                {
                    ["from_version"] = AppSettingsService.PendingUpdateFrom,
                    ["to_version"] = AppSettingsService.CurrentAppVersion
                });
                AppSettingsService.PendingUpdateFrom = null;
            }

            if (isRemoteTrialExpired)
            {
                ToastService.Instance.Warning(
                    "Пробний період завершився. Програма працює лише в режимі перегляду до активації в AdminPanel.");
            }

            _ = Task.Run(() => startupIntegrityService.RunBackgroundCheck(CompanyService.Companies));
            _ = Task.Run(() => RecentlyDeletedService.PurgeExpired());
            _ = Task.Run(PrewarmSalaryPath);
        }

        private static void PrewarmSalaryPath()
        {
            try
            {
                var sw = Stopwatch.StartNew();

                LocalDbService?.IsSalaryHistoryMigrationCompleted();

                if (SalaryDbService != null)
                {
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
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            base.OnExit(e);
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
            if (GeminiApiService == null || AppSettingsService == null)
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
