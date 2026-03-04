using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>
    /// Info about a cell's merge status.
    /// </summary>
    public class MergeInfo
    {
        /// <summary>True if this cell is a slave (non-top-left) part of a merged range.</summary>
        public bool IsSlave { get; set; }
        /// <summary>Range label, e.g. "A1:C3".</summary>
        public string RangeLabel { get; set; } = "";
        /// <summary>0-based row of the master (top-left) cell.</summary>
        public int MasterRow { get; set; }
        /// <summary>0-based column of the master (top-left) cell.</summary>
        public int MasterCol { get; set; }
    }

    public class XlsxEditorViewModel : ViewModelBase, IDisposable
    {
        private bool _disposed;
        private readonly string _firmName;
        private readonly TemplateEntry _template;
        private readonly TemplateService _templateService;
        private readonly string _xlsxFilePath;

        private XLWorkbook? _workbook;
        private Dictionary<string, DataTable> _sheetData = new();

        // ===== Merge tracking =====
        private Dictionary<string, Dictionary<(int row, int col), MergeInfo>> _mergeInfoPerSheet = new();
        private Dictionary<(int row, int col), MergeInfo> _currentMergeInfo = new();

        /// <summary>Merge info for the currently displayed sheet.</summary>
        public Dictionary<(int row, int col), MergeInfo> CurrentMergeInfo => _currentMergeInfo;

        private static new string Res(string key)
        {
            try { return Application.Current.FindResource(key) as string ?? key; }
            catch { return key; }
        }

        private static string ResF(string key, params object[] args)
        {
            var fmt = Res(key);
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        public string Title => ResF("XlsxEditorTitle", _template.Name);

        // Sheet tabs
        private ObservableCollection<string> _sheetNames = new();
        public ObservableCollection<string> SheetNames
        {
            get => _sheetNames;
            set => SetProperty(ref _sheetNames, value);
        }

        private string? _selectedSheet;
        public string? SelectedSheet
        {
            get => _selectedSheet;
            set
            {
                if (SetProperty(ref _selectedSheet, value) && value != null)
                {
                    LoadSheetData(value);
                }
            }
        }

        // DataGrid source
        private DataTable? _currentData;
        public DataTable? CurrentData
        {
            get => _currentData;
            set => SetProperty(ref _currentData, value);
        }

        // Tag groups
        public ObservableCollection<TagGroupViewModel> TagGroups { get; }

        // Tag search
        private string _tagSearchQuery = string.Empty;
        public string TagSearchQuery
        {
            get => _tagSearchQuery;
            set
            {
                if (SetProperty(ref _tagSearchQuery, value))
                    OnPropertyChanged(nameof(FilteredTagGroups));
            }
        }

        public ObservableCollection<TagGroupViewModel> FilteredTagGroups
            => TagGroups != null ? TagGroupViewModel.FilterTagGroups(TagGroups, TagSearchQuery) : new ObservableCollection<TagGroupViewModel>();

        // ===== Excel-like: Cell Address (Name Box) =====
        private string _cellAddress = "";
        public string CellAddress
        {
            get => _cellAddress;
            set => SetProperty(ref _cellAddress, value);
        }

        // ===== Excel-like: Formula Bar (cell content) =====
        private string _cellValue = "";
        public string CellValue
        {
            get => _cellValue;
            set
            {
                if (SetProperty(ref _cellValue, value))
                {
                    ApplyCellValueFromFormulaBar(value);
                }
            }
        }

        // Currently selected row/col (0-based)
        private int _selectedRow = -1;
        private int _selectedCol = -1;

        // Commands
        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand InsertTagCommand { get; }
        public ICommand CopyTagCommand { get; }

        // Events to communicate with View
        public Func<(int row, int col)>? RequestGetSelectedCell { get; set; }
        public Action? RequestRefreshGrid { get; set; }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public XlsxEditorViewModel(string firmName, TemplateEntry template, TemplateService? templateService = null)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService ?? App.TemplateService;

            _xlsxFilePath = _templateService.GetTemplateFullPath(firmName, template.FilePath);

            // Build tag groups with subcategories
            try
            {
                var allTags = App.TagCatalogService.GetAllTagDefinitions();
                var groups = TagGroupViewModel.BuildTagGroups(allTags);
                TagGroups = TagGroupViewModel.ApplyHiddenTagsFilter(groups, App.AppSettingsService.Settings.HiddenTags);
            }
            catch
            {
                TagGroups = new ObservableCollection<TagGroupViewModel>();
            }

            LoadWorkbook();

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new RelayCommand(o => Save());
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
        }

        /// <summary>
        /// Called by View when user selects a cell. Updates Name Box + Formula Bar.
        /// If user selected a slave cell, shows merge info and master value.
        /// </summary>
        public void OnCellSelected(int row, int col)
        {
            _selectedRow = row;
            _selectedCol = col;

            if (_currentMergeInfo.TryGetValue((row, col), out var info))
            {
                if (info.IsSlave)
                {
                    // Show slave cell address with merge indicator pointing to master
                    var masterAddr = $"{GetColumnLetter(info.MasterCol + 1)}{info.MasterRow + 1}";
                    CellAddress = $"{GetColumnLetter(col + 1)}{row + 1}  ◈ → {masterAddr}";

                    // Formula bar shows master cell's value
                    if (CurrentData != null &&
                        info.MasterRow < CurrentData.Rows.Count &&
                        info.MasterCol < CurrentData.Columns.Count)
                    {
                        _cellValue = CurrentData.Rows[info.MasterRow][info.MasterCol]?.ToString() ?? "";
                    }
                    else
                    {
                        _cellValue = "";
                    }
                    OnPropertyChanged(nameof(CellValue));
                    return;
                }
                else
                {
                    // Master cell — show address with merge range
                    CellAddress = $"{GetColumnLetter(col + 1)}{row + 1}  ◈ {info.RangeLabel}";
                }
            }
            else
            {
                CellAddress = $"{GetColumnLetter(col + 1)}{row + 1}";
            }

            // Update formula bar with cell content
            if (CurrentData != null && row >= 0 && col >= 0 &&
                row < CurrentData.Rows.Count && col < CurrentData.Columns.Count)
            {
                _cellValue = CurrentData.Rows[row][col]?.ToString() ?? "";
                OnPropertyChanged(nameof(CellValue));
            }
            else
            {
                _cellValue = "";
                OnPropertyChanged(nameof(CellValue));
            }
        }

        private void ApplyCellValueFromFormulaBar(string value)
        {
            int row = _selectedRow;
            int col = _selectedCol;

            // Redirect slave → master
            if (_currentMergeInfo.TryGetValue((row, col), out var info) && info.IsSlave)
            {
                row = info.MasterRow;
                col = info.MasterCol;
            }

            if (CurrentData == null || row < 0 || col < 0) return;
            if (row >= CurrentData.Rows.Count || col >= CurrentData.Columns.Count) return;

            CurrentData.Rows[row][col] = value;
            RequestRefreshGrid?.Invoke();
        }

        private void LoadWorkbook()
        {
            try
            {
                if (!File.Exists(_xlsxFilePath))
                {
                    StatusMessage = Res("XlsxFileNotFound");
                    return;
                }

                _workbook = new XLWorkbook(_xlsxFilePath);
                var names = new ObservableCollection<string>();
                foreach (var ws in _workbook.Worksheets)
                {
                    names.Add(ws.Name);
                    _sheetData[ws.Name] = WorksheetToDataTable(ws);
                }
                SheetNames = names;

                if (names.Count > 0)
                {
                    SelectedSheet = names[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("XlsxLoadError", ex.Message);
            }
        }

        private DataTable WorksheetToDataTable(IXLWorksheet ws)
        {
            var dt = new DataTable(ws.Name);
            var mergeInfo = new Dictionary<(int, int), MergeInfo>();

            // ===== Extract merge ranges =====
            try
            {
                foreach (var mergedRange in ws.MergedRanges)
                {
                    int fRow = mergedRange.FirstRow().RowNumber() - 1; // 0-based
                    int fCol = mergedRange.FirstColumn().ColumnNumber() - 1;
                    int lRow = mergedRange.LastRow().RowNumber() - 1;
                    int lCol = mergedRange.LastColumn().ColumnNumber() - 1;

                    string label = $"{GetColumnLetter(fCol + 1)}{fRow + 1}:{GetColumnLetter(lCol + 1)}{lRow + 1}";

                    for (int r = fRow; r <= lRow; r++)
                    {
                        for (int c = fCol; c <= lCol; c++)
                        {
                            bool isMaster = (r == fRow && c == fCol);
                            mergeInfo[(r, c)] = new MergeInfo
                            {
                                IsSlave = !isMaster,
                                RangeLabel = label,
                                MasterRow = fRow,
                                MasterCol = fCol,
                            };
                        }
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogWarning("XlsxEditorViewModel.WorksheetToDataTable", $"Merge parsing failed: {ex.Message}"); }

            _mergeInfoPerSheet[ws.Name] = mergeInfo;

            // ===== Determine grid size =====
            var rangeUsed = ws.RangeUsed();
            int maxCol = 10;
            int maxRow = 20;

            if (rangeUsed != null)
            {
                maxCol = Math.Max(rangeUsed.LastColumn().ColumnNumber(), 10);
                maxRow = Math.Max(rangeUsed.LastRow().RowNumber(), 20);
            }

            // Extend to cover all merge ranges
            foreach (var kvp in mergeInfo)
            {
                maxRow = Math.Max(maxRow, kvp.Key.Item1 + 1);
                maxCol = Math.Max(maxCol, kvp.Key.Item2 + 1);
            }

            // Create columns A, B, C, ...
            for (int c = 1; c <= maxCol; c++)
            {
                dt.Columns.Add(GetColumnLetter(c));
            }

            // Fill rows
            for (int r = 1; r <= maxRow; r++)
            {
                var row = dt.NewRow();
                for (int c = 1; c <= maxCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    row[c - 1] = cell.GetString();
                }
                dt.Rows.Add(row);
            }

            return dt;
        }

        private void LoadSheetData(string sheetName)
        {
            if (_sheetData.TryGetValue(sheetName, out var dt))
            {
                CurrentData = dt;

                // Switch merge info for current sheet
                _currentMergeInfo = _mergeInfoPerSheet.GetValueOrDefault(sheetName)
                                    ?? new Dictionary<(int, int), MergeInfo>();

                CellAddress = "";
                _cellValue = "";
                OnPropertyChanged(nameof(CellValue));
                _selectedRow = -1;
                _selectedCol = -1;
            }
        }

        private void NavigateBack()
        {
            Dispose();
            App.NavigationService?.NavigateTo(new TemplatesViewModel(App.CompanyService?.SelectedCompany));
        }

        private void Save()
        {
            try
            {
                if (_workbook == null)
                {
                    StatusMessage = Res("XlsxNoOpenFile");
                    return;
                }

                foreach (var kvp in _sheetData)
                {
                    var ws = _workbook.Worksheets.FirstOrDefault(w => w.Name == kvp.Key);
                    if (ws == null) continue;

                    var dt = kvp.Value;
                    var sheetMergeInfo = _mergeInfoPerSheet.GetValueOrDefault(kvp.Key)
                                         ?? new Dictionary<(int, int), MergeInfo>();

                    for (int r = 0; r < dt.Rows.Count; r++)
                    {
                        for (int c = 0; c < dt.Columns.Count; c++)
                        {
                            // Skip slave cells — they don't own data
                            if (sheetMergeInfo.TryGetValue((r, c), out var info) && info.IsSlave)
                                continue;

                            var value = dt.Rows[r][c]?.ToString() ?? string.Empty;
                            ws.Cell(r + 1, c + 1).SetValue(value);
                        }
                    }
                }

                _workbook.Save();
                StatusMessage = Res("XlsxSaved");
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("XlsxSaveError", ex.Message);
                LoggingService.LogError("XlsxEditor.Save", ex);
                MessageBox.Show(ResF("XlsxSaveError", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertTag(object? param)
        {
            if (param is not TagEntry tag) return;

            try
            {
                var (row, col) = RequestGetSelectedCell?.Invoke() ?? (-1, -1);
                if (row < 0 || col < 0 || CurrentData == null)
                {
                    StatusMessage = Res("XlsxSelectCell");
                    return;
                }

                // Redirect slave → master cell
                if (_currentMergeInfo.TryGetValue((row, col), out var info) && info.IsSlave)
                {
                    row = info.MasterRow;
                    col = info.MasterCol;
                }

                if (row >= CurrentData.Rows.Count || col >= CurrentData.Columns.Count) return;

                var current = CurrentData.Rows[row][col]?.ToString() ?? string.Empty;
                var tagText = $"${{{tag.Tag}}}";
                var newValue = current + tagText;
                CurrentData.Rows[row][col] = newValue;

                // Update name box & formula bar to reflect the master cell
                var addr = $"{GetColumnLetter(col + 1)}{row + 1}";
                if (_currentMergeInfo.TryGetValue((row, col), out var mi) && !mi.IsSlave)
                    CellAddress = $"{addr}  ◈ {mi.RangeLabel}";
                else
                    CellAddress = addr;

                _cellValue = newValue;
                OnPropertyChanged(nameof(CellValue));

                RequestRefreshGrid?.Invoke();
                StatusMessage = ResF("XlsxTagInserted", tagText, addr);
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("XlsxInsertError", ex.Message);
            }
        }

        private void CopyTag(object? param)
        {
            if (param is TagEntry tag)
            {
                var tagText = $"${{{tag.Tag}}}";
                Clipboard.SetText(tagText);
                StatusMessage = ResF("XlsxTagCopied", tagText);
            }
        }

        public static string GetColumnLetter(int colNumber)
        {
            string result = string.Empty;
            while (colNumber > 0)
            {
                colNumber--;
                result = (char)('A' + colNumber % 26) + result;
                colNumber /= 26;
            }
            return result;
        }

        public static int ColumnLetterToIndex(string columnLetter)
        {
            int index = 0;
            foreach (char c in columnLetter.ToUpper())
            {
                index = index * 26 + (c - 'A' + 1);
            }
            return index - 1; // 0-based
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _workbook?.Dispose();
            _workbook = null;
        }
    }
}
