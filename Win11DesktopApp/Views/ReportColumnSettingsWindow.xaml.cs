using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Win11DesktopApp.Services;

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
        private readonly ICollectionView _itemsView;
        private readonly ReportColumnLayoutService _reportColumnLayoutService;
        private string _searchQuery = string.Empty;

        public ReportColumnSettingsWindow(ReportColumnLayoutService reportColumnLayoutService)
        {
            _reportColumnLayoutService = reportColumnLayoutService ?? throw new ArgumentNullException(nameof(reportColumnLayoutService));
            InitializeComponent();
            _itemsView = CollectionViewSource.GetDefaultView(_items);
            _itemsView.Filter = MatchesSearch;
            ColumnsList.ItemsSource = _itemsView;
            LoadItems();
        }

        private void LoadItems()
        {
            _items.Clear();
            foreach (var col in _reportColumnLayoutService.GetEffectiveEmployeeColumns())
            {
                _items.Add(new ReportColumnDisplayItem
                {
                    Key = col.Key,
                    DisplayName = TryL(_reportColumnLayoutService.GetEmployeeColumnHeaderResourceKey(col.Key)) ?? col.Key,
                    CanToggle = !string.Equals(col.Key, "name", StringComparison.OrdinalIgnoreCase),
                    IsVisible = string.Equals(col.Key, "name", StringComparison.OrdinalIgnoreCase) || col.IsVisible,
                    Width = col.Width
                });
            }

            _itemsView.Refresh();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchQuery = (SearchBox.Text ?? string.Empty).Trim();
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            _itemsView.Refresh();
        }

        private bool MatchesSearch(object obj)
        {
            if (obj is not ReportColumnDisplayItem item)
                return false;
            if (string.IsNullOrEmpty(_searchQuery))
                return true;

            return ContainsQuery(item.DisplayName, _searchQuery)
                || ContainsQuery(item.Key, _searchQuery);
        }

        private static bool ContainsQuery(string? text, string query)
        {
            return !string.IsNullOrEmpty(text)
                && text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
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
            ColumnsList.SelectedItem = _items[targetIndex];
        }

        private void SaveLayout()
        {
            var layout = _items.Select((item, index) => new AppSettingsService.ReportColumnSetting
            {
                Key = item.Key,
                IsVisible = string.Equals(item.Key, "name", StringComparison.OrdinalIgnoreCase) || item.IsVisible,
                DisplayIndex = index,
                Width = item.Width
            });

            _reportColumnLayoutService.SaveEmployeeColumnLayout(layout);
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
            _reportColumnLayoutService.ResetEmployeeColumnsToDefaults();
            SearchBox.Text = string.Empty;
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
