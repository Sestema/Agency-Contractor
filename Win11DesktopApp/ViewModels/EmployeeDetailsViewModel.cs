using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        private static string DocRes(string key) =>
            App.DocumentLocalizationService?.Get(key) ?? Res(key);

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

        private string _workPermitFilePath = string.Empty;
        public string WorkPermitFilePath
        {
            get => _workPermitFilePath;
            set => SetProperty(ref _workPermitFilePath, value);
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

        public bool IsWorkPermitType => Data.EmployeeType == "work_permit";

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
            set => SetProperty(ref _isGenerating, value);
        }

        private string _generateStatusMessage = string.Empty;
        public string GenerateStatusMessage
        {
            get => _generateStatusMessage;
            set => SetProperty(ref _generateStatusMessage, value);
        }

        // ---- AI Validation ----
        private bool _isAIValidating;
        public bool IsAIValidating
        {
            get => _isAIValidating;
            set => SetProperty(ref _isAIValidating, value);
        }

        private string _aiValidationResult = string.Empty;
        public string AIValidationResult
        {
            get => _aiValidationResult;
            set => SetProperty(ref _aiValidationResult, value);
        }

        private bool _isAIValidationOpen;
        public bool IsAIValidationOpen
        {
            get => _isAIValidationOpen;
            set => SetProperty(ref _isAIValidationOpen, value);
        }

        public ICommand AIValidateCommand { get; }
        public ICommand CloseAIValidationCommand { get; }

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
        public ICommand RenewWorkPermitCommand { get; }
        public ICommand ConfirmRenewWpCommand { get; }
        public ICommand CancelRenewWpCommand { get; }
        public ICommand ArchiveEmployeeCommand { get; }
        public ICommand ConfirmArchiveCommand { get; }
        public ICommand CancelArchiveCommand { get; }
        public ICommand ShowHistoryCommand { get; }

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

        // Profile completion
        public int ProfileCompletionPercent => CalcProfileCompletion();

        private int CalcProfileCompletion()
        {
            var fields = new List<string> {
                Data.FirstName, Data.LastName, Data.BirthDate,
                Data.PassportNumber, Data.PassportExpiry, Data.PassportCity, Data.PassportCountry,
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

        public EmployeeDetailsViewModel(string firmName, string employeeFolder, EmployeeService? employeeService = null)
        {
            _firmName = firmName;
            _employeeFolder = employeeFolder;
            _employeeService = employeeService ?? App.EmployeeService;

            Data = _employeeService.LoadEmployeeData(employeeFolder) ?? new EmployeeData();
            Data.Status = StatusHelper.Normalize(Data.Status);
            RefreshDocuments();

            CloseCommand = new RelayCommand(o => RequestClose?.Invoke());
            ShowDocumentsCommand = new RelayCommand(o => TabIndex = 0);
            ShowProfileCommand = new RelayCommand(o => TabIndex = 1);
            ShowHistoryCommand = new RelayCommand(o => { TabIndex = 2; LoadHistory(); });
            SetHistoryFilterCommand = new RelayCommand(o => HistoryFilter = o?.ToString() ?? "All");
            ShowSalaryCommand = new RelayCommand(o => { TabIndex = 3; LoadSalaryHistory(); });
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
                catch (Exception ex) { LoggingService.LogError("EmployeeDetailsViewModel.OpenFolder", ex); StatusMessage = Res("MsgOpenFolderFail"); }
            });

            ExtendVisaCommand = new RelayCommand(o => ShowExtendDialog("visa"));
            ExtendInsuranceCommand = new RelayCommand(o => ShowExtendDialog("insurance"));
            ConfirmExtendCommand = new RelayCommand(o => ConfirmExtend());
            CancelExtendCommand = new RelayCommand(o => IsExtendDialogOpen = false);

            OpenWorkPermitCommand = new RelayCommand(o => OpenFile(WorkPermitFilePath), o => HasWorkPermit);
            RenewWorkPermitCommand = new RelayCommand(o => StartRenewWorkPermit(), o => IsWorkPermitType);
            ConfirmRenewWpCommand = new RelayCommand(o => ConfirmRenewWorkPermit());
            CancelRenewWpCommand = new RelayCommand(o => IsRenewWpDialogOpen = false);

            ArchiveEmployeeCommand = new RelayCommand(o =>
            {
                ArchiveEndDate = DateTime.Today.ToString("dd.MM.yyyy");
                ArchiveStatus = string.Empty;
                IsArchiveDialogOpen = true;
            });
            ConfirmArchiveCommand = new RelayCommand(o => ConfirmArchive());
            CancelArchiveCommand = new RelayCommand(o => IsArchiveDialogOpen = false);
            ExportProfilePdfCommand = new RelayCommand(o => ExportProfilePdf());
            AIValidateCommand = new RelayCommand(o => RunAIValidation(), o => !IsAIValidating);
            CloseAIValidationCommand = new RelayCommand(o => IsAIValidationOpen = false);


            AddCustomDocCommand = new RelayCommand(o =>
            {
                NewCustomDocName = string.Empty;
                NewCustomDocSignDate = DateTime.Today.ToString("dd.MM.yyyy");
                NewCustomDocExpiryDate = string.Empty;
                NewCustomDocFilePath = string.Empty;
                AddCustomDocError = string.Empty;
                IsAddCustomDocOpen = true;
            });
            CancelAddCustomDocCommand = new RelayCommand(o => IsAddCustomDocOpen = false);
            ConfirmAddCustomDocCommand = new RelayCommand(o => ConfirmAddCustomDoc());
            BrowseCustomDocFileCommand = new RelayCommand(o => BrowseCustomDocFile());
            OpenCustomDocCommand = new RelayCommand(o =>
            {
                if (o is CustomSignedDocument cd)
                {
                    var path = _employeeService.GetCustomDocPath(_employeeFolder, cd.FileName);
                    if (!string.IsNullOrEmpty(path)) OpenFile(path);
                }
            });
            HideCustomDocCommand = new RelayCommand(o =>
            {
                if (o is CustomSignedDocument cd) ToggleHideDoc(cd, true);
            });
            UnhideCustomDocCommand = new RelayCommand(o =>
            {
                if (o is CustomSignedDocument cd) ToggleHideDoc(cd, false);
            });
            ToggleHiddenDocsSectionCommand = new RelayCommand(o =>
                IsHiddenSectionVisible = !IsHiddenSectionVisible);

            OpenCustomDocFolderCommand = new RelayCommand(o =>
            {
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
            });
            DeleteCustomDocCommand = new RelayCommand(o =>
            {
                if (o is CustomSignedDocument cd) DeleteCustomDoc(cd);
            });

            LoadCustomDocuments();

            RefreshExpiryWarnings();
        }

        private void ShowExtendDialog(string type)
        {
            _extendType = type;
            ExtendDialogTitle = type == "visa" ? Res("DetExtendTitleVisa") : Res("DetExtendTitleIns");
            NewExpiryDate = type == "visa" ? Data.VisaExpiry : Data.InsuranceExpiry;
            IsExtendDialogOpen = true;
        }

        private async void ConfirmExtend()
        {
            if (string.IsNullOrWhiteSpace(NewExpiryDate))
            {
                StatusMessage = Res("MsgEnterNewDate");
                return;
            }

            var oldDate = _extendType == "visa" ? Data.VisaExpiry : Data.InsuranceExpiry;
            var fieldName = _extendType == "visa" ? Res("DetExtendFieldVisa") : Res("DetExtendFieldIns");
            var actionName = _extendType == "visa" ? Res("HistoryExtendVisa") : Res("HistoryExtendIns");

            if (_extendType == "visa")
                Data.VisaExpiry = NewExpiryDate;
            else if (_extendType == "insurance")
                Data.InsuranceExpiry = NewExpiryDate;

            if (_employeeService.SaveEmployeeData(_employeeFolder, Data))
            {
                await _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
                {
                    EventType = "DocumentExtended",
                    Action = actionName,
                    Field = fieldName,
                    OldValue = oldDate,
                    NewValue = NewExpiryDate,
                    Description = $"{fieldName}: {oldDate} → {NewExpiryDate}"
                });

                App.ActivityLogService?.Log(_extendType == "visa" ? "VisaExtended" : "InsuranceExtended",
                    "Document", _firmName, FullName,
                    $"{fieldName}: {oldDate} → {NewExpiryDate}",
                    oldDate, NewExpiryDate, employeeFolder: _employeeFolder);

                IsExtendDialogOpen = false;
                OnPropertyChanged(nameof(Data));
                DataChanged?.Invoke();
                StatusMessage = string.Empty;
            }
            else
            {
                StatusMessage = Res("MsgSaveFail");
            }
        }

        private async void ConfirmArchive()
        {
            if (string.IsNullOrWhiteSpace(ArchiveEndDate))
            {
                ArchiveStatus = Res("MsgEnterArchiveDate");
                return;
            }

            try
            {
                await _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
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
                OnPropertyChanged(nameof(PhotoFilePath));
                OnPropertyChanged(nameof(PassportFilePath));
                OnPropertyChanged(nameof(VisaFilePath));
                OnPropertyChanged(nameof(InsuranceFilePath));
                OnPropertyChanged(nameof(WorkPermitFilePath));

                // Force WPF to release image file handles
                await Task.Delay(100);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(200);

                var result = await _employeeService.ArchiveEmployee(_employeeFolder, _firmName, ArchiveEndDate);
                if (!string.IsNullOrEmpty(result))
                {
                    App.ActivityLogService?.Log("EmployeeArchived", "Archive", _firmName, FullName,
                        $"Архівовано {FullName} з {_firmName}, дата закінчення: {ArchiveEndDate}",
                        _firmName, ArchiveEndDate, employeeFolder: _employeeFolder);

                    IsArchiveDialogOpen = false;
                    DataChanged?.Invoke();
                    RequestClose?.Invoke();
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
        }

        // Document generation methods moved to EmployeeDetailsViewModel.Documents.cs
        // History and salary methods moved to EmployeeDetailsViewModel.History.cs

        private async void SaveProfile()
        {
            var oldData = _employeeService.LoadEmployeeData(_employeeFolder);

            if (_employeeService.SaveEmployeeData(_employeeFolder, Data))
            {
                if (oldData != null)
                {
                    await _employeeService.RecordChanges(_employeeFolder, oldData, Data);
                    LogProfileChanges(oldData, Data);
                }

                IsEditMode = false;
                DataChanged?.Invoke();
            }
            else
            {
                StatusMessage = Res("MsgProfileSaveFail");
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

        private async void ReplaceDocument(string type)
        {
            if (type == "photo")
            {
                await ReplacePhotoSimple();
                return;
            }

            var window = new Views.ReplaceDocumentWindow(type, Data);
            window.Owner = Application.Current.MainWindow;
            if (window.ShowDialog() != true || !window.Saved) return;

            try
            {
                SaveReplacedDocumentFile(type, window.ResultFilePath);
                var changes = ApplyNewFieldValues(window.NewValues);
                _employeeService.SaveEmployeeData(_employeeFolder, Data);
                await LogDocumentReplacement(type, changes);

                OnPropertyChanged(nameof(Data));
                OnPropertyChanged(nameof(FullName));
                RefreshDocuments();
                RefreshExpiryWarnings();
                DataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private void SaveReplacedDocumentFile(string type, string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            var suffix = type switch
            {
                "passport" => "Pass",
                "visa" => "Viza",
                "insurance" => Data.InsuranceCompanyShort,
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
                case "insurance": Data.Files.Insurance = saved; break;
                case "work_permit": Data.Files.WorkPermit = saved; break;
            }
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
                    "insurance"   => Data.Files.Insurance,
                    "work_permit" => Data.Files.WorkPermit,
                    _             => null
                };

                string? expiryDate = type switch
                {
                    "passport"    => Data.PassportExpiry,
                    "visa"        => Data.VisaExpiry,
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

                File.Move(oldFullPath, destPath);
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
                "insurance" => (Res("DetDocInsurance"), Res("DetDocInsurance")),
                "work_permit" => (Res("DetDocWorkPermit"), Res("DetDocWorkPermit")),
                _ => (type, type)
            };

            var desc = string.Format(Res("HistoryDescDocReplace"), descName);
            if (changes.Count > 0)
                desc += " | " + string.Join(", ", changes);

            await _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
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
                await _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
                {
                    EventType = "DocumentUpdated",
                    Action = Res("HistoryActionDocReplace"),
                    Field = Res("DetDocPhoto"),
                    Description = string.Format(Res("HistoryDescDocReplace"), Res("DetDocPhoto"))
                });
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
            WorkPermitFilePath = BuildPath(Data.Files.WorkPermit);

            HasPassport = File.Exists(PassportFilePath);
            HasVisa = File.Exists(VisaFilePath);
            HasInsurance = File.Exists(InsuranceFilePath);
            HasPhoto = File.Exists(PhotoFilePath);
            HasWorkPermit = File.Exists(WorkPermitFilePath);

            PassportIsPdf = IsPdf(PassportFilePath);
            VisaIsPdf = IsPdf(VisaFilePath);
            InsuranceIsPdf = IsPdf(InsuranceFilePath);
            WorkPermitIsPdf = IsPdf(WorkPermitFilePath);
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
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeDetailsViewModel.OpenFile", ex);
                StatusMessage = Res("MsgOpenFileFail");
            }
        }

        private void StartRenewWorkPermit()
        {
            ReplaceDocument("work_permit");
        }

        private async void ConfirmRenewWorkPermit()
        {
            if (string.IsNullOrWhiteSpace(RenewWpFilePath) || !File.Exists(RenewWpFilePath))
            {
                StatusMessage = Res("MsgFileNotFound");
                return;
            }

            try
            {
                var ext = Path.GetExtension(RenewWpFilePath);
                var destName = $"{Data.FirstName} {Data.LastName} - Povolení k práci{ext}";
                var destPath = Path.Combine(_employeeFolder, destName);

                if (!string.IsNullOrEmpty(Data.Files.WorkPermit))
                {
                    var oldPath = Path.Combine(_employeeFolder, Data.Files.WorkPermit);
                    if (File.Exists(oldPath))
                    {
                        try { File.Delete(oldPath); } catch (Exception ex) { LoggingService.LogWarning("EmployeeDetailsViewModel", $"Failed to delete old file: {ex.Message}"); }
                    }
                }

                File.Copy(RenewWpFilePath, destPath, true);
                Data.Files.WorkPermit = destName;

                var oldNumber = Data.WorkPermitNumber;
                var oldExpiry = Data.WorkPermitExpiry;

                Data.WorkPermitNumber = RenewWpNumber;
                Data.WorkPermitType = RenewWpType;
                Data.WorkPermitIssueDate = RenewWpIssueDate;
                Data.WorkPermitExpiry = RenewWpExpiry;
                Data.WorkPermitAuthority = RenewWpAuthority;

                _employeeService.SaveEmployeeData(_employeeFolder, Data);

                await _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
                {
                    EventType = "DocumentUpdated",
                    Timestamp = DateTime.Now,
                    Action = Res("HistoryActionRenewWp"),
                    Field = "WorkPermit",
                    OldValue = $"{oldNumber}, до {oldExpiry}",
                    NewValue = $"{RenewWpNumber}, до {RenewWpExpiry}",
                    Description = string.Format(Res("HistoryDescRenewWp"), oldNumber, RenewWpNumber, oldExpiry, RenewWpExpiry)
                });

                App.ActivityLogService?.Log("WorkPermitRenewed", "Document", _firmName, FullName,
                    $"{FullName}: дозвіл на роботу оновлено {oldNumber} → {RenewWpNumber}",
                    $"{oldNumber}, до {oldExpiry}", $"{RenewWpNumber}, до {RenewWpExpiry}",
                    employeeFolder: _employeeFolder);

                OnPropertyChanged(nameof(Data));
                RefreshDocuments();
                IsRenewWpDialogOpen = false;
                StatusMessage = Res("MsgWorkPermitUpdated");
                DataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
        }

        private async void RunAIValidation()
        {
            try
            {
                var geminiService = App.GeminiApiService;
                if (geminiService == null || !geminiService.IsConfigured)
                {
                    AIValidationResult = Res("AIChatNoModel");
                    IsAIValidationOpen = true;
                    return;
                }

                IsAIValidating = true;
                AIValidationResult = Res("AIChatThinking");
                IsAIValidationOpen = true;

                var d = Data;
                var context = $@"Employee data to validate:
- Full Name: {d.FirstName} {d.LastName}
- Birth Date: {d.BirthDate}
- Employee Type: {d.EmployeeType} (visa = needs visa, eu_citizen = EU citizen, work_permit = needs work permit)
- Passport Number: {d.PassportNumber}, Country: {d.PassportCountry}, Expiry: {d.PassportExpiry}
- Visa Number: {d.VisaNumber}, Type: {d.VisaType}, Expiry: {d.VisaExpiry}
- Insurance: {d.InsuranceCompanyShort}, Number: {d.InsuranceNumber}, Expiry: {d.InsuranceExpiry}
- Work Permit: Name={d.WorkPermitName}, Number={d.WorkPermitNumber}, Type={d.WorkPermitType}, Expiry={d.WorkPermitExpiry}
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

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var result = await geminiService.ChatAsync(context, systemPrompt, cts.Token);
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

        private static string GetUILanguageName()
        {
            var code = App.AppSettingsService?.Settings?.LanguageCode ?? "uk";
            return code switch
            {
                "en" => "English",
                "cs" => "Czech (čeština)",
                "ru" => "Russian",
                _ => "Ukrainian"
            };
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
            doc.IsHidden = hide;
            _employeeService.SaveEmployeeData(_employeeFolder, Data);

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

        private async void ConfirmAddCustomDoc()
        {
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
            _employeeService.SaveEmployeeData(_employeeFolder, Data);

            var expiryPart = string.IsNullOrEmpty(doc.ExpiryDate) ? "" : $", до: {doc.ExpiryDate}";
            var histDesc = $"{doc.Name} (підписано: {doc.SignDate}{expiryPart})";

            await _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
            {
                EventType = "CustomDocumentAdded",
                Action = Res("HistoryActionDocAdd") ?? "Додано документ",
                Field = doc.Name,
                Description = histDesc
            });

            App.ActivityLogService?.Log("CustomDocAdded", "Document", _firmName, FullName,
                histDesc, employeeFolder: _employeeFolder);

            CustomDocuments.Add(doc);
            IsAddCustomDocOpen = false;
            StatusMessage = Res("MsgSaved") ?? "Saved.";
            DataChanged?.Invoke();
        }

        private async void DeleteCustomDoc(CustomSignedDocument doc)
        {
            _employeeService.DeleteCustomDocFile(_employeeFolder, doc.FileName);
            Data.CustomDocuments?.Remove(doc);
            _employeeService.SaveEmployeeData(_employeeFolder, Data);

            var histDesc = $"{doc.Name} (підписано: {doc.SignDate})";

            await _employeeService.AddHistoryEntry(_employeeFolder, new EmployeeHistoryEntry
            {
                EventType = "CustomDocumentDeleted",
                Action = Res("HistoryActionDocDelete") ?? "Видалено документ",
                Field = doc.Name,
                Description = histDesc
            });

            App.ActivityLogService?.Log("CustomDocDeleted", "Document", _firmName, FullName,
                histDesc, employeeFolder: _employeeFolder);

            CustomDocuments.Remove(doc);
            DataChanged?.Invoke();
        }
    }
}
