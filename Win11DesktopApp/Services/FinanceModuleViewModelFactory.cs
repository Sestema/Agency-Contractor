using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services;

public sealed class FinanceModuleViewModelFactory
{
    private readonly NavigationService _navigationService;
    private readonly EmployeeService _employeeService;
    private readonly FinanceService _financeService;
    private readonly AppSettingsService _appSettingsService;
    private readonly ActivityLogService _activityLogService;
    private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
    private readonly DocumentLocalizationService _documentLocalizationService;
    private readonly CompanyService _companyService;

    public FinanceModuleViewModelFactory(
        NavigationService navigationService,
        EmployeeService employeeService,
        FinanceService financeService,
        AppSettingsService appSettingsService,
        ActivityLogService activityLogService,
        EmployeeDetailsViewModelFactory employeeDetailsViewModelFactory,
        DocumentLocalizationService documentLocalizationService,
        CompanyService companyService)
    {
        _navigationService = navigationService;
        _employeeService = employeeService;
        _financeService = financeService;
        _appSettingsService = appSettingsService;
        _activityLogService = activityLogService;
        _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory;
        _documentLocalizationService = documentLocalizationService;
        _companyService = companyService;
    }

    public SalaryViewModel CreateSalary()
    {
        return new SalaryViewModel(
            _navigationService,
            _financeService,
            _employeeService,
            _appSettingsService,
            _activityLogService,
            _employeeDetailsViewModelFactory,
            _documentLocalizationService,
            _companyService);
    }

    public TablesMenuViewModel CreateTablesMenu()
    {
        return new TablesMenuViewModel(_navigationService, this);
    }

    public AdvanceTableViewModel CreateAdvanceTable()
    {
        return new AdvanceTableViewModel(
            _employeeService,
            _navigationService,
            this,
            _companyService,
            _activityLogService,
            _documentLocalizationService);
    }

    public PaymentSignViewModel CreatePaymentSign()
    {
        return new PaymentSignViewModel(
            _employeeService,
            _navigationService,
            this,
            _financeService,
            _companyService,
            _activityLogService,
            _documentLocalizationService);
    }
}
