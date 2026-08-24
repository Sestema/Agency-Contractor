using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public sealed class SalaryQrPrintRow
    {
        public bool IsSelected { get; set; } = true;
        public string FullName { get; init; } = string.Empty;
        public string FirmName { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string AccountNumber { get; init; } = string.Empty;
        public string BankName { get; init; } = string.Empty;
        public string MessageText { get; init; } = string.Empty;
        public string AmountText => $"{Amount.ToString("N0", CultureInfo.CurrentCulture)} Kč";
    }

    public partial class SalaryQrPrintWindow : Window
    {
        private readonly ObservableCollection<SalaryQrPrintRow> _rows;

        public IReadOnlyList<SalaryQrPrintRow> SelectedRows { get; private set; } = Array.Empty<SalaryQrPrintRow>();

        public SalaryQrPrintWindow(IEnumerable<SalaryQrPrintRow> rows)
        {
            InitializeComponent();
            _rows = new ObservableCollection<SalaryQrPrintRow>(rows ?? Array.Empty<SalaryQrPrintRow>());
            PeopleList.ItemsSource = _rows;
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PeopleList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsSelected = true;
            PeopleList.Items.Refresh();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsSelected = false;
            PeopleList.Items.Refresh();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            SelectedRows = _rows.Where(r => r.IsSelected).ToList();
            if (SelectedRows.Count == 0)
            {
                MessageBox.Show(
                    TryL("FinQrPrintEmpty") ?? "Select at least one person.",
                    TryL("FinQrPrintTitle") ?? "QR",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

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
