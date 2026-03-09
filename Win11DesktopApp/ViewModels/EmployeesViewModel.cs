using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using Microsoft.Win32;
using ClosedXML.Excel;
using Win11DesktopApp.Models;
using EmployeeModels = Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class EmployeesViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;
        private readonly EmployerCompany? _company;
        private List<EmployeeModels.EmployeeSummary> _allEmployees = new List<EmployeeModels.EmployeeSummary>();
        private string _lastStatus = string.Empty;

        private ObservableCollection<EmployeeModels.EmployeeSummary> _employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
        public ObservableCollection<EmployeeModels.EmployeeSummary> Employees
        {
            get => _employees;
            set => SetProperty(ref _employees, value);
        }

        private bool _hasEmployees;
        public bool HasEmployees
        {
            get => _hasEmployees;
            set => SetProperty(ref _hasEmployees, value);
        }

        private bool _isCompanySelected;
        public bool IsCompanySelected
        {
            get => _isCompanySelected;
            set => SetProperty(ref _isCompanySelected, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set => SetProperty(ref _isError, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string Title => _company == null
            ? GetString("TitleEmployeesGeneric") ?? "Employees"
            : string.Format(GetString("TitleEmployees") ?? "{0}", _company.Name);

        // Statistics
        private int _totalCount;
        public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

        private int _problemsCount;
        public int ProblemsCount { get => _problemsCount; set => SetProperty(ref _problemsCount, value); }

        private int _newThisMonth;
        public int NewThisMonth { get => _newThisMonth; set => SetProperty(ref _newThisMonth, value); }

        private string _statFilter = "all";
        public string StatFilter
        {
            get => _statFilter;
            set
            {
                if (SetProperty(ref _statFilter, value))
                    ApplyFilter();
            }
        }

        // Selection mode
        private bool _isSelectionMode;
        public bool IsSelectionMode
        {
            get => _isSelectionMode;
            set => SetProperty(ref _isSelectionMode, value);
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        // Batch generate dialog
        private bool _isBatchGenerateOpen;
        public bool IsBatchGenerateOpen
        {
            get => _isBatchGenerateOpen;
            set => SetProperty(ref _isBatchGenerateOpen, value);
        }

        private ObservableCollection<TemplateEntry> _batchTemplates = new();
        public ObservableCollection<TemplateEntry> BatchTemplates
        {
            get => _batchTemplates;
            set => SetProperty(ref _batchTemplates, value);
        }

        private string _batchStatusMessage = string.Empty;
        public string BatchStatusMessage
        {
            get => _batchStatusMessage;
            set => SetProperty(ref _batchStatusMessage, value);
        }

        // Sorting
        private string _sortField = App.AppSettingsService?.Settings?.EmployeeSortField ?? "Name";
        public string SortField
        {
            get => _sortField;
            set => SetProperty(ref _sortField, value);
        }

        private bool _sortAscending = App.AppSettingsService?.Settings?.EmployeeSortAscending ?? true;
        public bool SortAscending
        {
            get => _sortAscending;
            set => SetProperty(ref _sortAscending, value);
        }

        public ICommand GoBackCommand { get; }
        public ICommand AddEmployeeCommand { get; }
        public ICommand CloseAddEmployeeDialogCommand { get; }
        public ICommand SelectCompanyCommand { get; }
        public ICommand OpenEmployeeCommand { get; }
        public ICommand EditEmployeeCommand { get; }
        public ICommand DeleteEmployeeCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }
        public ICommand CancelDeleteCommand { get; }
        public ICommand OpenEmployeeFolderCommand { get; }
        public ICommand ExportToExcelCommand { get; }
        public ICommand ToggleSelectionModeCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand BatchGenerateCommand { get; }
        public ICommand CloseBatchGenerateCommand { get; }
        public ICommand BatchGenerateFromTemplateCommand { get; }
        public ICommand SortByCommand { get; }
        public ICommand SetViewModeCommand { get; }
        public ICommand FilterByStatCommand { get; }

        private string _viewMode = App.AppSettingsService?.Settings?.EmployeeViewMode ?? "List";
        public string ViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    OnPropertyChanged(nameof(IsTableView));
                    OnPropertyChanged(nameof(IsListView));
                    OnPropertyChanged(nameof(IsTilesView));
                    OnPropertyChanged(nameof(IsIconsView));
                    if (App.AppSettingsService != null)
                    {
                        App.AppSettingsService.Settings.EmployeeViewMode = value;
                        App.AppSettingsService.SaveSettings();
                    }
                }
            }
        }

        public bool IsTableView => ViewMode == "Table";
        public bool IsListView => ViewMode == "List";
        public bool IsTilesView => ViewMode == "Tiles";
        public bool IsIconsView => ViewMode == "Icons";

        private double _zoomLevel = App.AppSettingsService?.Settings?.EmployeeZoomLevel ?? 1.0;
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (SetProperty(ref _zoomLevel, value))
                {
                    if (App.AppSettingsService != null)
                    {
                        App.AppSettingsService.Settings.EmployeeZoomLevel = value;
                        App.AppSettingsService.SaveSettings();
                    }
                }
            }
        }

        private bool _isAddEmployeeDialogOpen;
        public bool IsAddEmployeeDialogOpen
        {
            get => _isAddEmployeeDialogOpen;
            set => SetProperty(ref _isAddEmployeeDialogOpen, value);
        }

        private AddEmployeeWizardViewModel? _addEmployeeVm;
        public AddEmployeeWizardViewModel? AddEmployeeVm
        {
            get => _addEmployeeVm;
            set => SetProperty(ref _addEmployeeVm, value);
        }

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

        private bool _isDeleteConfirmOpen;
        public bool IsDeleteConfirmOpen
        {
            get => _isDeleteConfirmOpen;
            set => SetProperty(ref _isDeleteConfirmOpen, value);
        }

        private EmployeeModels.EmployeeSummary? _employeeToDelete;
        public EmployeeModels.EmployeeSummary? EmployeeToDelete
        {
            get => _employeeToDelete;
            set => SetProperty(ref _employeeToDelete, value);
        }

        public EmployeesViewModel(EmployerCompany? company, EmployeeService? employeeService = null)
        {
            _company = company;
            _employeeService = employeeService ?? App.EmployeeService;
            IsCompanySelected = _company != null;

            GoBackCommand = new RelayCommand(o => App.NavigationService?.NavigateTo(new MainViewModel()));
            AddEmployeeCommand = new RelayCommand(o =>
            {
                try
                {
                    if (_company == null) return;
                    CleanupAddEmployeeVm();
                    AddEmployeeVm = new AddEmployeeWizardViewModel(_company);
                    AddEmployeeVm.RequestClose += OnAddEmployeeClose;
                    IsAddEmployeeDialogOpen = true;
                }
                catch (Exception ex)
                {
                    var errTitle = Application.Current?.TryFindResource("TitleError") as string ?? "Error";
                    var errFmt = Application.Current?.TryFindResource("MsgErrorGeneric") as string ?? "Error: {0}";
                    System.Windows.MessageBox.Show(string.Format(errFmt, ex.Message), errTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            });

            CloseAddEmployeeDialogCommand = new RelayCommand(o =>
            {
                IsAddEmployeeDialogOpen = false;
                CleanupAddEmployeeVm();
            });
            SelectCompanyCommand = new RelayCommand(o => App.NavigationService?.NavigateTo(new MainViewModel()));
            OpenEmployeeCommand = new RelayCommand(o => OpenEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            EditEmployeeCommand = new RelayCommand(o => EditEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            DeleteEmployeeCommand = new RelayCommand(o => AskDeleteEmployee(o as EmployeeModels.EmployeeSummary), o => o is EmployeeModels.EmployeeSummary);
            ConfirmDeleteCommand = new RelayCommand(o => ConfirmDelete());
            CancelDeleteCommand = new RelayCommand(o => IsDeleteConfirmOpen = false);

            OpenEmployeeFolderCommand = new RelayCommand(o =>
            {
                if (o is EmployeeModels.EmployeeSummary emp && !string.IsNullOrEmpty(emp.EmployeeFolder))
                {
                    try { Process.Start(new ProcessStartInfo { FileName = emp.EmployeeFolder, UseShellExecute = true }); }
                    catch (Exception ex) { LoggingService.LogWarning("EmployeesViewModel.OpenFolder", ex.Message); }
                }
            }, o => o is EmployeeModels.EmployeeSummary);

            ExportToExcelCommand = new RelayCommand(o => ExportToExcel(), o => _allEmployees.Count > 0);

            ToggleSelectionModeCommand = new RelayCommand(o =>
            {
                IsSelectionMode = !IsSelectionMode;
                if (!IsSelectionMode)
                {
                    foreach (var e in Employees) e.IsSelected = false;
                    SelectedCount = 0;
                }
            });

            SelectAllCommand = new RelayCommand(o =>
            {
                foreach (var e in Employees) e.IsSelected = true;
                SelectedCount = Employees.Count;
            });

            DeselectAllCommand = new RelayCommand(o =>
            {
                foreach (var e in Employees) e.IsSelected = false;
                SelectedCount = 0;
                IsSelectionMode = false;
            });

            BatchGenerateCommand = new RelayCommand(o => OpenBatchGenerate(), o => Employees.Any(e => e.IsSelected));
            CloseBatchGenerateCommand = new RelayCommand(o => IsBatchGenerateOpen = false);
            BatchGenerateFromTemplateCommand = new RelayCommand(o => BatchGenerate(o as TemplateEntry));

            SortByCommand = new RelayCommand(o =>
            {
                var field = o as string ?? "Name";
                if (SortField == field)
                    SortAscending = !SortAscending;
                else
                {
                    SortField = field;
                    SortAscending = true;
                }
                if (App.AppSettingsService != null)
                {
                    App.AppSettingsService.Settings.EmployeeSortField = SortField;
                    App.AppSettingsService.Settings.EmployeeSortAscending = SortAscending;
                    App.AppSettingsService.SaveSettings();
                }
                ApplyFilter();
            });

            SetViewModeCommand = new RelayCommand(o => ViewMode = o as string ?? "List");
            FilterByStatCommand = new RelayCommand(o => StatFilter = o as string ?? "all");

            LoadEmployees();
        }

        private void LoadEmployees()
        {
            if (_company == null)
            {
                _allEmployees = new List<EmployeeModels.EmployeeSummary>();
                Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
                HasEmployees = false;
                StatusMessage = GetString("MsgEmployeesSelectCompany") ?? "Please select a company.";
                return;
            }
            var result = _employeeService.GetEmployeesForFirmWithStatus(_company.Name);
            _allEmployees = result.Employees;
            _lastStatus = result.Status;
            ApplyFilter();
            HasEmployees = Employees.Count > 0;
            StatusMessage = GetStatusMessage(result.Status);
            IsError = result.Status == "LoadError";
            RefreshStats();
            Debug.WriteLine($"EmployeesViewModel.LoadEmployees: {Employees.Count} items");
        }

        private static string DocRes(string key) =>
            App.DocumentLocalizationService?.Get(key) ?? Res(key);

        private string? GetString(string key)
        {
            return Application.Current?.TryFindResource(key) as string;
        }

        private string GetStatusMessage(string status)
        {
            if (status == "RootFolderNotSet")
                return GetString("MsgEmployeesRootMissing") ?? "Root folder is not configured.";
            if (status == "EmployeesFolderMissing")
                return GetString("MsgEmployeesFolderMissing") ?? "Employees folder not found.";
            if (status == "NoEmployees")
                return GetString("MsgEmployeesEmpty") ?? "No employees yet.";
            if (status == "LoadError")
                return GetString("MsgEmployeesLoadError") ?? "Failed to load employees.";
            return string.Empty;
        }

        private void ApplyFilter()
        {
            if (_allEmployees.Count == 0)
            {
                Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>();
                return;
            }

            var query = SearchQuery?.Trim() ?? string.Empty;
            List<EmployeeModels.EmployeeSummary> list;

            IEnumerable<EmployeeModels.EmployeeSummary> source = _allEmployees;

            if (_statFilter == "problems")
                source = source.Where(e => HasExpiringDocs(e));
            else if (_statFilter == "new")
                source = source.Where(e => IsThisMonth(e.StartDate));

            if (string.IsNullOrEmpty(query))
            {
                list = source.ToList();
            }
            else
            {
                list = source.Where(e =>
                    (!string.IsNullOrEmpty(e.FullName) && e.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.PassportNumber) && e.PassportNumber.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.VisaNumber) && e.VisaNumber.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.InsuranceNumber) && e.InsuranceNumber.Contains(query, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            list = SortField switch
            {
                "Name" => SortAscending
                    ? list.OrderBy(e => e.FullName).ToList()
                    : list.OrderByDescending(e => e.FullName).ToList(),
                "StartDate" => SortAscending
                    ? list.OrderBy(e => DateParsingHelper.TryParseDate(e.StartDate) ?? DateTime.MaxValue).ToList()
                    : list.OrderByDescending(e => DateParsingHelper.TryParseDate(e.StartDate) ?? DateTime.MinValue).ToList(),
                "Status" => SortAscending
                    ? list.OrderBy(e => e.Status ?? string.Empty).ToList()
                    : list.OrderByDescending(e => e.Status ?? string.Empty).ToList(),
                "Problems" => list.OrderByDescending(e => HasExpiringDocs(e) ? 1 : 0)
                                  .ThenBy(e => e.FullName).ToList(),
                _ => list
            };

            Employees = new ObservableCollection<EmployeeModels.EmployeeSummary>(list);
            HasEmployees = Employees.Count > 0;

            if (!HasEmployees)
            {
                StatusMessage = string.IsNullOrEmpty(query)
                    ? GetStatusMessage(_lastStatus)
                    : (GetString("MsgEmployeesSearchEmpty") ?? "No employees found.");
            }
            else
            {
                StatusMessage = string.IsNullOrEmpty(query) ? GetStatusMessage(_lastStatus) : string.Empty;
            }
        }

        private void CleanupDetailsVm()
        {
            if (EmployeeDetailsVm != null)
            {
                EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
            }
        }

        private void OnAddEmployeeClose()
        {
            IsAddEmployeeDialogOpen = false;
            CleanupAddEmployeeVm();
            LoadEmployees();
        }

        private void CleanupAddEmployeeVm()
        {
            if (AddEmployeeVm != null)
                AddEmployeeVm.RequestClose -= OnAddEmployeeClose;
        }

        private void OnDetailsClose() => IsEmployeeDetailsOpen = false;
        private void OnDetailsDataChanged() => LoadEmployees();

        private void OpenEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            if (employee == null || _company == null) return;
            CleanupDetailsVm();
            EmployeeDetailsVm = new EmployeeDetailsViewModel(_company.Name, employee.EmployeeFolder, _employeeService);
            EmployeeDetailsVm.RequestClose += OnDetailsClose;
            EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
            IsEmployeeDetailsOpen = true;
        }

        private void EditEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            if (employee == null || _company == null) return;
            CleanupDetailsVm();
            EmployeeDetailsVm = new EmployeeDetailsViewModel(_company.Name, employee.EmployeeFolder, _employeeService);
            EmployeeDetailsVm.RequestClose += OnDetailsClose;
            EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
            EmployeeDetailsVm.IsEditMode = true;
            EmployeeDetailsVm.TabIndex = 1;
            IsEmployeeDetailsOpen = true;
        }

        private void AskDeleteEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            if (employee == null) return;
            EmployeeToDelete = employee;
            IsDeleteConfirmOpen = true;
        }

        private void ConfirmDelete()
        {
            if (EmployeeToDelete == null) return;
            Debug.WriteLine($"EmployeesViewModel.ConfirmDelete: Deleting employee '{EmployeeToDelete.FullName}' from folder '{EmployeeToDelete.EmployeeFolder}'");
            _employeeService.DeleteEmployee(EmployeeToDelete.EmployeeFolder);
            IsDeleteConfirmOpen = false;
            EmployeeToDelete = null;
            LoadEmployees();
        }

        private void RefreshStats()
        {
            TotalCount = _allEmployees.Count;
            ProblemsCount = _allEmployees.Count(e => HasExpiringDocs(e));
            NewThisMonth = _allEmployees.Count(e => IsThisMonth(e.StartDate));
        }

        private static bool HasExpiringDocs(EmployeeModels.EmployeeSummary emp)
        {
            return IsProblematic(emp.PassportExpiry) || IsProblematic(emp.VisaExpiry) || IsProblematic(emp.InsuranceExpiry);
        }

        private static bool IsProblematic(string dateStr)
        {
            var s = DateParsingHelper.GetSeverity(dateStr);
            return s == "Expired" || s == "Critical" || s == "Warning";
        }

        private static bool IsThisMonth(string dateStr)
        {
            var dt = DateParsingHelper.TryParseDate(dateStr);
            if (dt == null) return false;
            return dt.Value.Year == DateTime.Now.Year && dt.Value.Month == DateTime.Now.Month;
        }

        public void UpdateSelectedCount()
        {
            SelectedCount = Employees.Count(e => e.IsSelected);
        }

        private void ExportToExcel()
        {
            if (_company == null) return;
            try
            {
                IsLoading = true;
                var dialog = new SaveFileDialog
                {
                    Filter = "Excel|*.xlsx",
                    FileName = $"{DocRes("ExportEmployees")}_{_company.Name}_{DateTime.Now:yyyyMMdd}.xlsx"
                };
                if (dialog.ShowDialog() != true) return;

                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(DocRes("ExportEmployees"));

                string[] headers = { DocRes("ExportColFirstName"), DocRes("ExportColLastName"), DocRes("ExportColPosition"), DocRes("ExportColPhone"), "Email",
                    DocRes("ExportColPassportNum"), DocRes("ExportColPassportExp"), DocRes("ExportColVisaNum"), DocRes("ExportColVisaExp"),
                    DocRes("ExportColInsNum"), DocRes("ExportColInsExp"), DocRes("ExportColContractType"),
                    DocRes("ExportColStartDate"), DocRes("ExportColStatus") };

                for (int i = 0; i < headers.Length; i++)
                    ws.Cell(1, i + 1).Value = headers[i];

                var headerRange = ws.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.CornflowerBlue;
                headerRange.Style.Font.FontColor = XLColor.White;

                int row = 2;
                foreach (var emp in _allEmployees)
                {
                    var data = _employeeService.LoadEmployeeData(emp.EmployeeFolder);
                    if (data == null) continue;

                    ws.Cell(row, 1).Value = data.FirstName;
                    ws.Cell(row, 2).Value = data.LastName;
                    ws.Cell(row, 3).Value = data.PositionTag;
                    ws.Cell(row, 4).Value = data.Phone;
                    ws.Cell(row, 5).Value = data.Email;
                    ws.Cell(row, 6).Value = data.PassportNumber;
                    ws.Cell(row, 7).Value = data.PassportExpiry;
                    ws.Cell(row, 8).Value = data.VisaNumber;
                    ws.Cell(row, 9).Value = data.VisaExpiry;
                    ws.Cell(row, 10).Value = data.InsuranceNumber;
                    ws.Cell(row, 11).Value = data.InsuranceExpiry;
                    ws.Cell(row, 12).Value = data.ContractType;
                    ws.Cell(row, 13).Value = data.StartDate;
                    ws.Cell(row, 14).Value = data.Status;

                    HighlightIfExpired(ws.Cell(row, 7));
                    HighlightIfExpired(ws.Cell(row, 9));
                    HighlightIfExpired(ws.Cell(row, 11));

                    row++;
                }

                ws.Columns().AdjustToContents();
                workbook.SaveAs(dialog.FileName);
                App.ActivityLogService?.Log("ExportExcel", "Export", _company?.Name ?? "", "",
                    $"Експортовано список працівників {_company?.Name} → Excel");
                Process.Start(new ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Res("MsgExportError"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static void HighlightIfExpired(IXLCell cell)
        {
            var val = cell.GetString();
            var severity = DateParsingHelper.GetSeverity(val);
            if (severity == "Expired" || severity == "Critical")
            {
                cell.Style.Font.FontColor = XLColor.Red;
                cell.Style.Font.Bold = true;
            }
            else if (severity == "Warning")
            {
                cell.Style.Font.FontColor = XLColor.OrangeRed;
            }
        }

        private void OpenBatchGenerate()
        {
            if (_company == null) return;
            var selected = Employees.Where(e => e.IsSelected).ToList();
            if (selected.Count == 0) return;

            BatchStatusMessage = string.Format(Res("MsgSelectedCount"), selected.Count);
            var templates = App.TemplateService?.GetTemplates(_company.Name) ?? new List<TemplateEntry>();
            BatchTemplates = new ObservableCollection<TemplateEntry>(templates);
            IsBatchGenerateOpen = true;
        }

        private void BatchGenerate(TemplateEntry? template)
        {
            if (template == null || _company == null) return;
            if (App.DocumentGenerationService == null) return;
            try
            {
                IsLoading = true;
                var selected = Employees.Where(e => e.IsSelected).ToList();
                int success = 0;
                int fail = 0;

                foreach (var emp in selected)
                {
                    try
                    {
                        var data = _employeeService.LoadEmployeeData(emp.EmployeeFolder);
                        if (data == null) { fail++; continue; }

                        var templateFullPath = App.TemplateService?.GetTemplateFullPath(_company.Name, template.FilePath) ?? string.Empty;
                        var templateFolder = Path.GetDirectoryName(templateFullPath) ?? string.Empty;
                        var rtfPath = Path.Combine(templateFolder, "content.rtf");
                        bool hasTemplateFile = File.Exists(templateFullPath);
                        bool hasRtfContent = File.Exists(rtfPath);

                        if (!hasTemplateFile && !hasRtfContent) { fail++; continue; }

                        var tagValues = App.TagCatalogService?.GetTagValueMapForEmployee(_company.Name, data)
                            ?? new Dictionary<string, string>();
                        var format = template.Format?.ToUpper() ?? Path.GetExtension(templateFullPath).TrimStart('.').ToUpper();

                        string SanitizeFn(string n) => string.Join("_", n.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

                        if (format == "DOCX" || hasRtfContent)
                        {
                            if (hasRtfContent)
                            {
                                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.docx");
                                var outPath = Path.Combine(emp.EmployeeFolder, outName);
                                App.DocumentGenerationService?.GenerateDocxFromRtf(rtfPath, outPath, tagValues);
                            }
                            else if (hasTemplateFile)
                            {
                                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.docx");
                                var outPath = Path.Combine(emp.EmployeeFolder, outName);
                                App.DocumentGenerationService?.GenerateDocx(templateFullPath, outPath, tagValues);
                            }
                        }
                        else if (format == "XLSX" && hasTemplateFile)
                        {
                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.xlsx");
                            var outPath = Path.Combine(emp.EmployeeFolder, outName);
                            App.DocumentGenerationService?.GenerateXlsx(templateFullPath, outPath, tagValues);
                        }
                        else if (format == "PDF" && hasTemplateFile)
                        {
                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.pdf");
                            var outPath = Path.Combine(emp.EmployeeFolder, outName);
                            File.Copy(templateFullPath, outPath, true);
                        }

                        success++;
                    }
                    catch (Exception ex) { LoggingService.LogError("EmployeesViewModel.BatchGenerate", ex); fail++; }
                }

                BatchStatusMessage = string.Format(Res("MsgBatchResult"), success, fail);
            }
            catch (Exception ex)
            {
                BatchStatusMessage = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
