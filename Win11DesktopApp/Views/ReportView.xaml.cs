using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class ReportView : UserControl
    {
        public ReportView()
        {
            InitializeComponent();
        }

        private void EmployeeRow_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is EmployeeReportRow emp
                && DataContext is ReportViewModel vm)
            {
                vm.OpenEmployeeCommand.Execute(emp);
                e.Handled = true;
            }
        }

        private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null) return;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
