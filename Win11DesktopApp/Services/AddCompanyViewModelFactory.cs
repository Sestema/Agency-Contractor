using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services;

public sealed class AddCompanyViewModelFactory
{
    private readonly CompanyService _companyService;
    private readonly EmployeeService _employeeService;
    private readonly FolderService _folderService;
    private readonly ActivityLogService _activityLogService;

    public AddCompanyViewModelFactory(
        CompanyService companyService,
        EmployeeService employeeService,
        FolderService folderService,
        ActivityLogService activityLogService)
    {
        _companyService = companyService;
        _employeeService = employeeService;
        _folderService = folderService;
        _activityLogService = activityLogService;
    }

    public AddCompanyViewModel CreateAdd()
    {
        return new AddCompanyViewModel(_companyService, _employeeService, _folderService, _activityLogService);
    }

    public AddCompanyViewModel CreateEdit(EmployerCompany existingCompany)
    {
        return new AddCompanyViewModel(existingCompany, _companyService, _employeeService, _folderService, _activityLogService);
    }
}
