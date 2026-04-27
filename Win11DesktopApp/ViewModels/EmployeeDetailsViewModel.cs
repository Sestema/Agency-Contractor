using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.Converters;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace Win11DesktopApp.ViewModels
{
    public partial class EmployeeDetailsViewModel : ViewModelBase
    {
        private string DocRes(string key) =>
            _documentLocalizationService.Get(key) ?? Res(key);

        private readonly EmployeeService _employeeService;
        private readonly GeminiApiService _geminiApiService;
        private readonly FinanceService _financeService;
        private readonly AppSettingsService _appSettingsService;
        private readonly ActivityLogService _activityLogService;
        private readonly CompanyService _companyService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly TemplateService _templateService;
        private readonly DocumentGenerationService _documentGenerationService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly AiWindowFactory _aiWindowFactory;
        private readonly string _employeeFolder;
        private readonly string _firmName;
        private bool _profileUnavailable;
        private bool _profileUnavailableNotified;
        private bool _profileCloseScheduled;

        public event Action? RequestClose;
        public event Action? DataChanged;

        public EmployeeData Data { get; private set; }
        public string EmployeeFolderPath => _employeeFolder;

        public bool HasBankAccountData
        {
            get => Data?.HasBankAccountData ?? false;
            set
            {
                if (Data == null || Data.HasBankAccountData == value)
                    return;

                Data.HasBankAccountData = value;
                NotifyBankAccountStateChanged();
                OnPropertyChanged(nameof(Data));
            }
        }

        public bool ShowBankAccountSection => HasBankAccountData;
        public bool ShowBankAccountCard => IsEditMode || HasBankAccountData;

        public string BankAccountNumber
        {
            get => Data?.BankAccountNumber ?? string.Empty;
            set
            {
                if (Data == null)
                    return;

                var normalized = value ?? string.Empty;
                if (Data.BankAccountNumber == normalized)
                    return;

                Data.BankAccountNumber = normalized;
                OnPropertyChanged(nameof(BankAccountNumber));
                TryAutofillBankName(normalized);
                OnPropertyChanged(nameof(Data));
            }
        }

        public string BankName
        {
            get => Data?.BankName ?? string.Empty;
            set
            {
                if (Data == null)
                    return;

                var normalized = value ?? string.Empty;
                if (Data.BankName == normalized)
                    return;

                Data.BankName = normalized;
                OnPropertyChanged(nameof(BankName));
                OnPropertyChanged(nameof(Data));
            }
        }

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
            set
            {
                if (SetProperty(ref _isEditMode, value))
                    OnPropertyChanged(nameof(ShowBankAccountCard));
            }
        }

        private bool _isArchiveMode;
        public bool IsArchiveMode
        {
            get => _isArchiveMode;
            set
            {
                if (SetProperty(ref _isArchiveMode, value))
                {
                    OnPropertyChanged(nameof(IsNotArchiveMode));
                    OnPropertyChanged(nameof(ShowGenerateActions));
                    OnPropertyChanged(nameof(HeaderSubtitle));
                    OnPropertyChanged(nameof(ShowArchiveModeChip));
                }
            }
        }
        private bool _isReadOnlyMode;
        public bool IsReadOnlyMode
        {
            get => _isReadOnlyMode;
            set
            {
                if (SetProperty(ref _isReadOnlyMode, value))
                {
                    if (value && IsEditMode)
                        IsEditMode = false;
                    OnPropertyChanged(nameof(IsNotArchiveMode));
                    OnPropertyChanged(nameof(ShowGenerateActions));
                    OnPropertyChanged(nameof(HeaderSubtitle));
                }
            }
        }

        public bool IsNotArchiveMode => !IsArchiveMode && !IsReadOnlyMode;
        public bool ShowGenerateActions => !IsReadOnlyMode;

        public bool IsGenderMale
        {
            get => Data?.Gender == "male" || string.IsNullOrEmpty(Data?.Gender);
            set { if (value && Data != null) { Data.Gender = "male"; OnPropertyChanged(nameof(IsGenderMale)); OnPropertyChanged(nameof(IsGenderFemale)); OnPropertyChanged(nameof(Data)); } }
        }

        public bool IsGenderFemale
        {
            get => Data?.Gender == "female";
            set { if (value && Data != null) { Data.Gender = "female"; OnPropertyChanged(nameof(IsGenderMale)); OnPropertyChanged(nameof(IsGenderFemale)); OnPropertyChanged(nameof(Data)); } }
        }

        private void NotifyBankAccountStateChanged()
        {
            OnPropertyChanged(nameof(HasBankAccountData));
            OnPropertyChanged(nameof(ShowBankAccountSection));
            OnPropertyChanged(nameof(ShowBankAccountCard));
            OnPropertyChanged(nameof(BankAccountNumber));
            OnPropertyChanged(nameof(BankName));
        }

        private void TryAutofillBankName(string accountNumber)
        {
            if (Data == null)
                return;

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

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isPageBusy;
        public bool IsPageBusy
        {
            get => _isPageBusy;
            set
            {
                if (SetProperty(ref _isPageBusy, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(BusyMessage));
                }
            }
        }

        private string _pageBusyMessage = string.Empty;
        public string PageBusyMessage
        {
            get => _pageBusyMessage;
            set
            {
                if (SetProperty(ref _pageBusyMessage, value))
                    OnPropertyChanged(nameof(BusyMessage));
            }
        }

        public bool IsBusy => IsPageBusy || IsGenerating || IsAIValidating;

        public string BusyMessage
        {
            get
            {
                if (IsPageBusy)
                    return string.IsNullOrWhiteSpace(PageBusyMessage)
                        ? (Res("DashLoading") ?? "Завантаження...")
                        : PageBusyMessage;

                if (IsGenerating)
                    return string.IsNullOrWhiteSpace(GenerateStatusMessage)
                        ? (Res("MsgGenerating") ?? "Генерація документа...")
                        : GenerateStatusMessage;

                if (IsAIValidating)
                    return string.IsNullOrWhiteSpace(AIValidationResult)
                        ? (Res("AIChatThinking") ?? "Обробка...")
                        : AIValidationResult;

                return string.Empty;
            }
        }

        public string HeaderSubtitle => IsArchiveMode
            ? $"{_firmName} · {Res("BtnArchive") ?? "Архів"}"
            : IsReadOnlyMode
                ? $"{_firmName} · {Res("BtnRecentlyDeleted") ?? "Недавно видалені"}"
                : _firmName;

        public bool ShowArchiveModeChip => IsArchiveMode;

        private string _passportFilePath = string.Empty;
        public string PassportFilePath
        {
            get => _passportFilePath;
            set => SetProperty(ref _passportFilePath, value);
        }

        private string _passportPreviewPath = string.Empty;
        public string PassportPreviewPath
        {
            get => _passportPreviewPath;
            set => SetProperty(ref _passportPreviewPath, value);
        }

        private DocPreviewState _passportPreviewState = DocPreviewState.Empty;
        public DocPreviewState PassportPreviewState
        {
            get => _passportPreviewState;
            set => SetProperty(ref _passportPreviewState, value);
        }

        private string _visaFilePath = string.Empty;
        public string VisaFilePath
        {
            get => _visaFilePath;
            set => SetProperty(ref _visaFilePath, value);
        }

        private string _visaPreviewPath = string.Empty;
        public string VisaPreviewPath
        {
            get => _visaPreviewPath;
            set => SetProperty(ref _visaPreviewPath, value);
        }

        private DocPreviewState _visaPreviewState = DocPreviewState.Empty;
        public DocPreviewState VisaPreviewState
        {
            get => _visaPreviewState;
            set => SetProperty(ref _visaPreviewState, value);
        }

        private string _passportPage2FilePath = string.Empty;
        public string PassportPage2FilePath
        {
            get => _passportPage2FilePath;
            set => SetProperty(ref _passportPage2FilePath, value);
        }

        private string _passportPage2PreviewPath = string.Empty;
        public string PassportPage2PreviewPath
        {
            get => _passportPage2PreviewPath;
            set => SetProperty(ref _passportPage2PreviewPath, value);
        }

        private DocPreviewState _passportPage2PreviewState = DocPreviewState.Empty;
        public DocPreviewState PassportPage2PreviewState
        {
            get => _passportPage2PreviewState;
            set => SetProperty(ref _passportPage2PreviewState, value);
        }

        private string _insuranceFilePath = string.Empty;
        public string InsuranceFilePath
        {
            get => _insuranceFilePath;
            set => SetProperty(ref _insuranceFilePath, value);
        }

        private string _insurancePreviewPath = string.Empty;
        public string InsurancePreviewPath
        {
            get => _insurancePreviewPath;
            set => SetProperty(ref _insurancePreviewPath, value);
        }

        private DocPreviewState _insurancePreviewState = DocPreviewState.Empty;
        public DocPreviewState InsurancePreviewState
        {
            get => _insurancePreviewState;
            set => SetProperty(ref _insurancePreviewState, value);
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

        private bool _hasPassportPage2;
        public bool HasPassportPage2
        {
            get => _hasPassportPage2;
            set => SetProperty(ref _hasPassportPage2, value);
        }

        private bool _passportPage2IsPdf;
        public bool PassportPage2IsPdf
        {
            get => _passportPage2IsPdf;
            set => SetProperty(ref _passportPage2IsPdf, value);
        }

        private bool _insuranceIsPdf;
        public bool InsuranceIsPdf
        {
            get => _insuranceIsPdf;
            set => SetProperty(ref _insuranceIsPdf, value);
        }

        private string _workPermitFilePath = string.Empty;
        public string WorkPermitFilePath
        {
            get => _workPermitFilePath;
            set => SetProperty(ref _workPermitFilePath, value);
        }

        private string _workPermitPreviewPath = string.Empty;
        public string WorkPermitPreviewPath
        {
            get => _workPermitPreviewPath;
            set => SetProperty(ref _workPermitPreviewPath, value);
        }

        private DocPreviewState _workPermitPreviewState = DocPreviewState.Empty;
        public DocPreviewState WorkPermitPreviewState
        {
            get => _workPermitPreviewState;
            set => SetProperty(ref _workPermitPreviewState, value);
        }

        private bool _hasWorkPermit;
        public bool HasWorkPermit
        {
            get => _hasWorkPermit;
            set => SetProperty(ref _hasWorkPermit, value);
        }

        private bool _workPermitIsPdf;
        public bool WorkPermitIsPdf
        {
            get => _workPermitIsPdf;
            set => SetProperty(ref _workPermitIsPdf, value);
        }

        private string? _pdfPreviewTempFolder;
        private CancellationTokenSource? _previewLoadCts;

        public bool IsWorkPermitType => Data.EmployeeType == "work_permit";
        public bool IsEuIdCardEmployee =>
            string.Equals(Data.EmployeeType, "eu_citizen", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Data.EuDocumentType, "id_card", StringComparison.OrdinalIgnoreCase);
        public bool HasPassportPage2SecondaryDocument => !string.IsNullOrWhiteSpace(PassportPage2FilePath);
        public bool ShowVisaDocumentCard => !HasPassportPage2SecondaryDocument;
        public bool ShowIdCardSecondSideCard => IsEuIdCardEmployee && HasPassportPage2SecondaryDocument;
        public string SecondaryDocumentDisplayName =>
            HasPassportPage2SecondaryDocument
                ? (IsEuIdCardEmployee
                    ? (Res("StepIdCardPage2Data") ?? "Дані ID-карти (2 сторони)")
                    : (Res("StepPassportPage2Data") ?? "Дані з паспорту (стор. 2)"))
                : (Res("DetDocVisa") ?? "Віза");
        public bool HasSecondaryDocument => HasPassportPage2 || HasVisa;
        public string SecondaryDocumentFilePath =>
            !string.IsNullOrWhiteSpace(PassportPage2FilePath) ? PassportPage2FilePath : VisaFilePath;
        public string SecondaryDocumentPreviewPath =>
            !string.IsNullOrWhiteSpace(PassportPage2PreviewPath) ? PassportPage2PreviewPath : VisaPreviewPath;
        public DocPreviewState SecondaryDocumentPreviewState =>
            !string.IsNullOrWhiteSpace(PassportPage2FilePath) ? PassportPage2PreviewState : VisaPreviewState;
        public string SecondaryDocumentNumberLabel => IsEuIdCardEmployee
            ? $"{Res("DetFieldVisaNum")} ({Res("WizIdCardNumberHint")})"
            : (Res("DetFieldVisaNum") ?? "Номер візи");
        public string SecondaryDocumentAuthorityLabel => IsEuIdCardEmployee
            ? $"{Res("DetFieldVisaAuthority")} ({Res("WizIdCardAuthorityHint")})"
            : (Res("DetFieldVisaAuthority") ?? "Орган, що видав візу");
        public string SecondaryDocumentExpiryLabel => IsEuIdCardEmployee
            ? $"{Res("DetFieldExpiry")} ({Res("WizIdCardExpiryHint")})"
            : (Res("DetFieldExpiry") ?? "Термін дії");
        public string SecondaryDocumentRetryPreviewKey => !string.IsNullOrWhiteSpace(PassportPage2FilePath)
            ? "passport_page2"
            : "visa";
        public string SecondaryDocumentAIValidationKey => HasPassportPage2SecondaryDocument
            ? (IsEuIdCardEmployee ? "id_card_back" : "passport2")
            : "visa";
        public string SecondaryDocumentSourceDocumentKey => HasPassportPage2SecondaryDocument
            ? (IsEuIdCardEmployee ? "id_card_back" : "passport2")
            : "visa";

        // ---- Custom Signed Documents ----
        private ObservableCollection<CustomSignedDocument> _customDocuments = new();
        public ObservableCollection<CustomSignedDocument> CustomDocuments
        {
            get => _customDocuments;
            set => SetProperty(ref _customDocuments, value);
        }

        private bool _isAddCustomDocOpen;
        public bool IsAddCustomDocOpen
        {
            get => _isAddCustomDocOpen;
            set => SetProperty(ref _isAddCustomDocOpen, value);
        }

        private string _newCustomDocName = string.Empty;
        public string NewCustomDocName
        {
            get => _newCustomDocName;
            set => SetProperty(ref _newCustomDocName, value);
        }

        private string _newCustomDocSignDate = string.Empty;
        public string NewCustomDocSignDate
        {
            get => _newCustomDocSignDate;
            set => SetProperty(ref _newCustomDocSignDate, value);
        }

        private string _newCustomDocExpiryDate = string.Empty;
        public string NewCustomDocExpiryDate
        {
            get => _newCustomDocExpiryDate;
            set => SetProperty(ref _newCustomDocExpiryDate, value);
        }

        private string _newCustomDocFilePath = string.Empty;
        public string NewCustomDocFilePath
        {
            get => _newCustomDocFilePath;
            set => SetProperty(ref _newCustomDocFilePath, value);
        }

        private string _addCustomDocError = string.Empty;
        public string AddCustomDocError
        {
            get => _addCustomDocError;
            set => SetProperty(ref _addCustomDocError, value);
        }

        public ICommand AddCustomDocCommand { get; private set; } = null!;
        public ICommand CancelAddCustomDocCommand { get; private set; } = null!;
        public ICommand ConfirmAddCustomDocCommand { get; private set; } = null!;
        public ICommand BrowseCustomDocFileCommand { get; private set; } = null!;
        public ICommand OpenCustomDocCommand { get; private set; } = null!;
        public ICommand OpenCustomDocFolderCommand { get; private set; } = null!;
        public ICommand ToggleHideCustomDocCommand { get; private set; } = null!;
        public ICommand DeleteCustomDocCommand { get; private set; } = null!;

        private ObservableCollection<CustomSignedDocument> _hiddenCustomDocuments = new();
        public ObservableCollection<CustomSignedDocument> HiddenCustomDocuments
        {
            get => _hiddenCustomDocuments;
            set => SetProperty(ref _hiddenCustomDocuments, value);
        }

        private bool _isHiddenSectionVisible;
        public bool IsHiddenSectionVisible
        {
            get => _isHiddenSectionVisible;
            set => SetProperty(ref _isHiddenSectionVisible, value);
        }

        public bool HasHiddenDocs => HiddenCustomDocuments.Count > 0;
        public int HiddenCustomDocsCount => HiddenCustomDocuments.Count;
        public ICommand HideCustomDocCommand { get; private set; } = null!;
        public ICommand UnhideCustomDocCommand { get; private set; } = null!;
        public ICommand ToggleHiddenDocsSectionCommand { get; private set; } = null!;

        // ---- Renew Work Permit dialog ----
        private bool _isRenewWpDialogOpen;
        public bool IsRenewWpDialogOpen
        {
            get => _isRenewWpDialogOpen;
            set => SetProperty(ref _isRenewWpDialogOpen, value);
        }

        private string _renewWpFilePath = string.Empty;
        public string RenewWpFilePath
        {
            get => _renewWpFilePath;
            set => SetProperty(ref _renewWpFilePath, value);
        }

        private string _renewWpNumber = string.Empty;
        public string RenewWpNumber
        {
            get => _renewWpNumber;
            set => SetProperty(ref _renewWpNumber, value);
        }

        private string _renewWpType = string.Empty;
        public string RenewWpType
        {
            get => _renewWpType;
            set => SetProperty(ref _renewWpType, value);
        }

        private string _renewWpIssueDate = string.Empty;
        public string RenewWpIssueDate
        {
            get => _renewWpIssueDate;
            set => SetProperty(ref _renewWpIssueDate, value);
        }

        private string _renewWpExpiry = string.Empty;
        public string RenewWpExpiry
        {
            get => _renewWpExpiry;
            set => SetProperty(ref _renewWpExpiry, value);
        }

        private string _renewWpAuthority = string.Empty;
        public string RenewWpAuthority
        {
            get => _renewWpAuthority;
            set => SetProperty(ref _renewWpAuthority, value);
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
            set
            {
                if (SetProperty(ref _isGenerating, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(BusyMessage));
                }
            }
        }

        private string _generateStatusMessage = string.Empty;
        public string GenerateStatusMessage
        {
            get => _generateStatusMessage;
            set
            {
                if (SetProperty(ref _generateStatusMessage, value))
                    OnPropertyChanged(nameof(BusyMessage));
            }
        }

        // ---- AI Validation ----
        private bool _isAIValidating;
        public bool IsAIValidating
        {
            get => _isAIValidating;
            set
            {
                if (SetProperty(ref _isAIValidating, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(BusyMessage));
                }
            }
        }

        private string _aiValidationResult = string.Empty;
        public string AIValidationResult
        {
            get => _aiValidationResult;
            set
            {
                if (SetProperty(ref _aiValidationResult, value))
                    OnPropertyChanged(nameof(BusyMessage));
            }
        }

        private bool _isAIValidationOpen;
        public bool IsAIValidationOpen
        {
            get => _isAIValidationOpen;
            set => SetProperty(ref _isAIValidationOpen, value);
        }

        private ObservableCollection<AIFieldCheckItem> _aiValidationItems = new();
        public ObservableCollection<AIFieldCheckItem> AIValidationItems
        {
            get => _aiValidationItems;
            set => SetProperty(ref _aiValidationItems, value);
        }

        public IEnumerable<AIFieldCheckItem> AIValidationAutofillItems =>
            AIValidationItems.Where(i => i.CanAutofill && !string.Equals(i.Severity, "ok", StringComparison.OrdinalIgnoreCase));

        public IEnumerable<AIFieldCheckItem> AIValidationErrorItems =>
            AIValidationItems.Where(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase));

        public IEnumerable<AIFieldCheckItem> AIValidationWarningItems =>
            AIValidationItems.Where(i => string.Equals(i.Severity, "warning", StringComparison.OrdinalIgnoreCase) && !i.CanAutofill);

        public IEnumerable<AIFieldCheckItem> AIValidationAttentionItems =>
            AIValidationItems.Where(i => !string.Equals(i.Severity, "ok", StringComparison.OrdinalIgnoreCase));

        public IEnumerable<AIFieldCheckItem> AIValidationOkItems =>
            AIValidationItems.Where(i => string.Equals(i.Severity, "ok", StringComparison.OrdinalIgnoreCase));

        public bool HasAIValidationItems => AIValidationItems.Count > 0;
        public bool HasAIValidationAttentionItems => AIValidationItems.Any(i => !string.Equals(i.Severity, "ok", StringComparison.OrdinalIgnoreCase));
        public bool HasAIValidationAutofillItems => AIValidationItems.Any(i => i.CanAutofill && !string.Equals(i.Severity, "ok", StringComparison.OrdinalIgnoreCase));
        public bool HasAIValidationErrorItems => AIValidationItems.Any(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase));
        public bool HasAIValidationWarningItems => AIValidationItems.Any(i => string.Equals(i.Severity, "warning", StringComparison.OrdinalIgnoreCase) && !i.CanAutofill);
        public bool HasAIValidationOkItems => AIValidationItems.Any(i => string.Equals(i.Severity, "ok", StringComparison.OrdinalIgnoreCase));

        public ICommand AIValidateCommand { get; }
        public ICommand CloseAIValidationCommand { get; }
        public ICommand ApplyAISuggestionCommand { get; }
        public ICommand ApplyAllAISuggestionsCommand { get; }
        public ICommand OpenAIValidationSourceCommand { get; }

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
        public ICommand OpenWorkPermitCommand { get; }
        public ICommand RetryPreviewCommand { get; }
        public ICommand RenewWorkPermitCommand { get; }
        public ICommand ConfirmRenewWpCommand { get; }
        public ICommand CancelRenewWpCommand { get; }
        public ICommand ArchiveEmployeeCommand { get; }
        public ICommand ConfirmArchiveCommand { get; }
        public ICommand CancelArchiveCommand { get; }
        public ICommand ShowHistoryCommand { get; }
        public ICommand DeleteHistoryEntryCommand { get; }

        public ICommand ShowSalaryCommand { get; }

        // Salary History
        private ObservableCollection<SalaryHistoryRecord> _salaryHistoryEntries = new();
        public ObservableCollection<SalaryHistoryRecord> SalaryHistoryEntries
        {
            get => _salaryHistoryEntries;
            set => SetProperty(ref _salaryHistoryEntries, value);
        }

        private bool _hasSalaryHistory;
        public bool HasSalaryHistory
        {
            get => _hasSalaryHistory;
            set => SetProperty(ref _hasSalaryHistory, value);
        }

        private decimal _totalSalaryEarned;
        public decimal TotalSalaryEarned
        {
            get => _totalSalaryEarned;
            set => SetProperty(ref _totalSalaryEarned, value);
        }

        private decimal _totalHoursAll;
        public decimal TotalHoursAll
        {
            get => _totalHoursAll;
            set => SetProperty(ref _totalHoursAll, value);
        }

        private ObservableCollection<SalaryMonthDisplay> _salaryMonthDisplays = new();
        public ObservableCollection<SalaryMonthDisplay> SalaryMonthDisplays
        {
            get => _salaryMonthDisplays;
            set => SetProperty(ref _salaryMonthDisplays, value);
        }

        private bool _isHistoryLoaded;
        private bool _isSalaryHistoryLoaded;

        private bool _hasAdvances;
        public bool HasAdvances
        {
            get => _hasAdvances;
            set => SetProperty(ref _hasAdvances, value);
        }

        private decimal _totalAdvances;
        public decimal TotalAdvances
        {
            get => _totalAdvances;
            set => SetProperty(ref _totalAdvances, value);
        }

        // History
        private List<EmployeeHistoryEntry> _allHistoryEntries = new();
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

        private string _historyFilter = "All";
        public string HistoryFilter
        {
            get => _historyFilter;
            set
            {
                if (SetProperty(ref _historyFilter, value))
                    ApplyHistoryFilter();
            }
        }

        public ICommand SetHistoryFilterCommand { get; private set; } = null!;

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
        public List<string> AvailableStatuses { get; } = new(StatusHelper.AllKeys);

        // Company positions for ComboBox
        public ObservableCollection<Position> CompanyPositions { get; } = new();
        public ObservableCollection<WorkAddress> CompanyAddresses { get; } = new();
        public ObservableCollection<InsuranceCompanyOption> InsuranceCompanies { get; } =
            new ObservableCollection<InsuranceCompanyOption>(InsuranceCompanyCatalog.All);
        public ObservableCollection<EducationOption> EducationOptions { get; } =
            new ObservableCollection<EducationOption>(EducationCatalog.All);

        private bool _isInitializingSelectedPosition;
        private Position? _selectedPosition;
        public Position? SelectedPosition
        {
            get => _selectedPosition;
            set
            {
                if (SetProperty(ref _selectedPosition, value) && value != null)
                {
                    if (_isInitializingSelectedPosition)
                        return;

                    Data.PositionTag = value.Title;
                    Data.PositionNumber = value.PositionNumber;
                    Data.MonthlySalaryBrutto = value.MonthlySalaryBrutto;
                    Data.HourlySalary = value.HourlySalary;
                    OnPropertyChanged(nameof(Data));
                }
            }
        }

        private WorkAddress? _selectedWorkAddress;
        public WorkAddress? SelectedWorkAddress
        {
            get => _selectedWorkAddress;
            set
            {
                if (SetProperty(ref _selectedWorkAddress, value) && value != null)
                {
                    Data.WorkAddressTag = FormatWorkAddress(value);
                    OnPropertyChanged(nameof(Data));
                }
            }
        }

        private bool _isInitializingSelectedInsuranceCompany;
        private InsuranceCompanyOption? _selectedInsuranceCompany;
        public InsuranceCompanyOption? SelectedInsuranceCompany
        {
            get => _selectedInsuranceCompany;
            set
            {
                if (SetProperty(ref _selectedInsuranceCompany, value) && value != null)
                {
                    if (_isInitializingSelectedInsuranceCompany)
                        return;

                    Data.InsuranceCompanyShort = value.ShortName;
                    Data.InsuranceCompanyFull = value.FullName;
                    OnPropertyChanged(nameof(InsuranceCompanyFullDisplay));
                    OnPropertyChanged(nameof(Data));
                }
            }
        }

        public string InsuranceCompanyFullDisplay => SelectedInsuranceCompany?.DisplayName
            ?? (string.IsNullOrWhiteSpace(Data.InsuranceCompanyFull) ? string.Empty : Data.InsuranceCompanyFull);

        private bool _isInitializingSelectedEducationOption;
        private EducationOption? _selectedEducationOption;
        public EducationOption? SelectedEducationOption
        {
            get => _selectedEducationOption;
            set
            {
                if (SetProperty(ref _selectedEducationOption, value) && value != null)
                {
                    if (_isInitializingSelectedEducationOption)
                        return;

                    Data.HighestEducationCode = value.Code;
                    OnPropertyChanged(nameof(HighestEducationDisplay));
                    OnPropertyChanged(nameof(Data));
                }
            }
        }

        public string HighestEducationDisplay => EducationCatalog.GetFullDisplay(Data?.HighestEducationCode);

        // Profile completion
        public int ProfileCompletionPercent => CalcProfileCompletion();

        private int CalcProfileCompletion()
        {
            var fields = new List<string> {
                Data.FirstName, Data.LastName, Data.BirthDate,
                Data.PassportNumber, Data.PassportExpiry, Data.PassportCity, Data.PassportCountry, Data.Citizenship, Data.IssuingCountry,
                Data.Phone, Data.Email, Data.StartDate, Data.ContractSignDate,
                Data.AddressLocal.Street, Data.AddressLocal.City,
                Data.PositionTag, Data.ContractType
            };

            if (Data.EmployeeType != "passport_only")
            {
                fields.AddRange(new[] { Data.VisaNumber, Data.VisaExpiry, Data.InsuranceNumber, Data.InsuranceExpiry });
            }

            int filled = fields.Count(f => !string.IsNullOrWhiteSpace(f));
            return fields.Count == 0 ? 0 : (int)Math.Round(filled * 100.0 / fields.Count);
        }

        // Expiry warnings
        public string PassportExpiryWarning => GetExpiryWarning(Data.PassportExpiry);
        public string VisaExpiryWarning => GetExpiryWarning(Data.VisaExpiry);
        public string InsuranceExpiryWarning => GetExpiryWarning(Data.InsuranceExpiry);

        private string GetExpiryWarning(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return string.Empty;
            if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.GetCultureInfo("cs-CZ"), System.Globalization.DateTimeStyles.None, out var dt) ||
                DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                var days = (dt - DateTime.Today).Days;
                if (days < 0) return "expired";
                if (days <= 7) return "critical";
                if (days <= 30) return "warning";
            }
            return string.Empty;
        }

        // Export PDF
        public ICommand ExportProfilePdfCommand { get; }

        public EmployeeDetailsViewModel(
            string firmName,
            string employeeFolder,
            EmployeeService? employeeService = null,
            bool isReadOnlyMode = false,
            string? employeeId = null,
            GeminiApiService? geminiApiService = null,
            FinanceService? financeService = null,
            AppSettingsService? appSettingsService = null,
            ActivityLogService? activityLogService = null,
            CompanyService? companyService = null,
            DocumentLocalizationService? documentLocalizationService = null,
            TemplateService? templateService = null,
            DocumentGenerationService? documentGenerationService = null,
            TagCatalogService? tagCatalogService = null,
            AiWindowFactory? aiWindowFactory = null)
        {
            _firmName = firmName;
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _geminiApiService = geminiApiService ?? throw new InvalidOperationException("GeminiApiService is not initialized.");
            _financeService = financeService ?? throw new InvalidOperationException("FinanceService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");
            _templateService = templateService ?? throw new InvalidOperationException("TemplateService is not initialized.");
            _documentGenerationService = documentGenerationService ?? throw new InvalidOperationException("DocumentGenerationService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _aiWindowFactory = aiWindowFactory ?? throw new InvalidOperationException("AiWindowFactory is not initialized.");
            _isReadOnlyMode = isReadOnlyMode;
            _employeeFolder = Directory.Exists(employeeFolder)
                ? employeeFolder
                : (_financeService.ResolveEmployeeFolder(employeeFolder, employeeId) ?? employeeFolder);

            var settings = _appSettingsService.Settings;
            _tabIndex = Math.Clamp(settings.EmployeeDetailsLastTabIndex, 0, 3);

            Data = LoadInitialEmployeeData();
            Data.Status = StatusHelper.Normalize(Data.Status);
            NormalizeInsuranceCompanyFields();
            NormalizeEducationFields();
            NotifyBankAccountStateChanged();
            TryAutofillBankName(Data.BankAccountNumber);
            RefreshDocuments();
            LoadCompanyPositions();
            LoadCompanyAddresses();

            if (_tabIndex == 2)
                EnsureHistoryLoaded();
            else if (_tabIndex == 3)
                EnsureSalaryHistoryLoaded();

            CloseCommand = new RelayCommand(o => RaiseRequestClose());
            ShowDocumentsCommand = new RelayCommand(o => TabIndex = 0);
            ShowProfileCommand = new RelayCommand(o => TabIndex = 1);
            ShowHistoryCommand = new RelayCommand(o =>
            {
                TabIndex = 2;
                EnsureHistoryLoaded();
            });
            SetHistoryFilterCommand = new RelayCommand(o => HistoryFilter = o?.ToString() ?? "All");
            ShowSalaryCommand = new RelayCommand(o =>
            {
                TabIndex = 3;
                EnsureSalaryHistoryLoaded();
            });
            EditProfileCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Редагувати профіль працівника"))
                    return;

                IsEditMode = true;
            }, _ => !IsReadOnlyMode && !IsArchiveMode);
            SaveProfileCommand = new AsyncRelayCommand(_ => SaveProfileAsync(), _ => !IsReadOnlyMode);
            CancelEditCommand = new RelayCommand(o => CancelEdit(), _ => !IsReadOnlyMode);

            ReplacePassportCommand = new AsyncRelayCommand(_ => ReplaceDocumentAsync("passport"), _ => !IsReadOnlyMode);
            ReplaceVisaCommand = new AsyncRelayCommand(_ => ReplaceDocumentAsync(HasPassportPage2SecondaryDocument ? "passport_page2" : "visa"), _ => !IsReadOnlyMode);
            ReplaceInsuranceCommand = new AsyncRelayCommand(_ => ReplaceDocumentAsync("insurance"), _ => !IsReadOnlyMode);
            ReplacePhotoCommand = new AsyncRelayCommand(_ => ReplaceDocumentAsync("photo"), _ => !IsReadOnlyMode);

            OpenPassportCommand = new RelayCommand(o => OpenFile(PassportFilePath), o => HasPassport);
            OpenVisaCommand = new RelayCommand(o => OpenFile(SecondaryDocumentFilePath), o => HasSecondaryDocument);
            OpenInsuranceCommand = new RelayCommand(o => OpenFile(InsuranceFilePath), o => HasInsurance);
            OpenPhotoCommand = new RelayCommand(o => OpenFile(PhotoFilePath), o => HasPhoto);
            RetryPreviewCommand = new RelayCommand(o =>
            {
                if (o is string docType)
                    RebuildSinglePreview(docType);
            });

            OpenGenerateDialogCommand = new RelayCommand(o => OpenGenerateDialog(), _ => !IsReadOnlyMode);
            CloseGenerateDialogCommand = new RelayCommand(o => IsGenerateDialogOpen = false);
            GenerateFromTemplateCommand = new AsyncRelayCommand(o => GenerateDocumentAsync(o as TemplateEntry), _ => !IsReadOnlyMode);

            OpenFolderCommand = new RelayCommand(o =>
            {
                if (!EnsureEmployeeFolderAvailable("EmployeeDetailsViewModel.OpenFolder", notifyUser: true))
                    return;

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _employeeFolder,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { LoggingService.LogError("EmployeeDetailsViewModel.OpenFolder", ex); StatusMessage = Res("MsgOpenFolderFail"); }
            });

            ExtendVisaCommand = new RelayCommand(o => ShowExtendDialog("visa"), _ => !IsReadOnlyMode);
            ExtendInsuranceCommand = new RelayCommand(o => ShowExtendDialog("insurance"), _ => !IsReadOnlyMode);
            ConfirmExtendCommand = new AsyncRelayCommand(_ => ConfirmExtendAsync(), _ => !IsReadOnlyMode);
            CancelExtendCommand = new RelayCommand(o => IsExtendDialogOpen = false, _ => !IsReadOnlyMode);

            OpenWorkPermitCommand = new RelayCommand(o => OpenFile(WorkPermitFilePath), o => HasWorkPermit);
            RenewWorkPermitCommand = new RelayCommand(o => StartRenewWorkPermit(), o => IsWorkPermitType && !IsReadOnlyMode);
            ConfirmRenewWpCommand = new AsyncRelayCommand(_ => ConfirmRenewWorkPermitAsync(), _ => !IsReadOnlyMode);
            CancelRenewWpCommand = new RelayCommand(o => IsRenewWpDialogOpen = false, _ => !IsReadOnlyMode);

            ArchiveEmployeeCommand = new RelayCommand(o =>
            {
                ArchiveEndDate = DateTime.Today.ToString("dd.MM.yyyy");
                ArchiveStatus = string.Empty;
                IsArchiveDialogOpen = true;
            }, _ => !IsReadOnlyMode);
            ConfirmArchiveCommand = new AsyncRelayCommand(_ => ConfirmArchiveAsync(), _ => !IsReadOnlyMode);
            CancelArchiveCommand = new RelayCommand(o => IsArchiveDialogOpen = false, _ => !IsReadOnlyMode);
            DeleteHistoryEntryCommand = new AsyncRelayCommand(
                async o =>
                {
                    if (o is EmployeeHistoryEntry item)
                        await DeleteHistoryEntryAsync(item);
                },
                o => o is EmployeeHistoryEntry && !IsReadOnlyMode);
            ExportProfilePdfCommand = new RelayCommand(o => ExportProfilePdf());
            AIValidateCommand = new AsyncRelayCommand(_ => RunAIValidationAsync(), _ => !IsAIValidating);
            CloseAIValidationCommand = new RelayCommand(o => IsAIValidationOpen = false);
            ApplyAISuggestionCommand = new AsyncRelayCommand(
                async o =>
                {
                    if (o is AIFieldCheckItem item)
                        await ApplyAISuggestionAsync(item);
                },
                o => o is AIFieldCheckItem item && item.CanAutofill && !item.IsApplied && !IsAIValidating && !IsReadOnlyMode);
            ApplyAllAISuggestionsCommand = new AsyncRelayCommand(
                async _ => await ApplyAllAISuggestionsAsync(),
                _ => !IsAIValidating && !IsReadOnlyMode && AIValidationAutofillItems.Any(i => !i.IsApplied));
            OpenAIValidationSourceCommand = new RelayCommand(
                o =>
                {
                    if (o is AIFieldCheckItem item)
                        OpenAIValidationSource(item);
                },
                o => o is AIFieldCheckItem item && CanOpenAIValidationSource(item));


            AddCustomDocCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Додати підписаний документ"))
                    return;

                NewCustomDocName = string.Empty;
                NewCustomDocSignDate = DateTime.Today.ToString("dd.MM.yyyy");
                NewCustomDocExpiryDate = string.Empty;
                NewCustomDocFilePath = string.Empty;
                AddCustomDocError = string.Empty;
                IsAddCustomDocOpen = true;
            }, _ => !IsReadOnlyMode);
            CancelAddCustomDocCommand = new RelayCommand(o => IsAddCustomDocOpen = false, _ => !IsReadOnlyMode);
            ConfirmAddCustomDocCommand = new AsyncRelayCommand(_ => ConfirmAddCustomDocAsync(), _ => !IsReadOnlyMode);
            BrowseCustomDocFileCommand = new RelayCommand(o => BrowseCustomDocFile(), _ => !IsReadOnlyMode);
            OpenCustomDocCommand = new RelayCommand(o =>
            {
                if (o is CustomSignedDocument cd)
                {
                    var path = _employeeService.GetCustomDocPath(_employeeFolder, cd.FileName);
                    if (!string.IsNullOrEmpty(path))
                    {
                        OpenFile(path);
                    }
                    else
                    {
                        LoggingService.LogWarning("EmployeeDetailsViewModel.OpenCustomDocCommand",
                            $"Custom document file not found for {_employeeFolder}: {cd.FileName}");
                        StatusMessage = Res("MsgFileNotFound");
                    }
                }
            });
            HideCustomDocCommand = new RelayCommand(o =>
            {
                if (o is CustomSignedDocument cd) ToggleHideDoc(cd, true);
            }, _ => !IsReadOnlyMode);
            UnhideCustomDocCommand = new RelayCommand(o =>
            {
                if (o is CustomSignedDocument cd) ToggleHideDoc(cd, false);
            }, _ => !IsReadOnlyMode);
            ToggleHiddenDocsSectionCommand = new RelayCommand(o =>
                IsHiddenSectionVisible = !IsHiddenSectionVisible);

            OpenCustomDocFolderCommand = new RelayCommand(o =>
            {
                if (!EnsureEmployeeFolderAvailable("EmployeeDetailsViewModel.OpenCustomDocFolder", notifyUser: true))
                    return;

                var folderPath = System.IO.Path.Combine(_employeeFolder, "CustomDocs");
                if (!System.IO.Directory.Exists(folderPath))
                    System.IO.Directory.CreateDirectory(folderPath);
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = folderPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { LoggingService.LogError("OpenCustomDocFolder", ex); }
            });
            ToggleHideCustomDocCommand = new RelayCommand(o =>
            {
                if (o is CustomSignedDocument cd) ToggleHideDoc(cd, !cd.IsHidden);
            }, _ => !IsReadOnlyMode);
            DeleteCustomDocCommand = new AsyncRelayCommand(o =>
            {
                if (o is CustomSignedDocument cd) return DeleteCustomDocAsync(cd);
                return Task.CompletedTask;
            }, _ => !IsReadOnlyMode);

            LoadCustomDocuments();

            RefreshExpiryWarnings();

            if (_profileUnavailable)
                ScheduleProfileClose();
        }

        private void SetBusyState(bool isBusy, string? message = null)
        {
            PageBusyMessage = isBusy ? (message ?? string.Empty) : string.Empty;
            IsPageBusy = isBusy;
        }

        private void ShowExtendDialog(string type)
        {
            _extendType = type;
            ExtendDialogTitle = type == "visa" ? Res("DetExtendTitleVisa") : Res("DetExtendTitleIns");
            NewExpiryDate = type == "visa" ? Data.VisaExpiry : Data.InsuranceExpiry;
            IsExtendDialogOpen = true;
        }

        private async Task ConfirmExtendAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Продовжити документ"))
                return;

            if (string.IsNullOrWhiteSpace(NewExpiryDate))
            {
                StatusMessage = Res("MsgEnterNewDate");
                return;
            }

            try
            {
                SetBusyState(true, Res("DashLoading") ?? "Завантаження...");
                var oldDate = _extendType == "visa" ? Data.VisaExpiry : Data.InsuranceExpiry;
                var fieldName = _extendType == "visa" ? Res("DetExtendFieldVisa") : Res("DetExtendFieldIns");
                var actionName = _extendType == "visa" ? Res("HistoryExtendVisa") : Res("HistoryExtendIns");

                if (_extendType == "visa")
                    Data.VisaExpiry = NewExpiryDate;
                else if (_extendType == "insurance")
                    Data.InsuranceExpiry = NewExpiryDate;

                if (_employeeService.SaveEmployeeData(_employeeFolder, Data, notifyUser: false))
                {
                    await _employeeService.AddHistoryEntry(_employeeFolder, Data.UniqueId, new EmployeeHistoryEntry
                    {
                        EventType = "DocumentExtended",
                        Action = actionName,
                        Field = fieldName,
                        OldValue = oldDate,
                        NewValue = NewExpiryDate,
                        Description = $"{fieldName}: {oldDate} → {NewExpiryDate}"
                    });

                    _activityLogService.Log(_extendType == "visa" ? "VisaExtended" : "InsuranceExtended",
                        "Document", _firmName, FullName,
                        $"{fieldName}: {oldDate} → {NewExpiryDate}",
                        oldDate, NewExpiryDate, employeeFolder: _employeeFolder);

                    IsExtendDialogOpen = false;
                    OnPropertyChanged(nameof(Data));
                    InvalidateDetailCaches();
                    DataChanged?.Invoke();
                    StatusMessage = string.Empty;
                }
                else
                {
                    StatusMessage = Res("MsgSaveFail");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private async Task ConfirmArchiveAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Архівувати працівника"))
                return;

            if (string.IsNullOrWhiteSpace(ArchiveEndDate))
            {
                ArchiveStatus = Res("MsgEnterArchiveDate");
                return;
            }

            try
            {
                SetBusyState(true, Res("DetArchiveTitle") ?? "Перемістити в архів");
                await _employeeService.AddHistoryEntry(_employeeFolder, Data.UniqueId, new EmployeeHistoryEntry
                {
                    EventType = "Archived",
                    Action = Res("HistoryActionArchive"),
                    Field = Res("HistoryFieldArchive"),
                    OldValue = _firmName,
                    NewValue = ArchiveEndDate,
                    Description = string.Format(Res("HistoryDescArchive"), _firmName, ArchiveEndDate)
                });

                PhotoFilePath = string.Empty;
                HasPhoto = false;
                PassportFilePath = string.Empty;
                VisaFilePath = string.Empty;
                InsuranceFilePath = string.Empty;
                WorkPermitFilePath = string.Empty;
                PassportPreviewPath = string.Empty;
                VisaPreviewPath = string.Empty;
                InsurancePreviewPath = string.Empty;
                WorkPermitPreviewPath = string.Empty;
                OnPropertyChanged(nameof(PhotoFilePath));
                OnPropertyChanged(nameof(PassportFilePath));
                OnPropertyChanged(nameof(VisaFilePath));
                OnPropertyChanged(nameof(InsuranceFilePath));
                OnPropertyChanged(nameof(WorkPermitFilePath));

                // Force WPF to release image file handles
                Converters.ImagePathConverter.InvalidateCache();
                await Task.Delay(100);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(200);

                var result = await _employeeService.ArchiveEmployee(_employeeFolder, _firmName, ArchiveEndDate);
                if (result.Success)
                {
                    _activityLogService.Log("EmployeeArchived", "Archive", _firmName, FullName,
                        $"Архівовано {FullName} з {_firmName}, дата закінчення: {ArchiveEndDate}",
                        _firmName, ArchiveEndDate, employeeFolder: _employeeFolder,
                        relatedOperationId: result.OperationId);

                    if (result.SourceCleanupDeferred)
                        ToastService.Instance.Warning(Res("MsgArchiveCleanupDeferred"));
                    else
                        ToastService.Instance.Success(Res("MsgArchiveSuccess"));

                    IsArchiveDialogOpen = false;
                    InvalidateDetailCaches();
                    DataChanged?.Invoke();
                    RaiseRequestClose();
                }
                else
                {
                    ArchiveStatus = Res("MsgArchiveError");
                }
            }
            catch (Exception ex)
            {
                ArchiveStatus = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        // Document generation methods moved to EmployeeDetailsViewModel.Documents.cs
        // History and salary methods moved to EmployeeDetailsViewModel.History.cs

        private async Task SaveProfileAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Зберегти профіль працівника"))
                return;

            try
            {
                SetBusyState(true, Res("EditorSaving") ?? "Збереження...");
                NormalizeInsuranceCompanyFields();
                var oldData = _employeeService.LoadEmployeeData(_employeeFolder);

                if (_employeeService.SaveEmployeeData(_employeeFolder, Data, notifyUser: false))
                {
                    if (oldData != null)
                    {
                        await _employeeService.RecordChanges(_employeeFolder, oldData, Data);
                        LogProfileChanges(oldData, Data);
                    }

                    IsEditMode = false;
                    OnPropertyChanged(nameof(IsGenderMale));
                    OnPropertyChanged(nameof(IsGenderFemale));
                    InvalidateDetailCaches();
                    DataChanged?.Invoke();
                }
                else
                {
                    StatusMessage = Res("MsgProfileSaveFail");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private void CancelEdit()
        {
            var data = _employeeService.LoadEmployeeData(_employeeFolder);
            if (data != null)
            {
                Data = data;
                NormalizeInsuranceCompanyFields();
                OnPropertyChanged(nameof(InsuranceCompanyFullDisplay));
                OnPropertyChanged(nameof(Data));
                OnPropertyChanged(nameof(FullName));
                NotifyBankAccountStateChanged();
                TryAutofillBankName(Data.BankAccountNumber);
            }
            IsEditMode = false;
            OnPropertyChanged(nameof(IsGenderMale));
            OnPropertyChanged(nameof(IsGenderFemale));
        }

        private async Task ReplaceDocumentAsync(string type)
        {
            if (!PolicyService.EnsureWriteAllowed("Оновити документ працівника"))
                return;

            try
            {
                SetBusyState(true, Res("DashLoading") ?? "Завантаження...");
                if (type == "photo")
                {
                    await ReplacePhotoSimple();
                    return;
                }

                var window = _aiWindowFactory.CreateReplaceDocumentWindow(type, Data);
                window.Owner = Application.Current?.MainWindow;
                if (window.ShowDialog() != true || !window.Saved) return;
                var tempFile = window.ResultFilePath;
                SaveReplacedDocumentFile(type, tempFile);
                CleanupTempFile(tempFile);
                var changes = ApplyNewFieldValues(window.NewValues);
                if (!_employeeService.SaveEmployeeData(_employeeFolder, Data))
                {
                    StatusMessage = Res("MsgProfileSaveFail");
                    return;
                }
                await LogDocumentReplacement(type, changes);

                OnPropertyChanged(nameof(Data));
                OnPropertyChanged(nameof(FullName));
                RefreshDocuments();
                RefreshExpiryWarnings();
                InvalidateDetailCaches();
                DataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private void SaveReplacedDocumentFile(string type, string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            var suffix = type switch
            {
                "passport" => "Pass",
                "visa" => "Viza",
                "passport_page2" => "PassPage2",
                "insurance" => string.IsNullOrWhiteSpace(Data.InsuranceCompanyShort) ? "Insurance" : Data.InsuranceCompanyShort,
                "work_permit" => "WorkPermit",
                _ => type
            };

            // Archive the old document to CustomDocs before replacing
            ArchiveOldDocument(type, suffix);

            var saved = _employeeService.SaveDocumentFromSource(
                filePath, _employeeFolder,
                $"{Data.FirstName} {Data.LastName} - {suffix}");

            switch (type)
            {
                case "passport": Data.Files.Passport = saved; break;
                case "visa": Data.Files.Visa = saved; break;
                case "passport_page2": Data.Files.PassportPage2 = saved; break;
                case "insurance": Data.Files.Insurance = saved; break;
                case "work_permit": Data.Files.WorkPermit = saved; break;
            }
        }

        private static void CleanupTempFile(string? path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)
                    && path.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                {
                    SafeFileService.DeleteFile(path);
                }
            }
            catch (Exception ex) { LoggingService.LogWarning("CleanupTempFile", ex.Message); }
        }

        private void ArchiveOldDocument(string type, string suffix)
        {
            try
            {
                // Get the current file path and expiry date for the document type
                string? currentRelativePath = type switch
                {
                    "passport"    => Data.Files.Passport,
                    "visa"        => Data.Files.Visa,
                    "passport_page2" => !string.IsNullOrWhiteSpace(Data.Files.PassportPage2) ? Data.Files.PassportPage2 : Data.Files.Visa,
                    "insurance"   => Data.Files.Insurance,
                    "work_permit" => Data.Files.WorkPermit,
                    _             => null
                };

                string? expiryDate = type switch
                {
                    "passport"    => Data.PassportExpiry,
                    "visa"        => Data.VisaExpiry,
                    "passport_page2" => Data.VisaExpiry,
                    "insurance"   => Data.InsuranceExpiry,
                    "work_permit" => Data.WorkPermitExpiry,
                    _             => null
                };

                if (string.IsNullOrEmpty(currentRelativePath)) return;

                // Build the full path of the old file (could be in employee root or CustomDocs)
                var oldFullPath = Path.Combine(_employeeFolder, currentRelativePath);
                if (!File.Exists(oldFullPath))
                    oldFullPath = Path.Combine(_employeeFolder, "CustomDocs", currentRelativePath);
                if (!File.Exists(oldFullPath)) return;

                // Build archive filename: "Ruslan Polishchuk - Pass_31.03.2026.jpg"
                var ext = Path.GetExtension(oldFullPath);
                var dateSuffix = string.IsNullOrWhiteSpace(expiryDate)
                    ? DateTime.Today.ToString("dd.MM.yyyy")
                    : expiryDate.Replace("/", ".").Replace("-", ".");
                // Sanitize date for filename
                foreach (var c in Path.GetInvalidFileNameChars())
                    dateSuffix = dateSuffix.Replace(c, '_');

                var archiveName = $"{Data.FirstName} {Data.LastName} - {suffix}_{dateSuffix}{ext}";
                foreach (var c in Path.GetInvalidFileNameChars())
                    archiveName = archiveName.Replace(c, '_');

                var customDocsFolder = Path.Combine(_employeeFolder, "CustomDocs");
                Directory.CreateDirectory(customDocsFolder);

                var destPath = Path.Combine(customDocsFolder, archiveName);

                // If destination already exists, add a counter
                if (File.Exists(destPath))
                {
                    var counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(customDocsFolder,
                            $"{Path.GetFileNameWithoutExtension(archiveName)}_{counter}{ext}");
                        counter++;
                    }
                }

                SafeFileService.MoveFile(oldFullPath, destPath);
                LoggingService.LogInfo("EmployeeDetailsViewModel.ArchiveOldDocument",
                    $"Archived old {type} → {Path.GetFileName(destPath)}");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeeDetailsViewModel.ArchiveOldDocument",
                    $"Could not archive old {type}: {ex.Message}");
            }
        }

        private List<string> ApplyNewFieldValues(Dictionary<string, string> newValues)
        {
            var changes = new List<string>();
            foreach (var (key, value) in newValues)
            {
                var prop = typeof(EmployeeData).GetProperty(key);
                if (prop == null) continue;
                var oldVal = prop.GetValue(Data)?.ToString() ?? "";
                if (value != oldVal)
                {
                    prop.SetValue(Data, value);
                    changes.Add($"{key}: {oldVal} → {value}");
                }
            }
            return changes;
        }

        private async Task LogDocumentReplacement(string type, List<string> changes)
        {
            var (fieldName, descName) = type switch
            {
                "passport" => (Res("DetDocPassport"), Res("DetDocPassport")),
                "visa" => (Res("DetDocVisa"), Res("DetDocVisa")),
                "passport_page2" => (SecondaryDocumentDisplayName, SecondaryDocumentDisplayName),
                "insurance" => (Res("DetDocInsurance"), Res("DetDocInsurance")),
                "work_permit" => (Res("DetDocWorkPermit"), Res("DetDocWorkPermit")),
                _ => (type, type)
            };

            var desc = string.Format(Res("HistoryDescDocReplace"), descName);
            if (changes.Count > 0)
                desc += " | " + string.Join(", ", changes);

            await _employeeService.AddHistoryEntry(_employeeFolder, Data.UniqueId, new EmployeeHistoryEntry
            {
                EventType = "DocumentUpdated",
                Action = Res("HistoryActionDocReplace"),
                Field = fieldName,
                Description = desc
            });
        }

        private async Task ReplacePhotoSimple()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Images|*.jpg;*.jpeg;*.png;*.heic"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                Data.Files.Photo = _employeeService.SaveDocumentFromSource(
                    dialog.FileName, _employeeFolder,
                    $"{Data.FirstName} {Data.LastName} - Photo");
                _employeeService.SaveEmployeeData(_employeeFolder, Data);
                await _employeeService.AddHistoryEntry(_employeeFolder, Data.UniqueId, new EmployeeHistoryEntry
                {
                    EventType = "DocumentUpdated",
                    Action = Res("HistoryActionDocReplace"),
                    Field = Res("DetDocPhoto"),
                    Description = string.Format(Res("HistoryDescDocReplace"), Res("DetDocPhoto"))
                });
                RefreshDocuments();
                InvalidateDetailCaches();
                DataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private void RefreshDocuments()
        {
            if (!EnsureEmployeeFolderAvailable("EmployeeDetailsViewModel.RefreshDocuments"))
            {
                ClearDocumentState();
                return;
            }

            CleanupPdfPreviews();

            PassportFilePath = ResolveDocumentPath(Data.Files.Passport, "passport");
            VisaFilePath = ResolveDocumentPath(Data.Files.Visa, "visa");
            PassportPage2FilePath = ResolveDocumentPath(Data.Files.PassportPage2, "passport page 2");
            InsuranceFilePath = ResolveDocumentPath(Data.Files.Insurance, "insurance");
            PhotoFilePath = ResolveDocumentPath(Data.Files.Photo, "photo");
            if (string.IsNullOrEmpty(PhotoFilePath))
            {
                var fallbackPhotoPath = Path.Combine(_employeeFolder, $"{Data.FirstName} {Data.LastName} - Photo.jpg");
                if (File.Exists(fallbackPhotoPath))
                    PhotoFilePath = fallbackPhotoPath;
            }
            WorkPermitFilePath = ResolveDocumentPath(Data.Files.WorkPermit, "work permit");

            HasPassport = !string.IsNullOrEmpty(PassportFilePath);
            HasVisa = !string.IsNullOrEmpty(VisaFilePath);
            HasPassportPage2 = !string.IsNullOrEmpty(PassportPage2FilePath);
            HasInsurance = !string.IsNullOrEmpty(InsuranceFilePath);
            HasPhoto = !string.IsNullOrEmpty(PhotoFilePath);
            HasWorkPermit = !string.IsNullOrEmpty(WorkPermitFilePath);

            PassportIsPdf = IsPdf(PassportFilePath);
            VisaIsPdf = IsPdf(VisaFilePath);
            PassportPage2IsPdf = IsPdf(PassportPage2FilePath);
            InsuranceIsPdf = IsPdf(InsuranceFilePath);
            WorkPermitIsPdf = IsPdf(WorkPermitFilePath);
            PassportPreviewPath = PassportIsPdf ? string.Empty : PassportFilePath;
            VisaPreviewPath = VisaIsPdf ? string.Empty : VisaFilePath;
            PassportPage2PreviewPath = PassportPage2IsPdf ? string.Empty : PassportPage2FilePath;
            InsurancePreviewPath = InsuranceIsPdf ? string.Empty : InsuranceFilePath;
            WorkPermitPreviewPath = WorkPermitIsPdf ? string.Empty : WorkPermitFilePath;
            PassportPreviewState = !HasPassport ? DocPreviewState.Empty : PassportIsPdf ? DocPreviewState.Loading : DocPreviewState.Ready;
            VisaPreviewState = !HasVisa ? DocPreviewState.Empty : VisaIsPdf ? DocPreviewState.Loading : DocPreviewState.Ready;
            PassportPage2PreviewState = !HasPassportPage2 ? DocPreviewState.Empty : PassportPage2IsPdf ? DocPreviewState.Loading : DocPreviewState.Ready;
            InsurancePreviewState = !HasInsurance ? DocPreviewState.Empty : InsuranceIsPdf ? DocPreviewState.Loading : DocPreviewState.Ready;
            WorkPermitPreviewState = !HasWorkPermit ? DocPreviewState.Empty : WorkPermitIsPdf ? DocPreviewState.Loading : DocPreviewState.Ready;

            OnPropertyChanged(nameof(IsEuIdCardEmployee));
            OnPropertyChanged(nameof(HasPassportPage2SecondaryDocument));
            OnPropertyChanged(nameof(ShowVisaDocumentCard));
            OnPropertyChanged(nameof(ShowIdCardSecondSideCard));
            OnPropertyChanged(nameof(SecondaryDocumentDisplayName));
            OnPropertyChanged(nameof(HasSecondaryDocument));
            OnPropertyChanged(nameof(SecondaryDocumentFilePath));
            OnPropertyChanged(nameof(SecondaryDocumentPreviewPath));
            OnPropertyChanged(nameof(SecondaryDocumentPreviewState));
            OnPropertyChanged(nameof(SecondaryDocumentNumberLabel));
            OnPropertyChanged(nameof(SecondaryDocumentAuthorityLabel));
            OnPropertyChanged(nameof(SecondaryDocumentExpiryLabel));
            OnPropertyChanged(nameof(SecondaryDocumentRetryPreviewKey));

            StartPdfPreviewLoading();
        }

        private void LoadCompanyPositions()
        {
            try
            {
                var company = _companyService.Companies.FirstOrDefault(c => c.Name == _firmName);
                if (company == null) return;

                CompanyPositions.Clear();
                foreach (var pos in company.Positions)
                    CompanyPositions.Add(pos);

                if (!string.IsNullOrWhiteSpace(Data.PositionTag) || !string.IsNullOrWhiteSpace(Data.PositionNumber))
                {
                    var matchedPosition = CompanyPositions.FirstOrDefault(p =>
                        string.Equals(p.Title, Data.PositionTag, StringComparison.OrdinalIgnoreCase))
                        ?? CompanyPositions.FirstOrDefault(p =>
                            string.Equals(p.PositionNumber, Data.PositionNumber, StringComparison.OrdinalIgnoreCase))
                        ?? CompanyPositions.FirstOrDefault(p =>
                            string.Equals(p.PositionNumber, Data.PositionTag, StringComparison.OrdinalIgnoreCase));

                    if (matchedPosition != null)
                    {
                        _isInitializingSelectedPosition = true;
                        try
                        {
                            SelectedPosition = matchedPosition;
                        }
                        finally
                        {
                            _isInitializingSelectedPosition = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadCompanyPositions error: {ex.Message}");
            }
        }

        private async void LoadCompanyAddresses()
        {
            try
            {
                var company = _companyService.Companies.FirstOrDefault(c => c.Name == _firmName);
                if (company == null) return;

                CompanyAddresses.Clear();
                foreach (var address in company.Addresses)
                    CompanyAddresses.Add(address);

                if (!string.IsNullOrWhiteSpace(Data.WorkAddressTag))
                {
                    var matchedAddress = CompanyAddresses.FirstOrDefault(address =>
                        string.Equals(FormatWorkAddress(address), Data.WorkAddressTag, StringComparison.OrdinalIgnoreCase));

                    if (matchedAddress != null)
                    {
                        var normalizedAddress = FormatWorkAddress(matchedAddress);
                        var shouldResave = !string.Equals(Data.WorkAddressTag, normalizedAddress, StringComparison.OrdinalIgnoreCase);
                        var oldData = shouldResave
                            ? _employeeService.LoadEmployeeData(_employeeFolder)
                            : null;

                        SelectedWorkAddress = matchedAddress;

                        if (shouldResave)
                        {
                            if (_employeeService.SaveEmployeeData(_employeeFolder, Data) && oldData != null)
                            {
                                await _employeeService.RecordChanges(_employeeFolder, oldData, Data);
                                LogProfileChanges(oldData, Data);
                                InvalidateDetailCaches();
                                DataChanged?.Invoke();

                                if (TabIndex == 2)
                                    EnsureHistoryLoaded();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadCompanyAddresses error: {ex.Message}");
            }
        }

        private static string FormatWorkAddress(WorkAddress address)
        {
            var streetPart = string.Join(" ", new[] { address.Street, address.Number }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim()));

            var cityPart = string.Join(" ", new[] { address.City, address.ZipCode }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim()));

            if (!string.IsNullOrWhiteSpace(streetPart) && !string.IsNullOrWhiteSpace(cityPart))
                return $"{streetPart}, {cityPart}";

            return !string.IsNullOrWhiteSpace(streetPart) ? streetPart : cityPart;
        }

        private string BuildPath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;
            if (string.IsNullOrWhiteSpace(_employeeFolder)) return string.Empty;
            return Path.Combine(_employeeFolder, fileName);
        }

        private static bool IsPdf(string path)
        {
            return !string.IsNullOrEmpty(path) && Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildDocPreviewPath(string filePath, string baseName, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return string.Empty;

            if (!IsPdf(filePath))
                return filePath;

            try
            {
                _pdfPreviewTempFolder ??= Path.Combine(Path.GetTempPath(), "AC_DetPreview_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(_pdfPreviewTempFolder);
                token.ThrowIfCancellationRequested();

                var pages = _employeeService.RenderPdfPages(filePath, _pdfPreviewTempFolder, baseName, maxPages: 1);
                token.ThrowIfCancellationRequested();
                if (pages.Count == 0)
                {
                    LoggingService.LogWarning("EmployeeDetailsViewModel.BuildDocPreviewPath",
                        $"PDF preview generation returned no pages for '{filePath}'.");
                    return string.Empty;
                }

                return pages[0];
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeeDetailsViewModel.BuildDocPreviewPath", ex.Message);
                return string.Empty;
            }
        }

        private void StartPdfPreviewLoading(string? docType = null)
        {
            _previewLoadCts?.Cancel();
            _previewLoadCts?.Dispose();
            _previewLoadCts = new CancellationTokenSource();
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            var token = _previewLoadCts.Token;
            var previewRequests = new List<(string DocType, string FilePath, string BaseName, Action<string> AssignPreview, Action<DocPreviewState> AssignState)>
            {
                ("passport", PassportFilePath, "det_pass", path => PassportPreviewPath = path, state => PassportPreviewState = state),
                ("visa", VisaFilePath, "det_visa", path => VisaPreviewPath = path, state => VisaPreviewState = state),
                ("passport_page2", PassportPage2FilePath, "det_pass2", path => PassportPage2PreviewPath = path, state => PassportPage2PreviewState = state),
                ("insurance", InsuranceFilePath, "det_ins", path => InsurancePreviewPath = path, state => InsurancePreviewState = state),
                ("work_permit", WorkPermitFilePath, "det_wp", path => WorkPermitPreviewPath = path, state => WorkPermitPreviewState = state)
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var request in previewRequests)
                    {
                        token.ThrowIfCancellationRequested();

                        if (!string.IsNullOrWhiteSpace(docType)
                            && !string.Equals(request.DocType, docType, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!IsPdf(request.FilePath) || !File.Exists(request.FilePath))
                            continue;

                        var previewPath = BuildDocPreviewPath(request.FilePath, request.BaseName, token);
                        token.ThrowIfCancellationRequested();

                        await dispatcher.InvokeAsync(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                request.AssignPreview(previewPath);
                                request.AssignState(string.IsNullOrWhiteSpace(previewPath) ? DocPreviewState.Error : DocPreviewState.Ready);
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }

        private void RebuildSinglePreview(string docType)
        {
            if (string.IsNullOrWhiteSpace(docType))
                return;

            switch (docType)
            {
                case "passport":
                    if (!PassportIsPdf || string.IsNullOrWhiteSpace(PassportFilePath))
                        return;
                    PassportPreviewPath = string.Empty;
                    PassportPreviewState = DocPreviewState.Loading;
                    break;
                case "visa":
                    if (!VisaIsPdf || string.IsNullOrWhiteSpace(VisaFilePath))
                        return;
                    VisaPreviewPath = string.Empty;
                    VisaPreviewState = DocPreviewState.Loading;
                    break;
                case "passport_page2":
                    if (!PassportPage2IsPdf || string.IsNullOrWhiteSpace(PassportPage2FilePath))
                        return;
                    PassportPage2PreviewPath = string.Empty;
                    PassportPage2PreviewState = DocPreviewState.Loading;
                    break;
                case "insurance":
                    if (!InsuranceIsPdf || string.IsNullOrWhiteSpace(InsuranceFilePath))
                        return;
                    InsurancePreviewPath = string.Empty;
                    InsurancePreviewState = DocPreviewState.Loading;
                    break;
                case "work_permit":
                    if (!WorkPermitIsPdf || string.IsNullOrWhiteSpace(WorkPermitFilePath))
                        return;
                    WorkPermitPreviewPath = string.Empty;
                    WorkPermitPreviewState = DocPreviewState.Loading;
                    break;
                default:
                    return;
            }

            StartPdfPreviewLoading(docType);
        }

        private void CleanupPdfPreviews()
        {
            _previewLoadCts?.Cancel();
            _previewLoadCts?.Dispose();
            _previewLoadCts = null;

            if (!string.IsNullOrWhiteSpace(_pdfPreviewTempFolder) && Directory.Exists(_pdfPreviewTempFolder))
            {
                try
                {
                    Directory.Delete(_pdfPreviewTempFolder, true);
                }
                catch
                {
                }
            }

            _pdfPreviewTempFolder = null;
        }

        private void RaiseRequestClose()
        {
            SaveLayoutSettings();
            CleanupPdfPreviews();
            RequestClose?.Invoke();
        }

        public void SaveLayoutSettings()
        {
            var settingsService = _appSettingsService;
            settingsService.Settings.EmployeeDetailsLastTabIndex = Math.Clamp(TabIndex, 0, 3);
            settingsService.SaveSettings();
        }

        private void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                LoggingService.LogWarning("EmployeeDetailsViewModel.OpenFile", $"File not found: {path}");
                StatusMessage = Res("MsgFileNotFound");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeDetailsViewModel.OpenFile", ex);
                StatusMessage = Res("MsgOpenFileFail");
            }
        }

        private void OpenAIValidationSource(AIFieldCheckItem item)
        {
            var path = ResolveAIValidationSourcePath(item);
            if (string.IsNullOrWhiteSpace(path))
                return;

            OpenFile(path);
        }

        private bool CanOpenAIValidationSource(AIFieldCheckItem item)
        {
            var path = ResolveAIValidationSourcePath(item);
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        private string ResolveAIValidationSourcePath(AIFieldCheckItem item)
        {
            if (item == null)
                return string.Empty;

            return item.SourceDocument switch
            {
                "passport" => PassportFilePath,
                "visa" => VisaFilePath,
                "passport2" => SecondaryDocumentFilePath,
                "id_card_back" => SecondaryDocumentFilePath,
                "insurance" => InsuranceFilePath,
                "permit" => WorkPermitFilePath,
                "cross-check" => ResolveCrossCheckSourcePath(item),
                _ => string.Empty
            };
        }

        private string ResolveCrossCheckSourcePath(AIFieldCheckItem item)
        {
            var key = item.FieldKey ?? string.Empty;
            if (key.Contains("insurance", StringComparison.OrdinalIgnoreCase))
                return InsuranceFilePath;

            if (key.Contains("permit", StringComparison.OrdinalIgnoreCase))
                return WorkPermitFilePath;

            if (key.Contains("visa", StringComparison.OrdinalIgnoreCase))
                return SecondaryDocumentFilePath;

            return PassportFilePath;
        }

        private EmployeeData LoadInitialEmployeeData()
        {
            if (!EnsureEmployeeFolderAvailable("EmployeeDetailsViewModel.LoadInitialEmployeeData", notifyUser: true))
                return new EmployeeData();

            var data = _employeeService.LoadEmployeeData(_employeeFolder);
            if (data != null)
            {
                NormalizeInsuranceCompanyFields(data);
                NormalizeEducationFields(data);
                return data;
            }

            LoggingService.LogWarning("EmployeeDetailsViewModel.LoadInitialEmployeeData",
                $"Employee profile could not be loaded from {_employeeFolder}");
            NotifyProfileUnavailable(Res("MsgEmployeeProfileMissing"));
            return new EmployeeData();
        }

        private void NormalizeInsuranceCompanyFields()
        {
            NormalizeInsuranceCompanyFields(Data);
        }

        private void NormalizeInsuranceCompanyFields(EmployeeData data)
        {
            var option = InsuranceCompanyNormalizer.Normalize(
                data.InsuranceCompanyShort,
                shortName: data.InsuranceCompanyShort,
                fullName: data.InsuranceCompanyFull);

            if (option != null)
            {
                data.InsuranceCompanyShort = option.ShortName;
                data.InsuranceCompanyFull = option.FullName;
            }

            _isInitializingSelectedInsuranceCompany = true;
            try
            {
                SelectedInsuranceCompany = option;
            }
            finally
            {
                _isInitializingSelectedInsuranceCompany = false;
            }

            OnPropertyChanged(nameof(InsuranceCompanyFullDisplay));
        }

        private void NormalizeEducationFields()
        {
            NormalizeEducationFields(Data);
        }

        private void NormalizeEducationFields(EmployeeData data)
        {
            data.HighestEducationCode = EducationCatalog.NormalizeCode(data.HighestEducationCode);

            _isInitializingSelectedEducationOption = true;
            try
            {
                SelectedEducationOption = EducationCatalog.FindByCode(data.HighestEducationCode);
            }
            finally
            {
                _isInitializingSelectedEducationOption = false;
            }

            OnPropertyChanged(nameof(HighestEducationDisplay));
        }

        private bool EnsureEmployeeFolderAvailable(string source, bool notifyUser = false)
        {
            if (!string.IsNullOrWhiteSpace(_employeeFolder) && Directory.Exists(_employeeFolder))
                return true;

            _profileUnavailable = true;
            ClearDocumentState();
            LoggingService.LogWarning(source, $"Employee folder not found: {_employeeFolder}");
            StatusMessage = Res("MsgEmployeeFolderMissing");
            if (notifyUser)
                NotifyProfileUnavailable(StatusMessage);
            return false;
        }

        private void NotifyProfileUnavailable(string message)
        {
            if (_profileUnavailableNotified || string.IsNullOrWhiteSpace(message))
                return;

            _profileUnavailableNotified = true;
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (Application.Current?.MainWindow?.IsVisible == true)
                {
                    ToastService.Instance.Warning(message);
                    return;
                }

                MessageBox.Show(message, Res("TitleWarning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private void ScheduleProfileClose()
        {
            if (_profileCloseScheduled)
                return;

            _profileCloseScheduled = true;
            Application.Current?.Dispatcher?.BeginInvoke(new Action(RaiseRequestClose));
        }

        private string ResolveDocumentPath(string fileName, string documentLabel)
        {
            var path = BuildPath(fileName);
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            if (File.Exists(path))
                return path;

            LoggingService.LogWarning("EmployeeDetailsViewModel.ResolveDocumentPath",
                $"Missing {documentLabel} file for {_employeeFolder}: {path}");
            return string.Empty;
        }

        private void ClearDocumentState()
        {
            CleanupPdfPreviews();
            PassportFilePath = string.Empty;
            VisaFilePath = string.Empty;
            PassportPage2FilePath = string.Empty;
            InsuranceFilePath = string.Empty;
            PhotoFilePath = string.Empty;
            WorkPermitFilePath = string.Empty;
            PassportPreviewPath = string.Empty;
            VisaPreviewPath = string.Empty;
            PassportPage2PreviewPath = string.Empty;
            InsurancePreviewPath = string.Empty;
            WorkPermitPreviewPath = string.Empty;
            HasPassport = false;
            HasVisa = false;
            HasPassportPage2 = false;
            HasInsurance = false;
            HasPhoto = false;
            HasWorkPermit = false;
            PassportIsPdf = false;
            VisaIsPdf = false;
            PassportPage2IsPdf = false;
            InsuranceIsPdf = false;
            WorkPermitIsPdf = false;
            PassportPreviewState = DocPreviewState.Empty;
            VisaPreviewState = DocPreviewState.Empty;
            PassportPage2PreviewState = DocPreviewState.Empty;
            InsurancePreviewState = DocPreviewState.Empty;
            WorkPermitPreviewState = DocPreviewState.Empty;
        }

        private void StartRenewWorkPermit()
        {
            _ = ReplaceDocumentAsync("work_permit");
        }

        private async Task ConfirmRenewWorkPermitAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Оновити дозвіл на роботу"))
                return;

            if (string.IsNullOrWhiteSpace(RenewWpFilePath) || !File.Exists(RenewWpFilePath))
            {
                StatusMessage = Res("MsgFileNotFound");
                return;
            }

            try
            {
                SetBusyState(true, Res("DashLoading") ?? "Завантаження...");
                var ext = Path.GetExtension(RenewWpFilePath);
                var destName = $"{Data.FirstName} {Data.LastName} - Povolení k práci{ext}";
                var destPath = Path.Combine(_employeeFolder, destName);

                if (!string.IsNullOrEmpty(Data.Files.WorkPermit))
                {
                    var oldPath = Path.Combine(_employeeFolder, Data.Files.WorkPermit);
                    if (File.Exists(oldPath))
                    {
                        try { SafeFileService.DeleteFile(oldPath); } catch (Exception ex) { LoggingService.LogWarning("EmployeeDetailsViewModel", $"Failed to delete old file: {ex.Message}"); }
                    }
                }

                SafeFileService.CopyFile(RenewWpFilePath, destPath);
                Data.Files.WorkPermit = destName;

                var oldNumber = Data.WorkPermitNumber;
                var oldExpiry = Data.WorkPermitExpiry;

                Data.WorkPermitNumber = RenewWpNumber;
                Data.WorkPermitType = RenewWpType;
                Data.WorkPermitIssueDate = RenewWpIssueDate;
                Data.WorkPermitExpiry = RenewWpExpiry;
                Data.WorkPermitAuthority = RenewWpAuthority;

                if (!_employeeService.SaveEmployeeData(_employeeFolder, Data))
                {
                    StatusMessage = Res("MsgProfileSaveFail");
                    return;
                }

                await _employeeService.AddHistoryEntry(_employeeFolder, Data.UniqueId, new EmployeeHistoryEntry
                {
                    EventType = "DocumentUpdated",
                    Timestamp = DateTime.Now,
                    Action = Res("HistoryActionRenewWp"),
                    Field = "WorkPermit",
                    OldValue = $"{oldNumber}, до {oldExpiry}",
                    NewValue = $"{RenewWpNumber}, до {RenewWpExpiry}",
                    Description = string.Format(Res("HistoryDescRenewWp"), oldNumber, RenewWpNumber, oldExpiry, RenewWpExpiry)
                });

                _activityLogService.Log("WorkPermitRenewed", "Document", _firmName, FullName,
                    $"{FullName}: дозвіл на роботу оновлено {oldNumber} → {RenewWpNumber}",
                    $"{oldNumber}, до {oldExpiry}", $"{RenewWpNumber}, до {RenewWpExpiry}",
                    employeeFolder: _employeeFolder);

                OnPropertyChanged(nameof(Data));
                RefreshDocuments();
                IsRenewWpDialogOpen = false;
                StatusMessage = Res("MsgWorkPermitUpdated");
                InvalidateDetailCaches();
                DataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private async Task RunAIValidationAsync()
        {
            try
            {
                var geminiService = _geminiApiService;
                if (geminiService == null || !geminiService.IsConfigured)
                {
                    AIValidationResult = Res("AIChatNoModel");
                    IsAIValidationOpen = true;
                    return;
                }

                IsAIValidating = true;
                AIValidationResult = Res("AIChatThinking");
                AIValidationItems.Clear();
                RaiseAIValidationCollectionsChanged();
                IsAIValidationOpen = true;

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var passportData = await ScanDocumentAsync(geminiService, HasPassport, PassportFilePath, PassportIsPdf,
                    Data.EmployeeType == "eu_citizen" && Data.EuDocumentType == "id_card" ? "id_card" : "passport", cts.Token);
                var visaData = await ScanDocumentAsync(
                    geminiService,
                    HasSecondaryDocument,
                    SecondaryDocumentFilePath,
                    IsPdf(SecondaryDocumentFilePath),
                    SecondaryDocumentAIValidationKey,
                    cts.Token);
                var insuranceData = await ScanDocumentAsync(geminiService, HasInsurance, InsuranceFilePath, InsuranceIsPdf, "insurance", cts.Token);
                var permitData = await ScanDocumentAsync(geminiService, HasWorkPermit, WorkPermitFilePath, WorkPermitIsPdf, "permit", cts.Token);
                BuildAIValidationItems(passportData, visaData, insuranceData, permitData);

                var d = Data;
                var context = $@"Employee data to validate:
- Full Name: {d.FirstName} {d.LastName}
- Birth Date: {d.BirthDate}
- Employee Type: {d.EmployeeType} (visa = needs visa, eu_citizen = EU citizen, work_permit = needs work permit)
- Passport Number: {d.PassportNumber}, Authority: {d.PassportAuthority}, City: {d.PassportCity}, Country: {d.PassportCountry}, Citizenship: {d.Citizenship}, Issuing Country: {d.IssuingCountry}, Expiry: {d.PassportExpiry}
- Visa Number: {d.VisaNumber}, Authority: {d.VisaAuthority}, Type: {d.VisaType}, Expiry: {d.VisaExpiry}
- Insurance: {d.InsuranceCompanyShort}, Number: {d.InsuranceNumber}, Expiry: {d.InsuranceExpiry}
- Work Permit: Name={d.WorkPermitName}, Number={d.WorkPermitNumber}, Type={d.WorkPermitType}, Authority={d.WorkPermitAuthority}, Expiry={d.WorkPermitExpiry}
- Position (CZ-ISCO tag): {d.PositionTag}, Position Number: {d.PositionNumber}
- Work Address Tag: {d.WorkAddressTag}
- Local Address: {d.AddressLocal.Street} {d.AddressLocal.Number}, {d.AddressLocal.City}, {d.AddressLocal.Zip}
- Abroad Address: {d.AddressAbroad.Street} {d.AddressAbroad.Number}, {d.AddressAbroad.City}, {d.AddressAbroad.Zip}
- Monthly Salary Brutto: {d.MonthlySalaryBrutto} CZK, Hourly: {d.HourlySalary} CZK
- Contract Type: {d.ContractType}, Start: {d.StartDate}, End: {d.EndDate}
- Phone: {d.Phone}, Email: {d.Email}
- Has Passport file: {HasPassport}, Has Visa file: {HasVisa}, Has Insurance file: {HasInsurance}, Has Photo: {HasPhoto}, Has Work Permit file: {HasWorkPermit}
- Company: {_firmName}";

                var systemPrompt = @"You are an expert validator for a Czech employment agency (agentura práce). 
Validate the employee data and find errors, warnings, and missing fields.

Check:
1. Birth date — is the person at least 18? Is the format valid (dd.MM.yyyy)?
2. Passport — does the number format match the country? Is it expired or expiring soon?
3. Visa / Work Permit logic:
   - For 'visa' type: visa document MUST be present.
   - For 'work_permit' type: work permit document MUST be present.
   - For 'eu_citizen' type: neither visa nor work permit is required.
   - IMPORTANT: If EmployeeType='visa' and WorkPermitName='Dočasná ochrana' — this means the D-visa (D/DO/667 etc.) was issued BASED ON temporary protection (Dočasná ochrana). This is NOT a separate work permit! The 'Dočasná ochrana' is the legal basis for the visa, not an additional document. In this case:
     * Work Permit FILE is NOT required — do NOT flag 'Has Work Permit file: False' as an error
     * Work Permit Number/Expiry being empty is NORMAL — do NOT flag it
     * There is NO conflict between having a D-visa and 'Dočasná ochrana' — they go together
     * The visa itself already grants the right to work
   - Similarly, if WorkPermitName='Strpění' with a D/VS or D/SD visa, the same logic applies.
4. Insurance — is it present and not expired?
5. CZ-ISCO position code — does PositionTag look like a valid Czech job classification?
6. Addresses:
   - 'Local Address' is the employee's HOME address in their country of citizenship (e.g. Ukraine for Ukrainians). It can be in any country and any language — this is NORMAL.
   - 'Abroad Address' is where the employee LIVES while working abroad (usually Czech Republic, but can be any foreign country). Check if this address looks valid: city should exist, PSC (zip) should be 5 digits for Czech addresses, street name should be in Latin script.
   - Do NOT flag 'Abroad Address' as wrong just because it is a Czech address — that IS the expected behavior for foreign workers.
   - If an address has spelling errors in Latin transliteration, suggest the correct Latin spelling.
7. Salary — is it reasonable for Czech Republic (minimum wage ~18900 CZK/month brutto)?
8. Missing documents — photo, passport file, insurance file should exist
9. Phone/email format
10. Dates consistency (start date, contract sign date, expiry dates)

Respond in " + GetUILanguageName() + @". Use text markers for severity (NEVER use emoji — they won't render):
[!!!] Error (critical issue)
[!] Warning (should review)  
[OK] Field is fine

Format: one line per check. Be concise. At the end, give a summary score like 'Score: 8/10'.";

                var result = await geminiService.ChatAsync(context, systemPrompt, cts.Token);
                if (GeminiApiService.IsTimeoutResponse(result))
                {
                    AIValidationResult = Res("AIChatTimeout");
                    return;
                }

                if (GeminiApiService.IsNetworkErrorResponse(result))
                {
                    AIValidationResult = Res("AIChatNetworkError");
                    return;
                }

                AIValidationResult = result;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeDetailsViewModel.RunAIValidation", ex);
                AIValidationResult = $"Error: {ex.Message}";
            }
            finally
            {
                IsAIValidating = false;
            }
        }

        private string GetUILanguageName()
        {
            var code = _appSettingsService.Settings.LanguageCode ?? "uk";
            return code switch
            {
                "en" => "English",
                "cs" => "Czech (čeština)",
                "ru" => "Russian",
                _ => "Ukrainian"
            };
        }

        private async Task<Dictionary<string, string>> ScanDocumentAsync(
            GeminiApiService geminiService,
            bool hasFile,
            string filePath,
            bool isPdf,
            string docKey,
            CancellationToken cancellationToken)
        {
            var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!hasFile || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return extracted;

            var prompt = AIScanPrompts.GetPrompt(docKey);
            var result = isPdf
                ? await geminiService.ChatWithFileAsync(filePath, prompt, ct: cancellationToken)
                : await geminiService.ChatWithImageAsync(filePath, prompt, ct: cancellationToken);

            if (GeminiApiService.IsFailureResponse(result))
                return extracted;

            foreach (var kv in AIScanPrompts.ParseResponse(result))
                extracted[kv.Key] = kv.Value;

            return extracted;
        }

        private void BuildAIValidationItems(
            Dictionary<string, string> passportData,
            Dictionary<string, string> visaData,
            Dictionary<string, string> insuranceData,
            Dictionary<string, string> permitData)
        {
            var items = new List<AIFieldCheckItem>();
            BuildFieldComparisonItems(items, passportData, visaData, insuranceData, permitData);
            BuildOwnershipItems(items, visaData, insuranceData, permitData);
            BuildCrossDocumentItems(items, passportData, visaData, insuranceData, permitData);

            AIValidationItems = new ObservableCollection<AIFieldCheckItem>(items);
            RaiseAIValidationCollectionsChanged();
        }

        private void BuildFieldComparisonItems(
            List<AIFieldCheckItem> items,
            Dictionary<string, string> passportData,
            Dictionary<string, string> visaData,
            Dictionary<string, string> insuranceData,
            Dictionary<string, string> permitData)
        {
            Dictionary<string, string> GetPassportFieldSource(string fieldKey)
            {
                if (!IsEuIdCardEmployee)
                    return passportData;

                return (fieldKey == "PassportAuthority" || fieldKey == "PassportCity" || fieldKey == "PassportCountry")
                       && TryGetNonEmpty(visaData, fieldKey, out _)
                    ? visaData
                    : passportData;
            }

            string GetPassportFieldSourceKey(string fieldKey)
            {
                if (!IsEuIdCardEmployee)
                    return "passport";

                return (fieldKey == "PassportAuthority" || fieldKey == "PassportCity" || fieldKey == "PassportCountry")
                       && TryGetNonEmpty(visaData, fieldKey, out _)
                    ? SecondaryDocumentSourceDocumentKey
                    : "passport";
            }

            CompareAIField(items, GetPassportFieldSource("PassportNumber"), "PassportNumber", Data.PassportNumber, Res("HistFieldPassportNum") ?? "Passport Number", GetPassportFieldSourceKey("PassportNumber"));
            CompareAIField(items, GetPassportFieldSource("PassportExpiry"), "PassportExpiry", Data.PassportExpiry, Res("HistFieldPassportExp") ?? "Passport Expiry", GetPassportFieldSourceKey("PassportExpiry"));
            CompareAIField(items, GetPassportFieldSource("PassportAuthority"), "PassportAuthority", Data.PassportAuthority, Res("HistFieldPassportAuthority") ?? "Passport Authority", GetPassportFieldSourceKey("PassportAuthority"));
            CompareAIField(items, GetPassportFieldSource("PassportCity"), "PassportCity", Data.PassportCity, Res("HistFieldPassportCity") ?? "Passport City", GetPassportFieldSourceKey("PassportCity"));
            CompareAIField(items, GetPassportFieldSource("PassportCountry"), "PassportCountry", Data.PassportCountry, Res("HistFieldPassportCountry") ?? "Passport Country", GetPassportFieldSourceKey("PassportCountry"));
            CompareAIField(items, passportData, "Citizenship", Data.Citizenship, Res("HistFieldCitizenship") ?? "Citizenship", "passport");
            CompareAIField(items, passportData, "IssuingCountry", Data.IssuingCountry, Res("HistFieldIssuingCountry") ?? "Issuing Country", "passport");

            CompareAIField(items, visaData, "VisaNumber", Data.VisaNumber, Res("HistFieldVisaNum") ?? "Visa Number", SecondaryDocumentSourceDocumentKey);
            CompareAIField(items, visaData, "VisaExpiry", Data.VisaExpiry, Res("HistFieldVisaExp") ?? "Visa Expiry", SecondaryDocumentSourceDocumentKey);
            CompareAIField(items, visaData, "VisaAuthority", Data.VisaAuthority, Res("HistFieldVisaAuthority") ?? "Visa Authority", SecondaryDocumentSourceDocumentKey);
            if (!IsEuIdCardEmployee)
                CompareAIField(items, visaData, "VisaType", Data.VisaType, Res("HistFieldVisaType") ?? "Visa Type", SecondaryDocumentSourceDocumentKey);

            CompareAIField(items, insuranceData, "InsuranceCompanyShort", Data.InsuranceCompanyShort, Res("HistFieldInsCompany") ?? "Insurance Company", "insurance");
            CompareAIField(items, insuranceData, "InsuranceCompanyFull", Data.InsuranceCompanyFull, Res("HistFieldInsCompanyFull") ?? "Insurance Company Full", "insurance");
            CompareAIField(items, insuranceData, "InsuranceNumber", Data.InsuranceNumber, Res("HistFieldInsNum") ?? "Insurance Number", "insurance");
            CompareAIField(items, insuranceData, "InsuranceExpiry", Data.InsuranceExpiry, Res("HistFieldInsExp") ?? "Insurance Expiry", "insurance");

            CompareAIField(items, permitData, "WorkPermitNumber", Data.WorkPermitNumber, Res("DetWpNumber") ?? "Work Permit Number", "permit");
            CompareAIField(items, permitData, "WorkPermitExpiry", Data.WorkPermitExpiry, Res("DetWpExpiry") ?? "Work Permit Expiry", "permit");
            CompareAIField(items, permitData, "WorkPermitIssueDate", Data.WorkPermitIssueDate, Res("DetWpIssueDate") ?? "Work Permit Issue Date", "permit");
            CompareAIField(items, permitData, "WorkPermitAuthority", Data.WorkPermitAuthority, Res("DetWpAuthority") ?? "Work Permit Authority", "permit");
        }

        private void BuildOwnershipItems(
            List<AIFieldCheckItem> items,
            Dictionary<string, string> visaData,
            Dictionary<string, string> insuranceData,
            Dictionary<string, string> permitData)
        {
            CheckDocumentOwnership(items, SecondaryDocumentSourceDocumentKey, Res("AIValidationVisaOwnershipTitle") ?? "Visa ownership", visaData, allowBirthDateOnlyError: true);
            CheckDocumentOwnership(items, "insurance", Res("AIValidationInsuranceOwnershipTitle") ?? "Insurance ownership", insuranceData, allowBirthDateOnlyError: false);
            CheckDocumentOwnership(items, "permit", Res("AIValidationPermitOwnershipTitle") ?? "Work permit ownership", permitData, allowBirthDateOnlyError: true);
        }

        private void CheckDocumentOwnership(
            List<AIFieldCheckItem> items,
            string sourceDocument,
            string displayName,
            Dictionary<string, string> docData,
            bool allowBirthDateOnlyError)
        {
            if (docData.Count == 0)
                return;

            var details = new List<string>();
            var strongMismatches = 0;
            var hasBirthDateMismatch = false;
            var hasFirstName = TryGetNonEmpty(docData, "FirstName", out var firstName);
            var hasLastName = TryGetNonEmpty(docData, "LastName", out var lastName);
            var isSwappedNameMatch = hasFirstName
                && hasLastName
                && NamesMatch(firstName, Data.LastName)
                && NamesMatch(lastName, Data.FirstName);

            if (hasFirstName && !NamesMatch(firstName, Data.FirstName) && !isSwappedNameMatch)
            {
                strongMismatches++;
                details.Add($"FirstName: {firstName} vs {Data.FirstName}");
            }

            if (hasLastName && !NamesMatch(lastName, Data.LastName) && !isSwappedNameMatch)
            {
                strongMismatches++;
                details.Add($"LastName: {lastName} vs {Data.LastName}");
            }

            if (TryGetNonEmpty(docData, "BirthDate", out var birthDate) && !ValuesMatch("BirthDate", Data.BirthDate, birthDate))
            {
                hasBirthDateMismatch = true;
                details.Add($"BirthDate: {birthDate} vs {Data.BirthDate}");
            }

            var mismatchCount = strongMismatches + (hasBirthDateMismatch ? 1 : 0);
            if (mismatchCount == 0)
                return;

            var severity = mismatchCount >= 2 || (allowBirthDateOnlyError && strongMismatches >= 1 && hasBirthDateMismatch)
                ? "error"
                : "warning";

            if (!allowBirthDateOnlyError && strongMismatches == 0 && hasBirthDateMismatch)
                severity = "warning";

            items.Add(new AIFieldCheckItem
            {
                FieldKey = $"_ownership_{sourceDocument}",
                FieldDisplayName = displayName,
                SourceDocument = sourceDocument,
                SourceDocumentDisplay = GetSourceDocumentDisplayName(sourceDocument),
                Severity = severity,
                Message = string.Join("; ", details),
                CanAutofill = false
            });
        }

        private void BuildCrossDocumentItems(
            List<AIFieldCheckItem> items,
            Dictionary<string, string> passportData,
            Dictionary<string, string> visaData,
            Dictionary<string, string> insuranceData,
            Dictionary<string, string> permitData)
        {
            AddCrossDocumentMismatch(
                items,
                "PassportNumber",
                passportData,
                visaData,
                "_cross_visa_passport_number",
                Res("AIValidationCrossVisaPassportTitle") ?? "Visa vs passport",
                Res("AIValidationCrossPassportNumberMessage") ?? "Passport number on visa does not match passport");

            AddCrossDocumentMismatch(
                items,
                "BirthDate",
                passportData,
                insuranceData,
                "_cross_insurance_passport_birth",
                Res("AIValidationCrossInsurancePassportTitle") ?? "Insurance vs passport",
                Res("AIValidationCrossBirthDateMessage") ?? "Birth date on insurance does not match passport",
                severityOverride: "warning");

            AddCrossDocumentIdentityCheck(
                items,
                passportData,
                visaData,
                "passport",
                SecondaryDocumentSourceDocumentKey,
                Res("AIValidationCrossVisaPassportIdentityTitle") ?? "Visa identity vs passport");

            AddCrossDocumentIdentityCheck(
                items,
                passportData,
                insuranceData,
                "passport",
                "insurance",
                Res("AIValidationCrossInsurancePassportIdentityTitle") ?? "Insurance identity vs passport",
                birthDateSeverity: "warning");

            AddCrossDocumentIdentityCheck(
                items,
                passportData,
                permitData,
                "passport",
                "permit",
                Res("AIValidationCrossPermitPassportIdentityTitle") ?? "Work permit identity vs passport");
        }

        private void AddCrossDocumentMismatch(
            List<AIFieldCheckItem> items,
            string fieldKey,
            Dictionary<string, string> leftDoc,
            Dictionary<string, string> rightDoc,
            string itemKey,
            string displayName,
            string messagePrefix,
            string severityOverride = "error")
        {
            if (!TryGetNonEmpty(leftDoc, fieldKey, out var leftValue) || !TryGetNonEmpty(rightDoc, fieldKey, out var rightValue))
                return;

            if (ValuesMatch(fieldKey, leftValue, rightValue))
                return;

            items.Add(new AIFieldCheckItem
            {
                FieldKey = itemKey,
                FieldDisplayName = displayName,
                SourceDocument = "cross-check",
                SourceDocumentDisplay = GetSourceDocumentDisplayName("cross-check"),
                CurrentValue = leftValue,
                SuggestedValue = rightValue,
                Severity = severityOverride,
                Message = $"{messagePrefix}: {leftValue} vs {rightValue}",
                CanAutofill = false
            });
        }

        private void AddCrossDocumentIdentityCheck(
            List<AIFieldCheckItem> items,
            Dictionary<string, string> leftDoc,
            Dictionary<string, string> rightDoc,
            string leftDocKey,
            string rightDocKey,
            string displayName,
            string birthDateSeverity = "error")
        {
            if (leftDoc.Count == 0 || rightDoc.Count == 0)
                return;

            var details = new List<string>();
            var mismatchCount = 0;

            if (TryGetNonEmpty(leftDoc, "FirstName", out var leftFirstName)
                && TryGetNonEmpty(rightDoc, "FirstName", out var rightFirstName)
                && !NamesMatch(leftFirstName, rightFirstName))
            {
                mismatchCount++;
                details.Add($"FirstName: {leftFirstName} vs {rightFirstName}");
            }

            if (TryGetNonEmpty(leftDoc, "LastName", out var leftLastName)
                && TryGetNonEmpty(rightDoc, "LastName", out var rightLastName)
                && !NamesMatch(leftLastName, rightLastName))
            {
                mismatchCount++;
                details.Add($"LastName: {leftLastName} vs {rightLastName}");
            }

            var leftBirthDate = string.Empty;
            var rightBirthDate = string.Empty;
            var hasBirthDateMismatch = TryGetNonEmpty(leftDoc, "BirthDate", out leftBirthDate)
                && TryGetNonEmpty(rightDoc, "BirthDate", out rightBirthDate)
                && !ValuesMatch("BirthDate", leftBirthDate, rightBirthDate);

            if (hasBirthDateMismatch)
            {
                mismatchCount++;
                details.Add($"BirthDate: {leftBirthDate} vs {rightBirthDate}");
            }

            if (mismatchCount == 0)
                return;

            var severity = mismatchCount >= 2 ? "error" : hasBirthDateMismatch ? birthDateSeverity : "warning";
            items.Add(new AIFieldCheckItem
            {
                FieldKey = $"_cross_identity_{leftDocKey}_{rightDocKey}",
                FieldDisplayName = displayName,
                SourceDocument = "cross-check",
                SourceDocumentDisplay = GetSourceDocumentDisplayName("cross-check"),
                Severity = severity,
                Message = string.Format(Res("AIValidationCrossIdentityMessage") ?? "{0} has mismatched identity fields: {1}", displayName, string.Join("; ", details)),
                CanAutofill = false
            });
        }

        private void CompareAIField(
            List<AIFieldCheckItem> items,
            Dictionary<string, string> extracted,
            string fieldKey,
            string? currentValue,
            string displayName,
            string sourceDocument)
        {
            if (!TryGetSuggestedValue(extracted, fieldKey, out var suggestedValue))
                return;

            var current = currentValue?.Trim() ?? string.Empty;
            var suggested = suggestedValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(suggested))
                return;

            if (IsSuspiciousDocumentValue(fieldKey, suggested))
            {
                items.Add(new AIFieldCheckItem
                {
                    FieldKey = $"_suspicious_{fieldKey}",
                    FieldDisplayName = displayName,
                    SourceDocument = sourceDocument,
                    SourceDocumentDisplay = GetSourceDocumentDisplayName(sourceDocument),
                    CurrentValue = current,
                    SuggestedValue = suggested,
                    Severity = "warning",
                    Message = string.Format(Res("AIValidationSuspiciousReadMessage") ?? "AI may have read this value unreliably: {0}", suggested),
                    CanAutofill = false
                });
                return;
            }

            var item = new AIFieldCheckItem
            {
                FieldKey = fieldKey,
                FieldDisplayName = displayName,
                SourceDocument = sourceDocument,
                SourceDocumentDisplay = GetSourceDocumentDisplayName(sourceDocument),
                CurrentValue = current,
                SuggestedValue = suggested,
                CanAutofill = true
            };

            if (string.IsNullOrWhiteSpace(current))
            {
                item.Severity = "missing";
                item.Message = string.Format(Res("AIValidationMissingMessage") ?? "Empty in profile, found in document: {0}", suggested);
            }
            else if (!ValuesMatch(fieldKey, current, suggested))
            {
                item.Severity = "warning";
                item.Message = string.Format(Res("AIValidationMismatchMessage") ?? "Profile: {0} -> Document: {1}", current, suggested);
            }
            else
            {
                item.Severity = "ok";
                item.Message = Res("AIValidationMatchMessage") ?? "Matches document";
                item.CanAutofill = false;
            }

            items.Add(item);
        }

        private static bool TryGetSuggestedValue(Dictionary<string, string> extracted, string fieldKey, out string suggestedValue)
        {
            suggestedValue = string.Empty;

            if (extracted.TryGetValue(fieldKey, out var directValue) && !string.IsNullOrWhiteSpace(directValue))
            {
                suggestedValue = directValue;
                return true;
            }

            if (string.Equals(fieldKey, "InsuranceCompanyShort", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "InsuranceCompanyFull", StringComparison.OrdinalIgnoreCase))
            {
                extracted.TryGetValue("InsuranceCompanyRaw", out var rawValue);
                extracted.TryGetValue("InsuranceCompanyCode", out var codeValue);
                extracted.TryGetValue("InsuranceCompanyShort", out var shortValue);
                extracted.TryGetValue("InsuranceCompanyFull", out var fullValue);
                var option = InsuranceCompanyNormalizer.Normalize(rawValue, codeValue, shortValue, fullValue);
                if (option != null)
                {
                    suggestedValue = string.Equals(fieldKey, "InsuranceCompanyFull", StringComparison.OrdinalIgnoreCase)
                        ? option.FullName
                        : option.ShortName;
                    return true;
                }
            }

            return false;
        }

        private static bool ValuesMatch(string fieldKey, string current, string suggested)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(suggested))
                return false;

            if (string.Equals(fieldKey, "FirstName", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "LastName", StringComparison.OrdinalIgnoreCase))
            {
                return NamesMatch(current, suggested);
            }

            current = current.Trim();
            suggested = suggested.Trim();

            if (string.Equals(fieldKey, "InsuranceCompanyShort", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "InsuranceCompanyFull", StringComparison.OrdinalIgnoreCase))
            {
                var currentOption = InsuranceCompanyNormalizer.Normalize(current);
                var suggestedOption = InsuranceCompanyNormalizer.Normalize(suggested);
                if (currentOption != null && suggestedOption != null)
                    return string.Equals(currentOption.Code, suggestedOption.Code, StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(fieldKey, "PassportCountry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "Citizenship", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "IssuingCountry", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(NormalizeCountry(current), NormalizeCountry(suggested), StringComparison.OrdinalIgnoreCase);
            }

            if (IsDocumentNumberField(fieldKey))
            {
                return string.Equals(NormalizeDocumentNumber(current), NormalizeDocumentNumber(suggested), StringComparison.OrdinalIgnoreCase);
            }

            if (fieldKey.EndsWith("Expiry", StringComparison.OrdinalIgnoreCase)
                || fieldKey.EndsWith("IssueDate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "BirthDate", StringComparison.OrdinalIgnoreCase))
            {
                var currentDate = DateParsingHelper.TryParseDate(current);
                var suggestedDate = DateParsingHelper.TryParseDate(suggested);
                if (currentDate != null && suggestedDate != null)
                    return currentDate.Value.Date == suggestedDate.Value.Date;
            }

            if (fieldKey.EndsWith("Authority", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Length < 4 || suggested.Length < 4)
                    return string.Equals(current, suggested, StringComparison.OrdinalIgnoreCase);

                return current.Contains(suggested, StringComparison.OrdinalIgnoreCase)
                    || suggested.Contains(current, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(current, suggested, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetNonEmpty(Dictionary<string, string> source, string key, out string value)
        {
            if (source.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                value = raw.Trim();
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool NamesMatch(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return true;

            return string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeName(string value)
        {
            var normalized = value.Trim().ToUpperInvariant()
                .Replace('-', ' ')
                .Replace('’', '\'')
                .Replace('`', '\'')
                .Replace('´', '\'')
                .Replace('\u00A0', ' ');

            normalized = normalized
                .Replace('А', 'A')
                .Replace('В', 'B')
                .Replace('Е', 'E')
                .Replace('К', 'K')
                .Replace('М', 'M')
                .Replace('Н', 'H')
                .Replace('О', 'O')
                .Replace('Р', 'P')
                .Replace('С', 'C')
                .Replace('Т', 'T')
                .Replace('У', 'Y')
                .Replace('Х', 'X')
                .Replace('І', 'I')
                .Replace('Ї', 'I')
                .Replace('Ё', 'E');

            normalized = Regex.Replace(normalized, @"[^\p{L}\s']", string.Empty);
            return Regex.Replace(normalized, @"\s+", " ");
        }

        private static string NormalizeCountry(string value)
        {
            var normalized = Regex.Replace(value.Trim().ToUpperInvariant(), @"\s+", " ");
            return normalized switch
            {
                "UA" => "UKRAINE",
                "UKR" => "UKRAINE",
                "UKRAINA" => "UKRAINE",
                "UKRAJINA" => "UKRAINE",
                "UKRAINE" => "UKRAINE",
                "УКРАЇНА" => "UKRAINE",
                "УКРАИНА" => "UKRAINE",
                _ => normalized
            };
        }

        private static bool IsDocumentNumberField(string fieldKey)
        {
            return string.Equals(fieldKey, "PassportNumber", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "VisaNumber", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "InsuranceNumber", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "WorkPermitNumber", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDocumentNumber(string value)
        {
            var normalized = Regex.Replace(value.Trim().ToUpperInvariant(), @"[^A-Z0-9]+", string.Empty);
            return normalized;
        }

        private static bool IsSuspiciousDocumentValue(string fieldKey, string value)
        {
            if (!IsDocumentNumberField(fieldKey))
                return false;

            var normalized = NormalizeDocumentNumber(value);
            if (normalized.Length < 5)
                return true;

            if (!Regex.IsMatch(normalized, @"^[A-Z0-9]+$"))
                return true;

            if (string.Equals(fieldKey, "PassportNumber", StringComparison.OrdinalIgnoreCase) && normalized.Length is < 6 or > 12)
                return true;

            return false;
        }

        private string GetSourceDocumentDisplayName(string sourceDocument)
        {
            return sourceDocument switch
            {
                "passport" => Res("AIValidationSourcePassport") ?? "Passport",
                "visa" => Res("AIValidationSourceVisa") ?? "Visa",
                "passport2" => SecondaryDocumentDisplayName,
                "id_card_back" => SecondaryDocumentDisplayName,
                "insurance" => Res("AIValidationSourceInsurance") ?? "Insurance",
                "permit" => Res("AIValidationSourcePermit") ?? "Work permit",
                "cross-check" => Res("AIValidationSourceCrossCheck") ?? "Cross-check",
                _ => sourceDocument
            };
        }

        private async Task ApplyAISuggestionAsync(AIFieldCheckItem item)
        {
            if (item == null || !item.CanAutofill || item.IsApplied || IsReadOnlyMode)
                return;

            var oldData = _employeeService.LoadEmployeeData(_employeeFolder);
            if (oldData == null)
                return;

            switch (item.FieldKey)
            {
                case "PassportNumber": Data.PassportNumber = item.SuggestedValue; break;
                case "PassportExpiry": Data.PassportExpiry = item.SuggestedValue; break;
                case "PassportAuthority": Data.PassportAuthority = item.SuggestedValue; break;
                case "PassportCity": Data.PassportCity = item.SuggestedValue; break;
                case "PassportCountry": Data.PassportCountry = item.SuggestedValue; break;
                case "Citizenship": Data.Citizenship = item.SuggestedValue; break;
                case "IssuingCountry": Data.IssuingCountry = item.SuggestedValue; break;
                case "VisaNumber": Data.VisaNumber = item.SuggestedValue; break;
                case "VisaExpiry": Data.VisaExpiry = item.SuggestedValue; break;
                case "VisaAuthority": Data.VisaAuthority = item.SuggestedValue; break;
                case "VisaType": Data.VisaType = item.SuggestedValue; break;
                case "InsuranceCompanyShort":
                case "InsuranceCompanyFull":
                    var insuranceOption = InsuranceCompanyNormalizer.Normalize(item.SuggestedValue);
                    if (insuranceOption != null)
                    {
                        Data.InsuranceCompanyShort = insuranceOption.ShortName;
                        Data.InsuranceCompanyFull = insuranceOption.FullName;
                        NormalizeInsuranceCompanyFields();
                    }
                    else
                    {
                        Data.InsuranceCompanyShort = item.SuggestedValue;
                        Data.InsuranceCompanyFull = string.Empty;
                    }
                    break;
                case "InsuranceNumber": Data.InsuranceNumber = item.SuggestedValue; break;
                case "InsuranceExpiry": Data.InsuranceExpiry = item.SuggestedValue; break;
                case "WorkPermitNumber": Data.WorkPermitNumber = item.SuggestedValue; break;
                case "WorkPermitExpiry": Data.WorkPermitExpiry = item.SuggestedValue; break;
                case "WorkPermitIssueDate": Data.WorkPermitIssueDate = item.SuggestedValue; break;
                case "WorkPermitAuthority": Data.WorkPermitAuthority = item.SuggestedValue; break;
                default: return;
            }

            if (!_employeeService.SaveEmployeeData(_employeeFolder, Data, notifyUser: false))
            {
                StatusMessage = Res("MsgProfileSaveFail");
                return;
            }

            await _employeeService.RecordChanges(_employeeFolder, oldData, Data);
            LogProfileChanges(oldData, Data);
            item.IsApplied = true;
            item.CurrentValue = item.SuggestedValue;
            item.CanAutofill = false;
            item.Severity = "ok";
            item.Message = Res("AIValidationAppliedMessage") ?? "Applied";
            OnPropertyChanged(nameof(Data));
            InvalidateDetailCaches();
            DataChanged?.Invoke();
            RaiseAIValidationCollectionsChanged();
        }

        private async Task ApplyAllAISuggestionsAsync()
        {
            foreach (var item in AIValidationItems.Where(i => i.CanAutofill && !i.IsApplied).ToList())
                await ApplyAISuggestionAsync(item);
        }

        private void RaiseAIValidationCollectionsChanged()
        {
            OnPropertyChanged(nameof(AIValidationItems));
            OnPropertyChanged(nameof(AIValidationAutofillItems));
            OnPropertyChanged(nameof(AIValidationErrorItems));
            OnPropertyChanged(nameof(AIValidationWarningItems));
            OnPropertyChanged(nameof(AIValidationAttentionItems));
            OnPropertyChanged(nameof(AIValidationOkItems));
            OnPropertyChanged(nameof(HasAIValidationItems));
            OnPropertyChanged(nameof(HasAIValidationAutofillItems));
            OnPropertyChanged(nameof(HasAIValidationErrorItems));
            OnPropertyChanged(nameof(HasAIValidationWarningItems));
            OnPropertyChanged(nameof(HasAIValidationAttentionItems));
            OnPropertyChanged(nameof(HasAIValidationOkItems));
        }

        // ExportProfilePdf moved to EmployeeDetailsViewModel.Documents.cs
        // LogProfileChanges moved to EmployeeDetailsViewModel.History.cs

        public void RefreshExpiryWarnings()
        {
            OnPropertyChanged(nameof(PassportExpiryWarning));
            OnPropertyChanged(nameof(VisaExpiryWarning));
            OnPropertyChanged(nameof(InsuranceExpiryWarning));
            OnPropertyChanged(nameof(ProfileCompletionPercent));
        }

        // ---- Custom Signed Documents methods ----

        private void LoadCustomDocuments()
        {
            var all = Data.CustomDocuments ?? new List<CustomSignedDocument>();
            CustomDocuments = new ObservableCollection<CustomSignedDocument>(
                all.Where(d => !d.IsHidden));
            HiddenCustomDocuments = new ObservableCollection<CustomSignedDocument>(
                all.Where(d => d.IsHidden));
            OnPropertyChanged(nameof(HiddenCustomDocsCount));
            OnPropertyChanged(nameof(HasHiddenDocs));
        }

        private void ToggleHideDoc(CustomSignedDocument doc, bool hide)
        {
            var previousHiddenState = doc.IsHidden;
            doc.IsHidden = hide;
            if (!_employeeService.SaveEmployeeData(_employeeFolder, Data))
            {
                doc.IsHidden = previousHiddenState;
                StatusMessage = Res("MsgProfileSaveFail");
                return;
            }

            if (hide)
            {
                CustomDocuments.Remove(doc);
                HiddenCustomDocuments.Add(doc);
                if (!IsHiddenSectionVisible)
                    IsHiddenSectionVisible = true;
            }
            else
            {
                HiddenCustomDocuments.Remove(doc);
                CustomDocuments.Add(doc);
            }
            OnPropertyChanged(nameof(HiddenCustomDocsCount));
            OnPropertyChanged(nameof(HasHiddenDocs));
        }

        private void BrowseCustomDocFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.pdf"
            };
            if (dialog.ShowDialog() == true)
                NewCustomDocFilePath = dialog.FileName;
        }

        private async Task ConfirmAddCustomDocAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Додати підписаний документ"))
                return;

            AddCustomDocError = string.Empty;

            if (string.IsNullOrWhiteSpace(NewCustomDocName))
            {
                AddCustomDocError = Res("MsgCustomDocNameRequired") ?? "Please enter a document name.";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewCustomDocSignDate) ||
                !DateTime.TryParseExact(NewCustomDocSignDate, "dd.MM.yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
            {
                AddCustomDocError = Res("MsgCustomDocSignDateRequired") ?? "Please enter the sign date (dd.MM.yyyy).";
                return;
            }

            if (!string.IsNullOrWhiteSpace(NewCustomDocExpiryDate) &&
                !DateTime.TryParseExact(NewCustomDocExpiryDate, "dd.MM.yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
            {
                AddCustomDocError = Res("MsgCustomDocSignDateRequired") ?? "Invalid expiry date format (dd.MM.yyyy).";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewCustomDocFilePath) || !File.Exists(NewCustomDocFilePath))
            {
                AddCustomDocError = Res("MsgCustomDocFileMissing") ?? "Please select a file.";
                return;
            }

            var safeDatePart = NewCustomDocSignDate.Replace(".", "-");
            var baseName = $"{Data.FirstName} {Data.LastName} - {NewCustomDocName.Trim()} - {safeDatePart}";
            var savedFileName = _employeeService.SaveCustomDocument(_employeeFolder, NewCustomDocFilePath, baseName);
            if (string.IsNullOrEmpty(savedFileName))
            {
                AddCustomDocError = Res("MsgSaveFail") ?? "Failed to save file.";
                return;
            }

            var doc = new CustomSignedDocument
            {
                Id = Guid.NewGuid().ToString(),
                Name = NewCustomDocName.Trim(),
                SignDate = NewCustomDocSignDate,
                ExpiryDate = NewCustomDocExpiryDate,
                FileName = savedFileName
            };

            Data.CustomDocuments ??= new List<CustomSignedDocument>();
            Data.CustomDocuments.Add(doc);
            if (!_employeeService.SaveEmployeeData(_employeeFolder, Data))
            {
                Data.CustomDocuments.Remove(doc);
                _employeeService.DeleteCustomDocFile(_employeeFolder, savedFileName);
                AddCustomDocError = Res("MsgProfileSaveFail") ?? "Failed to save profile.";
                return;
            }

            var expiryPart = string.IsNullOrEmpty(doc.ExpiryDate) ? "" : $", до: {doc.ExpiryDate}";
            var histDesc = $"{doc.Name} (підписано: {doc.SignDate}{expiryPart})";

            await _employeeService.AddHistoryEntry(_employeeFolder, Data.UniqueId, new EmployeeHistoryEntry
            {
                EventType = "CustomDocumentAdded",
                Action = Res("HistoryActionDocAdd") ?? "Додано документ",
                Field = doc.Name,
                Description = histDesc
            });

            _activityLogService.Log("CustomDocAdded", "Document", _firmName, FullName,
                histDesc, employeeFolder: _employeeFolder);

            CustomDocuments.Add(doc);
            IsAddCustomDocOpen = false;
            StatusMessage = Res("MsgSaved") ?? "Saved.";
            InvalidateDetailCaches();
            DataChanged?.Invoke();
        }

        private async Task DeleteCustomDocAsync(CustomSignedDocument doc)
        {
            if (!PolicyService.EnsureWriteAllowed("Видалити підписаний документ"))
                return;

            var docs = Data.CustomDocuments;
            if (docs == null)
                return;

            var removeIndex = docs.IndexOf(doc);
            docs.Remove(doc);
            if (!_employeeService.SaveEmployeeData(_employeeFolder, Data))
            {
                if (removeIndex >= 0 && removeIndex <= docs.Count)
                    docs.Insert(removeIndex, doc);
                else
                    docs.Add(doc);

                StatusMessage = Res("MsgProfileSaveFail");
                return;
            }

            _employeeService.DeleteCustomDocFile(_employeeFolder, doc.FileName);

            var histDesc = $"{doc.Name} (підписано: {doc.SignDate})";

            await _employeeService.AddHistoryEntry(_employeeFolder, Data.UniqueId, new EmployeeHistoryEntry
            {
                EventType = "CustomDocumentDeleted",
                Action = Res("HistoryActionDocDelete") ?? "Видалено документ",
                Field = doc.Name,
                Description = histDesc
            });

            _activityLogService.Log("CustomDocDeleted", "Document", _firmName, FullName,
                histDesc, employeeFolder: _employeeFolder);

            CustomDocuments.Remove(doc);
            InvalidateDetailCaches();
            DataChanged?.Invoke();
        }
    }
}
