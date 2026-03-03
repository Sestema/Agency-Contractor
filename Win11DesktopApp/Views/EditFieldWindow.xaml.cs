using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class EditFieldWindow : Window
    {
        public string FieldName { get; private set; } = string.Empty;
        public FieldOperation FieldOperation { get; private set; }
        public string FieldFirmName { get; private set; } = string.Empty;

        public EditFieldWindow(string name, FieldOperation operation, string firmName, List<string> availableFirms)
        {
            InitializeComponent();

            NameBox.Text = name;

            int opIdx = operation switch
            {
                FieldOperation.Add => 0,
                FieldOperation.Subtract => 1,
                FieldOperation.Multiply => 2,
                FieldOperation.Divide => 3,
                _ => 1
            };
            OpBox.SelectedIndex = opIdx;

            var allLabel = TryL("FinFilterAll") ?? "All firms";
            FirmBox.Items.Add(new ComboBoxItem { Content = allLabel, Tag = FinanceService.AllFirmsKey });
            int selectedIdx = 0;
            for (int i = 0; i < availableFirms.Count; i++)
            {
                FirmBox.Items.Add(new ComboBoxItem { Content = availableFirms[i], Tag = availableFirms[i] });
                if (availableFirms[i] == firmName)
                    selectedIdx = i + 1;
            }
            if (firmName == FinanceService.AllFirmsKey || string.IsNullOrEmpty(firmName))
                selectedIdx = 0;
            FirmBox.SelectedIndex = selectedIdx;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            FieldName = name;

            var opItem = OpBox.SelectedItem as ComboBoxItem;
            FieldOperation = opItem?.Tag?.ToString() switch
            {
                "Add" => FieldOperation.Add,
                "Multiply" => FieldOperation.Multiply,
                "Divide" => FieldOperation.Divide,
                _ => FieldOperation.Subtract
            };

            var firmItem = FirmBox.SelectedItem as ComboBoxItem;
            FieldFirmName = firmItem?.Tag?.ToString() ?? FinanceService.AllFirmsKey;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string? TryL(string key)
        {
            try { return Application.Current.FindResource(key) as string; } catch { return null; }
        }
    }
}
