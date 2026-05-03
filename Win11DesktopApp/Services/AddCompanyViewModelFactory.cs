using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;
using Win11DesktopApp.Invoices.Services;

namespace Win11DesktopApp.Services;

public sealed class AddCompanyViewModelFactory
{
    private readonly CompanyService _companyService;
    private readonly EmployeeService _employeeService;
    private readonly FolderService _folderService;
    private readonly ActivityLogService _activityLogService;
    private readonly AresLookupService _aresLookupService;

    public AddCompanyViewModelFactory(
        CompanyService companyService,
        EmployeeService employeeService,
        FolderService folderService,
        ActivityLogService activityLogService,
        AresLookupService aresLookupService)
    {
        _companyService = companyService;
        _employeeService = employeeService;
        _folderService = folderService;
        _activityLogService = activityLogService;
        _aresLookupService = aresLookupService;
    }

    public AddCompanyViewModel CreateAdd()
    {
        return new AddCompanyViewModel(_companyService, _employeeService, _folderService, _activityLogService, _aresLookupService);
    }

    public AddCompanyViewModel CreateEdit(EmployerCompany existingCompany)
    {
        return new AddCompanyViewModel(existingCompany, _companyService, _employeeService, _folderService, _activityLogService, _aresLookupService);
    }
}
