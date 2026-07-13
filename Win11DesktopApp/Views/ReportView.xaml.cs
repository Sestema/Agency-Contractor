using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class ReportView : UserControl
    {
        private static bool _datePickerWarmupCompleted;
        private bool _isApplyingLayout;
        private readonly Dictionary<DataGrid, List<(DataGridColumn Column, EventHandler Handler)>> _columnWidthSubscriptions = new();
        private readonly DispatcherTimer _widthSyncDebounceTimer = new() { Interval = System.TimeSpan.FromMilliseconds(220) };
        private DataGrid? _pendingWidthSyncGrid;

        private bool _syncingEmployersCheck;
        private bool _syncingAgenciesCheck;
        private ReportViewModel? _reportVm;

        public ReportView()
        {
            InitializeComponent();
            Language = XmlLanguage.GetLanguage(Thread.CurrentThread.CurrentUICulture.IetfLanguageTag);
            _widthSyncDebounceTimer.Tick += WidthSyncDebounceTimer_Tick;
            Loaded += ReportView_Loaded;
            DataContextChanged += ReportView_DataContextChanged;
        }

        private void ReportView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_reportVm != null)
                _reportVm.PropertyChanged -= ReportVm_PropertyChanged;

            _reportVm = e.NewValue as ReportViewModel;
            if (_reportVm != null)
            {
                _reportVm.PropertyChanged += ReportVm_PropertyChanged;
                SyncAllEmployersCheckState();
                SyncAllAgenciesCheckState();
            }
        }

        private void ReportVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ReportViewModel.AllCompaniesSelected))
                SyncAllEmployersCheckState();
            else if (e.PropertyName == nameof(ReportViewModel.AllAgenciesSelected))
                SyncAllAgenciesCheckState();
        }

        private void AllEmployersCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_syncingEmployersCheck || _reportVm == null) return;

            var visible = _reportVm.CompanyFilters.Where(f => f.IsVisible).ToList();
            if (visible.Count == 0) return;

            var allChecked = visible.All(f => f.IsChecked);
            _reportVm.SetAllCompaniesSelection(!allChecked);
        }

        private void AllAgenciesCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_syncingAgenciesCheck || _reportVm == null) return;

            var visible = _reportVm.AgencyFilters.Where(f => f.IsVisible).ToList();
            if (visible.Count == 0) return;

            var allChecked = visible.All(f => f.IsChecked);
            _reportVm.SetAllAgenciesSelection(!allChecked);
        }

        private void SyncAllEmployersCheckState()
        {
            if (AllEmployersCheck == null || _reportVm == null) return;

            _syncingEmployersCheck = true;
            try
            {
                AllEmployersCheck.IsChecked = _reportVm.AllCompaniesSelected;
            }
            finally
            {
                _syncingEmployersCheck = false;
            }
        }

        private void SyncAllAgenciesCheckState()
        {
            if (AllAgenciesCheck == null || _reportVm == null) return;

            _syncingAgenciesCheck = true;
            try
            {
                AllAgenciesCheck.IsChecked = _reportVm.AllAgenciesSelected;
            }
            finally
            {
                _syncingAgenciesCheck = false;
            }
        }

        private void FilterScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (FilterScrollViewer == null) return;

            var inner = FindInnerScrollViewer(e.OriginalSource as DependencyObject, FilterScrollViewer);
            if (inner != null && inner.ScrollableHeight > 0)
            {
                var delta = -e.Delta;
                var newOffset = inner.VerticalOffset + delta;

                if (newOffset >= 0 && newOffset <= inner.ScrollableHeight)
                {
                    inner.ScrollToVerticalOffset(newOffset);
                    e.Handled = true;
                    return;
                }

                if (newOffset < 0)
                {
                    inner.ScrollToVerticalOffset(0);
                    FilterScrollViewer.ScrollToVerticalOffset(FilterScrollViewer.VerticalOffset + newOffset);
                    e.Handled = true;
                    return;
                }

                var overflow = newOffset - inner.ScrollableHeight;
                inner.ScrollToVerticalOffset(inner.ScrollableHeight);
                FilterScrollViewer.ScrollToVerticalOffset(FilterScrollViewer.VerticalOffset + overflow);
                e.Handled = true;
                return;
            }

            FilterScrollViewer.ScrollToVerticalOffset(FilterScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private static ScrollViewer? FindInnerScrollViewer(DependencyObject? source, ScrollViewer outer)
        {
            while (source != null)
            {
                if (source is ScrollViewer sv && sv != outer)
                    return sv;
                source = GetParentObject(source);
            }

            return null;
        }

        private static DependencyObject? GetParentObject(DependencyObject? child)
        {
            if (child == null)
                return null;

            if (child is Visual)
                return VisualTreeHelper.GetParent(child);

            return LogicalTreeHelper.GetParent(child);
        }

        private void ReportView_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyDatePickerLanguage();
            WarmUpDatePickers();
        }

        private void ApplyDatePickerLanguage()
        {
            var xmlLanguage = XmlLanguage.GetLanguage(Thread.CurrentThread.CurrentUICulture.IetfLanguageTag);
            Language = xmlLanguage;
            DateFromPicker.Language = xmlLanguage;
            DateToPicker.Language = xmlLanguage;
        }

        private void WarmUpDatePickers()
        {
            if (_datePickerWarmupCompleted)
                return;

            _datePickerWarmupCompleted = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    ApplyDatePickerLanguage();
                    DateFromPicker.ApplyTemplate();
                    DateToPicker.ApplyTemplate();

                    // Force creation of calendar localization resources ahead of first real click.
                    var xmlLanguage = XmlLanguage.GetLanguage(Thread.CurrentThread.CurrentUICulture.IetfLanguageTag);
                    var warmupCalendar = new Calendar
                    {
                        Language = xmlLanguage
                    };
                    warmupCalendar.ApplyTemplate();
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("ReportView.WarmUpDatePickers", ex.Message);
                }
            }));
        }

        private void EmployeeRow_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is EmployeeReportRow emp
                && DataContext is ReportViewModel vm)
            {
                vm.OpenEmployeeCommand.Execute(emp);
                e.Handled = true;
            }
        }

        private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null) return;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private string? GetColumnKey(DataGridColumn col)
        {
            if (col is DataGridBoundColumn bound && bound.Binding is Binding binding)
            {
                var path = binding.Path?.Path;
                return path switch
                {
                    "FullName" => "name",
                    "EmployeeType" => "type",
                    "DocumentType" => "documentType",
                    "PassportNumber" => "passportNumber",
                    "VisaNumber" => "visaNumber",
                    "VisaAuthority" => "visaAuthority",
                    "WorkAddress" => "workAddress",
                    "HighestEducation" => "highestEducation",
                    "BirthDate" => "birthDate",
                    "RodneCislo" => "rodneCislo",
                    "Citizenship" => "citizenship",
                    "BirthCity" => "birthCity",
                    "BirthCountry" => "birthCountry",
                    "Gender" => "gender",
                    "AddressCz" => "addressCz",
                    "AddressAbroad" => "addressAbroad",
                    "PassportIssuedBy" => "passportIssuedBy",
                    "PositionCode" => "positionCode",
                    "Agency" => "agency",
                    "VisaStartDate" => "visaStartDate",
                    "StartDate" => "startDate",
                    "EndDate" => "endDate",
                    "Phone" => "phone",
                    "BankAccountNumber" => "bankAccount",
                    "BankName" => "bankName",
                    "Position" => "position",
                    _ => null
                };
            }

            if (col is DataGridTemplateColumn template)
            {
                if (string.Equals(template.SortMemberPath, "PassportExpiry", System.StringComparison.Ordinal))
                    return "passportExpiry";
                if (string.Equals(template.SortMemberPath, "VisaExpiry", System.StringComparison.Ordinal))
                    return "visaExpiry";
                if (string.Equals(template.SortMemberPath, "InsuranceExpiry", System.StringComparison.Ordinal))
                    return "insuranceExpiry";
            }

            return null;
        }

        private static IEnumerable<DataGrid> FindEmployeeGrids(DependencyObject root)
        {
            if (root is DataGrid grid && string.Equals(grid.Tag as string, "EmployeeReportGrid", System.StringComparison.Ordinal))
                yield return grid;

            var children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < children; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                foreach (var nested in FindEmployeeGrids(child))
                    yield return nested;
            }
        }

        private void ApplyColumnLayout(DataGrid grid)
        {
            if (DataContext is not ReportViewModel vm)
                return;

            var layout = vm.ReportColumnLayoutService.GetEffectiveEmployeeColumns();
            var layoutByKey = layout.ToDictionary(c => c.Key, System.StringComparer.OrdinalIgnoreCase);

            _isApplyingLayout = true;
            try
            {
                foreach (var col in grid.Columns)
                {
                    var key = GetColumnKey(col);
                    if (string.IsNullOrEmpty(key) || !layoutByKey.TryGetValue(key, out var setting))
                        continue;

                    col.Visibility = setting.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                    col.DisplayIndex = System.Math.Min(setting.DisplayIndex, grid.Columns.Count - 1);
                    col.Width = new DataGridLength(setting.Width);
                }
            }
            finally
            {
                _isApplyingLayout = false;
            }
        }

        private void CaptureColumnLayout(DataGrid grid)
        {
            if (_isApplyingLayout)
                return;

            var layout = new List<AppSettingsService.ReportColumnSetting>();
            foreach (var col in grid.Columns.OrderBy(c => c.DisplayIndex))
            {
                var key = GetColumnKey(col);
                if (string.IsNullOrEmpty(key))
                    continue;

                layout.Add(new AppSettingsService.ReportColumnSetting
                {
                    Key = key,
                    IsVisible = key == "name" || col.Visibility == Visibility.Visible,
                    DisplayIndex = col.DisplayIndex,
                    Width = col.ActualWidth > 0 ? col.ActualWidth : 120
                });
            }

            if (DataContext is ReportViewModel vm)
                vm.ReportColumnLayoutService.SaveEmployeeColumnLayout(layout);
        }

        private void SaveAndReapplyLayout(DataGrid grid)
        {
            if (_isApplyingLayout)
                return;

            CaptureColumnLayout(grid);
            ReapplyLayoutToAllEmployeeGrids();
        }

        private void ScheduleWidthSync(DataGrid grid)
        {
            _pendingWidthSyncGrid = grid;
            _widthSyncDebounceTimer.Stop();
            _widthSyncDebounceTimer.Start();
        }

        private void WidthSyncDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _widthSyncDebounceTimer.Stop();

            if (_pendingWidthSyncGrid != null)
            {
                SaveAndReapplyLayout(_pendingWidthSyncGrid);
                _pendingWidthSyncGrid = null;
            }
        }

        private void AttachColumnWidthHandlers(DataGrid grid)
        {
            if (_columnWidthSubscriptions.ContainsKey(grid))
                return;

            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            if (descriptor == null)
                return;

            var subscriptions = new List<(DataGridColumn Column, EventHandler Handler)>();
            foreach (var column in grid.Columns)
            {
                EventHandler handler = (_, _) =>
                {
                    if (_isApplyingLayout)
                        return;

                    ScheduleWidthSync(grid);
                };

                descriptor.AddValueChanged(column, handler);
                subscriptions.Add((column, handler));
            }

            _columnWidthSubscriptions[grid] = subscriptions;
        }

        private void DetachColumnWidthHandlers(DataGrid grid)
        {
            if (!_columnWidthSubscriptions.TryGetValue(grid, out var subscriptions))
                return;

            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            if (descriptor != null)
            {
                foreach (var subscription in subscriptions)
                    descriptor.RemoveValueChanged(subscription.Column, subscription.Handler);
            }

            _columnWidthSubscriptions.Remove(grid);
        }

        private void ReapplyLayoutToAllEmployeeGrids()
        {
            foreach (var grid in FindEmployeeGrids(this))
                ApplyColumnLayout(grid);
        }

        private void EmployeeGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                AttachColumnWidthHandlers(grid);
                ApplyColumnLayout(grid);
            }
        }

        private void EmployeeGrid_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                if (ReferenceEquals(_pendingWidthSyncGrid, grid))
                {
                    _widthSyncDebounceTimer.Stop();
                    _pendingWidthSyncGrid = null;
                }
                CaptureColumnLayout(grid);
                DetachColumnWidthHandlers(grid);
            }
        }

        private void EmployeeGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            if (sender is DataGrid grid)
                SaveAndReapplyLayout(grid);
        }

        private void ColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ReportViewModel vm)
                return;

            var window = new ReportColumnSettingsWindow(vm.ReportColumnLayoutService)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() == true)
                ReapplyLayoutToAllEmployeeGrids();
        }

        private void ResetColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ReportViewModel vm)
                vm.ReportColumnLayoutService.ResetEmployeeColumnsToDefaults();
            ReapplyLayoutToAllEmployeeGrids();
        }
    }
}
