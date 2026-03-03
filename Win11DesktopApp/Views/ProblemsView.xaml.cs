using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class ProblemsView : UserControl
    {
        public ProblemsView()
        {
            InitializeComponent();
        }

        private void EmployeeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is EmployeeProblemGroup group)
            {
                var vm = DataContext as ProblemsViewModel;
                vm?.OpenEmployeeCommand.Execute(group);
                e.Handled = true;
            }
        }
    }
}
