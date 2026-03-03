using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.Models;
using EmployeeModels = Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.ViewModels
{
    public class CarouselDocItem : ViewModelBase
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";

        private string _imagePath = "";
        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public class AddEmployeeWizardViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;
        private readonly EmployerCompany _company;
        private readonly string _tempFolder;
        private CancellationTokenSource _cts = new();

        public event Action? RequestClose;
        public event Action? CropSourceChanged;

        private int _stepIndex;
        public int StepIndex
        {
            get => _stepIndex;
            set
            {
                if (SetProperty(ref _stepIndex, value))
                {
                    OnPropertyChanged(nameof(CurrentStepDisplay));
                    RefreshCarousel();
                    AutoSelectCarouselForStep(value);
                }
            }
        }

        public int CurrentStepDisplay => ActiveSteps.IndexOf(StepIndex) + 1;

        public EmployeeModels.EmployeeData Data { get; } = new EmployeeModels.EmployeeData();

        public ObservableCollection<WorkAddress> CompanyAddresses { get; }
        public ObservableCollection<Position> CompanyPositions { get; }

        private WorkAddress? _selectedWorkAddress;
        public WorkAddress? SelectedWorkAddress
        {
            get => _selectedWorkAddress;
            set => SetProperty(ref _selectedWorkAddress, value);
        }

        private Position? _selectedPosition;
        public Position? SelectedPosition
        {
            get => _selectedPosition;
            set => SetProperty(ref _selectedPosition, value);
        }

        private string _contractType = "HPP";
        public string ContractType
        {
            get => _contractType;
            set => SetProperty(ref _contractType, value);
        }

        // ===== Employee Type =====
        private string _employeeType = "visa";
        public string EmployeeType
        {
            get => _employeeType;
            set
            {
                if (SetProperty(ref _employeeType, value))
                {
                    Data.EmployeeType = value;
                    UpdateCropTargets();
                    OnPropertyChanged(nameof(IsVisaType));
                    OnPropertyChanged(nameof(IsEuCitizenType));
                    OnPropertyChanged(nameof(IsWorkPermitType));
                    OnPropertyChanged(nameof(IsPassportOnlyType));
                    OnPropertyChanged(nameof(ShowVisaUpload));
                    OnPropertyChanged(nameof(ShowWorkPermitUpload));
                    OnPropertyChanged(nameof(ShowPassportUpload));
                    OnPropertyChanged(nameof(ShowPassportPage2Upload));
                    OnPropertyChanged(nameof(ShowInsuranceUpload));
                    OnPropertyChanged(nameof(ActiveSteps));
                    OnPropertyChanged(nameof(TotalSteps));
                    OnPropertyChanged(nameof(PersonalDocPreviewPath));
                }
            }
        }

        public bool IsVisaType => _employeeType == "visa";
        public bool IsEuCitizenType => _employeeType == "eu_citizen";
        public bool IsWorkPermitType => _employeeType == "work_permit";
        public bool IsPassportOnlyType => _employeeType == "passport_only";

        private string _euDocumentType = "passport";
        public string EuDocumentType
        {
            get => _euDocumentType;
            set
            {
                if (SetProperty(ref _euDocumentType, value))
                {
                    Data.EuDocumentType = value;
                    if (value == "id_card")
                        Data.WorkPermitName = "Osvědčení o registraci občana EU";
                    OnPropertyChanged(nameof(IsEuIdCard));
                    OnPropertyChanged(nameof(IsEuPassport));
                    OnPropertyChanged(nameof(ShowPassportPage2Upload));
                    OnPropertyChanged(nameof(ActiveSteps));
                    OnPropertyChanged(nameof(TotalSteps));
                }
            }
        }

        public bool IsEuPassport => _euDocumentType == "passport";
        public bool IsEuIdCard => _euDocumentType == "id_card";

        public bool ShowPassportUpload => true;
        public bool ShowPassportPage2Upload => _employeeType == "eu_citizen" && _euDocumentType == "passport";
        public bool ShowVisaUpload => _employeeType == "visa" || _employeeType == "work_permit";
        public bool ShowWorkPermitUpload => _employeeType == "work_permit";
        public bool ShowInsuranceUpload => _employeeType != "passport_only";

        public string PersonalDocPreviewPath => PassportPreviewPath;

        // ===== Active steps based on employee type =====
        public List<int> ActiveSteps
        {
            get
            {
                return _employeeType switch
                {
                    "eu_citizen" when _euDocumentType == "id_card" => new List<int> { 0, 1, 2, 3, 5, 6 },
                    "eu_citizen" => new List<int> { 0, 1, 2, 8, 3, 5, 6 },
                    "work_permit" => new List<int> { 0, 1, 2, 4, 3, 7, 5, 6 },
                    "passport_only" => new List<int> { 0, 1, 2, 6 },
                    _ => new List<int> { 0, 1, 2, 4, 3, 5, 6 }
                };
            }
        }

        public int TotalSteps => ActiveSteps.Count;

        // ===== Documents =====
        public EmployeeModels.EmployeeDocumentTemp PassportDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp VisaDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp InsuranceDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp PassportPage2Doc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp WorkPermitDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();

        private string _passportPreviewPath = string.Empty;
        public string PassportPreviewPath
        {
            get => _passportPreviewPath;
            set
            {
                if (SetProperty(ref _passportPreviewPath, value))
                    OnPropertyChanged(nameof(PersonalDocPreviewPath));
            }
        }

        private string _visaPreviewPath = string.Empty;
        public string VisaPreviewPath
        {
            get => _visaPreviewPath;
            set => SetProperty(ref _visaPreviewPath, value);
        }

        private string _insurancePreviewPath = string.Empty;
        public string InsurancePreviewPath
        {
            get => _insurancePreviewPath;
            set => SetProperty(ref _insurancePreviewPath, value);
        }

        private string _passportPage2PreviewPath = string.Empty;
        public string PassportPage2PreviewPath
        {
            get => _passportPage2PreviewPath;
            set => SetProperty(ref _passportPage2PreviewPath, value);
        }

        private string _workPermitPreviewPath = string.Empty;
        public string WorkPermitPreviewPath
        {
            get => _workPermitPreviewPath;
            set => SetProperty(ref _workPermitPreviewPath, value);
        }

        public ObservableCollection<string> WorkPermitPagePreviews { get; } = new ObservableCollection<string>();

        // ===== Document Carousel =====
        public ObservableCollection<CarouselDocItem> CarouselItems { get; } = new ObservableCollection<CarouselDocItem>();

        private int _selectedCarouselIndex = -1;
        public int SelectedCarouselIndex
        {
            get => _selectedCarouselIndex;
            set
            {
                if (value < 0 || value >= CarouselItems.Count) return;
                if (SetProperty(ref _selectedCarouselIndex, value))
                {
                    for (int i = 0; i < CarouselItems.Count; i++)
                        CarouselItems[i].IsSelected = i == value;
                    OnPropertyChanged(nameof(CarouselPreviewPath));
                    OnPropertyChanged(nameof(HasCarouselItems));
                }
            }
        }

        public string CarouselPreviewPath =>
            _selectedCarouselIndex >= 0 && _selectedCarouselIndex < CarouselItems.Count
                ? CarouselItems[_selectedCarouselIndex].ImagePath
                : string.Empty;

        public bool HasCarouselItems => CarouselItems.Count > 1;

        public ICommand CarouselPrevCommand { get; }
        public ICommand CarouselNextCommand { get; }
        public ICommand SelectCarouselTabCommand { get; }

        private void RefreshCarousel()
        {
            CarouselItems.Clear();

            if (!string.IsNullOrEmpty(PassportPreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "passport", Label = Res("CarouselPassport"), ImagePath = PassportPreviewPath });
            if (!string.IsNullOrEmpty(PassportPage2PreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "passport2", Label = Res("CarouselPassport2"), ImagePath = PassportPage2PreviewPath });
            if (!string.IsNullOrEmpty(VisaPreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "visa", Label = Res("CarouselVisa"), ImagePath = VisaPreviewPath });
            if (!string.IsNullOrEmpty(InsurancePreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "insurance", Label = Res("CarouselInsurance"), ImagePath = InsurancePreviewPath });
            if (!string.IsNullOrEmpty(WorkPermitPreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "permit", Label = Res("CarouselPermit"), ImagePath = WorkPermitPreviewPath });

            OnPropertyChanged(nameof(HasCarouselItems));

            if (CarouselItems.Count > 0 && (_selectedCarouselIndex < 0 || _selectedCarouselIndex >= CarouselItems.Count))
                SelectedCarouselIndex = 0;
        }

        private void AutoSelectCarouselForStep(int step)
        {
            string targetKey = step switch
            {
                2 => "passport",
                3 => "insurance",
                4 => "visa",
                7 => "permit",
                8 => "passport2",
                _ => ""
            };
            if (string.IsNullOrEmpty(targetKey)) return;
            for (int i = 0; i < CarouselItems.Count; i++)
            {
                if (CarouselItems[i].Key == targetKey)
                {
                    SelectedCarouselIndex = i;
                    return;
                }
            }
        }

        private void CarouselPrev()
        {
            if (_selectedCarouselIndex > 0)
                SelectedCarouselIndex = _selectedCarouselIndex - 1;
        }

        private void CarouselNext()
        {
            if (_selectedCarouselIndex < CarouselItems.Count - 1)
                SelectedCarouselIndex = _selectedCarouselIndex + 1;
        }

        private string _workPermitFileName = string.Empty;
        public string WorkPermitFileName
        {
            get => _workPermitFileName;
            set => SetProperty(ref _workPermitFileName, value);
        }

        private string _croppedPhotoPath = string.Empty;
        public string CroppedPhotoPath
        {
            get => _croppedPhotoPath;
            set => SetProperty(ref _croppedPhotoPath, value);
        }

        private Int32Rect _cropRect = new Int32Rect(0, 0, 200, 200);
        public Int32Rect CropRect
        {
            get => _cropRect;
            set => SetProperty(ref _cropRect, value);
        }

        // ===== Crop document selector =====
        public ObservableCollection<string> CropTargets { get; } = new ObservableCollection<string>
        {
            Res("CropPassport"), Res("CropVisa"), Res("CropInsurance"), Res("CropPhoto")
        };

        private string _selectedCropTarget = Res("CropPhoto");
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

        public string CurrentCropImagePath
        {
            get
            {
                if (_selectedCropTarget == Res("CropVisa")) return VisaPreviewPath;
                if (_selectedCropTarget == Res("CropInsurance")) return InsurancePreviewPath;
                if (_selectedCropTarget == Res("CropPassport2")) return PassportPage2PreviewPath;
                if (_selectedCropTarget == Res("CropPhoto")) return PassportPreviewPath;
                if (_selectedCropTarget == Res("CropPermit")) return WorkPermitPreviewPath;
                return PassportPreviewPath;
            }
        }

        // ===== Aspect ratio =====
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

        // ===== Commands =====
        public ICommand NextCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SaveCommand { get; }

        public ICommand UploadPassportCommand { get; }
        public ICommand UploadVisaCommand { get; }
        public ICommand UploadInsuranceCommand { get; }
        public ICommand UploadPassportPage2Command { get; }
        public ICommand UploadWorkPermitCommand { get; }
        public ICommand ApplyCropCommand { get; }
        public ICommand RotateLeftCommand { get; }
        public ICommand RotateRightCommand { get; }
        public ICommand EnhanceDocumentCommand { get; }
        public ICommand SetEmployeeTypeCommand { get; }
        public ICommand SetEuDocTypeCommand { get; }
        public ICommand AIScanDocumentCommand { get; }

        private bool _isAIScanning;
        public bool IsAIScanning
        {
            get => _isAIScanning;
            set => SetProperty(ref _isAIScanning, value);
        }

        private string _aiScanStatus = "";
        public string AIScanStatus
        {
            get => _aiScanStatus;
            set => SetProperty(ref _aiScanStatus, value);
        }

        public AddEmployeeWizardViewModel(EmployerCompany company, EmployeeService? employeeService = null)
        {
            _company = company;
            _employeeService = employeeService ?? App.EmployeeService ?? throw new InvalidOperationException("EmployeeService is not available");
            _tempFolder = _employeeService.CreateTempFolder();

            CompanyAddresses = company.Addresses;
            CompanyPositions = company.Positions;

            NextCommand = new RelayCommand(o => GoNext(), o => CanGoNext());
            BackCommand = new RelayCommand(o => GoBack(), o => CanGoBack());
            CancelCommand = new RelayCommand(o => TryClose());
            SaveCommand = new RelayCommand(o => SaveEmployee());

            UploadPassportCommand = new RelayCommand(o => UploadDocument("passport"));
            UploadVisaCommand = new RelayCommand(o => UploadDocument("visa"));
            UploadInsuranceCommand = new RelayCommand(o => UploadDocument("insurance"));
            UploadPassportPage2Command = new RelayCommand(o => UploadDocument("passport_page2"));
            UploadWorkPermitCommand = new RelayCommand(o => UploadDocument("work_permit"));
            ApplyCropCommand = new RelayCommand(o => ApplyCrop());
            RotateLeftCommand = new RelayCommand(o => RotateCurrentImage(-90));
            RotateRightCommand = new RelayCommand(o => RotateCurrentImage(90));
            EnhanceDocumentCommand = new RelayCommand(o => EnhanceCurrentDocument(), o => !IsCropPhotoMode);
            SetEmployeeTypeCommand = new RelayCommand(o => EmployeeType = o?.ToString() ?? "visa");
            SetEuDocTypeCommand = new RelayCommand(o => EuDocumentType = o?.ToString() ?? "passport");
            AIScanDocumentCommand = new RelayCommand(async o => await AIScanCurrentStepAsync(),
                o => !_isAIScanning && App.GeminiApiService?.IsConfigured == true);
            CarouselPrevCommand = new RelayCommand(o => CarouselPrev(), o => _selectedCarouselIndex > 0);
            CarouselNextCommand = new RelayCommand(o => CarouselNext(), o => _selectedCarouselIndex < CarouselItems.Count - 1);
            SelectCarouselTabCommand = new RelayCommand(o =>
            {
                if (o is CarouselDocItem item)
                {
                    var idx = CarouselItems.IndexOf(item);
                    if (idx >= 0) SelectedCarouselIndex = idx;
                }
                else if (o is int idx2) SelectedCarouselIndex = idx2;
                else if (o is string s && int.TryParse(s, out var i)) SelectedCarouselIndex = i;
            });
        }

        // ===== Step Navigation =====
        private void GoNext()
        {
            var steps = ActiveSteps;
            int currentIdx = steps.IndexOf(StepIndex);
            if (currentIdx >= 0 && currentIdx < steps.Count - 1)
                StepIndex = steps[currentIdx + 1];
        }

        private bool CanGoNext()
        {
            var steps = ActiveSteps;
            int currentIdx = steps.IndexOf(StepIndex);
            return currentIdx >= 0 && currentIdx < steps.Count - 1;
        }

        private void GoBack()
        {
            var steps = ActiveSteps;
            int currentIdx = steps.IndexOf(StepIndex);
            if (currentIdx > 0)
                StepIndex = steps[currentIdx - 1];
        }

        private bool CanGoBack()
        {
            var steps = ActiveSteps;
            int currentIdx = steps.IndexOf(StepIndex);
            return currentIdx > 0;
        }

        private void UpdateCropTargets()
        {
            CropTargets.Clear();
            CropTargets.Add(Res("CropPassport"));

            if (ShowPassportPage2Upload) CropTargets.Add(Res("CropPassport2"));
            if (ShowVisaUpload) CropTargets.Add(Res("CropVisa"));
            CropTargets.Add(Res("CropInsurance"));
            if (ShowWorkPermitUpload) CropTargets.Add(Res("CropPermit"));
            CropTargets.Add(Res("CropPhoto"));

            SelectedCropTarget = Res("CropPhoto");
        }

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".heic", ".pdf" };

        public void UploadDocumentFromPath(string filePath, string type)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            var ext = Path.GetExtension(filePath);
            if (!AllowedExtensions.Contains(ext))
            {
                MessageBox.Show(Res("DragDropInvalidFormat"), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ProcessUploadedFile(filePath, type);
        }

        private void UploadDocument(string type)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.pdf";
            if (dialog.ShowDialog() != true) return;
            ProcessUploadedFile(dialog.FileName, type);
        }

        private void ProcessUploadedFile(string filePath, string type)
        {
            try
            {
                var temp = _employeeService.PrepareTempDocument(filePath, _tempFolder, type);
                switch (type)
                {
                    case "passport":
                        PassportDoc = temp;
                        PassportPreviewPath = temp.IsPdf ? string.Empty : temp.ImagePath;
                        OnPropertyChanged(nameof(PassportDoc));
                        break;
                    case "visa":
                        VisaDoc = temp;
                        VisaPreviewPath = temp.IsPdf ? string.Empty : temp.ImagePath;
                        OnPropertyChanged(nameof(VisaDoc));
                        break;
                    case "insurance":
                        InsuranceDoc = temp;
                        InsurancePreviewPath = temp.IsPdf ? string.Empty : temp.ImagePath;
                        OnPropertyChanged(nameof(InsuranceDoc));
                        break;
                    case "passport_page2":
                        PassportPage2Doc = temp;
                        PassportPage2PreviewPath = temp.IsPdf ? string.Empty : temp.ImagePath;
                        OnPropertyChanged(nameof(PassportPage2Doc));
                        break;
                    case "work_permit":
                        WorkPermitDoc = temp;
                        WorkPermitPagePreviews.Clear();
                        if (temp.IsPdf)
                        {
                            WorkPermitFileName = Path.GetFileName(filePath);
                            var pages = _employeeService.RenderPdfPages(temp.PdfPath, _tempFolder, "wp_preview");
                            foreach (var p in pages)
                                WorkPermitPagePreviews.Add(p);
                            WorkPermitPreviewPath = pages.Count > 0 ? pages[0] : string.Empty;
                        }
                        else
                        {
                            WorkPermitFileName = string.Empty;
                            WorkPermitPreviewPath = temp.ImagePath;
                        }
                        OnPropertyChanged(nameof(WorkPermitDoc));
                        break;
                }

                OnPropertyChanged(nameof(CurrentCropImagePath));
                RefreshCarousel();
                CropSourceChanged?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RotateCurrentImage(int angle)
        {
            var sourcePath = CurrentCropImagePath;
            if (string.IsNullOrEmpty(sourcePath))
            {
                MessageBox.Show(Res("MsgNoImageRotate"), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var rotatedPath = System.IO.Path.Combine(_tempFolder, $"rotated_{Guid.NewGuid():N}.jpg");
                _employeeService.RotateImage(sourcePath, angle, rotatedPath);

                if (_selectedCropTarget == Res("CropVisa"))
                {
                    VisaPreviewPath = rotatedPath;
                    if (VisaDoc != null) VisaDoc.ImagePath = rotatedPath;
                }
                else if (_selectedCropTarget == Res("CropInsurance"))
                {
                    InsurancePreviewPath = rotatedPath;
                    if (InsuranceDoc != null) InsuranceDoc.ImagePath = rotatedPath;
                }
                else if (_selectedCropTarget == Res("CropPermit"))
                {
                    WorkPermitPreviewPath = rotatedPath;
                    if (WorkPermitDoc != null) WorkPermitDoc.ImagePath = rotatedPath;
                }
                else if (_selectedCropTarget == Res("CropPassport2"))
                {
                    PassportPage2PreviewPath = rotatedPath;
                    if (PassportPage2Doc != null) PassportPage2Doc.ImagePath = rotatedPath;
                }
                else
                {
                    PassportPreviewPath = rotatedPath;
                    if (PassportDoc != null) PassportDoc.ImagePath = rotatedPath;
                }

                OnPropertyChanged(nameof(CurrentCropImagePath));
                CropSourceChanged?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyCrop()
        {
            if (_selectedCropTarget == Res("CropPhoto"))
            {
                var photoSource = PassportPreviewPath;
                if (string.IsNullOrEmpty(photoSource))
                {
                    MessageBox.Show(Res("MsgDocNotLoaded"), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dest = System.IO.Path.Combine(_tempFolder, "employee_photo.jpg");
                try
                {
                    _employeeService.CreateCroppedPhoto(photoSource, CropRect, dest);
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

        private void EnhanceCurrentDocument()
        {
            string currentPath;
            if (_selectedCropTarget == Res("CropVisa")) currentPath = VisaPreviewPath;
            else if (_selectedCropTarget == Res("CropInsurance")) currentPath = InsurancePreviewPath;
            else if (_selectedCropTarget == Res("CropPassport2")) currentPath = PassportPage2PreviewPath;
            else if (_selectedCropTarget == Res("CropPermit")) currentPath = WorkPermitPreviewPath;
            else currentPath = PassportPreviewPath;

            if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
            {
                MessageBox.Show(Res("MsgUploadFirst"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var editor = new ImageEditorWindow(currentPath);
            editor.Owner = Application.Current.MainWindow;
            editor.ShowDialog();

            if (editor.Saved && !string.IsNullOrEmpty(editor.ResultPath) && File.Exists(editor.ResultPath))
            {
                var newPath = Path.Combine(_tempFolder, $"enh_{Guid.NewGuid():N}{Path.GetExtension(currentPath)}");
                try
                {
                    File.Copy(editor.ResultPath, newPath, true);
                    File.Delete(editor.ResultPath);
                }
                catch
                {
                    newPath = editor.ResultPath;
                }

                if (_selectedCropTarget == Res("CropVisa"))
                {
                    VisaPreviewPath = newPath;
                    if (VisaDoc != null) VisaDoc.ImagePath = newPath;
                }
                else if (_selectedCropTarget == Res("CropInsurance"))
                {
                    InsurancePreviewPath = newPath;
                    if (InsuranceDoc != null) InsuranceDoc.ImagePath = newPath;
                }
                else if (_selectedCropTarget == Res("CropPermit"))
                {
                    WorkPermitPreviewPath = newPath;
                    if (WorkPermitDoc != null) WorkPermitDoc.ImagePath = newPath;
                }
                else if (_selectedCropTarget == Res("CropPassport2"))
                {
                    PassportPage2PreviewPath = newPath;
                    if (PassportPage2Doc != null) PassportPage2Doc.ImagePath = newPath;
                }
                else
                {
                    PassportPreviewPath = newPath;
                    if (PassportDoc != null) PassportDoc.ImagePath = newPath;
                }

                OnPropertyChanged(nameof(CurrentCropImagePath));
                CropSourceChanged?.Invoke();

                if (_selectedCarouselIndex >= 0 && _selectedCarouselIndex < CarouselItems.Count)
                    CarouselItems[_selectedCarouselIndex].ImagePath = newPath;
                OnPropertyChanged(nameof(CarouselPreviewPath));
            }
        }

        private async void SaveEmployee()
        {
            if (string.IsNullOrWhiteSpace(Data.FirstName) || string.IsNullOrWhiteSpace(Data.LastName))
            {
                ToastService.Instance.Warning(Res("MsgFillRequiredFields"));
                return;
            }

            try
            {
                Data.WorkAddressTag = SelectedWorkAddress != null
                    ? $"{SelectedWorkAddress.Street} {SelectedWorkAddress.Number}, {SelectedWorkAddress.City} {SelectedWorkAddress.ZipCode}"
                    : string.Empty;
                Data.PositionTag = SelectedPosition?.Title ?? string.Empty;
                Data.PositionNumber = SelectedPosition?.PositionNumber ?? string.Empty;
                Data.MonthlySalaryBrutto = SelectedPosition?.MonthlySalaryBrutto ?? 0;
                Data.HourlySalary = SelectedPosition?.HourlySalary ?? 0;
                Data.ContractType = ContractType;

                var folder = _employeeService.SaveEmployee(_company.Name, Data, PassportDoc, VisaDoc, InsuranceDoc, CroppedPhotoPath,
                    null, null, WorkPermitDoc, PassportPage2Doc);
                if (string.IsNullOrEmpty(folder))
                {
                    ToastService.Instance.Error(Res("MsgSaveEmployeeFail"));
                    return;
                }

                await _employeeService.AddHistoryEntry(folder, new EmployeeModels.EmployeeHistoryEntry
                {
                    EventType = "Created",
                    Action = Res("HistoryActionCreation"),
                    Field = _company.Name,
                    Description = string.Format(Res("HistoryDescCreated"), $"{Data.FirstName} {Data.LastName}")
                });

                App.ActivityLogService?.Log("EmployeeAdded", "Employee", _company.Name,
                    $"{Data.FirstName} {Data.LastName}",
                    $"Додано працівника {Data.FirstName} {Data.LastName} до {_company.Name}",
                    employeeFolder: folder);

                ToastService.Instance.Success($"{Data.FirstName} {Data.LastName}");
                Close();
            }
            catch (Exception ex)
            {
                ErrorHandler.Report("SaveEmployee", ex, ErrorSeverity.Error);
                _employeeService.CleanupTempFolder(_tempFolder);
            }
        }

        private bool HasUnsavedData()
        {
            if (StepIndex > 0) return true;
            if (!string.IsNullOrWhiteSpace(PassportPreviewPath) || !string.IsNullOrWhiteSpace(VisaPreviewPath) ||
                !string.IsNullOrWhiteSpace(InsurancePreviewPath) || !string.IsNullOrWhiteSpace(WorkPermitPreviewPath) ||
                !string.IsNullOrWhiteSpace(PassportPage2PreviewPath)) return true;
            var d = Data;
            if (!string.IsNullOrWhiteSpace(d.FirstName) || !string.IsNullOrWhiteSpace(d.LastName) ||
                !string.IsNullOrWhiteSpace(d.BirthDate) || !string.IsNullOrWhiteSpace(d.PassportNumber) ||
                !string.IsNullOrWhiteSpace(d.VisaNumber) || !string.IsNullOrWhiteSpace(d.StartDate) ||
                !string.IsNullOrWhiteSpace(d.Phone) || !string.IsNullOrWhiteSpace(d.Email)) return true;
            if (d.AddressLocal != null && (!string.IsNullOrWhiteSpace(d.AddressLocal.Street) || !string.IsNullOrWhiteSpace(d.AddressLocal.City))) return true;
            if (d.AddressAbroad != null && (!string.IsNullOrWhiteSpace(d.AddressAbroad.Street) || !string.IsNullOrWhiteSpace(d.AddressAbroad.City))) return true;
            return false;
        }

        private void TryClose()
        {
            if (HasUnsavedData())
            {
                var msg = Res("MsgWizUnsavedClose") ?? "Є незбережені дані. Закрити без збереження?";
                var title = Res("MsgWizUnsavedTitle") ?? "Скасувати";
                if (MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }
            Close();
        }

        private void Close()
        {
            _cts.Cancel();
            _employeeService.CleanupTempFolder(_tempFolder);
            RequestClose?.Invoke();
        }

        // ===== AI Document Scanning =====
        private async Task AIScanCurrentStepAsync()
        {
            if (!(App.GeminiApiService?.IsConfigured ?? false))
                return;

            if (_selectedCarouselIndex < 0 || _selectedCarouselIndex >= CarouselItems.Count)
            {
                ToastService.Instance.Warning(Res("MsgUploadFirst"));
                return;
            }

            var selectedDoc = CarouselItems[_selectedCarouselIndex];
            var imagePath = selectedDoc.ImagePath;
            var docKey = selectedDoc.Key;

            if (docKey == "passport" && _employeeType == "eu_citizen" && _euDocumentType == "id_card")
                docKey = "id_card";

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                ToastService.Instance.Warning(Res("MsgUploadFirst"));
                return;
            }

            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsAIScanning = true;
            AIScanStatus = Res("AIScanWorking");

            try
            {
                var prompt = AIScanPrompts.GetPrompt(docKey);
                string result;
                if (docKey == "permit" && WorkPermitDoc != null && WorkPermitDoc.IsPdf && !string.IsNullOrEmpty(WorkPermitDoc.PdfPath) && File.Exists(WorkPermitDoc.PdfPath))
                    result = await (App.GeminiApiService?.ChatWithFileAsync(WorkPermitDoc.PdfPath, prompt, null, token) ?? Task.FromResult(""));
                else
                    result = await (App.GeminiApiService?.ChatWithImageAsync(imagePath, prompt, null, token) ?? Task.FromResult(""));

                token.ThrowIfCancellationRequested();

                if (result.StartsWith("["))
                {
                    AIScanStatus = result;
                    return;
                }

                var parsed = AIScanPrompts.ParseResponse(result);
                if (parsed.Count == 0)
                {
                    AIScanStatus = Res("AIScanNoData");
                    return;
                }

                Application.Current.Dispatcher.Invoke(() => ApplyParsedDataByKey(docKey, parsed));
                AIScanStatus = string.Format(Res("AIScanDone"), parsed.Count);
            }
            catch (OperationCanceledException)
            {
                AIScanStatus = "";
            }
            catch (Exception ex)
            {
                LoggingService.LogError("AIScanDocument", ex);
                AIScanStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsAIScanning = false;
            }
        }

        private static string ToTitleCase(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            s = s.Trim();
            if (s.Length == 1) return s.ToUpper();
            return char.ToUpper(s[0]) + s[1..].ToLower();
        }

        private static readonly Dictionary<string, string> OblastToCityMap = new(StringComparer.OrdinalIgnoreCase)
        {
            {"ОДЕСЬКА", "Odesa"}, {"ОДЕСЬКА ОБЛ", "Odesa"}, {"ОДЕСЬКА ОБЛА", "Odesa"}, {"ОДЕСЬКА ОБЛАСТЬ", "Odesa"}, {"ODESSA", "Odesa"}, {"ODESKA", "Odesa"},
            {"КИЇВСЬКА", "Kyiv"}, {"КИЇВСЬКА ОБЛ", "Kyiv"}, {"КИЇВСЬКА ОБЛАСТЬ", "Kyiv"}, {"КИЇВ", "Kyiv"}, {"KYIV", "Kyiv"}, {"KIEV", "Kyiv"},
            {"ЛЬВІВСЬКА", "Lviv"}, {"ЛЬВІВСЬКА ОБЛ", "Lviv"}, {"ЛЬВІВСЬКА ОБЛАСТЬ", "Lviv"}, {"LVIV", "Lviv"},
            {"ХАРКІВСЬКА", "Kharkiv"}, {"ХАРКІВСЬКА ОБЛ", "Kharkiv"}, {"ХАРКІВСЬКА ОБЛАСТЬ", "Kharkiv"}, {"KHARKIV", "Kharkiv"},
            {"ДНІПРОПЕТРОВСЬКА", "Dnipro"}, {"ДНІПРОПЕТРОВСЬКА ОБЛ", "Dnipro"}, {"ДНІПРОПЕТРОВСЬКА ОБЛАСТЬ", "Dnipro"}, {"DNIPRO", "Dnipro"},
            {"ЗАПОРІЗЬКА", "Zaporizhzhia"}, {"ЗАПОРІЗЬКА ОБЛ", "Zaporizhzhia"}, {"ЗАПОРІЗЬКА ОБЛАСТЬ", "Zaporizhzhia"},
            {"ВІННИЦЬКА", "Vinnytsia"}, {"ВІННИЦЬКА ОБЛ", "Vinnytsia"}, {"ВІННИЦЬКА ОБЛАСТЬ", "Vinnytsia"},
            {"ПОЛТАВСЬКА", "Poltava"}, {"ПОЛТАВСЬКА ОБЛ", "Poltava"}, {"ПОЛТАВСЬКА ОБЛАСТЬ", "Poltava"},
            {"МИКОЛАЇВСЬКА", "Mykolaiv"}, {"МИКОЛАЇВСЬКА ОБЛ", "Mykolaiv"}, {"МИКОЛАЇВСЬКА ОБЛАСТЬ", "Mykolaiv"},
            {"ЧЕРНІГІВСЬКА", "Chernihiv"}, {"ЧЕРНІГІВСЬКА ОБЛ", "Chernihiv"}, {"ЧЕРНІГІВСЬКА ОБЛАСТЬ", "Chernihiv"},
            {"ЧЕРКАСЬКА", "Cherkasy"}, {"ЧЕРКАСЬКА ОБЛ", "Cherkasy"}, {"ЧЕРКАСЬКА ОБЛАСТЬ", "Cherkasy"},
            {"СУМСЬКА", "Sumy"}, {"СУМСЬКА ОБЛ", "Sumy"}, {"СУМСЬКА ОБЛАСТЬ", "Sumy"},
            {"ЖИТОМИРСЬКА", "Zhytomyr"}, {"ЖИТОМИРСЬКА ОБЛ", "Zhytomyr"}, {"ЖИТОМИРСЬКА ОБЛАСТЬ", "Zhytomyr"},
            {"ХМЕЛЬНИЦЬКА", "Khmelnytskyi"}, {"ХМЕЛЬНИЦЬКА ОБЛ", "Khmelnytskyi"}, {"ХМЕЛЬНИЦЬКА ОБЛАСТЬ", "Khmelnytskyi"},
            {"РІВНЕНСЬКА", "Rivne"}, {"РІВНЕНСЬКА ОБЛ", "Rivne"}, {"РІВНЕНСЬКА ОБЛАСТЬ", "Rivne"},
            {"ВОЛИНСЬКА", "Lutsk"}, {"ВОЛИНСЬКА ОБЛ", "Lutsk"}, {"ВОЛИНСЬКА ОБЛАСТЬ", "Lutsk"},
            {"ТЕРНОПІЛЬСЬКА", "Ternopil"}, {"ТЕРНОПІЛЬСЬКА ОБЛ", "Ternopil"}, {"ТЕРНОПІЛЬСЬКА ОБЛАСТЬ", "Ternopil"},
            {"ІВАНО-ФРАНКІВСЬКА", "Ivano-Frankivsk"}, {"ІВАНО-ФРАНКІВСЬКА ОБЛ", "Ivano-Frankivsk"}, {"ІВАНО-ФРАНКІВСЬКА ОБЛАСТЬ", "Ivano-Frankivsk"},
            {"ЗАКАРПАТСЬКА", "Uzhhorod"}, {"ЗАКАРПАТСЬКА ОБЛ", "Uzhhorod"}, {"ЗАКАРПАТСЬКА ОБЛАСТЬ", "Uzhhorod"},
            {"ЧЕРНІВЕЦЬКА", "Chernivtsi"}, {"ЧЕРНІВЕЦЬКА ОБЛ", "Chernivtsi"}, {"ЧЕРНІВЕЦЬКА ОБЛАСТЬ", "Chernivtsi"},
            {"КІРОВОГРАДСЬКА", "Kropyvnytskyi"}, {"КІРОВОГРАДСЬКА ОБЛ", "Kropyvnytskyi"}, {"КІРОВОГРАДСЬКА ОБЛАСТЬ", "Kropyvnytskyi"},
            {"ХЕРСОНСЬКА", "Kherson"}, {"ХЕРСОНСЬКА ОБЛ", "Kherson"}, {"ХЕРСОНСЬКА ОБЛАСТЬ", "Kherson"},
            {"ДОНЕЦЬКА", "Donetsk"}, {"ДОНЕЦЬКА ОБЛ", "Donetsk"}, {"ДОНЕЦЬКА ОБЛАСТЬ", "Donetsk"},
            {"ЛУГАНСЬКА", "Luhansk"}, {"ЛУГАНСЬКА ОБЛ", "Luhansk"}, {"ЛУГАНСЬКА ОБЛАСТЬ", "Luhansk"},
            {"ОДЕСЬКА ОБЛ./UKR", "Odesa"}, {"ОДЕСЬКА ОБЛ/UKR", "Odesa"},
        };

        private static string NormalizeCity(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var trimmed = raw.Trim().TrimEnd('.', ',', ' ');
            if (OblastToCityMap.TryGetValue(trimmed, out var city))
                return city;
            foreach (var kvp in OblastToCityMap)
            {
                if (trimmed.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return ToTitleCase(trimmed);
        }

        private static string NormalizeWorkPermitName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw.Trim();

            if (s.IndexOf("osvědčení", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("registrac", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("osvedceni", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Přechodný pobyt";

            if (s.IndexOf("přechodn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("prechodn", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Přechodný pobyt";

            if (s.IndexOf("trval", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Trvalý pobyt";

            if (s.IndexOf("dočasn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("ochran", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Dočasná ochrana";

            if (s.IndexOf("strpění", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("strpeni", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Strpění";

            return s;
        }

        private void ApplyParsedDataByKey(string docKey, Dictionary<string, string> data)
        {
            void Set(string key, Action<string> setter)
            {
                if (data.TryGetValue(key, out var val))
                    setter(val);
            }

            switch (docKey)
            {
                case "passport":
                    Set("FirstName", v => Data.FirstName = ToTitleCase(v));
                    Set("LastName", v => Data.LastName = ToTitleCase(v));
                    Set("BirthDate", v => Data.BirthDate = v);
                    Set("PassportNumber", v => Data.PassportNumber = v.ToUpper());
                    Set("PassportCity", v => Data.PassportCity = NormalizeCity(v));
                    Set("PassportCountry", v => Data.PassportCountry = ToTitleCase(v));
                    Set("PassportExpiry", v => Data.PassportExpiry = v);
                    break;

                case "id_card":
                    Set("FirstName", v => Data.FirstName = ToTitleCase(v));
                    Set("LastName", v => Data.LastName = ToTitleCase(v));
                    Set("BirthDate", v => Data.BirthDate = v);
                    Set("PassportNumber", v => Data.PassportNumber = v.ToUpper());
                    Set("PassportCity", v => Data.PassportCity = NormalizeCity(v));
                    Set("PassportCountry", v => Data.PassportCountry = ToTitleCase(v));
                    Set("PassportExpiry", v => Data.PassportExpiry = v);
                    Set("PassportExpiry", v => Data.VisaExpiry = v);
                    Data.WorkPermitName = "Osvědčení o registraci občana EU";
                    break;

                case "insurance":
                    Set("InsuranceCompanyShort", v => Data.InsuranceCompanyShort = v);
                    Set("InsuranceNumber", v => Data.InsuranceNumber = v);
                    Set("InsuranceExpiry", v => Data.InsuranceExpiry = v);
                    break;

                case "visa":
                    Set("VisaNumber", v => Data.VisaNumber = v);
                    Set("VisaType", v => Data.VisaType = v);
                    Set("VisaExpiry", v => Data.VisaExpiry = v);
                    Set("WorkPermitName", v => Data.WorkPermitName = NormalizeWorkPermitName(v));
                    break;

                case "permit":
                    Set("WorkPermitNumber", v => Data.WorkPermitNumber = v);
                    Set("WorkPermitType", v => Data.WorkPermitType = v);
                    Set("WorkPermitIssueDate", v => Data.WorkPermitIssueDate = v);
                    Set("WorkPermitExpiry", v => Data.WorkPermitExpiry = v);
                    Set("WorkPermitAuthority", v => Data.WorkPermitAuthority = v);
                    break;

                case "passport2":
                    Set("WorkPermitName", v => Data.WorkPermitName = NormalizeWorkPermitName(v));
                    Set("VisaNumber", v => Data.VisaNumber = v);
                    Set("VisaExpiry", v => Data.VisaExpiry = v);
                    break;
            }

            OnPropertyChanged(nameof(Data));
        }
    }
}
