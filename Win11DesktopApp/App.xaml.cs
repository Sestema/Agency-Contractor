using System;
using System.Threading.Tasks;
using System.Windows;
using PdfSharp.Fonts;
using Velopack;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp
{
    public partial class App : Application
    {
        public static NavigationService NavigationService { get; private set; } = null!;
        public static ThemeService ThemeService { get; private set; } = null!;
        public static LanguageService LanguageService { get; private set; } = null!;
        public static AppSettingsService AppSettingsService { get; private set; } = null!;
        public static FolderService FolderService { get; private set; } = null!;
        public static TagCatalogService TagCatalogService { get; private set; } = null!;
        public static CompanyService CompanyService { get; private set; } = null!;
        public static TemplateService TemplateService { get; private set; } = null!;
        public static PersistenceService PersistenceService { get; private set; } = null!;
        public static EmployeeService EmployeeService { get; private set; } = null!;
        public static DocumentGenerationService DocumentGenerationService { get; private set; } = null!;
        public static DocumentLocalizationService DocumentLocalizationService { get; private set; } = null!;

        public static FinanceService FinanceService { get; private set; } = null!;
        public static ActivityLogService ActivityLogService { get; private set; } = null!;
        public static CandidateService CandidateService { get; private set; } = null!;
        public static GeminiApiService GeminiApiService { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            VelopackApp.Build().Run();

            base.OnStartup(e);

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
                ErrorHandler.Report("App.DispatcherUnhandledException", args.Exception, ErrorSeverity.Critical, showUser: true);
                args.Handled = true;
            };

            NavigationService = new NavigationService();
            ThemeService = new ThemeService();
            LanguageService = new LanguageService();
            AppSettingsService = new AppSettingsService();
            FolderService = new FolderService(AppSettingsService);

            if (!string.IsNullOrEmpty(FolderService.RootPath))
                LoggingService.Initialize(FolderService.RootPath);

            LoggingService.LogInfo("App", $"Application started v{AppSettingsService.CurrentAppVersion}");

            PersistenceService = new PersistenceService(AppSettingsService, FolderService);
            TagCatalogService = new TagCatalogService();
            CompanyService = new CompanyService(TagCatalogService, AppSettingsService, PersistenceService, FolderService);
            TemplateService = new TemplateService(AppSettingsService, FolderService, TagCatalogService);
            EmployeeService = new EmployeeService(AppSettingsService, TagCatalogService, FolderService);
            FinanceService = new FinanceService(FolderService);
            ActivityLogService = new ActivityLogService(FolderService);
            CandidateService = new CandidateService(FolderService);
            GeminiApiService = new GeminiApiService();
            if (!string.IsNullOrEmpty(AppSettingsService.Settings.GeminiApiKey))
                GeminiApiService.SetApiKey(AppSettingsService.Settings.GeminiApiKey);
            if (!string.IsNullOrEmpty(AppSettingsService.Settings.GeminiModel))
                GeminiApiService.SetModel(AppSettingsService.Settings.GeminiModel);
            DocumentGenerationService = new DocumentGenerationService();
            DocumentLocalizationService = new DocumentLocalizationService();

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

            LoggingService.LogInfo("App", "All services initialized");

            if (!LicenseService.IsLicenseValid())
            {
                var licenseWindow = new Views.LicenseWindow();
                if (licenseWindow.ShowDialog() != true || !licenseWindow.IsActivated)
                {
                    Shutdown();
                    return;
                }
            }

            NavigationService.NavigateTo(new MainViewModel());
            
            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(NavigationService)
            };
            mainWindow.Show();
        }
    }
}
