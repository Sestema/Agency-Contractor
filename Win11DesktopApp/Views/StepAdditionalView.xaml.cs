using System;
using System.Globalization;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Markup;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class StepAdditionalView : UserControl
    {
        public StepAdditionalView()
        {
            InitializeComponent();
            ApplyDatePickerLanguage();
        }

        private void ApplyDatePickerLanguage()
        {
            var xmlLanguage = XmlLanguage.GetLanguage(Thread.CurrentThread.CurrentUICulture.IetfLanguageTag);
            Language = xmlLanguage;
            StartDatePicker.Language = xmlLanguage;
            SignDatePicker.Language = xmlLanguage;
        }

        private void OpenStartDatePicker_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyDatePickerLanguage();
            StartDatePicker.IsDropDownOpen = true;
        }

        private void OpenSignDatePicker_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyDatePickerLanguage();
            SignDatePicker.IsDropDownOpen = true;
        }

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
