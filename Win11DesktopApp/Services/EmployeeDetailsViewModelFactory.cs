using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services
{
    public sealed class EmployeeDetailsViewModelFactory
    {
        private readonly EmployeeService _employeeService;
        private readonly GeminiApiService _geminiApiService;
        private readonly FinanceService _financeService;
        private readonly AppSettingsService _appSettingsService;
        private readonly ActivityLogService _activityLogService;
        private readonly CompanyService _companyService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly TemplateService _templateService;
        private readonly DocumentGenerationService _documentGenerationService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly AiWindowFactory _aiWindowFactory;

        public EmployeeDetailsViewModelFactory(
            EmployeeService employeeService,
            GeminiApiService geminiApiService,
            FinanceService financeService,
            AppSettingsService appSettingsService,
            ActivityLogService activityLogService,
            CompanyService companyService,
            DocumentLocalizationService documentLocalizationService,
            TemplateService templateService,
            DocumentGenerationService documentGenerationService,
            TagCatalogService tagCatalogService,
            AiWindowFactory aiWindowFactory)
        {
            _employeeService = employeeService;
            _geminiApiService = geminiApiService;
            _financeService = financeService;
            _appSettingsService = appSettingsService;
            _activityLogService = activityLogService;
            _companyService = companyService;
            _documentLocalizationService = documentLocalizationService;
            _templateService = templateService;
            _documentGenerationService = documentGenerationService;
            _tagCatalogService = tagCatalogService;
            _aiWindowFactory = aiWindowFactory;
        }

        public EmployeeDetailsViewModel Create(
            string firmName,
            string employeeFolder,
            EmployeeService? employeeService = null,
            bool isReadOnlyMode = false,
            string? employeeId = null)
        {
            return new EmployeeDetailsViewModel(
                firmName,
                employeeFolder,
                employeeService ?? _employeeService,
                isReadOnlyMode,
                employeeId,
                _geminiApiService,
                _financeService,
                _appSettingsService,
                _activityLogService,
                _companyService,
                _documentLocalizationService,
                _templateService,
                _documentGenerationService,
                _tagCatalogService,
                _aiWindowFactory);
        }
    }
}
