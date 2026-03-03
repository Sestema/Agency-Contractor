using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.Views;
using EmployeeModels = Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.ViewModels
{
    public class AddCandidateViewModel : ViewModelBase
    {
        private readonly CandidateService _service;
        private readonly EmployeeService _employeeService;
        private readonly string _tempFolder;

        public event Action? RequestClose;
        public event Action? CropSourceChanged;

        public CandidateData Data { get; } = new();

        // ===== Step navigation =====
        private int _stepIndex;
        public int StepIndex
        {
            get => _stepIndex;
            set
            {
                if (SetProperty(ref _stepIndex, value))
                    OnPropertyChanged(nameof(IsLastStep));
            }
        }

        public bool IsLastStep => StepIndex == 2;

        // ===== Step 0: Passport upload =====
        private EmployeeModels.EmployeeDocumentTemp _passportDoc = new();
        public EmployeeModels.EmployeeDocumentTemp PassportDoc
        {
            get => _passportDoc;
            set { SetProperty(ref _passportDoc, value); OnPropertyChanged(nameof(HasPassportImage)); }
        }

        private string _passportPreviewPath = "";
        public string PassportPreviewPath
        {
            get => _passportPreviewPath;
            set
            {
                if (SetProperty(ref _passportPreviewPath, value))
                {
                    OnPropertyChanged(nameof(HasPassportImage));
                    OnPropertyChanged(nameof(CurrentCropImagePath));
                }
            }
        }

        private string _passportFileName = "";
        public string PassportFileName
        {
            get => _passportFileName;
            set => SetProperty(ref _passportFileName, value);
        }

        public bool HasPassportImage => !string.IsNullOrEmpty(PassportPreviewPath);

        // ===== Step 1: Crop photo from passport =====
        private string _croppedPhotoPath = "";
        public string CroppedPhotoPath
        {
            get => _croppedPhotoPath;
            set { SetProperty(ref _croppedPhotoPath, value); OnPropertyChanged(nameof(HasCroppedPhoto)); }
        }

        public bool HasCroppedPhoto => !string.IsNullOrEmpty(_croppedPhotoPath) && File.Exists(_croppedPhotoPath);

        private Int32Rect _cropRect = new Int32Rect(0, 0, 200, 200);
        public Int32Rect CropRect
        {
            get => _cropRect;
            set => SetProperty(ref _cropRect, value);
        }

        public ObservableCollection<string> CropTargets { get; } = new();

        private string _selectedCropTarget = "";
        public string SelectedCropTarget
        {
            get => _selectedCropTarget;
            set
            {
                if (SetProperty(ref _selectedCropTarget, value))
                {
                    OnPropertyChanged(nameof(CurrentCropImagePath));
                    OnPropertyChanged(nameof(IsCropPhotoMode));
                    CropSourceChanged?.Invoke();
                }
            }
        }

        public bool IsCropPhotoMode => _selectedCropTarget == Res("CropPhoto");

        public string CurrentCropImagePath => PassportPreviewPath;

        private bool _keepAspectRatio;
        public bool KeepAspectRatio
        {
            get => _keepAspectRatio;
            set => SetProperty(ref _keepAspectRatio, value);
        }

        private double _cropAspectRatio = 1.0;
        public double CropAspectRatio
        {
            get => _cropAspectRatio;
            set => SetProperty(ref _cropAspectRatio, value);
        }

        // ===== Step 2: Data fields =====
        private bool _isAllCzech = true;
        public bool IsAllCzech
        {
            get => _isAllCzech;
            set
            {
                if (SetProperty(ref _isAllCzech, value))
                {
                    Data.LocationPreference = value ? "all" : "specific";
                    OnPropertyChanged(nameof(IsSpecificLocation));
                }
            }
        }

        public bool IsSpecificLocation
        {
            get => !_isAllCzech;
            set => IsAllCzech = !value;
        }

        public ObservableCollection<string> KnownPositions { get; } = new();

        // ===== Commands =====
        public ICommand NextCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand UploadPassportCommand { get; }
        public ICommand ApplyCropCommand { get; }
        public ICommand RotateLeftCommand { get; }
        public ICommand RotateRightCommand { get; }
        public ICommand EnhanceDocumentCommand { get; }

        public AddCandidateViewModel()
        {
            _service = App.CandidateService;
            _employeeService = App.EmployeeService;
            _tempFolder = _employeeService.CreateTempFolder();

            var positions = _service.GetAllPositions();
            foreach (var p in positions) KnownPositions.Add(p);

            CropTargets.Add(Res("CropPassport"));
            CropTargets.Add(Res("CropPhoto"));
            _selectedCropTarget = Res("CropPhoto");

            NextCommand = new RelayCommand(o => GoNext(), o => CanGoNext());
            BackCommand = new RelayCommand(o => GoBack(), o => StepIndex > 0);
            CancelCommand = new RelayCommand(o => Close());
            SaveCommand = new RelayCommand(o => Save(), o => CanSave());

            UploadPassportCommand = new RelayCommand(o => UploadPassport());
            ApplyCropCommand = new RelayCommand(o => ApplyCrop());
            RotateLeftCommand = new RelayCommand(o => RotateCurrentImage(-90));
            RotateRightCommand = new RelayCommand(o => RotateCurrentImage(90));
            EnhanceDocumentCommand = new RelayCommand(o => EnhanceCurrentDocument(), o => !IsCropPhotoMode);
        }

        private void GoNext()
        {
            if (StepIndex == 0 && !HasPassportImage)
            {
                StepIndex = 2;
                return;
            }
            if (StepIndex < 2) StepIndex++;
        }

        private bool CanGoNext()
        {
            if (StepIndex == 0) return true;
            if (StepIndex == 1) return true;
            return false;
        }

        private void GoBack()
        {
            if (StepIndex == 2 && !HasPassportImage)
            {
                StepIndex = 0;
                return;
            }
            if (StepIndex > 0) StepIndex--;
        }

        private bool CanSave()
        {
            return !string.IsNullOrWhiteSpace(Data.FirstName)
                && !string.IsNullOrWhiteSpace(Data.LastName);
        }

        private void Close()
        {
            try { _employeeService.CleanupTempFolder(_tempFolder); } catch { }
            RequestClose?.Invoke();
        }

        private void Save()
        {
            Data.DateAdded = DateTime.Now.ToString("yyyy-MM-dd");

            string? passportPath = null;
            if (_passportDoc != null && !string.IsNullOrEmpty(_passportDoc.ImagePath) && File.Exists(_passportDoc.ImagePath))
                passportPath = _passportDoc.ImagePath;
            else if (_passportDoc != null && !string.IsNullOrEmpty(_passportDoc.PdfPath) && File.Exists(_passportDoc.PdfPath))
                passportPath = _passportDoc.PdfPath;

            string? photoPath = HasCroppedPhoto ? CroppedPhotoPath : null;

            var folder = _service.SaveNewCandidate(Data, photoPath, passportPath);
            if (string.IsNullOrEmpty(folder))
            {
                MessageBox.Show(Res("CandSaveFail"), Res("TitleError"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            App.ActivityLogService?.Log("CandidateAdded", "Candidate", "",
                $"{Data.FirstName} {Data.LastName}",
                $"{Data.FirstName} {Data.LastName} ({Data.DesiredPosition})");

            try { _employeeService.CleanupTempFolder(_tempFolder); } catch { }
            RequestClose?.Invoke();
        }

        // ===== Passport upload =====
        private void UploadPassport()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.pdf",
                Title = Res("CandSelectPassport")
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var temp = _employeeService.PrepareTempDocument(dialog.FileName, _tempFolder, "passport");
                PassportDoc = temp;
                PassportPreviewPath = temp.IsPdf ? string.Empty : temp.ImagePath;
                PassportFileName = Path.GetFileName(dialog.FileName);
                OnPropertyChanged(nameof(PassportDoc));
                CropSourceChanged?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== Crop =====
        private void ApplyCrop()
        {
            if (_selectedCropTarget == Res("CropPhoto"))
            {
                if (string.IsNullOrEmpty(PassportPreviewPath))
                {
                    MessageBox.Show(Res("MsgDocNotLoaded"), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dest = Path.Combine(_tempFolder, "candidate_photo.jpg");
                try
                {
                    _employeeService.CreateCroppedPhoto(PassportPreviewPath, CropRect, dest);
                    CroppedPhotoPath = dest;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(Res("MsgCropHint"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RotateCurrentImage(int angle)
        {
            if (string.IsNullOrEmpty(PassportPreviewPath))
            {
                MessageBox.Show(Res("MsgNoImageRotate"), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var rotatedPath = Path.Combine(_tempFolder, $"rot_{Guid.NewGuid():N}.jpg");
                _employeeService.RotateImage(PassportPreviewPath, angle, rotatedPath);
                PassportPreviewPath = rotatedPath;
                if (PassportDoc != null) PassportDoc.ImagePath = rotatedPath;
                OnPropertyChanged(nameof(CurrentCropImagePath));
                CropSourceChanged?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnhanceCurrentDocument()
        {
            if (string.IsNullOrEmpty(PassportPreviewPath) || !File.Exists(PassportPreviewPath))
            {
                MessageBox.Show(Res("MsgUploadFirst"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ImageEditorWindow editor;
            try
            {
                editor = new ImageEditorWindow(PassportPreviewPath);
                if (editor.LoadFailed) return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося відкрити редактор:\n{ex.Message}",
                    Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            editor.Owner = Application.Current.MainWindow;
            editor.ShowDialog();

            if (editor.Saved && !string.IsNullOrEmpty(editor.ResultPath) && File.Exists(editor.ResultPath))
            {
                var newPath = Path.Combine(_tempFolder, $"enh_{Guid.NewGuid():N}{Path.GetExtension(PassportPreviewPath)}");
                try
                {
                    File.Copy(editor.ResultPath, newPath, true);
                    File.Delete(editor.ResultPath);
                }
                catch { newPath = editor.ResultPath; }

                PassportPreviewPath = newPath;
                if (PassportDoc != null) PassportDoc.ImagePath = newPath;
                OnPropertyChanged(nameof(CurrentCropImagePath));
                CropSourceChanged?.Invoke();
            }
        }
    }
}
