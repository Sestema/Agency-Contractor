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

        private int _activeCount;
        public int ActiveCount { get => _activeCount; set => SetProperty(ref _activeCount, value); }

        private int _problemsCount;
        public int ProblemsCount { get => _problemsCount; set => SetProperty(ref _problemsCount, value); }

        private int _newThisMonth;
        public int NewThisMonth { get => _newThisMonth; set => SetProperty(ref _newThisMonth, value); }

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
        private string _sortField = "Name";
        public string SortField
        {
            get => _sortField;
            set => SetProperty(ref _sortField, value);
        }

        private bool _sortAscending = true;
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

            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));
            AddEmployeeCommand = new RelayCommand(o =>
            {
                if (_company == null) return;
                AddEmployeeVm = new AddEmployeeWizardViewModel(_company);
                AddEmployeeVm.RequestClose += () =>
                {
                    IsAddEmployeeDialogOpen = false;
                    LoadEmployees();
                };
                IsAddEmployeeDialogOpen = true;
            });

            CloseAddEmployeeDialogCommand = new RelayCommand(o => IsAddEmployeeDialogOpen = false);
            SelectCompanyCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));
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
                    catch { }
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
                ApplyFilter();
            });

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

            var query = SearchQuery?.Trim().ToLower() ?? string.Empty;
            List<EmployeeModels.EmployeeSummary> list;

            if (string.IsNullOrEmpty(query))
            {
                list = new List<EmployeeModels.EmployeeSummary>(_allEmployees);
            }
            else
            {
                list = _allEmployees.Where(e =>
                    (!string.IsNullOrEmpty(e.FullName) && e.FullName.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(e.PassportNumber) && e.PassportNumber.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(e.VisaNumber) && e.VisaNumber.ToLower().Contains(query)) ||
                    (!string.IsNullOrEmpty(e.InsuranceNumber) && e.InsuranceNumber.ToLower().Contains(query))
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

        private void OpenEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            if (employee == null || _company == null) return;
            EmployeeDetailsVm = new EmployeeDetailsViewModel(_company.Name, employee.EmployeeFolder, _employeeService);
            EmployeeDetailsVm.RequestClose += () => IsEmployeeDetailsOpen = false;
            EmployeeDetailsVm.DataChanged += () => LoadEmployees();
            IsEmployeeDetailsOpen = true;
        }

        private void EditEmployee(EmployeeModels.EmployeeSummary? employee)
        {
            if (employee == null || _company == null) return;
            EmployeeDetailsVm = new EmployeeDetailsViewModel(_company.Name, employee.EmployeeFolder, _employeeService);
            EmployeeDetailsVm.RequestClose += () => IsEmployeeDetailsOpen = false;
            EmployeeDetailsVm.DataChanged += () => LoadEmployees();
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
            _employeeService.DeleteEmployee(EmployeeToDelete.EmployeeFolder);
            IsDeleteConfirmOpen = false;
            EmployeeToDelete = null;
            LoadEmployees();
        }

        private void RefreshStats()
        {
            TotalCount = _allEmployees.Count;
            ActiveCount = _allEmployees.Count(e => string.IsNullOrEmpty(e.Status) || e.Status == "Активний");
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
                var dialog = new SaveFileDialog
                {
                    Filter = "Excel|*.xlsx",
                    FileName = $"Працівники_{_company.Name}_{DateTime.Now:yyyyMMdd}.xlsx"
                };
                if (dialog.ShowDialog() != true) return;

                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("Працівники");

                string[] headers = { "Ім'я", "Прізвище", "Посада", "Телефон", "Email",
                    "Паспорт №", "Паспорт до", "Віза №", "Віза до",
                    "Страховка №", "Страховка до", "Тип контракту",
                    "Дата початку", "Статус" };

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

                Process.Start(new ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка експорту: {ex.Message}";
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

            BatchStatusMessage = $"Обрано працівників: {selected.Count}";
            var templates = App.TemplateService.GetTemplates(_company.Name);
            BatchTemplates = new ObservableCollection<TemplateEntry>(templates);
            IsBatchGenerateOpen = true;
        }

        private void BatchGenerate(TemplateEntry? template)
        {
            if (template == null || _company == null) return;
            try
            {
                var selected = Employees.Where(e => e.IsSelected).ToList();
                int success = 0;
                int fail = 0;

                foreach (var emp in selected)
                {
                    try
                    {
                        var data = _employeeService.LoadEmployeeData(emp.EmployeeFolder);
                        if (data == null) { fail++; continue; }

                        var templateFullPath = App.TemplateService.GetTemplateFullPath(_company.Name, template.FilePath);
                        if (!File.Exists(templateFullPath)) { fail++; continue; }

                        var tagValues = App.TagCatalogService.GetTagValueMapForEmployee(_company.Name, data);
                        var format = template.Format?.ToUpper() ?? Path.GetExtension(templateFullPath).TrimStart('.').ToUpper();

                        string SanitizeFn(string n) => string.Join("_", n.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

                        if (format == "DOCX")
                        {
                            var templateFolder = Path.GetDirectoryName(templateFullPath) ?? string.Empty;
                            var rtfPath = Path.Combine(templateFolder, "content.rtf");
                            if (File.Exists(rtfPath))
                            {
                                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.rtf");
                                var outPath = Path.Combine(emp.EmployeeFolder, outName);
                                App.DocumentGenerationService.GenerateFromRtf(rtfPath, outPath, tagValues);
                            }
                            else
                            {
                                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.docx");
                                var outPath = Path.Combine(emp.EmployeeFolder, outName);
                                App.DocumentGenerationService.GenerateDocx(templateFullPath, outPath, tagValues);
                            }
                        }
                        else if (format == "XLSX")
                        {
                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.xlsx");
                            var outPath = Path.Combine(emp.EmployeeFolder, outName);
                            App.DocumentGenerationService.GenerateXlsx(templateFullPath, outPath, tagValues);
                        }
                        else if (format == "PDF")
                        {
                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.pdf");
                            var outPath = Path.Combine(emp.EmployeeFolder, outName);
                            File.Copy(templateFullPath, outPath, true);
                        }

                        success++;
                    }
                    catch { fail++; }
                }

                BatchStatusMessage = $"Згенеровано: {success}, помилок: {fail}";
            }
            catch (Exception ex)
            {
                BatchStatusMessage = $"Помилка: {ex.Message}";
            }
        }
    }
}
