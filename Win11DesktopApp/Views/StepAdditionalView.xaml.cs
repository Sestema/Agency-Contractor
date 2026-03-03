using System;
using System.Windows.Controls;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class StepAdditionalView : UserControl
    {
        public StepAdditionalView() => InitializeComponent();

        private void StartDatePicker_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (StartDatePicker.SelectedDate is DateTime dt)
            {
                var formatted = dt.ToString("dd.MM.yyyy");
                StartDateTextBox.Text = formatted;
                if (DataContext is AddEmployeeWizardViewModel vm)
                    vm.Data.StartDate = formatted;
            }
        }

        private void SignDatePicker_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (SignDatePicker.SelectedDate is DateTime dt)
            {
                var formatted = dt.ToString("dd.MM.yyyy");
                SignDateTextBox.Text = formatted;
                if (DataContext is AddEmployeeWizardViewModel vm)
                    vm.Data.ContractSignDate = formatted;
            }
        }
    }
}
