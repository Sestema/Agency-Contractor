using System.Globalization;
using System.Windows;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class SalaryQrPreviewWindow : Window
    {
        public SalaryQrPreviewWindow(SalaryQrPaymentPreview preview)
        {
            InitializeComponent();

            NameText.Text = preview.EmployeeName;
            AmountText.Text = $"{TryL("FinQrAmount") ?? "Amount"}: {preview.Amount.ToString("N0", CultureInfo.CurrentCulture)} Kč";
            AccountText.Text = $"{TryL("FinQrAccount") ?? "Account"}: {preview.AccountNumber}";
            BankText.Text = string.IsNullOrWhiteSpace(preview.BankName)
                ? string.Empty
                : $"{TryL("FinQrBank") ?? "Bank"}: {preview.BankName}";
            IbanText.Text = string.IsNullOrWhiteSpace(preview.Iban)
                ? string.Empty
                : $"{TryL("FinQrIban") ?? "IBAN"}: {preview.Iban}";
            MessageText.Text = string.IsNullOrWhiteSpace(preview.MessageText)
                ? string.Empty
                : $"{TryL("FinQrMessage") ?? "Message"}: {preview.MessageText}";

            if (preview.IsReady && preview.Image != null)
            {
                QrImage.Source = preview.Image;
                QrImage.Visibility = Visibility.Visible;
                StatusText.Text = string.Empty;
            }
            else
            {
                QrImage.Visibility = Visibility.Collapsed;
                StatusText.Text = string.IsNullOrWhiteSpace(preview.Message)
                    ? (TryL("FinQrNoImage") ?? "QR is not available.")
                    : preview.Message;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static string? TryL(string key)
        {
            try { return Application.Current.FindResource(key) as string; } catch { return null; }
        }
    }
}
