using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class EmployeeDetailsViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;
        private readonly string _employeeFolder;
        private readonly string _firmName;

        public event Action? RequestClose;
        public event Action? DataChanged;

        public EmployeeData Data { get; private set; }

        private int _tabIndex;
        public int TabIndex
        {
            get => _tabIndex;
            set => SetProperty(ref _tabIndex, value);
        }

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set => SetProperty(ref _isEditMode, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _passportFilePath = string.Empty;
        public string PassportFilePath
        {
            get => _passportFilePath;
            set => SetProperty(ref _passportFilePath, value);
        }

        private string _visaFilePath = string.Empty;
        public string VisaFilePath
        {
            get => _visaFilePath;
            set => SetProperty(ref _visaFilePath, value);
        }

        private string _insuranceFilePath = string.Empty;
        public string InsuranceFilePath
        {
            get => _insuranceFilePath;
            set => SetProperty(ref _insuranceFilePath, value);
        }

        private string _photoFilePath = string.Empty;
        public string PhotoFilePath
        {
            get => _photoFilePath;
            set => SetProperty(ref _photoFilePath, value);
        }

        private bool _hasPassport;
        public bool HasPassport
        {
            get => _hasPassport;
            set => SetProperty(ref _hasPassport, value);
        }

        private bool _hasVisa;
        public bool HasVisa
        {
            get => _hasVisa;
            set => SetProperty(ref _hasVisa, value);
        }

        private bool _hasInsurance;
        public bool HasInsurance
        {
            get => _hasInsurance;
            set => SetProperty(ref _hasInsurance, value);
        }

        private bool _hasPhoto;
        public bool HasPhoto
        {
            get => _hasPhoto;
            set => SetProperty(ref _hasPhoto, value);
        }

        private bool _passportIsPdf;
        public bool PassportIsPdf
        {
            get => _passportIsPdf;
            set => SetProperty(ref _passportIsPdf, value);
        }

        private bool _visaIsPdf;
        public bool VisaIsPdf
        {
            get => _visaIsPdf;
            set => SetProperty(ref _visaIsPdf, value);
        }

        private bool _insuranceIsPdf;
        public bool InsuranceIsPdf
        {
            get => _insuranceIsPdf;
            set => SetProperty(ref _insuranceIsPdf, value);
        }

        public string FullName => $"{Data.FirstName} {Data.LastName}";

        // ---- Generate Document ----
        private bool _isGenerateDialogOpen;
        public bool IsGenerateDialogOpen
        {
            get => _isGenerateDialogOpen;
            set => SetProperty(ref _isGenerateDialogOpen, value);
        }

        private ObservableCollection<TemplateEntry> _availableTemplates = new();
        public ObservableCollection<TemplateEntry> AvailableTemplates
        {
            get => _availableTemplates;
            set => SetProperty(ref _availableTemplates, value);
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set => SetProperty(ref _isGenerating, value);
        }

        private string _generateStatusMessage = string.Empty;
        public string GenerateStatusMessage
        {
            get => _generateStatusMessage;
            set => SetProperty(ref _generateStatusMessage, value);
        }

        // ---- Commands ----
        public ICommand CloseCommand { get; }
        public ICommand ShowDocumentsCommand { get; }
        public ICommand ShowProfileCommand { get; }
        public ICommand EditProfileCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand ReplacePassportCommand { get; }
        public ICommand ReplaceVisaCommand { get; }
        public ICommand ReplaceInsuranceCommand { get; }
        public ICommand ReplacePhotoCommand { get; }
        public ICommand OpenPassportCommand { get; }
        public ICommand OpenVisaCommand { get; }
        public ICommand OpenInsuranceCommand { get; }
        public ICommand OpenPhotoCommand { get; }
        public ICommand OpenGenerateDialogCommand { get; }
        public ICommand CloseGenerateDialogCommand { get; }
        public ICommand GenerateFromTemplateCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ExtendVisaCommand { get; }
        public ICommand ExtendInsuranceCommand { get; }
        public ICommand ConfirmExtendCommand { get; }
        public ICommand CancelExtendCommand { get; }
        public ICommand ArchiveEmployeeCommand { get; }
        public ICommand ConfirmArchiveCommand { get; }
        public ICommand CancelArchiveCommand { get; }
        public ICommand ShowHistoryCommand { get; }

        // History
        private ObservableCollection<EmployeeHistoryEntry> _historyEntries = new();
        public ObservableCollection<EmployeeHistoryEntry> HistoryEntries
        {
            get => _historyEntries;
            set => SetProperty(ref _historyEntries, value);
        }

        private bool _hasHistory;
        public bool HasHistory
        {
            get => _hasHistory;
            set => SetProperty(ref _hasHistory, value);
        }

        // Extend dialog
        private bool _isExtendDialogOpen;
        public bool IsExtendDialogOpen
        {
            get => _isExtendDialogOpen;
            set => SetProperty(ref _isExtendDialogOpen, value);
        }

        private string _extendDialogTitle = string.Empty;
        public string ExtendDialogTitle
        {
            get => _extendDialogTitle;
            set => SetProperty(ref _extendDialogTitle, value);
        }

        private string _newExpiryDate = string.Empty;
        public string NewExpiryDate
        {
            get => _newExpiryDate;
            set => SetProperty(ref _newExpiryDate, value);
        }

        private string _extendType = string.Empty;

        // Archive dialog
        private bool _isArchiveDialogOpen;
        public bool IsArchiveDialogOpen
        {
            get => _isArchiveDialogOpen;
            set => SetProperty(ref _isArchiveDialogOpen, value);
        }

        private string _archiveEndDate = string.Empty;
        public string ArchiveEndDate
        {
            get => _archiveEndDate;
            set => SetProperty(ref _archiveEndDate, value);
        }

        private string _archiveStatus = string.Empty;
        public string ArchiveStatus
        {
            get => _archiveStatus;
            set => SetProperty(ref _archiveStatus, value);
        }

        // Available statuses for ComboBox
        public List<string> AvailableStatuses { get; } = new()
        {
            "Активний", "У відпустці", "Звільнений", "Очікує документи"
        };

        public EmployeeDetailsViewModel(string firmName, string employeeFolder, EmployeeService? employeeService = null)
        {
            _firmName = firmName;
            _employeeFolder = employeeFolder;
            _employeeService = employeeService ?? App.EmployeeService;

            Data = _employeeService.LoadEmployeeData(employeeFolder) ?? new EmployeeData();
            RefreshDocuments();

            CloseCommand = new RelayCommand(o => RequestClose?.Invoke());
            ShowDocumentsCommand = new RelayCommand(o => TabIndex = 0);
            ShowProfileCommand = new RelayCommand(o => TabIndex = 1);
            ShowHistoryCommand = new RelayCommand(o => { TabIndex = 2; LoadHistory(); });
            EditProfileCommand = new RelayCommand(o => IsEditMode = true);
            SaveProfileCommand = new RelayCommand(o => SaveProfile());
            CancelEditCommand = new RelayCommand(o => CancelEdit());

            ReplacePassportCommand = new RelayCommand(o => ReplaceDocument("passport"));
            ReplaceVisaCommand = new RelayCommand(o => ReplaceDocument("visa"));
            ReplaceInsuranceCommand = new RelayCommand(o => ReplaceDocument("insurance"));
            ReplacePhotoCommand = new RelayCommand(o => ReplaceDocument("photo"));

            OpenPassportCommand = new RelayCommand(o => OpenFile(PassportFilePath), o => HasPassport);
            OpenVisaCommand = new RelayCommand(o => OpenFile(VisaFilePath), o => HasVisa);
            OpenInsuranceCommand = new RelayCommand(o => OpenFile(InsuranceFilePath), o => HasInsurance);
            OpenPhotoCommand = new RelayCommand(o => OpenFile(PhotoFilePath), o => HasPhoto);

            OpenGenerateDialogCommand = new RelayCommand(o => OpenGenerateDialog());
            CloseGenerateDialogCommand = new RelayCommand(o => IsGenerateDialogOpen = false);
            GenerateFromTemplateCommand = new RelayCommand(o => GenerateDocument(o as TemplateEntry));

            OpenFolderCommand = new RelayCommand(o =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _employeeFolder,
                        UseShellExecute = true
                    });
                }
                catch { StatusMessage = "Не вдалося відкрити папку."; }
            });

            ExtendVisaCommand = new RelayCommand(o => ShowExtendDialog("visa"));
            ExtendInsuranceCommand = new RelayCommand(o => ShowExtendDialog("insurance"));
            ConfirmExtendCommand = new RelayCommand(o => ConfirmExtend());
            CancelExtendCommand = new RelayCommand(o => IsExtendDialogOpen = false);

            ArchiveEmployeeCommand = new RelayCommand(o =>
            {
                ArchiveEndDate = DateTime.Today.ToString("dd.MM.yyyy");
                ArchiveStatus = string.Empty;
                IsArchiveDialogOpen = true;
            });
            ConfirmArchiveCommand = new RelayCommand(o => ConfirmArchive());
            CancelArchiveCommand = new RelayCommand(o => IsArchiveDialogOpen = false);
        }

        private void ShowExtendDialog(string type)
        {
            _extendType = type;
            ExtendDialogTitle = type == "visa" ? "Подовжити візу" : "Подовжити страховку";
            NewExpiryDate = type == "visa" ? Data.VisaExpiry : Data.InsuranceExpiry;
            IsExtendDialogOpen = true;
        }

        private void ConfirmExtend()
        {
            if (string.IsNullOrWhiteSpace(NewExpiryDate))
            {
                StatusMessage = "Введіть нову дату.";
                return;
            }

            var oldDate = _extendType == "visa" ? Data.VisaExpiry : Data.InsuranceExpiry;
            var fieldName = _extendType == "visa" ? "Віза до" : "Страховка до";
            var actionName = _extendType == "visa" ? "Подовження візи" : "Подовження страховки";

            if (_extendType == "visa")
                Data.VisaExpiry = NewExpiryDate;
            else if (_extendType == "insurance")
                Data.InsuranceExpiry = NewExpiryDate;

            if (_employeeService.SaveEmployeeData(_employeeFolder, Data))
            {
                _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
                {
                    Action = actionName,
                    Field = fieldName,
                    OldValue = oldDate,
                    NewValue = NewExpiryDate,
                    Description = $"{fieldName}: {oldDate} → {NewExpiryDate}"
                });

                IsExtendDialogOpen = false;
                OnPropertyChanged(nameof(Data));
                DataChanged?.Invoke();
                StatusMessage = string.Empty;
            }
            else
            {
                StatusMessage = "Не вдалося зберегти.";
            }
        }

        private void ConfirmArchive()
        {
            if (string.IsNullOrWhiteSpace(ArchiveEndDate))
            {
                ArchiveStatus = "Введіть дату закінчення співпраці.";
                return;
            }

            try
            {
                _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
                {
                    Action = "Переміщено в архів",
                    Field = "Архів",
                    OldValue = _firmName,
                    NewValue = ArchiveEndDate,
                    Description = $"Переміщено в архів з фірми {_firmName}. Дата закінчення: {ArchiveEndDate}"
                });

                var result = _employeeService.ArchiveEmployee(_employeeFolder, _firmName, ArchiveEndDate);
                if (!string.IsNullOrEmpty(result))
                {
                    IsArchiveDialogOpen = false;
                    DataChanged?.Invoke();
                    RequestClose?.Invoke();
                }
                else
                {
                    ArchiveStatus = "Помилка при архівуванні.";
                }
            }
            catch (Exception ex)
            {
                ArchiveStatus = $"Помилка: {ex.Message}";
            }
        }

        private void OpenGenerateDialog()
        {
            GenerateStatusMessage = string.Empty;
            var templates = App.TemplateService.GetTemplates(_firmName);
            AvailableTemplates = new ObservableCollection<TemplateEntry>(templates);
            IsGenerateDialogOpen = true;
        }

        private void GenerateDocument(TemplateEntry? template)
        {
            if (template == null) return;

            try
            {
                IsGenerating = true;
                GenerateStatusMessage = "Генерація документа...";

                var templateFullPath = App.TemplateService.GetTemplateFullPath(_firmName, template.FilePath);
                if (!File.Exists(templateFullPath))
                {
                    GenerateStatusMessage = "Файл шаблону не знайдено.";
                    IsGenerating = false;
                    return;
                }

                var ext = Path.GetExtension(templateFullPath).ToLower();
                var format = template.Format?.ToUpper() ?? ext.TrimStart('.').ToUpper();

                if (format == "PDF")
                {
                    var tagValues = App.TagCatalogService.GetTagValueMapForEmployee(_firmName, Data);
                    var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.pdf";
                    var sanitized = SanitizeFileName(outputFileName);
                    var outputPath = Path.Combine(_employeeFolder, sanitized);

                    App.DocumentGenerationService.GeneratePdf(templateFullPath, outputPath, tagValues);
                    GenerateStatusMessage = $"Документ згенеровано: {sanitized}";
                    DocumentGenerationService.OpenFile(outputPath);
                    IsGenerating = false;
                    return;
                }

                if (format == "DOCX")
                {
                    var tagValues = App.TagCatalogService.GetTagValueMapForEmployee(_firmName, Data);

                    // Check if built-in editor RTF template exists
                    var templateFolder = Path.GetDirectoryName(templateFullPath) ?? string.Empty;
                    var rtfPath = Path.Combine(templateFolder, "content.rtf");

                    if (File.Exists(rtfPath))
                    {
                        // Generate from RTF template (created with built-in editor)
                        var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.rtf";
                        var sanitized = SanitizeFileName(outputFileName);
                        var outputPath = Path.Combine(_employeeFolder, sanitized);

                        App.DocumentGenerationService.GenerateFromRtf(rtfPath, outputPath, tagValues);
                        GenerateStatusMessage = $"Документ згенеровано: {sanitized}";
                        DocumentGenerationService.OpenFile(outputPath);
                    }
                    else
                    {
                        // Generate from DOCX template (imported file, edited externally)
                        var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.docx";
                        var sanitized = SanitizeFileName(outputFileName);
                        var outputPath = Path.Combine(_employeeFolder, sanitized);

                        App.DocumentGenerationService.GenerateDocx(templateFullPath, outputPath, tagValues);
                        GenerateStatusMessage = $"Документ згенеровано: {sanitized}";
                        DocumentGenerationService.OpenFile(outputPath);
                    }
                }
                else if (format == "XLSX")
                {
                    var tagValues = App.TagCatalogService.GetTagValueMapForEmployee(_firmName, Data);
                    var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.xlsx";
                    var sanitized = SanitizeFileName(outputFileName);
                    var outputPath = Path.Combine(_employeeFolder, sanitized);

                    App.DocumentGenerationService.GenerateXlsx(templateFullPath, outputPath, tagValues);
                    GenerateStatusMessage = $"Документ згенеровано: {sanitized}";
                    DocumentGenerationService.OpenFile(outputPath);
                }
                else
                {
                    GenerateStatusMessage = $"Непідтримуваний формат: {format}";
                }
            }
            catch (Exception ex)
            {
                GenerateStatusMessage = $"Помилка: {ex.Message}";
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        private void LoadHistory()
        {
            try
            {
                var entries = _employeeService.LoadHistory(_employeeFolder);
                HistoryEntries = new ObservableCollection<EmployeeHistoryEntry>(entries);
                HasHistory = HistoryEntries.Count > 0;
            }
            catch
            {
                HistoryEntries = new ObservableCollection<EmployeeHistoryEntry>();
                HasHistory = false;
            }
        }

        private void SaveProfile()
        {
            var oldData = _employeeService.LoadEmployeeData(_employeeFolder);

            if (_employeeService.SaveEmployeeData(_employeeFolder, Data))
            {
                if (oldData != null)
                    _employeeService.RecordChanges(_employeeFolder, oldData, Data);

                IsEditMode = false;
                DataChanged?.Invoke();
            }
            else
            {
                StatusMessage = "Не вдалося зберегти анкету.";
            }
        }

        private void CancelEdit()
        {
            var data = _employeeService.LoadEmployeeData(_employeeFolder);
            if (data != null)
            {
                Data = data;
                OnPropertyChanged(nameof(Data));
                OnPropertyChanged(nameof(FullName));
            }
            IsEditMode = false;
        }

        private void ReplaceDocument(string type)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.pdf"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                if (type == "passport")
                {
                    Data.Files.Passport = _employeeService.SaveDocumentFromSource(dialog.FileName, _employeeFolder, $"{Data.FirstName} {Data.LastName} - Pass");
                }
                else if (type == "visa")
                {
                    Data.Files.Visa = _employeeService.SaveDocumentFromSource(dialog.FileName, _employeeFolder, $"{Data.FirstName} {Data.LastName} - Viza");
                }
                else if (type == "insurance")
                {
                    Data.Files.Insurance = _employeeService.SaveDocumentFromSource(dialog.FileName, _employeeFolder, $"{Data.FirstName} {Data.LastName} - {Data.InsuranceCompanyShort}");
                }
                else if (type == "photo")
                {
                    Data.Files.Photo = _employeeService.SaveDocumentFromSource(dialog.FileName, _employeeFolder, $"{Data.FirstName} {Data.LastName} - Photo");
                }

                _employeeService.SaveEmployeeData(_employeeFolder, Data);
                RefreshDocuments();
                DataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private void RefreshDocuments()
        {
            PassportFilePath = BuildPath(Data.Files.Passport);
            VisaFilePath = BuildPath(Data.Files.Visa);
            InsuranceFilePath = BuildPath(Data.Files.Insurance);
            PhotoFilePath = BuildPath(Data.Files.Photo);

            HasPassport = File.Exists(PassportFilePath);
            HasVisa = File.Exists(VisaFilePath);
            HasInsurance = File.Exists(InsuranceFilePath);
            HasPhoto = File.Exists(PhotoFilePath);

            PassportIsPdf = IsPdf(PassportFilePath);
            VisaIsPdf = IsPdf(VisaFilePath);
            InsuranceIsPdf = IsPdf(InsuranceFilePath);
        }

        private string BuildPath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;
            return Path.Combine(_employeeFolder, fileName);
        }

        private static bool IsPdf(string path)
        {
            return !string.IsNullOrEmpty(path) && Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch
            {
                StatusMessage = "Не вдалося відкрити файл.";
            }
        }
    }
}
