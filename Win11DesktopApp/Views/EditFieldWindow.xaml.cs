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
        public bool IsQrTransfer { get; private set; }
        public string QrMessageText { get; private set; } = string.Empty;

        public EditFieldWindow(
            string name,
            FieldOperation operation,
            string firmName,
            bool isQrTransfer,
            string qrMessageText,
            List<string> availableFirms)
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
            FirmBox.Items.Add(new ComboBoxItem { Content = allLabel, Tag = FinanceConstants.AllFirmsKey });
            int selectedIdx = 0;
            for (int i = 0; i < availableFirms.Count; i++)
            {
                FirmBox.Items.Add(new ComboBoxItem { Content = availableFirms[i], Tag = availableFirms[i] });
                if (availableFirms[i] == firmName)
                    selectedIdx = i + 1;
            }
            if (firmName == FinanceConstants.AllFirmsKey || string.IsNullOrEmpty(firmName))
                selectedIdx = 0;
            FirmBox.SelectedIndex = selectedIdx;

            IsQrBox.IsChecked = isQrTransfer;
            QrMessageBox.Text = qrMessageText ?? string.Empty;
            QrMessageBox.IsEnabled = isQrTransfer;
        }

        private void IsQrBox_Changed(object sender, RoutedEventArgs e)
        {
            if (QrMessageBox != null)
                QrMessageBox.IsEnabled = IsQrBox.IsChecked == true;
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
            FieldFirmName = firmItem?.Tag?.ToString() ?? FinanceConstants.AllFirmsKey;
            IsQrTransfer = IsQrBox.IsChecked == true;
            QrMessageText = IsQrTransfer ? (QrMessageBox.Text ?? string.Empty).Trim() : string.Empty;

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
