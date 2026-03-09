using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class CategoryItem
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Color { get; set; } = "#1976D2";
        public string Icon { get; set; } = "\uE7C3";
    }

    public class ActivityLogViewModel : ViewModelBase
    {
        private readonly ActivityLogService _logService;
        private List<ActivityLogEntry> _allEntries = new();

        private static readonly Dictionary<string, (string colorHex, string icon, string resKey)> CategoryMeta = new()
        {
            ["Employee"]  = ("#43A047", "\uE77B", "ActLogCatEmployee"),
            ["Salary"]    = ("#1976D2", "\uE8CB", "ActLogCatSalary"),
            ["Advance"]   = ("#E65100", "\uE8C7", "ActLogCatAdvance"),
            ["Archive"]   = ("#C62828", "\uE7B8", "ActLogCatArchive"),
            ["Document"]  = ("#7B1FA2", "\uE8A5", "ActLogCatDocument"),
            ["Export"]    = ("#00838F", "\uE8A1", "ActLogCatExport"),
            ["Company"]   = ("#4E342E", "\uE731", "ActLogCatCompany"),
            ["Template"]  = ("#F57F17", "\uE8A5", "ActLogCatTemplate"),
            ["Settings"]  = ("#546E7A", "\uE713", "ActLogCatSettings"),
            ["Candidate"] = ("#FF9800", "\uE77B", "ActLogCatCandidate"),
        };

        public ICommand GoBackCommand { get; }
        public ICommand ClearFilterCommand { get; }
        public ICommand FilterByCategoryCommand { get; }
        public ICommand OpenEmployeeCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ClearAllCommand { get; }

        public ObservableCollection<ActivityLogEntry> Entries { get; } = new();
        public ICollectionView GroupedEntries { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
        }

        private string _selectedCategory = "";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set { if (SetProperty(ref _selectedCategory, value)) ApplyFilter(); }
        }

        private string _selectedFirm = "";
        public string SelectedFirm
        {
            get => _selectedFirm;
            set { if (SetProperty(ref _selectedFirm, value)) ApplyFilter(); }
        }

        private DateTime? _dateFrom;
        public DateTime? DateFrom
        {
            get => _dateFrom;
            set { if (SetProperty(ref _dateFrom, value)) ApplyFilter(); }
        }

        private DateTime? _dateTo;
        public DateTime? DateTo
        {
            get => _dateTo;
            set { if (SetProperty(ref _dateTo, value)) ApplyFilter(); }
        }

        public ObservableCollection<CategoryItem> Categories { get; } = new();
        public ObservableCollection<string> FirmNames { get; } = new();

        private int _totalCount;
        public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

        private int _filteredCount;
        public int FilteredCount { get => _filteredCount; set => SetProperty(ref _filteredCount, value); }

        private bool _hasEntries;
        public bool HasEntries { get => _hasEntries; set => SetProperty(ref _hasEntries, value); }

        private bool _isEmployeeDetailsOpen;
        public bool IsEmployeeDetailsOpen
        {
            get => _isEmployeeDetailsOpen;
            set => SetProperty(ref _isEmployeeDetailsOpen, value);
        }

        private EmployeeDetailsViewModel? _employeeDetailsVm;
        public EmployeeDetailsViewModel? EmployeeDetailsVm
        {
            get => _employeeDetailsVm;
            set => SetProperty(ref _employeeDetailsVm, value);
        }

        public ActivityLogViewModel()
        {
            _logService = App.ActivityLogService;

            GroupedEntries = CollectionViewSource.GetDefaultView(Entries);
            GroupedEntries.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActivityLogEntry.Timestamp),
                new DateGroupConverter()));

            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));
            ClearFilterCommand = new RelayCommand(o =>
            {
                SearchText = "";
                SelectedCategory = "";
                SelectedFirm = "";
                DateFrom = null;
                DateTo = null;
            });
            FilterByCategoryCommand = new RelayCommand(o =>
            {
                if (o is string cat)
                    SelectedCategory = SelectedCategory == cat ? "" : cat;
            });
            OpenEmployeeCommand = new RelayCommand(OpenEmployeeProfile, CanOpenEmployeeProfile);
            ExportCommand = new RelayCommand(o => ExportToExcel());
            ClearAllCommand = new RelayCommand(o => ClearAllHistory());

            LoadEntries();
        }

        public static string GetCategoryColor(string category)
        {
            return CategoryMeta.TryGetValue(category, out var meta) ? meta.colorHex : "#757575";
        }

        public static string GetCategoryIcon(string category)
        {
            return CategoryMeta.TryGetValue(category, out var meta) ? meta.icon : "\uE7C3";
        }

        private bool CanOpenEmployeeProfile(object? o)
        {
            return o is ActivityLogEntry entry && !string.IsNullOrEmpty(entry.EmployeeName);
        }

        private void OpenEmployeeProfile(object? o)
        {
            if (o is not ActivityLogEntry entry) return;

            var folder = entry.EmployeeFolder;
            var firmName = entry.FirmName;

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                var resolved = ResolveEmployeeFolder(entry.FirmName, entry.EmployeeName);
                if (resolved == null) return;
                folder = resolved.Value.folder;
                firmName = resolved.Value.firm;
            }

            CleanupDetailsVm();
            EmployeeDetailsVm = new EmployeeDetailsViewModel(firmName, folder);
            EmployeeDetailsVm.RequestClose += OnDetailsClose;
            EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
            IsEmployeeDetailsOpen = true;
        }

        private void CleanupDetailsVm()
        {
            if (EmployeeDetailsVm != null)
            {
                EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
            }
        }

        private void OnDetailsClose() => IsEmployeeDetailsOpen = false;
        private void OnDetailsDataChanged() => LoadEntries();

        private (string folder, string firm)? ResolveEmployeeFolder(string firmName, string employeeName)
        {
            if (string.IsNullOrEmpty(employeeName)) return null;

            try
            {
                if (!string.IsNullOrEmpty(firmName))
                {
                    var employees = App.EmployeeService.GetEmployeesForFirm(firmName);
                    var match = employees.FirstOrDefault(e => e.FullName == employeeName);
                    if (match != null && Directory.Exists(match.EmployeeFolder))
                        return (match.EmployeeFolder, firmName);
                }

                var archived = App.EmployeeService.GetArchivedEmployees();
                var archiveMatch = archived.FirstOrDefault(a => a.FullName == employeeName
                    && (string.IsNullOrEmpty(firmName) || a.FirmName == firmName));
                if (archiveMatch != null && Directory.Exists(archiveMatch.EmployeeFolder))
                    return (archiveMatch.EmployeeFolder, archiveMatch.FirmName);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ActivityLogViewModel.ResolveEmployeeFolder", ex.Message);
            }

            return null;
        }

        private void LoadEntries()
        {
            _allEntries = _logService.GetAll();
            TotalCount = _allEntries.Count;

            var cats = _allEntries.Select(e => e.Category)
                .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            Categories.Clear();
            foreach (var c in cats)
            {
                var hasMeta = CategoryMeta.TryGetValue(c, out var m);
                Categories.Add(new CategoryItem
                {
                    Key = c,
                    DisplayName = hasMeta ? Res(m.resKey) : c,
                    Color = hasMeta ? m.colorHex : "#757575",
                    Icon = hasMeta ? m.icon : "\uE7C3"
                });
            }

            var firms = _allEntries.Select(e => e.FirmName)
                .Where(f => !string.IsNullOrEmpty(f)).Distinct().OrderBy(f => f).ToList();
            FirmNames.Clear();
            FirmNames.Add("");
            foreach (var f in firms) FirmNames.Add(f);

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            Entries.Clear();
            var query = _searchText?.Trim() ?? string.Empty;

            foreach (var entry in _allEntries)
            {
                if (!string.IsNullOrEmpty(_selectedCategory) &&
                    !string.Equals(entry.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(_selectedFirm) &&
                    !string.Equals(entry.FirmName, _selectedFirm, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_dateFrom.HasValue || _dateTo.HasValue)
                {
                    if (DateTime.TryParse(entry.Timestamp, out var entryDate))
                    {
                        if (_dateFrom.HasValue && entryDate.Date < _dateFrom.Value.Date) continue;
                        if (_dateTo.HasValue && entryDate.Date > _dateTo.Value.Date) continue;
                    }
                }

                if (!string.IsNullOrEmpty(query))
                {
                    if (!(entry.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(entry.EmployeeName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(entry.FirmName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(entry.ActionType?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                        continue;
                }

                Entries.Add(entry);
            }

            FilteredCount = Entries.Count;
            HasEntries = Entries.Count > 0;
        }

        private void ExportToExcel()
        {
            var toExport = Entries.ToList();
            if (toExport.Count == 0) return;

            var dlg = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"ActivityLog_{DateTime.Now:yyyy-MM-dd}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet(Res("BtnActivityLog"));

                ws.Cell(1, 1).Value = Res("ActLogColDate");
                ws.Cell(1, 2).Value = Res("ActLogColDescription");
                ws.Cell(1, 3).Value = Res("ActLogColCategory");
                ws.Cell(1, 4).Value = Res("ActLogColFirm");
                ws.Cell(1, 5).Value = Res("ActLogColEmployee");
                ws.Cell(1, 6).Value = Res("ActLogColOld");
                ws.Cell(1, 7).Value = Res("ActLogColNew");

                var headerRange = ws.Range(1, 1, 1, 7);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F2FD");

                for (int i = 0; i < toExport.Count; i++)
                {
                    var e = toExport[i];
                    int r = i + 2;
                    ws.Cell(r, 1).Value = e.Timestamp;
                    ws.Cell(r, 2).Value = e.Description;
                    ws.Cell(r, 3).Value = e.Category;
                    ws.Cell(r, 4).Value = e.FirmName;
                    ws.Cell(r, 5).Value = e.EmployeeName;
                    ws.Cell(r, 6).Value = e.OldValue;
                    ws.Cell(r, 7).Value = e.NewValue;
                }

                ws.Columns().AdjustToContents();
                wb.SaveAs(dlg.FileName);

                MessageBox.Show(Res("ActLogExported"), Res("TitleSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ActivityLogViewModel.ExportToExcel", ex);
            }
        }

        private void ClearAllHistory()
        {
            var result = MessageBox.Show(Res("ActLogClearConfirm"), Res("TitleWarning"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _logService.ClearAll();
            LoadEntries();
        }
    }

    public class DateGroupConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string ts && DateTime.TryParse(ts, out var dt))
                return dt.ToString("dd.MM.yyyy");
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class EqualityConverter : IMultiValueConverter
    {
        public static readonly EqualityConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values.Length < 2) return false;
            var a = values[0]?.ToString() ?? "";
            var b = values[1]?.ToString() ?? "";
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.Ordinal);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}
