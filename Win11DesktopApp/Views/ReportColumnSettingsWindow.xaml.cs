using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public class ReportColumnDisplayItem : INotifyPropertyChanged
    {
        private bool _isVisible;

        public string Key { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool CanToggle { get; init; } = true;
        public double Width { get; set; }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                var target = CanToggle ? value : true;
                if (_isVisible == target)
                    return;

                _isVisible = target;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ReportColumnSettingsWindow : Window
    {
        private readonly ObservableCollection<ReportColumnDisplayItem> _items = new();

        public ReportColumnSettingsWindow()
        {
            InitializeComponent();
            ColumnsList.DataContext = _items;
            LoadItems();
        }

        private void LoadItems()
        {
            _items.Clear();
            foreach (var col in ReportViewModel.GetEffectiveEmployeeColumns())
            {
                _items.Add(new ReportColumnDisplayItem
                {
                    Key = col.Key,
                    DisplayName = TryL(ReportViewModel.GetEmployeeColumnHeaderResourceKey(col.Key)) ?? col.Key,
                    CanToggle = !string.Equals(col.Key, "name", System.StringComparison.OrdinalIgnoreCase),
                    IsVisible = string.Equals(col.Key, "name", System.StringComparison.OrdinalIgnoreCase) || col.IsVisible,
                    Width = col.Width
                });
            }
        }

        private void Move(string key, int delta)
        {
            var index = _items.ToList().FindIndex(x => x.Key == key);
            if (index < 0)
                return;

            var targetIndex = index + delta;
            if (targetIndex < 0 || targetIndex >= _items.Count)
                return;

            _items.Move(index, targetIndex);
            ColumnsList.SelectedIndex = targetIndex;
        }

        private void SaveLayout()
        {
            var layout = _items.Select((item, index) => new AppSettingsService.ReportColumnSetting
            {
                Key = item.Key,
                IsVisible = string.Equals(item.Key, "name", System.StringComparison.OrdinalIgnoreCase) || item.IsVisible,
                DisplayIndex = index,
                Width = item.Width
            });

            ReportViewModel.SaveEmployeeColumnLayout(layout);
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string key)
                Move(key, -1);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string key)
                Move(key, 1);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ReportViewModel.ResetEmployeeColumnsToDefaults();
            LoadItems();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveLayout();
            DialogResult = true;
            Close();
        }

        private static string? TryL(string key)
        {
            try
            {
                return Application.Current.FindResource(key) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
