using System.Windows;
using System.Windows.Input;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using Win11DesktopApp.Services;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.ViewModels
{
    public class AddTemplateViewModel : ViewModelBase
    {
        private readonly TemplateService _templateService;
        private readonly ActivityLogService _activityLogService;
        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _description = string.Empty;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private string _selectedFormat = "DOCX";
        public string SelectedFormat
        {
            get => _selectedFormat;
            set
            {
                if (SetProperty(ref _selectedFormat, value))
                {
                    OnPropertyChanged(nameof(IsDocxFormat));
                    OnPropertyChanged(nameof(IsFileRequired));
                    OnPropertyChanged(nameof(ShowDocxCreationOptions));
                    OnPropertyChanged(nameof(ShowStandardFileUpload));
                    OnPropertyChanged(nameof(ShowStandardUploadedFile));
                    OnPropertyChanged(nameof(UploadButtonText));
                    OnPropertyChanged(nameof(DocxCreationHint));
                    if (value == "DOCX" && !IsImportDocx)
                        UploadedFilePath = string.Empty;
                }
            }
        }

        public bool IsDocxFormat => SelectedFormat == "DOCX";

        private bool _isImportDocx;
        public bool IsImportDocx
        {
            get => _isImportDocx;
            set
            {
                if (SetProperty(ref _isImportDocx, value))
                {
                    OnPropertyChanged(nameof(IsEmptyDocx));
                    OnPropertyChanged(nameof(IsFileRequired));
                    OnPropertyChanged(nameof(ShowDocxCreationOptions));
                    OnPropertyChanged(nameof(ShowStandardFileUpload));
                    OnPropertyChanged(nameof(ShowStandardUploadedFile));
                    OnPropertyChanged(nameof(UploadButtonText));
                    OnPropertyChanged(nameof(DocxCreationHint));
                    if (!value && SelectedFormat == "DOCX")
                        UploadedFilePath = string.Empty;
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsEmptyDocx
        {
            get => !IsImportDocx;
            set => IsImportDocx = !value;
        }

        public bool ShowDocxCreationOptions => SelectedFormat == "DOCX";

        public bool ShowStandardFileUpload => SelectedFormat != "DOCX";

        public bool ShowStandardUploadedFile => ShowStandardFileUpload && IsFileUploaded;

        public bool IsFileRequired => SelectedFormat != "DOCX" || IsImportDocx;

        public string UploadButtonText => SelectedFormat == "DOCX"
            ? Res("TplSelectDocxImport")
            : (Application.Current?.TryFindResource("BtnUploadFile") as string ?? "Upload file");

        public string DocxCreationHint => IsImportDocx
            ? Res("TplDocxImportHint")
            : Res("TplDocxEmptyHint");

        private string _uploadedFilePath = string.Empty;
        public string UploadedFilePath 
        { 
            get => _uploadedFilePath; 
            set 
            {
                SetProperty(ref _uploadedFilePath, value);
                OnPropertyChanged(nameof(IsFileUploaded));
                OnPropertyChanged(nameof(UploadedFileName));
                OnPropertyChanged(nameof(ShowStandardUploadedFile));
                CommandManager.InvalidateRequerySuggested();
            } 
        }

        public bool IsFileUploaded => !string.IsNullOrEmpty(UploadedFilePath);
        public string UploadedFileName => System.IO.Path.GetFileName(UploadedFilePath);

        public ICommand UploadFileCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action? RequestClose;
        public TemplateEntry? CreatedTemplate { get; private set; }

        private readonly string _firmName;

        public AddTemplateViewModel(
            string firmName,
            TemplateService? templateService = null,
            ActivityLogService? activityLogService = null)
        {
            _firmName = firmName;
            _templateService = templateService ?? throw new InvalidOperationException("TemplateService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");

            UploadFileCommand = new RelayCommand(o => UploadFile(), o => IsFileRequired);
            SaveCommand = new RelayCommand(o => Save(), o => CanSave());
            CancelCommand = new RelayCommand(o => RequestClose?.Invoke());
        }

        private void UploadFile()
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = SelectedFormat == "DOCX"
                ? "Word Documents|*.docx"
                : "All Supported Formats|*.docx;*.xlsx;*.pdf|Word Documents|*.docx|Excel Workbooks|*.xlsx|PDF Documents|*.pdf";

            if (dialog.ShowDialog() == true)
            {
                UploadedFilePath = dialog.FileName;

                var detected = _templateService.DetectTemplateFormat(UploadedFilePath);
                if (detected == null)
                {
                    UploadedFilePath = string.Empty;
                    MessageBox.Show(
                        Application.Current?.TryFindResource("MsgUnsupportedFormat") as string ?? "Unsupported format.",
                        Application.Current?.TryFindResource("TitleError") as string ?? "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (SelectedFormat == "DOCX" && detected != "DOCX")
                {
                    UploadedFilePath = string.Empty;
                    MessageBox.Show(
                        Res("TplDocxImportOnlyDocx"),
                        Application.Current?.TryFindResource("TitleError") as string ?? "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SelectedFormat = detected;
                if (detected == "DOCX")
                    IsImportDocx = true;
            }
        }

        private bool CanSave()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return false;

            if (SelectedFormat == "DOCX")
                return !IsImportDocx || !string.IsNullOrEmpty(UploadedFilePath);

            // XLSX/PDF require a file
            return !string.IsNullOrEmpty(UploadedFilePath);
        }

        private void Save()
        {
            CreatedTemplate = null;

            if (SelectedFormat == "DOCX")
            {
                if (IsImportDocx)
                {
                    if (!_templateService.TryValidateTemplateFile(UploadedFilePath, out var detectedFormat, out var error) || detectedFormat != "DOCX")
                    {
                        _templateService.LogTemplateError($"AddTemplate DOCX import validation failed: {UploadedFilePath} | {error}");
                        MessageBox.Show(string.IsNullOrWhiteSpace(error) ? Res("TplDocxImportInvalid") : error, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    CreatedTemplate = _templateService.AddTemplateFromDocxImport(_firmName, Name, Description, UploadedFilePath);
                }
                else
                {
                    CreatedTemplate = _templateService.AddTemplateWithoutFile(_firmName, Name, Description, SelectedFormat);
                }
            }
            else
            {
                if (!_templateService.TryValidateTemplateFile(UploadedFilePath, out var detectedFormat, out var error))
                {
                    _templateService.LogTemplateError($"AddTemplate validation failed: {UploadedFilePath} | {error}");
                    MessageBox.Show(error, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SelectedFormat = detectedFormat;
                CreatedTemplate = _templateService.AddTemplate(_firmName, Name, Description, SelectedFormat, UploadedFilePath);
            }
            _activityLogService.Log("TemplateAdded", "Template", _firmName, "",
                $"Додано шаблон «{Name}» ({SelectedFormat}) до {_firmName}");
            RequestClose?.Invoke();
        }
    }
}
