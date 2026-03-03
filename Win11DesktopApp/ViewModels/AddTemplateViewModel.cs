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
                    // Clear file when switching to DOCX
                    if (value == "DOCX")
                        UploadedFilePath = string.Empty;
                }
            }
        }

        /// <summary>True when DOCX format is selected — file is not needed.</summary>
        public bool IsDocxFormat => SelectedFormat == "DOCX";

        /// <summary>True when file upload is required (XLSX, PDF).</summary>
        public bool IsFileRequired => SelectedFormat != "DOCX";

        private string _uploadedFilePath = string.Empty;
        public string UploadedFilePath 
        { 
            get => _uploadedFilePath; 
            set 
            {
                SetProperty(ref _uploadedFilePath, value);
                OnPropertyChanged(nameof(IsFileUploaded));
                OnPropertyChanged(nameof(UploadedFileName));
            } 
        }

        public bool IsFileUploaded => !string.IsNullOrEmpty(UploadedFilePath);
        public string UploadedFileName => System.IO.Path.GetFileName(UploadedFilePath);

        public ICommand UploadFileCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action? RequestClose;

        private readonly string _firmName;

        public AddTemplateViewModel(string firmName)
        {
            _firmName = firmName;

            UploadFileCommand = new RelayCommand(o => UploadFile(), o => IsFileRequired);
            SaveCommand = new RelayCommand(o => Save(), o => CanSave());
            CancelCommand = new RelayCommand(o => RequestClose?.Invoke());
        }

        private void UploadFile()
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "All Supported Formats|*.docx;*.xlsx;*.pdf|Word Documents|*.docx|Excel Workbooks|*.xlsx|PDF Documents|*.pdf";

            if (dialog.ShowDialog() == true)
            {
                UploadedFilePath = dialog.FileName;

                var detected = App.TemplateService.DetectTemplateFormat(UploadedFilePath);
                if (detected == null)
                {
                    UploadedFilePath = string.Empty;
                    MessageBox.Show(
                        Application.Current?.TryFindResource("MsgUnsupportedFormat") as string ?? "Unsupported format.",
                        Application.Current?.TryFindResource("TitleError") as string ?? "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SelectedFormat = detected;
            }
        }

        private bool CanSave()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return false;

            // DOCX doesn't require a file — will use built-in editor
            if (SelectedFormat == "DOCX")
                return true;

            // XLSX/PDF require a file
            return !string.IsNullOrEmpty(UploadedFilePath);
        }

        private void Save()
        {
            if (SelectedFormat == "DOCX")
            {
                // Create template without a source file
                App.TemplateService.AddTemplateWithoutFile(_firmName, Name, Description, SelectedFormat);
            }
            else
            {
                if (!App.TemplateService.TryValidateTemplateFile(UploadedFilePath, out var detectedFormat, out var error))
                {
                    App.TemplateService.LogTemplateError($"AddTemplate validation failed: {UploadedFilePath} | {error}");
                    MessageBox.Show(error, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SelectedFormat = detectedFormat;
                App.TemplateService.AddTemplate(_firmName, Name, Description, SelectedFormat, UploadedFilePath);
            }
            App.ActivityLogService.Log("TemplateAdded", "Template", _firmName, "",
                $"Додано шаблон «{Name}» ({SelectedFormat}) до {_firmName}");
            RequestClose?.Invoke();
        }
    }
}
