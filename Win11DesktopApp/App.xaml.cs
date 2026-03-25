using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public static RecentlyDeletedService RecentlyDeletedService { get; private set; } = null!;
        public static DocumentGenerationService DocumentGenerationService { get; private set; } = null!;
        public static DocumentLocalizationService DocumentLocalizationService { get; private set; } = null!;
        public static InvoiceStorageService InvoiceStorageService { get; private set; } = null!;
        public static AresLookupService AresLookupService { get; private set; } = null!;
        public static InvoiceQrPaymentService InvoiceQrPaymentService { get; private set; } = null!;
        public static InvoicePdfRenderService InvoicePdfRenderService { get; private set; } = null!;

        public static FinanceService FinanceService { get; private set; } = null!;
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

            PersistenceService = new PersistenceService(AppSettingsService, FolderService);
            var startupIntegrityService = new StartupIntegrityService(FolderService, PersistenceService);
            startupIntegrityService.IncludeSettingsStartupState(AppSettingsService);
            startupIntegrityService.RunQuickCheck();
            TagCatalogService = new TagCatalogService();
            CompanyService = new CompanyService(TagCatalogService, AppSettingsService, PersistenceService, FolderService);
            TemplateService = new TemplateService(AppSettingsService, FolderService, TagCatalogService);
            StarterTemplateCatalogService = new StarterTemplateCatalogService();
            EmployeeService = new EmployeeService(AppSettingsService, TagCatalogService, FolderService);
            RecentlyDeletedService = new RecentlyDeletedService(FolderService, EmployeeService);
            FinanceService = new FinanceService(FolderService, suppressStartupNotifications: true);
            InvoiceStorageService = new InvoiceStorageService(FolderService);
            AresLookupService = new AresLookupService(InvoiceStorageService);
            InvoiceQrPaymentService = new InvoiceQrPaymentService();
            InvoicePdfRenderService = new InvoicePdfRenderService(InvoiceStorageService, InvoiceQrPaymentService);
            startupIntegrityService.IncludeFinanceStartupState(FinanceService);
            ActivityLogService = new ActivityLogService(FolderService);
            CandidateService = new CandidateService(FolderService);
            GeminiApiService = new GeminiApiService();
            NewsService = new NewsService();
            ProfileAuthService = new ProfileAuthService();
            ProfileSessionService = new ProfileSessionService(AppSettingsService);
            if (!string.IsNullOrEmpty(AppSettingsService.Settings.GeminiApiKey))
                GeminiApiService.SetApiKey(AppSettingsService.Settings.GeminiApiKey);
            if (!string.IsNullOrEmpty(AppSettingsService.Settings.GeminiModel))
                GeminiApiService.SetModel(AppSettingsService.Settings.GeminiModel);
            DocumentGenerationService = new DocumentGenerationService();
            DocumentLocalizationService = new DocumentLocalizationService();
            _ = Task.Run(() => PendingCleanupService.ProcessPendingCleanupAsync(EmployeeService.TryCleanupDeferredDirectory));

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

            RecentlyDeletedService.EnsureStorage();

            LoggingService.LogInfo("App", "All services initialized");

            var skipLicenseGate =
#if DEBUG
                true;
#else
                Debugger.IsAttached;
#endif

            var hasValidLocalLicense = LicenseService.IsLicenseValid();
            var startupClientTask = TelemetryService.GetStartupAccessStateAsync();
            var startupAccess = new ClientAccessState();
            string? startupClientId = null;
            var startupTelemetryCompleted = await Task.WhenAny(startupClientTask, Task.Delay(3500)) == startupClientTask;
            if (startupTelemetryCompleted)
            {
                startupAccess = await startupClientTask;
                startupClientId = startupAccess.ClientId;
            }
            else
            {
                LoggingService.LogWarning("App.ProfileGate",
                    "Telemetry startup sync timed out. Continuing without profile gate.");
            }

            if (!string.IsNullOrWhiteSpace(startupClientId))
            {
                var profileCheck = await ProfileAuthService.CheckProfileAsync(startupClientId);
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
            }
            else
            {
                LoggingService.LogWarning("App.ProfileGate",
                    "Client ID unavailable during startup. Continuing without profile gate.");
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

            var startupPolicy = !string.IsNullOrWhiteSpace(startupClientId)
                ? await PolicyService.FetchPolicyAsync(startupClientId)
                : null;

            var isRemoteTrialExpired = !hasValidLocalLicense && startupAccess.IsExpired;
            if (isRemoteTrialExpired)
            {
                startupPolicy ??= new RemotePolicy
                {
                    ClientId = startupClientId ?? string.Empty
                };

                startupPolicy.ReadOnlyMode = true;
                startupPolicy.DisableAI = true;
                startupPolicy.DisableExports = true;
                startupPolicy.HideTemplates = true;
                startupPolicy.HideFinance = true;

                if (string.IsNullOrWhiteSpace(startupPolicy.AdminMessage))
                {
                    startupPolicy.AdminMessage =
                        "Пробний період завершився. Доступ переведено в режим перегляду до активації через AdminPanel.";
                }
            }

            await PolicyService.ApplyPolicyAsync(startupPolicy);

            if (!skipLicenseGate && !hasValidLocalLicense && !startupAccess.HasRemoteAccessWindow && !isRemoteTrialExpired)
            {
                var licenseWindow = new Views.LicenseWindow();
                MainWindow = licenseWindow;
                var licenseAccepted = licenseWindow.ShowDialog() == true && licenseWindow.IsActivated;
                MainWindow = null;

                if (!licenseAccepted)
                {
                    Shutdown();
                    return;
                }

                hasValidLocalLicense = LicenseService.IsLicenseValid();
            }

            AccessStatusService.Initialize(hasValidLocalLicense, startupAccess, startupPolicy);

            NavigationService.NavigateTo(new MainViewModel());
            
            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(NavigationService)
            };
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

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
                        await TelemetryService.SendHeartbeatAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("App.HeartbeatLoop", ex.Message);
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
    }
}
