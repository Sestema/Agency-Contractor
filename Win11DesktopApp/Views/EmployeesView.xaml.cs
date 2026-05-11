using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class EmployeesView : UserControl
    {
        public EmployeesView()
        {
            InitializeComponent();
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid dg && dg.SelectedItem is EmployeeModels.EmployeeSummary emp
                && DataContext is EmployeesViewModel vm && vm.OpenEmployeeCommand.CanExecute(emp))
            {
                vm.OpenEmployeeCommand.Execute(emp);
            }
        }

        private void EmployeesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }
    }
}
