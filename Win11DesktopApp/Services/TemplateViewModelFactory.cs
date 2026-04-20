using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services
{
    public sealed class TemplateViewModelFactory
    {
        private readonly TemplateService _templateService;
        private readonly ActivityLogService _activityLogService;
        private readonly NavigationService _navigationService;
        private readonly CompanyService _companyService;
        private readonly GeminiApiService _geminiApiService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly AppSettingsService _appSettingsService;
        private readonly StarterTemplateCatalogService _starterTemplateCatalogService;
        private readonly AiWindowFactory _aiWindowFactory;

    public TemplateViewModelFactory(
        TemplateService templateService,
        ActivityLogService activityLogService,
        NavigationService navigationService,
        CompanyService companyService,
        GeminiApiService geminiApiService,
        TagCatalogService tagCatalogService,
        AppSettingsService appSettingsService,
        StarterTemplateCatalogService starterTemplateCatalogService,
        AiWindowFactory aiWindowFactory)
        {
            _templateService = templateService;
            _activityLogService = activityLogService;
            _navigationService = navigationService;
            _companyService = companyService;
            _geminiApiService = geminiApiService;
            _tagCatalogService = tagCatalogService;
            _appSettingsService = appSettingsService;
            _starterTemplateCatalogService = starterTemplateCatalogService;
            _aiWindowFactory = aiWindowFactory;
        }

        public AddTemplateViewModel CreateAddTemplate(string firmName)
        {
            return new AddTemplateViewModel(firmName, _templateService, _activityLogService);
        }

        public TemplatesViewModel CreateTemplates(EmployerCompany? company)
        {
            return new TemplatesViewModel(
                company,
                _templateService,
                _navigationService,
                this,
                _activityLogService,
                _companyService,
                _tagCatalogService,
                _appSettingsService);
        }

        public TemplateEditorViewModel CreateTemplateEditor(string firmName, TemplateEntry template)
        {
            return new TemplateEditorViewModel(
                firmName,
                template,
                _templateService,
                _navigationService,
                this,
                _companyService,
                _geminiApiService,
                _tagCatalogService,
                _starterTemplateCatalogService,
                _appSettingsService,
                _aiWindowFactory);
        }

        public XlsxEditorViewModel CreateXlsxEditor(string firmName, TemplateEntry template)
        {
            return new XlsxEditorViewModel(
                firmName,
                template,
                _templateService,
                _navigationService,
                this,
                _companyService,
                _tagCatalogService,
                _appSettingsService,
                _aiWindowFactory);
        }

        public PdfEditorViewModel CreatePdfEditor(string firmName, TemplateEntry template)
        {
            return new PdfEditorViewModel(
                firmName,
                template,
                _templateService,
                _navigationService,
                this,
                _companyService,
                _geminiApiService,
                _tagCatalogService,
                _appSettingsService);
        }
    }
}
