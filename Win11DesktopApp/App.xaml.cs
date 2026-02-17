using System.Windows;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            NavigationService = new NavigationService();
            ThemeService = new ThemeService();
            LanguageService = new LanguageService();
            AppSettingsService = new AppSettingsService();
            FolderService = new FolderService(AppSettingsService);
            PersistenceService = new PersistenceService(AppSettingsService, FolderService);
            TagCatalogService = new TagCatalogService();
            CompanyService = new CompanyService(TagCatalogService, AppSettingsService, PersistenceService, FolderService);
            TemplateService = new TemplateService(AppSettingsService, FolderService, TagCatalogService);
            EmployeeService = new EmployeeService(AppSettingsService, TagCatalogService, FolderService);
            DocumentGenerationService = new DocumentGenerationService();

            if (!string.IsNullOrEmpty(AppSettingsService.Settings.LanguageCode))
            {
                LanguageService.SetLanguage(AppSettingsService.Settings.LanguageCode);
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
