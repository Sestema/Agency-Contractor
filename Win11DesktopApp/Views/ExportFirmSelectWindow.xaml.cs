using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public class FirmExportItem : INotifyPropertyChanged
    {
        public string FirmName { get; set; } = string.Empty;
        public string AgencyName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnChanged(nameof(IsSelected)); }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnChanged(nameof(IsVisible)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AgencyGroupItem : INotifyPropertyChanged
    {
        public string AgencyName { get; set; } = string.Empty;
        public ObservableCollection<FirmExportItem> Firms { get; } = new();

        private bool? _isSelected = true;
        public bool? IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnChanged(nameof(IsSelected)); }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnChanged(nameof(IsExpanded)); }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnChanged(nameof(IsVisible)); }
        }

        public int FirmCount => Firms.Count;
        public int EmployeeCount => Firms.Sum(f => f.EmployeeCount);
        public int SelectedFirmCount => Firms.Count(f => f.IsSelected);

        public void RaiseCounts()
        {
            OnChanged(nameof(FirmCount));
            OnChanged(nameof(EmployeeCount));
            OnChanged(nameof(SelectedFirmCount));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class ExportFirmSelectWindow : Window
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly List<FirmExportItem> _allItems = new();
        private readonly ObservableCollection<AgencyGroupItem> _groups = new();
        private bool _syncingSelectAllState;
        private bool _cascading;

        public HashSet<string> SelectedFirms { get; private set; } = new();

        public ExportFirmSelectWindow(List<(string firmName, int count, string agencyName)> firms, AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            InitializeComponent();
            RestoreWindowSize();
            Closing += (_, _) => SaveWindowSize();

            var noAgency = TryL("FinExportNoAgency") ?? "Без агенції";

            foreach (var (firmName, count, agencyName) in firms)
            {
                var item = new FirmExportItem
                {
                    FirmName = firmName,
                    EmployeeCount = count,
                    AgencyName = string.IsNullOrWhiteSpace(agencyName) ? noAgency : agencyName,
                    IsSelected = true
                };
                item.PropertyChanged += Item_PropertyChanged;
                _allItems.Add(item);
            }

            foreach (var group in _allItems
                .GroupBy(i => NormalizeAgencyKey(i.AgencyName), StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                // Same agency typed slightly differently across firms (trailing spaces, casing,
                // double spaces) must collapse into ONE group. Use a normalized key for grouping
                // but show a clean representative name in the header.
                var displayName = group.First().AgencyName.Trim();
                var groupItem = new AgencyGroupItem { AgencyName = displayName, IsExpanded = false };
                foreach (var firm in group.OrderBy(f => f.FirmName, StringComparer.CurrentCultureIgnoreCase))
                    groupItem.Firms.Add(firm);
                _groups.Add(groupItem);
            }

            GroupList.ItemsSource = _groups;
            RecomputeAllGroupStates();
            RefreshSelectionState();
            UpdateSearchPlaceholder();
        }

        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncingSelectAllState) return;
            SetAllSelection(true);
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_syncingSelectAllState) return;
            SetAllSelection(false);
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e) => SetAllSelection(false);

        private void SetAllSelection(bool value)
        {
            _cascading = true;
            try
            {
                foreach (var item in _allItems) item.IsSelected = value;
            }
            finally { _cascading = false; }

            RecomputeAllGroupStates();
            RefreshSelectionState();
        }

        // User toggled an agency checkbox: cascade to its firms (respecting the current search filter).
        private void GroupCheck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not AgencyGroupItem group) return;

            var target = group.IsSelected != true;

            _cascading = true;
            try
            {
                var firms = group.Firms.Where(f => f.IsVisible).ToList();
                if (firms.Count == 0) firms = group.Firms.ToList();
                foreach (var firm in firms) firm.IsSelected = target;
            }
            finally { _cascading = false; }

            UpdateGroupState(group);
            group.RaiseCounts();
            RefreshSelectionState();
        }

        private void ToggleGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is AgencyGroupItem group)
                group.IsExpanded = !group.IsExpanded;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter((SearchBox.Text ?? string.Empty).Trim());
            UpdateSearchPlaceholder();
        }

        private void ApplyFilter(string query)
        {
            var hasQuery = !string.IsNullOrWhiteSpace(query);

            foreach (var group in _groups)
            {
                var agencyMatch = hasQuery
                    && group.AgencyName.Contains(query, StringComparison.CurrentCultureIgnoreCase);

                var visibleFirms = 0;
                foreach (var firm in group.Firms)
                {
                    var firmMatch = !hasQuery
                        || agencyMatch
                        || firm.FirmName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                    firm.IsVisible = firmMatch;
                    if (firmMatch) visibleFirms++;
                }

                group.IsVisible = !hasQuery || agencyMatch || visibleFirms > 0;
                group.IsExpanded = hasQuery && group.IsVisible;
            }
        }

        private void UpdateSearchPlaceholder()
        {
            if (SearchPlaceholder == null || SearchBox == null) return;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            SelectedFirms = _allItems.Where(i => i.IsSelected).Select(i => i.FirmName).ToHashSet();

            if (SelectedFirms.Count == 0)
            {
                var msg = TryL("FinExportNoFirms") ?? "Select at least one firm";
                var title = TryL("TitleWarning") ?? "Warning";
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // Canonical agency key: trims edges and collapses inner whitespace so visually identical
        // agency names (with stray spaces) group together. Case is handled by the comparer.
        private static string NormalizeAgencyKey(string value)
            => System.Text.RegularExpressions.Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");

        private void RestoreWindowSize()
        {
            var settings = _appSettingsService.Settings;

            if (settings.ExportFirmSelectWindowWidth >= MinWidth)
                Width = settings.ExportFirmSelectWindowWidth;

            if (settings.ExportFirmSelectWindowHeight >= MinHeight)
                Height = settings.ExportFirmSelectWindowHeight;
        }

        private async void SaveWindowSize()
        {
            try
            {
                var settings = _appSettingsService.Settings;
                var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;

                settings.ExportFirmSelectWindowWidth = bounds.Width;
                settings.ExportFirmSelectWindowHeight = bounds.Height;

                await _appSettingsService.SaveSettingsImmediate();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ExportFirmSelectWindow.SaveWindowSize", ex);
            }
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FirmExportItem.IsSelected)) return;
            if (_cascading) return;

            if (sender is FirmExportItem firm)
            {
                var group = _groups.FirstOrDefault(g => g.Firms.Contains(firm));
                if (group != null)
                {
                    UpdateGroupState(group);
                    group.RaiseCounts();
                }
            }

            RefreshSelectionState();
        }

        private void UpdateGroupState(AgencyGroupItem group)
        {
            var total = group.Firms.Count;
            var selected = group.Firms.Count(f => f.IsSelected);
            group.IsSelected = selected == 0 ? false : (selected == total ? true : (bool?)null);
        }

        private void RecomputeAllGroupStates()
        {
            foreach (var group in _groups)
            {
                UpdateGroupState(group);
                group.RaiseCounts();
            }
        }

        private void RefreshSelectionState()
        {
            if (TotalFirmsText == null ||
                SelectedFirmsText == null ||
                SelectedRowsText == null ||
                SelectionHintText == null ||
                ExportButton == null ||
                SelectAllBox == null)
            {
                return;
            }

            var totalFirms = _allItems.Count;
            var selectedItems = _allItems.Where(i => i.IsSelected).ToList();
            var selectedFirms = selectedItems.Count;
            var selectedRows = selectedItems.Sum(i => i.EmployeeCount);

            TotalFirmsText.Text = totalFirms.ToString();
            SelectedFirmsText.Text = selectedFirms.ToString();
            SelectedRowsText.Text = selectedRows.ToString();

            SelectionHintText.Text = string.Format(
                TryL("FinExportSelectionHint") ?? "{0} / {1}",
                selectedFirms,
                totalFirms);

            ExportButton.IsEnabled = selectedFirms > 0;

            _syncingSelectAllState = true;
            try
            {
                SelectAllBox.IsThreeState = true;
                if (selectedFirms == 0)
                    SelectAllBox.IsChecked = false;
                else if (selectedFirms == totalFirms)
                    SelectAllBox.IsChecked = true;
                else
                    SelectAllBox.IsChecked = null;
            }
            finally
            {
                _syncingSelectAllState = false;
            }
        }
    }
}
