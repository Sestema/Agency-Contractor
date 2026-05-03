using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly NavigationService _navigationService;
        private readonly ActivityLogService _logService;
        private readonly RecentlyDeletedService _recentlyDeletedService;
        private readonly EmployeeService _employeeService;
        private readonly ProfileAuthService _profileAuthService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private readonly CurrentProfileService _currentProfileService;
        private List<ActivityLogEntry> _allEntries = new();
        private HashSet<string> _undoableArchiveOperationIds = new(StringComparer.OrdinalIgnoreCase);
        private int _loadVersion;

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
        public ICommand UndoCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ClearAllCommand { get; }

        private readonly BulkObservableCollection<ActivityLogEntry> _entries = new();
        public ObservableCollection<ActivityLogEntry> Entries => _entries;
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

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

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

        public ActivityLogViewModel(
            ActivityLogService? activityLogService = null,
            NavigationService? navigationService = null,
            RecentlyDeletedService? recentlyDeletedService = null,
            EmployeeService? employeeService = null,
            ProfileAuthService? profileAuthService = null,
            EmployeeDetailsViewModelFactory? employeeDetailsViewModelFactory = null,
            CurrentProfileService? currentProfileService = null)
        {
            _logService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _recentlyDeletedService = recentlyDeletedService ?? throw new InvalidOperationException("RecentlyDeletedService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _profileAuthService = profileAuthService ?? throw new InvalidOperationException("ProfileAuthService is not initialized.");
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new InvalidOperationException("EmployeeDetailsViewModelFactory is not initialized.");
            _currentProfileService = currentProfileService ?? throw new InvalidOperationException("CurrentProfileService is not initialized.");

            GroupedEntries = CollectionViewSource.GetDefaultView(Entries);
            GroupedEntries.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActivityLogEntry.Timestamp),
                new DateGroupConverter()));

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());
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
            UndoCommand = new AsyncRelayCommand(UndoArchiveAsync, CanUndoArchive);
            ExportCommand = new RelayCommand(o => ExportToExcel());
            ClearAllCommand = new RelayCommand(o => ClearAllHistory());

            _ = LoadEntriesAsync();
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
            var isReadOnlyPreview = false;

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                var recentlyDeleted = _recentlyDeletedService.FindItem(entry.EmployeeFolder, entry.FirmName, entry.EmployeeName);
                if (recentlyDeleted != null && Directory.Exists(recentlyDeleted.DeletedEmployeeFolder))
                {
                    folder = recentlyDeleted.DeletedEmployeeFolder;
                    firmName = recentlyDeleted.FirmName;
                    isReadOnlyPreview = true;
                }
                else
                {
                    var resolved = ResolveEmployeeFolder(entry.FirmName, entry.EmployeeName);
                    if (resolved == null)
                    {
                        MessageBox.Show(
                            Res("MsgEmployeeProfileMissing"),
                            Res("TitleWarning"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    folder = resolved.Value.folder;
                    firmName = resolved.Value.firm;
                }
            }

            CleanupDetailsVm();
            EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(
                firmName,
                folder,
                isReadOnlyMode: isReadOnlyPreview);
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
        private void OnDetailsDataChanged() => _ = LoadEntriesAsync();

        private (string folder, string firm)? ResolveEmployeeFolder(string firmName, string employeeName)
        {
            if (string.IsNullOrEmpty(employeeName)) return null;

            try
            {
                if (!string.IsNullOrEmpty(firmName))
                {
                    var employees = _employeeService.GetEmployeesForFirm(firmName);
                    var match = employees.FirstOrDefault(e => e.FullName == employeeName);
                    if (match != null && Directory.Exists(match.EmployeeFolder))
                        return (match.EmployeeFolder, firmName);
                }

                var archived = _employeeService.GetArchivedEmployees();
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

        private async Task LoadEntriesAsync()
        {
            var version = ++_loadVersion;
            IsLoading = true;

            try
            {
                var snapshot = await Task.Run(() =>
                {
                    var entries = _logService.GetAll();
                    var undoableArchiveOperationIds = _employeeService.LoadArchiveLog()
                        .Where(entry =>
                            string.Equals(entry.Action, "Archived", StringComparison.OrdinalIgnoreCase)
                            && !entry.IsReverted
                            && !string.IsNullOrWhiteSpace(entry.OperationId))
                        .Select(entry => entry.OperationId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var categories = entries.Select(e => e.Category)
                        .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
                    var firms = entries.Select(e => e.FirmName)
                        .Where(f => !string.IsNullOrEmpty(f)).Distinct().OrderBy(f => f).ToList();

                    return new ActivityLogSnapshot(entries, undoableArchiveOperationIds, categories, firms);
                });

                if (version != _loadVersion)
                    return;

                _allEntries = snapshot.Entries;
                _undoableArchiveOperationIds = snapshot.UndoableArchiveOperationIds;
                TotalCount = _allEntries.Count;

                Categories.Clear();
                foreach (var c in snapshot.Categories)
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

                FirmNames.Clear();
                FirmNames.Add("");
                foreach (var f in snapshot.Firms) FirmNames.Add(f);

                ApplyFilter();
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ActivityLogViewModel.LoadEntriesAsync", ex);
            }
            finally
            {
                if (version == _loadVersion)
                    IsLoading = false;
            }
        }

        private void ApplyFilter()
        {
            var query = _searchText?.Trim() ?? string.Empty;
            var filtered = new List<ActivityLogEntry>();

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
                        && !(entry.Details?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(entry.ActorName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(entry.EmployeeName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(entry.FirmName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        && !(entry.ActionType?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                        continue;
                }

                filtered.Add(entry);
            }

            _entries.ReplaceAll(filtered);
            FilteredCount = filtered.Count;
            HasEntries = filtered.Count > 0;
        }

        private bool CanUndoArchive(object? parameter)
        {
            return IsUndoEligible(parameter as ActivityLogEntry, _undoableArchiveOperationIds, DateTime.Now);
        }

        internal static bool IsUndoEligible(ActivityLogEntry? entry, ISet<string> undoableArchiveOperationIds, DateTime now)
        {
            if (entry == null)
                return false;

            if (!string.Equals(entry.ActionType, "EmployeeArchived", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(entry.RelatedOperationId))
                return false;

            if (!undoableArchiveOperationIds.Contains(entry.RelatedOperationId))
                return false;

            if (!DateTime.TryParse(entry.Timestamp, out var ts))
                return false;

            return now - ts <= TimeSpan.FromHours(24);
        }

        private async Task UndoArchiveAsync(object? parameter)
        {
            if (parameter is not ActivityLogEntry entry)
                return;

            if (!CanUndoArchive(entry))
            {
                MessageBox.Show(
                    Res("ActLogUndoUnavailable"),
                    Res("TitleWarning"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var currentProfile = _currentProfileService.CurrentProfile;
            if (currentProfile == null || string.IsNullOrWhiteSpace(currentProfile.ClientId))
            {
                MessageBox.Show(
                    Res("ConfirmPasswordNoProfile"),
                    Res("ConfirmPasswordTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var passwordWindow = new Views.ConfirmPasswordWindow
            {
                Owner = Application.Current?.MainWindow
            };
            if (passwordWindow.ShowDialog() != true || !passwordWindow.IsConfirmed)
                return;

            var authResult = await _profileAuthService.AuthenticateAsync(currentProfile.ClientId, passwordWindow.EnteredPassword);
            if (!authResult.Success)
            {
                MessageBox.Show(
                    Res("ConfirmPasswordFailed"),
                    Res("ConfirmPasswordTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                Res("ActLogUndoConfirm"),
                Res("TitleWarning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            var result = await _employeeService.UndoArchiveAsync(entry.RelatedOperationId);
            if (!result.Success)
            {
                MessageBox.Show(
                    Res("ActLogUndoFailed"),
                    Res("TitleError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _logService.Log(
                "ArchiveUndone",
                "Archive",
                entry.FirmName,
                entry.EmployeeName,
                Res("ActLogUndoSuccess"),
                entry.NewValue,
                entry.OldValue,
                employeeFolder: result.RestoredFolder,
                relatedOperationId: result.UndoOperationId);

            await LoadEntriesAsync();
            MessageBox.Show(
                Res("ActLogUndoSuccess"),
                Res("TitleSuccess"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
                ws.Cell(1, 8).Value = "Actor";
                ws.Cell(1, 9).Value = "Details";

                var headerRange = ws.Range(1, 1, 1, 9);
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
                    ws.Cell(r, 8).Value = e.ActorName;
                    ws.Cell(r, 9).Value = e.Details;
                }

                ws.Columns().AdjustToContents();
                wb.SaveAs(dlg.FileName);
                _logService.Log("ExportExcel", "Export", SelectedFirm, "",
                    "Експортовано журнал дій → Excel",
                    details: BuildExportDetailsForLog(dlg.FileName));

                MessageBox.Show(Res("ActLogExported"), Res("TitleSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ActivityLogViewModel.ExportToExcel", ex);
            }
        }

        private string BuildExportDetailsForLog(string outputPath)
        {
            var activeFilters = new List<string>
            {
                $"Записів: {Entries.Count}"
            };

            if (!string.IsNullOrWhiteSpace(SearchText))
                activeFilters.Add($"Пошук: {SearchText}");

            if (!string.IsNullOrWhiteSpace(SelectedCategory))
                activeFilters.Add($"Категорія: {SelectedCategory}");

            if (!string.IsNullOrWhiteSpace(SelectedFirm))
                activeFilters.Add($"Фірма: {SelectedFirm}");

            if (DateFrom != null || DateTo != null)
                activeFilters.Add($"Період: {DateFrom?.ToString("dd.MM.yyyy") ?? "..." } - {DateTo?.ToString("dd.MM.yyyy") ?? "..."}");

            activeFilters.Add($"Файл: {Path.GetFileName(outputPath)}");
            return string.Join("; ", activeFilters);
        }

        private void ClearAllHistory()
        {
            var result = MessageBox.Show(Res("ActLogClearConfirm"), Res("TitleWarning"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _logService.ClearAll();
            _ = LoadEntriesAsync();
        }
    }

    internal sealed class ActivityLogSnapshot
    {
        public ActivityLogSnapshot(
            List<ActivityLogEntry> entries,
            HashSet<string> undoableArchiveOperationIds,
            List<string> categories,
            List<string> firms)
        {
            Entries = entries;
            UndoableArchiveOperationIds = undoableArchiveOperationIds;
            Categories = categories;
            Firms = firms;
        }

        public List<ActivityLogEntry> Entries { get; }
        public HashSet<string> UndoableArchiveOperationIds { get; }
        public List<string> Categories { get; }
        public List<string> Firms { get; }
    }

    internal sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotifications;

        public void ReplaceAll(IEnumerable<T> items)
        {
            _suppressNotifications = true;
            try
            {
                Items.Clear();
                foreach (var item in items)
                    Items.Add(item);
            }
            finally
            {
                _suppressNotifications = false;
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotifications)
                base.OnCollectionChanged(e);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (!_suppressNotifications)
                base.OnPropertyChanged(e);
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
            => System.Windows.Data.Binding.DoNothing;
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
            => Array.ConvertAll<Type, object>(targetTypes, _ => System.Windows.Data.Binding.DoNothing);
    }
}
