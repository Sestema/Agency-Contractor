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
        private bool _syncingSelectAllState;

        public HashSet<string> SelectedFirms { get; private set; } = new();

        public ExportFirmSelectWindow(List<(string firmName, int count)> firms)
        {
            InitializeComponent();
            RestoreWindowSize();
            Closing += (_, _) => SaveWindowSize();

            foreach (var (firmName, count) in firms)
            {
                var item = new FirmExportItem { FirmName = firmName, EmployeeCount = count, IsSelected = true };
                item.PropertyChanged += Item_PropertyChanged;
                _items.Add(item);
            }

            FirmList.ItemsSource = _items;
            RefreshSelectionState();
        }

        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncingSelectAllState) return;
            foreach (var item in _items) item.IsSelected = true;
            RefreshSelectionState();
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_syncingSelectAllState) return;
            foreach (var item in _items) item.IsSelected = false;
            RefreshSelectionState();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsSelected = false;
            RefreshSelectionState();
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

        private void RestoreWindowSize()
        {
            var settings = App.AppSettingsService?.Settings;
            if (settings == null) return;

            if (settings.ExportFirmSelectWindowWidth >= MinWidth)
                Width = settings.ExportFirmSelectWindowWidth;

            if (settings.ExportFirmSelectWindowHeight >= MinHeight)
                Height = settings.ExportFirmSelectWindowHeight;
        }

        private async void SaveWindowSize()
        {
            try
            {
                var appSettings = App.AppSettingsService;
                if (appSettings == null) return;

                var settings = appSettings.Settings;
                var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;

                settings.ExportFirmSelectWindowWidth = bounds.Width;
                settings.ExportFirmSelectWindowHeight = bounds.Height;

                await appSettings.SaveSettingsImmediate();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ExportFirmSelectWindow.SaveWindowSize", ex);
            }
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FirmExportItem.IsSelected))
                RefreshSelectionState();
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

            var totalFirms = _items.Count;
            var selectedItems = _items.Where(i => i.IsSelected).ToList();
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
                if (selectedFirms == 0)
                {
                    SelectAllBox.IsThreeState = true;
                    SelectAllBox.IsChecked = false;
                }
                else if (selectedFirms == totalFirms)
                {
                    SelectAllBox.IsThreeState = true;
                    SelectAllBox.IsChecked = true;
                }
                else
                {
                    SelectAllBox.IsThreeState = true;
                    SelectAllBox.IsChecked = null;
                }
            }
            finally
            {
                _syncingSelectAllState = false;
            }
        }
    }
}
