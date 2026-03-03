using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Win11DesktopApp.Views
{
    public class FirmExportItem : INotifyPropertyChanged
    {
        public string FirmName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ExportFirmSelectWindow : Window
    {
        private readonly ObservableCollection<FirmExportItem> _items = new();

        public HashSet<string> SelectedFirms { get; private set; } = new();

        public ExportFirmSelectWindow(List<(string firmName, int count)> firms)
        {
            InitializeComponent();

            foreach (var (firmName, count) in firms)
                _items.Add(new FirmExportItem { FirmName = firmName, EmployeeCount = count, IsSelected = true });

            FirmList.ItemsSource = _items;
        }

        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsSelected = true;
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsSelected = false;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            SelectedFirms = _items.Where(i => i.IsSelected).Select(i => i.FirmName).ToHashSet();

            if (SelectedFirms.Count == 0)
            {
                var msg = TryL("FinExportNoFirms") ?? "Select at least one firm";
                MessageBox.Show(msg, "", MessageBoxButton.OK, MessageBoxImage.Warning);
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
