using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services;

public sealed class MainModuleViewModelFactory
{
    private readonly EmployeeService _employeeService;
    private readonly AddEmployeeWizardViewModelFactory _addEmployeeWizardViewModelFactory;
    private readonly TemplateService _templateService;
    private readonly NavigationService _navigationService;
    private readonly TemplateViewModelFactory _templateViewModelFactory;
    private readonly RecentlyDeletedService _recentlyDeletedService;
    private readonly GeminiApiService _geminiApiService;
    private readonly ChatPersistenceService _chatPersistenceService;
    private readonly Telegram.TelegramBotService _telegramBotService;
    private readonly CurrentProfileService _currentProfileService;
    private readonly ProfileAuthService _profileAuthService;
    private readonly ActivityLogService _activityLogService;
    private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
    private readonly AppSettingsService _appSettingsService;
    private readonly DocumentLocalizationService _documentLocalizationService;
    private readonly DocumentGenerationService _documentGenerationService;
    private readonly TagCatalogService _tagCatalogService;
    private readonly CompanyService _companyService;
    private readonly AppNotificationService _notificationService;

    public MainModuleViewModelFactory(
        EmployeeService employeeService,
        AddEmployeeWizardViewModelFactory addEmployeeWizardViewModelFactory,
        TemplateService templateService,
        NavigationService navigationService,
        TemplateViewModelFactory templateViewModelFactory,
        RecentlyDeletedService recentlyDeletedService,
        GeminiApiService geminiApiService,
        ChatPersistenceService chatPersistenceService,
        Telegram.TelegramBotService telegramBotService,
        CurrentProfileService currentProfileService,
        ProfileAuthService profileAuthService,
        ActivityLogService activityLogService,
        EmployeeDetailsViewModelFactory employeeDetailsViewModelFactory,
        AppSettingsService appSettingsService,
        DocumentLocalizationService documentLocalizationService,
        DocumentGenerationService documentGenerationService,
        TagCatalogService tagCatalogService,
        CompanyService companyService,
        AppNotificationService notificationService)
    {
        _employeeService = employeeService;
        _addEmployeeWizardViewModelFactory = addEmployeeWizardViewModelFactory;
        _templateService = templateService;
        _navigationService = navigationService;
        _templateViewModelFactory = templateViewModelFactory;
        _recentlyDeletedService = recentlyDeletedService;
        _geminiApiService = geminiApiService;
        _chatPersistenceService = chatPersistenceService;
        _telegramBotService = telegramBotService;
        _currentProfileService = currentProfileService;
        _profileAuthService = profileAuthService;
        _activityLogService = activityLogService;
        _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory;
        _appSettingsService = appSettingsService;
        _documentLocalizationService = documentLocalizationService;
        _documentGenerationService = documentGenerationService;
        _tagCatalogService = tagCatalogService;
        _companyService = companyService;
        _notificationService = notificationService;
    }

    public EmployeesViewModel CreateEmployees(EmployerCompany? company)
    {
        return new EmployeesViewModel(
            company,
            _employeeService,
            _addEmployeeWizardViewModelFactory,
            _navigationService,
            _currentProfileService,
            _profileAuthService,
            _recentlyDeletedService,
            _appSettingsService,
            _documentLocalizationService,
            _employeeDetailsViewModelFactory,
            _activityLogService,
            _templateService,
            _documentGenerationService,
            _tagCatalogService,
            _geminiApiService);
    }

    public EmployeesViewModel CreateAllEmployees()
    {
        return new EmployeesViewModel(
            null,
            _employeeService,
            _addEmployeeWizardViewModelFactory,
            _navigationService,
            _currentProfileService,
            _profileAuthService,
            _recentlyDeletedService,
            _appSettingsService,
            _documentLocalizationService,
            _employeeDetailsViewModelFactory,
            _activityLogService,
            _templateService,
            _documentGenerationService,
            _tagCatalogService,
            _geminiApiService,
            _companyService,
            showAllCompanies: true);
    }

    public TemplatesViewModel CreateTemplates(EmployerCompany? company)
    {
        return _templateViewModelFactory.CreateTemplates(company);
    }

    public ProblemsViewModel CreateProblems()
    {
        return new ProblemsViewModel(
            _navigationService,
            _employeeService,
            _companyService,
            _employeeDetailsViewModelFactory,
            _activityLogService,
            _documentLocalizationService);
    }

    public ArchiveViewModel CreateArchive(string? employeeToOpenFolder = null)
    {
        return new ArchiveViewModel(
            employeeToOpenFolder,
            _navigationService,
            _employeeService,
            _appSettingsService,
            _companyService,
            _employeeDetailsViewModelFactory,
            _activityLogService,
            _notificationService);
    }

    public RecentlyDeletedViewModel CreateRecentlyDeleted()
    {
        return new RecentlyDeletedViewModel(
            _recentlyDeletedService,
            _navigationService,
            _currentProfileService,
            _profileAuthService,
            _activityLogService,
            _employeeDetailsViewModelFactory);
    }

    public AIChatViewModel CreateAiChat()
    {
        return new AIChatViewModel(_navigationService, _geminiApiService, _chatPersistenceService, _telegramBotService);
    }
}
