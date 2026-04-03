using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
                    OnPropertyChanged(nameof(IsLastStep));
                    RefreshCarousel();
                    AutoSelectCarouselForStep(value);
                    OnPropertyChanged(nameof(CarouselPreviewPath));
                }
            }
        }

        public int CurrentStepDisplay => ActiveSteps.IndexOf(StepIndex) + 1;
        public bool IsLastStep => ActiveSteps.IndexOf(StepIndex) == ActiveSteps.Count - 1;

        public EmployeeModels.EmployeeData Data { get; } = new EmployeeModels.EmployeeData();

        public bool HasBankAccountData
        {
            get => Data.HasBankAccountData;
            set
            {
                if (Data.HasBankAccountData == value)
                    return;

                Data.HasBankAccountData = value;
                OnPropertyChanged(nameof(HasBankAccountData));
                OnPropertyChanged(nameof(ShowBankAccountSection));
            }
        }

        public bool ShowBankAccountSection => HasBankAccountData;

        public string BankAccountNumber
        {
            get => Data.BankAccountNumber;
            set
            {
                var normalized = value ?? string.Empty;
                if (Data.BankAccountNumber == normalized)
                    return;

                Data.BankAccountNumber = normalized;
                OnPropertyChanged(nameof(BankAccountNumber));
                TryAutofillBankName(normalized);
            }
        }

        public string BankName
        {
            get => Data.BankName;
            set
            {
                var normalized = value ?? string.Empty;
                if (Data.BankName == normalized)
                    return;

                Data.BankName = normalized;
                OnPropertyChanged(nameof(BankName));
            }
        }

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

        // ===== Document checkboxes =====
        private bool _hasVisa = true;
        public bool HasVisa
        {
            get => _hasVisa;
            set { if (SetProperty(ref _hasVisa, value)) OnDocOptionsChanged(); }
        }

        private bool _hasInsurance = true;
        public bool HasInsurance
        {
            get => _hasInsurance;
            set { if (SetProperty(ref _hasInsurance, value)) OnDocOptionsChanged(); }
        }

        private bool _hasWorkPermit;
        public bool HasWorkPermit
        {
            get => _hasWorkPermit;
            set { if (SetProperty(ref _hasWorkPermit, value)) OnDocOptionsChanged(); }
        }

        private bool _hasPassportPage2;
        public bool HasPassportPage2
        {
            get => _hasPassportPage2;
            set { if (SetProperty(ref _hasPassportPage2, value)) OnDocOptionsChanged(); }
        }

        private bool _hasVisaPage2;
        public bool HasVisaPage2
        {
            get => _hasVisaPage2;
            set { if (SetProperty(ref _hasVisaPage2, value)) OnDocOptionsChanged(); }
        }

        private string _visaDocType = "visa_sticker";
        public string VisaDocType
        {
            get => _visaDocType;
            set
            {
                if (SetProperty(ref _visaDocType, value))
                {
                    Data.VisaDocType = value;
                    OnPropertyChanged(nameof(IsVisaIdCard));
                    OnPropertyChanged(nameof(ShowVisaPage2Upload));
                    OnDocOptionsChanged();
                }
            }
        }

        public bool IsVisaIdCard => _visaDocType == "id_card";
        public bool ShowVisaPage2Upload => _hasVisa && _visaDocType == "id_card" && _hasVisaPage2;
        public bool ShowVisaDocTypeSelector => _hasVisa;
        public bool CanToggleVisaPage2 => _hasVisa && _visaDocType == "id_card";

        private void OnDocOptionsChanged()
        {
            Data.EmployeeType = EmployeeType;
            UpdateCropTargets();
            OnPropertyChanged(nameof(EmployeeType));
            OnPropertyChanged(nameof(IsWorkPermitType));
            OnPropertyChanged(nameof(IsPassportOnlyType));
            OnPropertyChanged(nameof(ShowVisaUpload));
            OnPropertyChanged(nameof(ShowWorkPermitUpload));
            OnPropertyChanged(nameof(ShowPassportUpload));
            OnPropertyChanged(nameof(ShowPassportPage2Upload));
            OnPropertyChanged(nameof(ShowIdCardFallbackFields));
            OnPropertyChanged(nameof(ShowInsuranceUpload));
            OnPropertyChanged(nameof(ShowEuDocTypeSelector));
            OnPropertyChanged(nameof(ShowVisaDocTypeSelector));
            OnPropertyChanged(nameof(ShowVisaPage2Upload));
            OnPropertyChanged(nameof(CanToggleVisaPage2));
            OnPropertyChanged(nameof(IsVisaIdCard));
            OnPropertyChanged(nameof(ActiveSteps));
            OnPropertyChanged(nameof(TotalSteps));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(PersonalDocPreviewPath));
        }

        public string EmployeeType
        {
            get
            {
                if (_hasVisa && _hasWorkPermit) return "work_permit";
                if (_hasVisa) return "visa";
                if (_hasInsurance) return "eu_citizen";
                return "passport_only";
            }
        }

        public bool IsWorkPermitType => EmployeeType == "work_permit";
        public bool IsPassportOnlyType => EmployeeType == "passport_only";

        private string _euDocumentType = "passport";
        public string EuDocumentType
        {
            get => _euDocumentType;
            set
            {
                if (SetProperty(ref _euDocumentType, value))
                {
                    Data.EuDocumentType = value;
                    OnPropertyChanged(nameof(IsEuIdCard));
                    OnPropertyChanged(nameof(IsEuPassport));
                    OnPropertyChanged(nameof(ShowPassportPage2Upload));
                    OnPropertyChanged(nameof(ShowIdCardFallbackFields));
                    OnPropertyChanged(nameof(ActiveSteps));
                    OnPropertyChanged(nameof(TotalSteps));
                    OnPropertyChanged(nameof(IsLastStep));
                    OnPropertyChanged(nameof(PrimaryDocument1Label));
                    OnPropertyChanged(nameof(PrimaryDocument2Label));
                    OnPropertyChanged(nameof(PrimaryDocumentStepTitle));
                    OnPropertyChanged(nameof(SecondaryDocumentStepTitle));
                    OnPropertyChanged(nameof(SecondaryDocumentStepHint));
                    RefreshCarousel();
                    UpdateCropTargets();
                }
            }
        }

        public bool IsEuPassport => _euDocumentType == "passport";
        public bool IsEuIdCard => _euDocumentType == "id_card";
        public bool ShowIdCardFallbackFields => IsEuIdCard && !ShowPassportPage2Upload;
        public bool ShowEuDocTypeSelector => !_hasVisa;
        public string PrimaryDocument1Label => IsEuIdCard ? Res("WizardIdCard1") : Res("WizardPassport1");
        public string PrimaryDocument2Label => IsEuIdCard ? Res("WizardIdCard2") : Res("WizardPassport2");
        public string PrimaryDocumentStepTitle => IsEuIdCard ? Res("StepIdCardData") : Res("StepPassportData");
        public string SecondaryDocumentStepTitle => IsEuIdCard ? Res("StepIdCardPage2Data") : Res("StepPassportPage2Data");
        public string SecondaryDocumentStepHint => IsEuIdCard ? Res("StepIdCardPage2Hint") : Res("StepPassportPage2Hint");

        public bool IsGenderMale
        {
            get => Data.Gender == "male";
            set { if (value) { Data.Gender = "male"; OnPropertyChanged(nameof(IsGenderMale)); OnPropertyChanged(nameof(IsGenderFemale)); } }
        }

        public bool IsGenderFemale
        {
            get => Data.Gender == "female";
            set { if (value) { Data.Gender = "female"; OnPropertyChanged(nameof(IsGenderMale)); OnPropertyChanged(nameof(IsGenderFemale)); } }
        }

        private void TryAutofillBankName(string accountNumber)
        {
            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                if (!string.IsNullOrEmpty(Data.BankName))
                {
                    Data.BankName = string.Empty;
                    OnPropertyChanged(nameof(BankName));
                }

                return;
            }

            if (CzechBankAccountResolver.TryResolveBankName(accountNumber, out var resolvedBankName))
            {
                if (!string.Equals(Data.BankName, resolvedBankName, StringComparison.Ordinal))
                {
                    Data.BankName = resolvedBankName;
                    OnPropertyChanged(nameof(BankName));
                }

                return;
            }

            if (!string.IsNullOrEmpty(CzechBankAccountResolver.ExtractBankCode(accountNumber)) && !string.IsNullOrEmpty(Data.BankName))
            {
                Data.BankName = string.Empty;
                OnPropertyChanged(nameof(BankName));
            }
        }

        public bool ShowPassportUpload => true;
        public bool ShowPassportPage2Upload => _hasPassportPage2;
        public bool ShowVisaUpload => _hasVisa;
        public bool ShowWorkPermitUpload => _hasWorkPermit;
        public bool ShowInsuranceUpload => _hasInsurance;

        public string PersonalDocPreviewPath => PassportPreviewPath;

        // ===== Active steps based on document checkboxes =====
        public List<int> ActiveSteps
        {
            get
            {
                var steps = new List<int> { 0, 1, 2 };

                if (_hasPassportPage2)
                    steps.Add(8);

                if (_hasVisa)
                    steps.Add(4);

                if (_hasInsurance)
                    steps.Add(3);

                if (_hasWorkPermit)
                    steps.Add(7);

                steps.Add(5);
                steps.Add(6);
                return steps;
            }
        }

        public int TotalSteps => ActiveSteps.Count;

        // ===== Documents =====
        public EmployeeModels.EmployeeDocumentTemp PassportDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp VisaDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp InsuranceDoc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp PassportPage2Doc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
        public EmployeeModels.EmployeeDocumentTemp VisaPage2Doc { get; private set; } = new EmployeeModels.EmployeeDocumentTemp();
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

        private string _visaPage2PreviewPath = string.Empty;
        public string VisaPage2PreviewPath
        {
            get => _visaPage2PreviewPath;
            set => SetProperty(ref _visaPage2PreviewPath, value);
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

        private DocPreviewState _carouselPreviewState = DocPreviewState.Empty;
        public DocPreviewState CarouselPreviewState
        {
            get => _carouselPreviewState;
            set => SetProperty(ref _carouselPreviewState, value);
        }

        public bool HasCarouselItems => CarouselItems.Count > 1;

        public ICommand CarouselPrevCommand { get; }
        public ICommand CarouselNextCommand { get; }
        public ICommand SelectCarouselTabCommand { get; }

        private void RefreshCarousel()
        {
            var previousKey =
                _selectedCarouselIndex >= 0 && _selectedCarouselIndex < CarouselItems.Count
                    ? CarouselItems[_selectedCarouselIndex].Key
                    : null;

            CarouselItems.Clear();

            if (!string.IsNullOrEmpty(PassportPreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "passport", Label = GetPrimaryCarouselLabel(), ImagePath = PassportPreviewPath });
            if (!string.IsNullOrEmpty(PassportPage2PreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "passport2", Label = GetSecondaryCarouselLabel(), ImagePath = PassportPage2PreviewPath });
            if (!string.IsNullOrEmpty(VisaPreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "visa", Label = Res("CarouselVisa"), ImagePath = VisaPreviewPath });
            if (!string.IsNullOrEmpty(VisaPage2PreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "visa2", Label = Res("CarouselVisaPage2"), ImagePath = VisaPage2PreviewPath });
            if (!string.IsNullOrEmpty(InsurancePreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "insurance", Label = Res("CarouselInsurance"), ImagePath = InsurancePreviewPath });
            if (!string.IsNullOrEmpty(WorkPermitPreviewPath))
                CarouselItems.Add(new CarouselDocItem { Key = "permit", Label = Res("CarouselPermit"), ImagePath = WorkPermitPreviewPath });

            var restoredIndex = -1;
            if (!string.IsNullOrEmpty(previousKey))
                restoredIndex = CarouselItems.ToList().FindIndex(x => x.Key == previousKey);

            if (restoredIndex >= 0)
                _selectedCarouselIndex = restoredIndex;
            else if (CarouselItems.Count > 0)
                _selectedCarouselIndex = 0;
            else
                _selectedCarouselIndex = -1;

            for (int i = 0; i < CarouselItems.Count; i++)
                CarouselItems[i].IsSelected = i == _selectedCarouselIndex;

            OnPropertyChanged(nameof(SelectedCarouselIndex));
            OnPropertyChanged(nameof(CarouselPreviewPath));
            OnPropertyChanged(nameof(HasCarouselItems));
            CarouselPreviewState = CarouselItems.Count > 0 ? DocPreviewState.Ready : DocPreviewState.Empty;
        }

        private bool AutoSelectCarouselForStep(int step)
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
            if (string.IsNullOrEmpty(targetKey)) return false;
            for (int i = 0; i < CarouselItems.Count; i++)
            {
                if (CarouselItems[i].Key == targetKey)
                {
                    SelectedCarouselIndex = i;
                    return true;
                }
            }
            return false;
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

        private void OpenCurrentPreviewFile()
        {
            var path = GetCurrentDocumentOpenPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ToastService.Instance.Warning(Res("MsgUploadFirst"));
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AddEmployeeWizardViewModel.OpenCurrentPreviewFile", ex.Message);
                MessageBox.Show(Res("MsgOpenFileFail"), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string GetCurrentDocumentOpenPath()
        {
            var docKey = _selectedCarouselIndex >= 0 && _selectedCarouselIndex < CarouselItems.Count
                ? CarouselItems[_selectedCarouselIndex].Key
                : GetStepPreviewDocumentKey(StepIndex);

            return docKey switch
            {
                "passport" => GetDocumentSourcePath(PassportDoc),
                "passport2" => GetDocumentSourcePath(PassportPage2Doc),
                "visa" => GetDocumentSourcePath(VisaDoc),
                "visa2" => GetDocumentSourcePath(VisaPage2Doc),
                "insurance" => GetDocumentSourcePath(InsuranceDoc),
                "permit" => GetDocumentSourcePath(WorkPermitDoc),
                _ => string.Empty
            };
        }

        private static string GetDocumentSourcePath(EmployeeModels.EmployeeDocumentTemp? temp)
        {
            if (temp == null)
                return string.Empty;

            return temp.IsPdf ? temp.PdfPath : temp.ImagePath;
        }

        private static string GetStepPreviewDocumentKey(int step)
        {
            return step switch
            {
                2 => "passport",
                3 => "insurance",
                4 => "visa",
                7 => "permit",
                8 => "passport2",
                _ => string.Empty
            };
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
                if (_selectedCropTarget == Res("CropVisaPage2")) return VisaPage2PreviewPath;
                if (_selectedCropTarget == Res("CropInsurance")) return InsurancePreviewPath;
                if (_selectedCropTarget == GetSecondaryCropLabel()) return PassportPage2PreviewPath;
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
        public ICommand UploadVisaPage2Command { get; }
        public ICommand UploadWorkPermitCommand { get; }
        public ICommand SetVisaDocTypeCommand { get; }
        public ICommand ApplyCropCommand { get; }
        public ICommand RotateLeftCommand { get; }
        public ICommand RotateRightCommand { get; }
        public ICommand EnhanceDocumentCommand { get; }
        public ICommand SetEuDocTypeCommand { get; }
        public ICommand AIScanDocumentCommand { get; }
        public ICommand OpenCurrentPreviewFileCommand { get; }

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
            SaveCommand = new AsyncRelayCommand(_ => SaveEmployeeAsync());

            UploadPassportCommand = new RelayCommand(o => UploadDocument("passport"));
            UploadVisaCommand = new RelayCommand(o => UploadDocument("visa"));
            UploadInsuranceCommand = new RelayCommand(o => UploadDocument("insurance"));
            UploadPassportPage2Command = new RelayCommand(o => UploadDocument("passport_page2"));
            UploadVisaPage2Command = new RelayCommand(o => UploadDocument("visa_page2"));
            UploadWorkPermitCommand = new RelayCommand(o => UploadDocument("work_permit"));
            SetVisaDocTypeCommand = new RelayCommand(o => VisaDocType = o?.ToString() ?? "visa_sticker");
            ApplyCropCommand = new RelayCommand(o => ApplyCrop());
            RotateLeftCommand = new RelayCommand(o => RotateCurrentImage(-90));
            RotateRightCommand = new RelayCommand(o => RotateCurrentImage(90));
            EnhanceDocumentCommand = new RelayCommand(o => EnhanceCurrentDocument(), o => !IsCropPhotoMode);
            SetEuDocTypeCommand = new RelayCommand(o => EuDocumentType = o?.ToString() ?? "passport");
            AIScanDocumentCommand = new AsyncRelayCommand(_ => AIScanCurrentStepAsync(),
                _ => !_isAIScanning && App.GeminiApiService?.IsConfigured == true);
            OpenCurrentPreviewFileCommand = new RelayCommand(_ => OpenCurrentPreviewFile());
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
            CropTargets.Add(GetPrimaryCropLabel());

            if (ShowPassportPage2Upload) CropTargets.Add(GetSecondaryCropLabel());
            if (ShowVisaUpload) CropTargets.Add(Res("CropVisa"));
            if (ShowVisaPage2Upload) CropTargets.Add(Res("CropVisaPage2"));
            CropTargets.Add(Res("CropInsurance"));
            if (ShowWorkPermitUpload) CropTargets.Add(Res("CropPermit"));
            CropTargets.Add(Res("CropPhoto"));

            SelectedCropTarget = Res("CropPhoto");
        }

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".heic", ".pdf" };

        private bool TryNormalizeUploadedFilePath(string filePath, string type, out string normalizedPath)
        {
            normalizedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                normalizedPath = Path.GetFullPath(filePath);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AddEmployeeWizardViewModel.UploadDocument",
                    $"Invalid path for '{type}': {ex.Message}");
                MessageBox.Show(string.Format(Res("MsgOpenFileError"), ex.Message),
                    Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (!File.Exists(normalizedPath))
            {
                LoggingService.LogWarning("AddEmployeeWizardViewModel.UploadDocument",
                    $"Source file not found for '{type}': {normalizedPath}");
                MessageBox.Show(Res("MsgFileNotFound"), Res("TitleWarning"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                if (new FileInfo(normalizedPath).Length <= 0)
                {
                    LoggingService.LogWarning("AddEmployeeWizardViewModel.UploadDocument",
                        $"Source file is empty for '{type}': {normalizedPath}");
                    MessageBox.Show(Res("MsgOpenFileFail"), Res("TitleError"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AddEmployeeWizardViewModel.UploadDocument",
                    $"Cannot inspect source file for '{type}': {ex.Message}");
                MessageBox.Show(string.Format(Res("MsgOpenFileError"), ex.Message),
                    Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private static void EnsurePreparedDocumentReady(EmployeeModels.EmployeeDocumentTemp temp, string type)
        {
            var preparedPath = temp.IsPdf ? temp.PdfPath : temp.ImagePath;
            if (string.IsNullOrWhiteSpace(preparedPath) || !File.Exists(preparedPath))
            {
                LoggingService.LogWarning("AddEmployeeWizardViewModel.ProcessUploadedFile",
                    $"Prepared temp file is missing for '{type}'.");
                throw new IOException("Could not open file.");
            }

            if (new FileInfo(preparedPath).Length <= 0)
            {
                LoggingService.LogWarning("AddEmployeeWizardViewModel.ProcessUploadedFile",
                    $"Prepared temp file is empty for '{type}': {preparedPath}");
                throw new IOException("Could not open file.");
            }
        }

        private static string FormatAiServiceMessage(string response)
        {
            if (GeminiApiService.IsTimeoutResponse(response))
                return Res("AIChatTimeout");

            if (GeminiApiService.IsNetworkErrorResponse(response))
                return Res("AIChatNetworkError");

            return response;
        }

        public void UploadDocumentFromPath(string filePath, string type)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            if (!TryNormalizeUploadedFilePath(filePath, type, out var normalizedPath))
                return;

            var ext = Path.GetExtension(normalizedPath);
            if (!AllowedExtensions.Contains(ext))
            {
                MessageBox.Show(Res("DragDropInvalidFormat"), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ProcessUploadedFile(normalizedPath, type);
        }

        private void UploadDocument(string type)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.pdf";
            if (dialog.ShowDialog() != true) return;
            if (!TryNormalizeUploadedFilePath(dialog.FileName, type, out var normalizedPath))
                return;

            ProcessUploadedFile(normalizedPath, type);
        }

        private string BuildPdfFirstPagePreview(EmployeeModels.EmployeeDocumentTemp temp, string baseName, string filePath)
        {
            if (!temp.IsPdf)
                return temp.ImagePath;

            var pages = _employeeService.RenderPdfPages(temp.PdfPath, _tempFolder, baseName, maxPages: 1);
            if (pages.Count == 0)
            {
                LoggingService.LogWarning("AddEmployeeWizardViewModel.ProcessUploadedFile",
                    $"PDF preview generation returned no pages for '{filePath}'.");
                throw new IOException("Could not open file.");
            }

            return pages[0];
        }

        private void ProcessUploadedFile(string filePath, string type)
        {
            try
            {
                var temp = _employeeService.PrepareTempDocument(filePath, _tempFolder, type);
                EnsurePreparedDocumentReady(temp, type);
                CarouselPreviewState = temp.IsPdf ? DocPreviewState.Loading : DocPreviewState.Ready;
                switch (type)
                {
                    case "passport":
                        PassportDoc = temp;
                        PassportPreviewPath = BuildPdfFirstPagePreview(temp, "pass_preview", filePath);
                        OnPropertyChanged(nameof(PassportDoc));
                        break;
                    case "visa":
                        VisaDoc = temp;
                        VisaPreviewPath = BuildPdfFirstPagePreview(temp, "visa_preview", filePath);
                        OnPropertyChanged(nameof(VisaDoc));
                        break;
                    case "insurance":
                        InsuranceDoc = temp;
                        InsurancePreviewPath = BuildPdfFirstPagePreview(temp, "ins_preview", filePath);
                        OnPropertyChanged(nameof(InsuranceDoc));
                        break;
                    case "passport_page2":
                        PassportPage2Doc = temp;
                        PassportPage2PreviewPath = BuildPdfFirstPagePreview(temp, "pass2_preview", filePath);
                        OnPropertyChanged(nameof(PassportPage2Doc));
                        break;
                    case "visa_page2":
                        VisaPage2Doc = temp;
                        VisaPage2PreviewPath = BuildPdfFirstPagePreview(temp, "visa2_preview", filePath);
                        OnPropertyChanged(nameof(VisaPage2Doc));
                        break;
                    case "work_permit":
                        WorkPermitDoc = temp;
                        WorkPermitPagePreviews.Clear();
                        if (temp.IsPdf)
                        {
                            WorkPermitFileName = Path.GetFileName(filePath);
                            var pages = _employeeService.RenderPdfPages(temp.PdfPath, _tempFolder, "wp_preview");
                            if (pages.Count == 0)
                            {
                                LoggingService.LogWarning("AddEmployeeWizardViewModel.ProcessUploadedFile",
                                    $"PDF preview generation returned no pages for '{filePath}'.");
                                throw new IOException("Could not open file.");
                            }
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

                Converters.ImagePathConverter.InvalidateCache();
                OnPropertyChanged(nameof(CurrentCropImagePath));
                RefreshCarousel();
                CarouselPreviewState = DocPreviewState.Ready;
                CropSourceChanged?.Invoke();
            }
            catch (Exception ex)
            {
                CarouselPreviewState = DocPreviewState.Error;
                ShowDocumentProcessingError("Не вдалося оновити документ.", ex);
            }
        }

        private void RotateCurrentImage(int angle)
        {
            var sourcePath = CurrentCropImagePath;
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                LoggingService.LogWarning("AddEmployeeWizardViewModel.RotateCurrentImage",
                    $"Image not found for rotate: {sourcePath}");
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
                else if (_selectedCropTarget == GetSecondaryCropLabel())
                {
                    PassportPage2PreviewPath = rotatedPath;
                    if (PassportPage2Doc != null) PassportPage2Doc.ImagePath = rotatedPath;
                }
                else if (_selectedCropTarget == Res("CropVisaPage2"))
                {
                    VisaPage2PreviewPath = rotatedPath;
                    if (VisaPage2Doc != null) VisaPage2Doc.ImagePath = rotatedPath;
                }
                else
                {
                    PassportPreviewPath = rotatedPath;
                    if (PassportDoc != null) PassportDoc.ImagePath = rotatedPath;
                }

                Converters.ImagePathConverter.InvalidateCache(sourcePath);
                Converters.ImagePathConverter.InvalidateCache(rotatedPath);
                OnPropertyChanged(nameof(CurrentCropImagePath));
                if (_selectedCarouselIndex >= 0 && _selectedCarouselIndex < CarouselItems.Count)
                    CarouselItems[_selectedCarouselIndex].ImagePath = rotatedPath;
                OnPropertyChanged(nameof(CarouselPreviewPath));
                CropSourceChanged?.Invoke();
            }
            catch (Exception ex)
            {
                ShowDocumentProcessingError("Не вдалося повернути зображення.", ex);
            }
        }

        private void ApplyCrop()
        {
            if (_selectedCropTarget == Res("CropPhoto"))
            {
                var photoSource = PassportPreviewPath;
                if (string.IsNullOrEmpty(photoSource) || !File.Exists(photoSource))
                {
                    LoggingService.LogWarning("AddEmployeeWizardViewModel.ApplyCrop",
                        $"Photo source not available for crop: {photoSource}");
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
                    ShowDocumentProcessingError("Не вдалося застосувати обрізання.", ex);
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
            else if (_selectedCropTarget == Res("CropVisaPage2")) currentPath = VisaPage2PreviewPath;
            else if (_selectedCropTarget == Res("CropInsurance")) currentPath = InsurancePreviewPath;
            else if (_selectedCropTarget == GetSecondaryCropLabel()) currentPath = PassportPage2PreviewPath;
            else if (_selectedCropTarget == Res("CropPermit")) currentPath = WorkPermitPreviewPath;
            else currentPath = PassportPreviewPath;

            if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
            {
                MessageBox.Show(Res("MsgUploadFirst"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ImageEditorWindow editor;
            try
            {
                editor = new ImageEditorWindow(currentPath);
                if (editor.LoadFailed) return;
            }
            catch (Exception ex)
            {
                var fmt = Res("MsgEditorOpenError");
                MessageBox.Show(string.Format(fmt, ex.Message),
                    Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            editor.Owner = Application.Current.MainWindow;
            editor.ShowDialog();

            if (editor.Saved && !string.IsNullOrEmpty(editor.ResultPath) && File.Exists(editor.ResultPath))
            {
                var newPath = Path.Combine(_tempFolder, $"enh_{Guid.NewGuid():N}{Path.GetExtension(currentPath)}");
                try
                {
                    SafeFileService.CopyFile(editor.ResultPath, newPath);
                    SafeFileService.DeleteFile(editor.ResultPath);
                }
                catch (IOException)
                {
                    newPath = editor.ResultPath;
                }
                catch (UnauthorizedAccessException)
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
                else if (_selectedCropTarget == GetSecondaryCropLabel())
                {
                    PassportPage2PreviewPath = newPath;
                    if (PassportPage2Doc != null) PassportPage2Doc.ImagePath = newPath;
                }
                else if (_selectedCropTarget == Res("CropVisaPage2"))
                {
                    VisaPage2PreviewPath = newPath;
                    if (VisaPage2Doc != null) VisaPage2Doc.ImagePath = newPath;
                }
                else
                {
                    PassportPreviewPath = newPath;
                    if (PassportDoc != null) PassportDoc.ImagePath = newPath;
                }

                Converters.ImagePathConverter.InvalidateCache(currentPath);
                Converters.ImagePathConverter.InvalidateCache(newPath);
                OnPropertyChanged(nameof(CurrentCropImagePath));
                CropSourceChanged?.Invoke();

                if (_selectedCarouselIndex >= 0 && _selectedCarouselIndex < CarouselItems.Count)
                    CarouselItems[_selectedCarouselIndex].ImagePath = newPath;
                OnPropertyChanged(nameof(CarouselPreviewPath));
            }
        }

        private async Task SaveEmployeeAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Зберегти працівника"))
                return;

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
                    null, null, WorkPermitDoc, PassportPage2Doc, VisaPage2Doc);
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

                TelemetryService.TrackEvent("employee_added", new Dictionary<string, object>
                {
                    ["employee_name"] = $"{Data.FirstName} {Data.LastName}",
                    ["firm_name"] = _company.Name
                });

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

            if (StepIndex == 8 && IsEuIdCard)
            {
                await AIScanEuIdCardStepAsync();
                return;
            }

            if (_selectedCarouselIndex < 0 || _selectedCarouselIndex >= CarouselItems.Count)
            {
                ToastService.Instance.Warning(Res("MsgUploadFirst"));
                return;
            }

            var selectedDoc = CarouselItems[_selectedCarouselIndex];
            var imagePath = selectedDoc.ImagePath;
            var docKey = selectedDoc.Key;

            if (docKey == "passport" && EmployeeType == "eu_citizen" && _euDocumentType == "id_card")
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
                    AIScanStatus = FormatAiServiceMessage(result);
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
                AIScanStatus = FormatAiServiceMessage($"[Error: {ex.Message}]");
            }
            finally
            {
                IsAIScanning = false;
            }
        }

        private async Task AIScanEuIdCardStepAsync()
        {
            if (string.IsNullOrWhiteSpace(PassportPreviewPath) || !File.Exists(PassportPreviewPath))
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
                var totalFields = 0;

                var primaryParsed = await ScanImageDocumentAsync("id_card", PassportPreviewPath, token);
                token.ThrowIfCancellationRequested();
                if (primaryParsed.Count > 0)
                {
                    totalFields += primaryParsed.Count;
                    Application.Current.Dispatcher.Invoke(() => ApplyParsedDataByKey("id_card", primaryParsed));
                }

                if (!string.IsNullOrWhiteSpace(PassportPage2PreviewPath) && File.Exists(PassportPage2PreviewPath))
                {
                    var secondaryParsed = await ScanImageDocumentAsync("passport2", PassportPage2PreviewPath, token);
                    token.ThrowIfCancellationRequested();
                    if (secondaryParsed.Count > 0)
                    {
                        totalFields += secondaryParsed.Count;
                        Application.Current.Dispatcher.Invoke(() => ApplyParsedDataByKey("passport2", secondaryParsed));
                    }
                }

                AIScanStatus = totalFields > 0
                    ? string.Format(Res("AIScanDone"), totalFields)
                    : Res("AIScanNoData");
            }
            catch (OperationCanceledException)
            {
                AIScanStatus = "";
            }
            catch (Exception ex)
            {
                LoggingService.LogError("AIScanEuIdCardStep", ex);
                AIScanStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsAIScanning = false;
            }
        }

        private async Task<Dictionary<string, string>> ScanImageDocumentAsync(string docKey, string imagePath, CancellationToken token)
        {
            var prompt = AIScanPrompts.GetPrompt(docKey);
            var result = await (App.GeminiApiService?.ChatWithImageAsync(imagePath, prompt, null, token) ?? Task.FromResult(""));
            if (result.StartsWith("["))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return AIScanPrompts.ParseResponse(result);
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

        private string GetPrimaryCropLabel() => IsEuIdCard ? Res("CropIdCard1") : Res("CropPassport");
        private string GetSecondaryCropLabel() => IsEuIdCard ? Res("CropIdCard2") : Res("CropPassport2");
        private string GetPrimaryCarouselLabel() => IsEuIdCard ? Res("CarouselIdCard1") : Res("CarouselPassport");
        private string GetSecondaryCarouselLabel() => IsEuIdCard ? Res("CarouselIdCard2") : Res("CarouselPassport2");

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
                    Set("PassportAuthority", v => Data.PassportAuthority = v);
                    Set("PassportCity", v => Data.PassportCity = NormalizeCity(v));
                    Set("PassportCountry", v => Data.PassportCountry = ToTitleCase(v));
                    Set("PassportExpiry", v => Data.PassportExpiry = v);
                    break;

                case "id_card":
                    Set("FirstName", v => Data.FirstName = ToTitleCase(v));
                    Set("LastName", v => Data.LastName = ToTitleCase(v));
                    Set("BirthDate", v => Data.BirthDate = v);
                    Set("PassportNumber", v => Data.PassportNumber = v.ToUpper());
                    Set("PassportAuthority", v => Data.PassportAuthority = v);
                    Set("PassportCity", v => Data.PassportCity = NormalizeCity(v));
                    Set("PassportCountry", v => Data.PassportCountry = ToTitleCase(v));
                    Set("PassportExpiry", v => Data.PassportExpiry = v);
                    Set("PassportExpiry", v => Data.VisaExpiry = v);
                    break;

                case "insurance":
                    Set("InsuranceCompanyShort", v => Data.InsuranceCompanyShort = v);
                    Set("InsuranceNumber", v => Data.InsuranceNumber = v);
                    Set("InsuranceExpiry", v => Data.InsuranceExpiry = v);
                    break;

                case "visa":
                    Set("VisaNumber", v => Data.VisaNumber = v);
                    Set("VisaAuthority", v => Data.VisaAuthority = v);
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

        private static void ShowDocumentProcessingError(string prefix, Exception ex)
        {
            MessageBox.Show($"{prefix}\n\n{ex.Message}", Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
