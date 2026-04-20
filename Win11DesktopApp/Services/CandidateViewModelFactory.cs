using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services
{
    public sealed class CandidateViewModelFactory
    {
        private readonly CandidateService _candidateService;
        private readonly EmployeeService _employeeService;
        private readonly ActivityLogService _activityLogService;

        public CandidateViewModelFactory(
            CandidateService candidateService,
            EmployeeService employeeService,
            ActivityLogService activityLogService)
        {
            _candidateService = candidateService;
            _employeeService = employeeService;
            _activityLogService = activityLogService;
        }

        public AddCandidateViewModel CreateAddCandidate()
        {
            return new AddCandidateViewModel(_candidateService, _employeeService, _activityLogService);
        }

        public CandidateDetailsViewModel CreateCandidateDetails(string candidateFolder)
        {
            return new CandidateDetailsViewModel(candidateFolder, _candidateService, _activityLogService);
        }
    }
}
