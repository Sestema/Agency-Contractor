namespace Win11DesktopApp.ViewModels
{
    public partial class EmployeeDetailsViewModel
    {
        private void EnsureHistoryLoaded()
        {
            if (!_isHistoryLoaded)
            {
                LoadHistory();
                _isHistoryLoaded = true;
            }
        }

        private void EnsureSalaryHistoryLoaded()
        {
            if (!_isSalaryHistoryLoaded)
            {
                LoadSalaryHistory();
                _isSalaryHistoryLoaded = true;
            }
        }

        private void InvalidateDetailCaches()
        {
            _isHistoryLoaded = false;
            _isSalaryHistoryLoaded = false;
        }
    }
}
