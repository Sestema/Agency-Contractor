using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class StepAdditionalView : UserControl
    {
        private static readonly string[] DateFormats = { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy" };

        public StepAdditionalView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            ApplyDatePickerLanguage();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // New wizard instance: clear sticky SelectedDate so the same day can be picked again.
            SyncPickerFromText(StartDatePicker, StartDateTextBox?.Text);
            SyncPickerFromText(SignDatePicker, SignDateTextBox?.Text);
        }

        private void ApplyDatePickerLanguage()
        {
            var xmlLanguage = XmlLanguage.GetLanguage(Thread.CurrentThread.CurrentUICulture.IetfLanguageTag);
            Language = xmlLanguage;
            StartDatePicker.Language = xmlLanguage;
            SignDatePicker.Language = xmlLanguage;
        }

        private void OpenStartDatePicker_Click(object sender, RoutedEventArgs e)
        {
            ApplyDatePickerLanguage();
            SyncPickerFromText(StartDatePicker, StartDateTextBox.Text);
            StartDatePicker.IsDropDownOpen = true;
        }

        private void OpenSignDatePicker_Click(object sender, RoutedEventArgs e)
        {
            ApplyDatePickerLanguage();
            SyncPickerFromText(SignDatePicker, SignDateTextBox.Text);
            SignDatePicker.IsDropDownOpen = true;
        }

        private static void SyncPickerFromText(DatePicker picker, string? text)
        {
            if (TryParseWizardDate(text, out var dt))
            {
                if (picker.SelectedDate != dt.Date)
                    picker.SelectedDate = dt.Date;
            }
            else if (picker.SelectedDate != null)
            {
                picker.SelectedDate = null;
            }
        }

        private static bool TryParseWizardDate(string? text, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return DateTime.TryParseExact(
                text.Trim(),
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
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
