using System.Windows;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class EmployeeHistoryCleanupWindow : Window
    {
        private readonly EmployeeHistoryCleanupViewModel _viewModel;

        public EmployeeHistoryCleanupWindow(EmployeeHistoryCleanupViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            Loaded += async (_, _) => await _viewModel.LoadAsync();
        }
    }
}
