using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class MainPage : UserControl
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private void CompanyItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is EmployerCompany company)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.EditCompanyCommand.Execute(company);
                    e.Handled = true;
                }
            }
        }
    }
}
