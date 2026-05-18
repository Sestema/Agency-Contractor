using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services
{
    public sealed class AddEmployeeWizardViewModelFactory
    {
        private readonly EmployeeService _employeeService;
        private readonly GeminiApiService _geminiApiService;
        private readonly ActivityLogService _activityLogService;
        private readonly AppStatisticsService _appStatisticsService;
        private readonly SyncEventService? _syncEventService;
        private readonly SharedOperationLockService? _sharedOperationLockService;

        public AddEmployeeWizardViewModelFactory(
            EmployeeService employeeService,
            GeminiApiService geminiApiService,
            ActivityLogService activityLogService,
            AppStatisticsService appStatisticsService,
            SyncEventService? syncEventService = null,
            SharedOperationLockService? sharedOperationLockService = null)
        {
            _employeeService = employeeService;
            _geminiApiService = geminiApiService;
            _activityLogService = activityLogService;
            _appStatisticsService = appStatisticsService;
            _syncEventService = syncEventService;
            _sharedOperationLockService = sharedOperationLockService;
        }

        public AddEmployeeWizardViewModel Create(
            EmployerCompany company,
            EmployeeService? employeeService = null,
            GeminiApiService? geminiApiService = null)
        {
            return new AddEmployeeWizardViewModel(
                company,
                employeeService ?? _employeeService,
                geminiApiService ?? _geminiApiService,
                _activityLogService,
                _appStatisticsService,
                _syncEventService,
                _sharedOperationLockService);
        }
    }
}
