using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class EmployeeDetailsView : UserControl
    {
        public EmployeeDetailsView()
        {
            InitializeComponent();
            PreviewKeyDown += EmployeeDetailsView_PreviewKeyDown;
            Loaded += (_, _) => Focus();
            Focusable = true;
        }

        private void EmployeeDetailsView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            if (DataContext is not EmployeeDetailsViewModel vm)
                return;

            if (vm.TryHandleEscape())
                e.Handled = true;
        }
    }
}
