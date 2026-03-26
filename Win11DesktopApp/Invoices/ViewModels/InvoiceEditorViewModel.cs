using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Invoices.Views;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Invoices.ViewModels;

public sealed class InvoiceTypeOption
{
    public InvoiceDocumentType Type { get; init; }
    public string ResourceKey { get; init; } = string.Empty;
    public string Label => Application.Current?.TryFindResource(ResourceKey) as string ?? ResourceKey;
}

public sealed class InvoiceStatusOption
{
    public InvoiceDocumentStatus Status { get; init; }
    public string ResourceKey { get; init; } = string.Empty;
    public string Label => Application.Current?.TryFindResource(ResourceKey) as string ?? ResourceKey;
}

public sealed class InvoiceLanguageOption
{
    public string Code { get; init; } = "cs";
    public string ResourceKey { get; init; } = string.Empty;
    public string Label => Application.Current?.TryFindResource(ResourceKey) as string ?? ResourceKey;
}

public sealed class InvoiceChoiceOption
{
    public string Value { get; init; } = string.Empty;
    public string ResourceKey { get; init; } = string.Empty;
    public string Label => Application.Current?.TryFindResource(ResourceKey) as string ?? ResourceKey;
}

public sealed class InvoiceTemplateOption
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PreviewGlyph { get; init; } = "\uE8A5";
}

public sealed class InvoiceBoolOption
{
    public bool Value { get; init; }
    public string ResourceKey { get; init; } = string.Empty;
    public string Label => Application.Current?.TryFindResource(ResourceKey) as string ?? ResourceKey;
}

public sealed class InvoicePaymentPreviewRow
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed class InvoiceEditorViewModel : ViewModelBase, ICleanable
{
    private readonly AresLookupService _aresLookupService;
    private readonly InvoicePdfRenderService _pdfRenderService;
    private readonly InvoiceQrPaymentService _qrPaymentService;
    private readonly InvoiceStorageService _storageService;
    private readonly string _documentId;
    private InvoiceDocument _document = new();
    private InvoiceLineItem? _selectedLineItem;
    private InvoiceParty? _selectedSupplier;
    private InvoiceParty? _selectedCustomer;
    private InvoiceCatalogItem? _selectedCatalogItem;
    private string _newTag = string.Empty;
    private string _qrPreviewStatusText = string.Empty;
    private ImageSource? _qrPreviewImage;
    private bool _isLoaded;
    private bool _numberEditedByUser;
    private bool _suppressNumberEditedTracking;

    public InvoiceEditorViewModel(string documentId, InvoiceStorageService? storageService = null, AresLookupService? aresLookupService = null, InvoiceQrPaymentService? qrPaymentService = null, InvoicePdfRenderService? pdfRenderService = null)
    {
        _documentId = documentId;
        _storageService = storageService ?? App.InvoiceStorageService;
        _aresLookupService = aresLookupService ?? App.AresLookupService;
        _qrPaymentService = qrPaymentService ?? App.InvoiceQrPaymentService;
        _pdfRenderService = pdfRenderService ?? App.InvoicePdfRenderService;

        DocumentTypes = new ObservableCollection<InvoiceTypeOption>
        {
            new() { Type = InvoiceDocumentType.Invoice, ResourceKey = "InvoicesTypeInvoice" },
            new() { Type = InvoiceDocumentType.Quote, ResourceKey = "InvoicesTypeQuote" },
            new() { Type = InvoiceDocumentType.Order, ResourceKey = "InvoicesTypeOrder" },
            new() { Type = InvoiceDocumentType.CashReceiptIncome, ResourceKey = "InvoicesTypeCashReceiptIncome" },
            new() { Type = InvoiceDocumentType.CashReceiptExpense, ResourceKey = "InvoicesTypeCashReceiptExpense" }
        };

        StatusOptions = new ObservableCollection<InvoiceStatusOption>
        {
            new() { Status = InvoiceDocumentStatus.Draft, ResourceKey = "InvoicesStatusDraft" },
            new() { Status = InvoiceDocumentStatus.Issued, ResourceKey = "InvoicesStatusIssued" },
            new() { Status = InvoiceDocumentStatus.Paid, ResourceKey = "InvoicesStatusPaid" },
            new() { Status = InvoiceDocumentStatus.Overdue, ResourceKey = "InvoicesStatusOverdue" },
            new() { Status = InvoiceDocumentStatus.Cancelled, ResourceKey = "InvoicesStatusCancelled" }
        };

        LanguageOptions = new ObservableCollection<InvoiceLanguageOption>
        {
            new() { Code = "uk", ResourceKey = "InvoicesLanguageUkrainian" },
            new() { Code = "cs", ResourceKey = "InvoicesLanguageCzech" },
            new() { Code = "en", ResourceKey = "InvoicesLanguageEnglish" },
            new() { Code = "ru", ResourceKey = "InvoicesLanguageRussian" }
        };

        PaymentMethodOptions = new ObservableCollection<InvoiceChoiceOption>
        {
            new() { Value = "bank_transfer", ResourceKey = "InvoicesPaymentBankTransfer" },
            new() { Value = "cash", ResourceKey = "InvoicesPaymentCash" },
            new() { Value = "card", ResourceKey = "InvoicesPaymentCard" }
        };

        RoundingOptions = new ObservableCollection<InvoiceChoiceOption>
        {
            new() { Value = "none", ResourceKey = "InvoicesRoundingNone" },
            new() { Value = "whole", ResourceKey = "InvoicesRoundingWhole" },
            new() { Value = "half", ResourceKey = "InvoicesRoundingHalf" }
        };

        QrFormatOptions = new ObservableCollection<InvoiceChoiceOption>
        {
            new() { Value = "spayd", ResourceKey = "InvoicesQrFormatSpayd" },
            new() { Value = "epc", ResourceKey = "InvoicesQrFormatEpc" }
        };

        PaidMessageOptions = new ObservableCollection<InvoiceBoolOption>
        {
            new() { Value = false, ResourceKey = "InvoicesBoolNo" },
            new() { Value = true, ResourceKey = "InvoicesBoolYes" }
        };

        CashReceiptVatPayerOptions = new ObservableCollection<InvoiceBoolOption>
        {
            new() { Value = true, ResourceKey = "InvoicesCashReceiptVatYes" },
            new() { Value = false, ResourceKey = "InvoicesCashReceiptVatNo" }
        };

        CashReceiptRoundingOptions = new ObservableCollection<InvoiceChoiceOption>
        {
            new() { Value = "none", ResourceKey = "InvoicesCashReceiptRoundingNone" },
            new() { Value = "cash_5", ResourceKey = "InvoicesCashReceiptRoundingCash5" },
            new() { Value = "cash_up", ResourceKey = "InvoicesCashReceiptRoundingCashUp" },
            new() { Value = "cash_down", ResourceKey = "InvoicesCashReceiptRoundingCashDown" }
        };

        CashReceiptIncomeVariantOptions = new ObservableCollection<InvoiceChoiceOption>
        {
            new() { Value = "simple", ResourceKey = "InvoicesCashReceiptIncomeVariantSimple" },
            new() { Value = "cashdesk", ResourceKey = "InvoicesCashReceiptIncomeVariantCashdesk" }
        };

        CashReceiptExpenseVariantOptions = new ObservableCollection<InvoiceChoiceOption>
        {
            new() { Value = "simple", ResourceKey = "InvoicesCashReceiptExpenseVariantSimple" },
            new() { Value = "cashdesk", ResourceKey = "InvoicesCashReceiptExpenseVariantCashdesk" }
        };

        TemplateOptions = new ObservableCollection<InvoiceTemplateOption>
        {
            new() { Id = "style1", Name = Res("InvoicesTemplateStyle1"), PreviewGlyph = "1" },
            new() { Id = "style2", Name = Res("InvoicesTemplateStyle2"), PreviewGlyph = "2" },
            new() { Id = "style3", Name = Res("InvoicesTemplateStyle3"), PreviewGlyph = "3" },
            new() { Id = "style4", Name = Res("InvoicesTemplateStyle4"), PreviewGlyph = "4" },
            new() { Id = "style5", Name = Res("InvoicesTemplateStyle5"), PreviewGlyph = "5" },
            new() { Id = "style6", Name = Res("InvoicesTemplateStyle6"), PreviewGlyph = "6" },
            new() { Id = "cash1", Name = Res("InvoicesTemplateCash1"), PreviewGlyph = "A" },
            new() { Id = "cash2", Name = Res("InvoicesTemplateCash2"), PreviewGlyph = "B" }
        };

        ThemeOptions = new ObservableCollection<InvoiceChoiceOption>
        {
            new() { Value = "skyblue", ResourceKey = "InvoicesThemeSkyBlue" },
            new() { Value = "emerald", ResourceKey = "InvoicesThemeEmerald" },
            new() { Value = "graphite", ResourceKey = "InvoicesThemeGraphite" },
            new() { Value = "violet", ResourceKey = "InvoicesThemeViolet" }
        };

        CurrencyOptions = new ObservableCollection<string> { "CZK", "EUR", "USD", "PLN" };
        AssetOptions = new ObservableCollection<string> { Res("InvoicesNoChangeAssetOption") };

        AvailableCompanies = new ObservableCollection<InvoiceParty>(_storageService.GetCompanies());
        AvailableCustomers = new ObservableCollection<InvoiceParty>(_storageService.GetCustomers());
        AvailableItems = new ObservableCollection<InvoiceCatalogItem>(_storageService.GetItems());
        AvailableTagSuggestions = new ObservableCollection<string>(_storageService.GetTags());

        Items = new ObservableCollection<InvoiceLineItem>();
        Tags = new ObservableCollection<string>();

        Items.CollectionChanged += OnItemsCollectionChanged;
        Tags.CollectionChanged += OnTagsCollectionChanged;

        GoBackCommand = new RelayCommand(_ =>
        {
            if (_storageService.IsPendingDocument(_documentId))
                _storageService.DiscardPendingDocument(_documentId);

            App.NavigationService?.NavigateTo(new InvoicesViewModel(_storageService, InvoiceModuleSection.Documents));
        });
        SaveCommand = new RelayCommand(_ => Save(openPreviewAfterSave: false), _ => _isLoaded);
        SaveAndPreviewCommand = new RelayCommand(_ => Save(openPreviewAfterSave: true), _ => _isLoaded);
        AddLineItemCommand = new RelayCommand(_ => AddLineItem(), _ => _isLoaded);
        AddTextItemCommand = new RelayCommand(_ => AddTextItem(), _ => _isLoaded);
        RemoveLineItemCommand = new RelayCommand(RemoveLineItem, parameter => parameter is InvoiceLineItem || SelectedLineItem != null);
        AddTagCommand = new RelayCommand(_ => AddTag(), _ => !string.IsNullOrWhiteSpace(NewTag));
        RemoveTagCommand = new RelayCommand(RemoveTag, static parameter => parameter is string);
        InsertSelectedCatalogItemCommand = new RelayCommand(_ => InsertSelectedCatalogItem(), _ => SelectedCatalogItem != null);
        LookupSupplierAresCommand = new AsyncRelayCommand(_ => LookupSupplierAresAsync(), _ => _isLoaded && CanLookupAres(SupplierIco));
        LookupCustomerAresCommand = new AsyncRelayCommand(_ => LookupCustomerAresAsync(), _ => _isLoaded && CanLookupAres(CustomerIco));

        LoadDocument();
    }

    public ICommand GoBackCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAndPreviewCommand { get; }
    public ICommand AddLineItemCommand { get; }
    public ICommand AddTextItemCommand { get; }
    public ICommand RemoveLineItemCommand { get; }
    public ICommand AddTagCommand { get; }
    public ICommand RemoveTagCommand { get; }
    public ICommand InsertSelectedCatalogItemCommand { get; }
    public ICommand LookupSupplierAresCommand { get; }
    public ICommand LookupCustomerAresCommand { get; }

    public ObservableCollection<InvoiceTypeOption> DocumentTypes { get; }
    public ObservableCollection<InvoiceStatusOption> StatusOptions { get; }
    public ObservableCollection<InvoiceLanguageOption> LanguageOptions { get; }
    public ObservableCollection<InvoiceChoiceOption> PaymentMethodOptions { get; }
    public ObservableCollection<InvoiceChoiceOption> RoundingOptions { get; }
    public ObservableCollection<InvoiceChoiceOption> QrFormatOptions { get; }
    public ObservableCollection<InvoiceChoiceOption> ThemeOptions { get; }
    public ObservableCollection<InvoiceBoolOption> PaidMessageOptions { get; }
    public ObservableCollection<InvoiceBoolOption> CashReceiptVatPayerOptions { get; }
    public ObservableCollection<InvoiceChoiceOption> CashReceiptRoundingOptions { get; }
    public ObservableCollection<InvoiceChoiceOption> CashReceiptIncomeVariantOptions { get; }
    public ObservableCollection<InvoiceChoiceOption> CashReceiptExpenseVariantOptions { get; }
    public ObservableCollection<string> CurrencyOptions { get; }
    public ObservableCollection<string> AssetOptions { get; }
    public ObservableCollection<InvoiceTemplateOption> TemplateOptions { get; }
    public ObservableCollection<InvoiceParty> AvailableCompanies { get; }
    public ObservableCollection<InvoiceParty> AvailableCustomers { get; }
    public ObservableCollection<InvoiceCatalogItem> AvailableItems { get; }
    public ObservableCollection<string> AvailableTagSuggestions { get; }
    public ObservableCollection<InvoiceLineItem> Items { get; }
    public ObservableCollection<string> Tags { get; }

    public InvoiceLineItem? SelectedLineItem
    {
        get => _selectedLineItem;
        set
        {
            if (SetProperty(ref _selectedLineItem, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public InvoiceParty? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (SetProperty(ref _selectedSupplier, value) && value != null)
            {
                _document.SupplierCatalogId = value.Ico;
                ApplyParty(_document.Supplier, value);
                TryUpdateInvoiceNumberSuggestion();
                RaiseSupplierProperties();
                RefreshTotals();
            }
        }
    }

    public InvoiceParty? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value) && value != null)
            {
                _document.CustomerCatalogId = value.Ico;
                ApplyParty(_document.Customer, value);
                RaiseCustomerProperties();
            }
        }
    }

    public InvoiceCatalogItem? SelectedCatalogItem
    {
        get => _selectedCatalogItem;
        set
        {
            if (SetProperty(ref _selectedCatalogItem, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewTag
    {
        get => _newTag;
        set
        {
            if (SetProperty(ref _newTag, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string EditorTitle => _document.IsCashReceiptDocument ? CashReceiptTitle : GetDocumentTypeLabel(_document.Type);
    public string Breadcrumb => $"{Res("InvoicesSectionDocuments")} > {EditorTitle}";

    public string Number
    {
        get => _document.Number;
        set
        {
            if (string.Equals(_document.Number, value, StringComparison.Ordinal))
                return;

            var previousNumber = _document.Number;
            _document.Number = value;
            if (_isLoaded && !_suppressNumberEditedTracking)
                _numberEditedByUser = true;
            SyncLinkedReceiptPurposes(previousNumber);
            OnPropertyChanged();
        }
    }

    public InvoiceDocumentType SelectedType
    {
        get => _document.Type;
        set
        {
            if (_document.Type == value)
                return;

            var previousTotal = _document.TotalAmount;
            _document.Type = value;
            _document.SelectedTemplateId = NormalizeTemplateId(_document.SelectedTemplateId, value);
            _document.CashReceiptDocumentVariant = InvoiceCashReceiptHelper.NormalizeVariant(_document.CashReceiptDocumentVariant, value, _document.CashReceiptTitle);
            _document.CashReceiptTitle = InvoiceCashReceiptHelper.GetTitle(value, _document.CashReceiptDocumentVariant, _document.CashReceiptTitle, Res);

            if (_document.IsCashReceiptDocument && _document.CashReceiptAmount <= 0m && previousTotal > 0m)
                _document.CashReceiptAmount = previousTotal;

            OnPropertyChanged();
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(IsCashReceiptMode));
            OnPropertyChanged(nameof(IsStandardInvoiceEditorMode));
            OnPropertyChanged(nameof(AvailableTemplateOptions));
            OnPropertyChanged(nameof(CanEditThemeSelection));
            OnPropertyChanged(nameof(AvailableCashReceiptDocumentVariantOptions));
            OnPropertyChanged(nameof(SelectedCashReceiptDocumentVariant));
            OnPropertyChanged(nameof(SelectedTemplateId));
            OnPropertyChanged(nameof(CashReceiptTitle));
            RefreshTotals();
        }
    }

    public InvoiceDocumentStatus SelectedStatus
    {
        get => _document.Status;
        set => SetDocumentValue(_document.Status, value, static (doc, val) => doc.Status = val, nameof(SelectedStatus));
    }

    public string Language
    {
        get => _document.Language;
        set => SetDocumentValue(_document.Language, value, static (doc, val) => doc.Language = val, nameof(Language));
    }

    public string SelectedLanguageCode
    {
        get => Language;
        set
        {
            if (Language != value)
            {
                Language = value;
                OnPropertyChanged();
            }
        }
    }

    public string Currency
    {
        get => _document.Currency;
        set
        {
            if (_document.Currency != value)
            {
                _document.Currency = value;
                OnPropertyChanged();
                RefreshTotals();
            }
        }
    }

    public string SelectedLogoAssetName
    {
        get => string.IsNullOrWhiteSpace(_document.LogoAssetName) ? AssetOptions[0] : _document.LogoAssetName;
        set
        {
            var normalized = value == AssetOptions[0] ? string.Empty : value;
            SetDocumentValue(_document.LogoAssetName, normalized, static (doc, val) => doc.LogoAssetName = val, nameof(SelectedLogoAssetName));
        }
    }

    public string SelectedStampAssetName
    {
        get => string.IsNullOrWhiteSpace(_document.StampAssetName) ? AssetOptions[0] : _document.StampAssetName;
        set
        {
            var normalized = value == AssetOptions[0] ? string.Empty : value;
            SetDocumentValue(_document.StampAssetName, normalized, static (doc, val) => doc.StampAssetName = val, nameof(SelectedStampAssetName));
        }
    }

    public DateTime IssueDate
    {
        get => _document.IssueDate;
        set => SetDocumentValue(_document.IssueDate, value, static (doc, val) => doc.IssueDate = val, nameof(IssueDate));
    }

    public DateTime DeliveryDate
    {
        get => _document.DeliveryDate;
        set => SetDocumentValue(_document.DeliveryDate, value, static (doc, val) => doc.DeliveryDate = val, nameof(DeliveryDate));
    }

    public DateTime DueDate
    {
        get => _document.DueDate;
        set => SetDocumentValue(_document.DueDate, value, static (doc, val) => doc.DueDate = val, nameof(DueDate));
    }

    public string PaymentMethod
    {
        get => _document.PaymentMethod;
        set => SetDocumentValue(_document.PaymentMethod, value, static (doc, val) => doc.PaymentMethod = val, nameof(PaymentMethod));
    }

    public string SelectedPaymentMethod
    {
        get => PaymentMethod;
        set
        {
            if (PaymentMethod != value)
            {
                PaymentMethod = value;
                OnPropertyChanged();
            }
        }
    }

    public string RoundingMode
    {
        get => _document.RoundingMode;
        set => SetDocumentValue(_document.RoundingMode, value, static (doc, val) => doc.RoundingMode = val, nameof(RoundingMode));
    }

    public string SelectedRoundingMode
    {
        get => RoundingMode;
        set
        {
            if (RoundingMode != value)
            {
                RoundingMode = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedQrPaymentFormat
    {
        get => _document.QrPaymentFormat;
        set => SetDocumentValue(_document.QrPaymentFormat, value, static (doc, val) => doc.QrPaymentFormat = val, nameof(SelectedQrPaymentFormat));
    }

    public string VariableSymbol
    {
        get => _document.VariableSymbol;
        set => SetDocumentValue(_document.VariableSymbol, value, static (doc, val) => doc.VariableSymbol = val, nameof(VariableSymbol));
    }

    public string ConstantSymbol
    {
        get => _document.ConstantSymbol;
        set => SetDocumentValue(_document.ConstantSymbol, value, static (doc, val) => doc.ConstantSymbol = val, nameof(ConstantSymbol));
    }

    public string OrderNumber
    {
        get => _document.OrderNumber;
        set => SetDocumentValue(_document.OrderNumber, value, static (doc, val) => doc.OrderNumber = val, nameof(OrderNumber));
    }

    public decimal AlreadyPaidAmount
    {
        get => _document.AlreadyPaidAmount;
        set
        {
            if (_document.AlreadyPaidAmount != value)
            {
                _document.AlreadyPaidAmount = value;
                OnPropertyChanged();
                RefreshTotals();
            }
        }
    }

    public decimal DiscountPercent
    {
        get => _document.DiscountPercent;
        set
        {
            if (_document.DiscountPercent != value)
            {
                _document.DiscountPercent = value;
                OnPropertyChanged();
                RefreshTotals();
            }
        }
    }

    public bool ShowPaidStamp
    {
        get => _document.ShowPaidStamp;
        set => SetDocumentValue(_document.ShowPaidStamp, value, static (doc, val) => doc.ShowPaidStamp = val, nameof(ShowPaidStamp));
    }

    public bool ShowQrCode
    {
        get => _document.ShowQrCode;
        set => SetDocumentValue(_document.ShowQrCode, value, static (doc, val) => doc.ShowQrCode = val, nameof(ShowQrCode));
    }

    public bool ShowPaidMessage
    {
        get => _document.ShowPaidMessage;
        set => SetDocumentValue(_document.ShowPaidMessage, value, static (doc, val) => doc.ShowPaidMessage = val, nameof(ShowPaidMessage));
    }

    public string QrPreviewStatusText
    {
        get => _qrPreviewStatusText;
        private set => SetProperty(ref _qrPreviewStatusText, value);
    }

    public ImageSource? QrPreviewImage
    {
        get => _qrPreviewImage;
        private set => SetProperty(ref _qrPreviewImage, value);
    }

    public bool HasQrPreview => QrPreviewImage != null;
    public bool HasQrPreviewStatus => !string.IsNullOrWhiteSpace(QrPreviewStatusText);
    public IEnumerable<InvoicePaymentPreviewRow> PaymentPreviewRows => BuildPaymentPreviewRows();
    public bool HasPaymentPreviewSection => HasQrPreviewStatus || PaymentPreviewRows.Any();

    public string NotesAbove
    {
        get => _document.NotesAbove;
        set => SetDocumentValue(_document.NotesAbove, value, static (doc, val) => doc.NotesAbove = val, nameof(NotesAbove));
    }

    public string NotesBelow
    {
        get => _document.NotesBelow;
        set => SetDocumentValue(_document.NotesBelow, value, static (doc, val) => doc.NotesBelow = val, nameof(NotesBelow));
    }

    public string InternalNote
    {
        get => _document.InternalNote;
        set => SetDocumentValue(_document.InternalNote, value, static (doc, val) => doc.InternalNote = val, nameof(InternalNote));
    }

    public string IssuedBy
    {
        get => _document.IssuedBy;
        set => SetDocumentValue(_document.IssuedBy, value, static (doc, val) => doc.IssuedBy = val, nameof(IssuedBy));
    }

    public string SelectedTemplateId
    {
        get => _document.SelectedTemplateId;
        set => SetDocumentValue(_document.SelectedTemplateId, NormalizeTemplateId(value, _document.Type), static (doc, val) => doc.SelectedTemplateId = val, nameof(SelectedTemplateId));
    }

    public string SelectedTheme
    {
        get => _document.SelectedTheme;
        set => SetDocumentValue(_document.SelectedTheme, value, static (doc, val) => doc.SelectedTheme = val, nameof(SelectedTheme));
    }

    public bool CreateIncomeReceipt
    {
        get => _document.CreateIncomeReceipt;
        set => SetDocumentValue(_document.CreateIncomeReceipt, value, static (doc, val) => doc.CreateIncomeReceipt = val, nameof(CreateIncomeReceipt));
    }

    public bool CreateExpenseReceipt
    {
        get => _document.CreateExpenseReceipt;
        set => SetDocumentValue(_document.CreateExpenseReceipt, value, static (doc, val) => doc.CreateExpenseReceipt = val, nameof(CreateExpenseReceipt));
    }

    public string LinkedIncomeReceiptPurpose
    {
        get => string.IsNullOrWhiteSpace(_document.LinkedIncomeReceiptPurpose) ? BuildDefaultLinkedReceiptPurpose(_document.Number) : _document.LinkedIncomeReceiptPurpose;
        set => SetDocumentValue(_document.LinkedIncomeReceiptPurpose, value, static (doc, val) => doc.LinkedIncomeReceiptPurpose = val, nameof(LinkedIncomeReceiptPurpose));
    }

    public string LinkedIncomeReceiptNumber
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_document.LinkedIncomeReceiptNumber))
                return _document.LinkedIncomeReceiptNumber;

            if (string.IsNullOrWhiteSpace(_document.LinkedIncomeReceiptId))
                return string.Empty;

            var linked = _storageService.LoadDocument(_document.LinkedIncomeReceiptId);
            return linked?.Number ?? string.Empty;
        }
        set => SetDocumentValue(_document.LinkedIncomeReceiptNumber, value, static (doc, val) => doc.LinkedIncomeReceiptNumber = val, nameof(LinkedIncomeReceiptNumber));
    }

    public string LinkedExpenseReceiptPurpose
    {
        get => string.IsNullOrWhiteSpace(_document.LinkedExpenseReceiptPurpose) ? BuildDefaultLinkedReceiptPurpose(_document.Number) : _document.LinkedExpenseReceiptPurpose;
        set => SetDocumentValue(_document.LinkedExpenseReceiptPurpose, value, static (doc, val) => doc.LinkedExpenseReceiptPurpose = val, nameof(LinkedExpenseReceiptPurpose));
    }

    public string LinkedExpenseReceiptNumber
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_document.LinkedExpenseReceiptNumber))
                return _document.LinkedExpenseReceiptNumber;

            if (string.IsNullOrWhiteSpace(_document.LinkedExpenseReceiptId))
                return string.Empty;

            var linked = _storageService.LoadDocument(_document.LinkedExpenseReceiptId);
            return linked?.Number ?? string.Empty;
        }
        set => SetDocumentValue(_document.LinkedExpenseReceiptNumber, value, static (doc, val) => doc.LinkedExpenseReceiptNumber = val, nameof(LinkedExpenseReceiptNumber));
    }

    public string LinkedIncomeReceiptNumberText
    {
        get
        {
            return string.IsNullOrWhiteSpace(LinkedIncomeReceiptNumber)
                ? Res("InvoicesAutoAfterSave")
                : LinkedIncomeReceiptNumber;
        }
    }

    public string LinkedExpenseReceiptNumberText
    {
        get
        {
            return string.IsNullOrWhiteSpace(LinkedExpenseReceiptNumber)
                ? Res("InvoicesAutoAfterSave")
                : LinkedExpenseReceiptNumber;
        }
    }

    public bool IsCashReceiptMode => _document.IsCashReceiptDocument;
    public bool IsStandardInvoiceEditorMode => !IsCashReceiptMode;
    public bool CanEditThemeSelection => !IsCashReceiptMode;
    public IEnumerable<InvoiceTemplateOption> AvailableTemplateOptions => IsCashReceiptMode
        ? TemplateOptions.Where(static option => option.Id is "cash1" or "cash2")
        : TemplateOptions.Where(static option => option.Id is not "cash1" and not "cash2");
    public IEnumerable<InvoiceChoiceOption> AvailableCashReceiptDocumentVariantOptions => _document.Type == InvoiceDocumentType.CashReceiptExpense
        ? CashReceiptExpenseVariantOptions
        : CashReceiptIncomeVariantOptions;

    public string SelectedCashReceiptDocumentVariant
    {
        get => InvoiceCashReceiptHelper.NormalizeVariant(_document.CashReceiptDocumentVariant, _document.Type, _document.CashReceiptTitle);
        set
        {
            var normalized = InvoiceCashReceiptHelper.NormalizeVariant(value, _document.Type, _document.CashReceiptTitle);
            if (string.Equals(_document.CashReceiptDocumentVariant, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            _document.CashReceiptDocumentVariant = normalized;
            _document.CashReceiptTitle = InvoiceCashReceiptHelper.GetTitle(_document.Type, normalized, _document.CashReceiptTitle, Res);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CashReceiptTitle));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(Breadcrumb));
        }
    }

    public string CashReceiptTitle
    {
        get => InvoiceCashReceiptHelper.GetTitle(_document.Type, _document.CashReceiptDocumentVariant, _document.CashReceiptTitle, Res);
        set
        {
            var normalizedVariant = InvoiceCashReceiptHelper.NormalizeVariant(_document.CashReceiptDocumentVariant, _document.Type, value);
            var normalizedTitle = InvoiceCashReceiptHelper.GetTitle(_document.Type, normalizedVariant, value, Res);
            if (string.Equals(_document.CashReceiptDocumentVariant, normalizedVariant, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_document.CashReceiptTitle, normalizedTitle, StringComparison.Ordinal))
                return;

            _document.CashReceiptDocumentVariant = normalizedVariant;
            _document.CashReceiptTitle = normalizedTitle;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCashReceiptDocumentVariant));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(Breadcrumb));
        }
    }

    public DateTime CashReceiptPaymentDate
    {
        get => _document.CashReceiptPaymentDate;
        set => SetDocumentValue(_document.CashReceiptPaymentDate, value, static (doc, val) => doc.CashReceiptPaymentDate = val, nameof(CashReceiptPaymentDate));
    }

    public string CashReceiptPlace
    {
        get => _document.CashReceiptPlace;
        set => SetDocumentValue(_document.CashReceiptPlace, value, static (doc, val) => doc.CashReceiptPlace = val, nameof(CashReceiptPlace));
    }

    public string CashReceiptLedgerNumber
    {
        get => _document.CashReceiptLedgerNumber;
        set => SetDocumentValue(_document.CashReceiptLedgerNumber, value, static (doc, val) => doc.CashReceiptLedgerNumber = val, nameof(CashReceiptLedgerNumber));
    }

    public decimal CashReceiptBaseAmount
    {
        get => _document.CashReceiptBaseAmount;
        set
        {
            if (_document.CashReceiptBaseAmount == value)
                return;

            _document.CashReceiptBaseAmount = value;
            OnPropertyChanged();
            RefreshTotals();
        }
    }

    public bool CashReceiptVatPayer
    {
        get => _document.Supplier.IsVatPayer;
        set
        {
            if (_document.Supplier.IsVatPayer == value)
                return;

            _document.Supplier.IsVatPayer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SupplierIsVatPayer));
            RefreshTotals();
        }
    }

    public decimal CashReceiptTaxRate
    {
        get => _document.CashReceiptTaxRate;
        set
        {
            if (_document.CashReceiptTaxRate == value)
                return;

            _document.CashReceiptTaxRate = value;
            OnPropertyChanged();
            RefreshTotals();
        }
    }

    public decimal CashReceiptTotalWithVat
    {
        get => _document.CashReceiptTotalWithVat;
        set
        {
            if (_document.CashReceiptTotalWithVat == value)
                return;

            _document.CashReceiptTotalWithVat = value;
            OnPropertyChanged();
            RefreshTotals();
        }
    }

    public decimal CashReceiptNonTaxableAmount
    {
        get => _document.CashReceiptNonTaxableAmount;
        set
        {
            if (_document.CashReceiptNonTaxableAmount == value)
                return;

            _document.CashReceiptNonTaxableAmount = value;
            OnPropertyChanged();
            RefreshTotals();
        }
    }

    public decimal CashReceiptAmount
    {
        get => _document.CashReceiptAmount;
        set
        {
            if (_document.CashReceiptAmount == value)
                return;

            _document.CashReceiptAmount = value;
            OnPropertyChanged();
            RefreshTotals();
        }
    }

    public string CashReceiptAmountText
    {
        get => _document.CashReceiptAmountText;
        set => SetDocumentValue(_document.CashReceiptAmountText, value, static (doc, val) => doc.CashReceiptAmountText = val, nameof(CashReceiptAmountText));
    }

    public string CashReceiptPurpose
    {
        get => _document.CashReceiptPurpose;
        set => SetDocumentValue(_document.CashReceiptPurpose, value, static (doc, val) => doc.CashReceiptPurpose = val, nameof(CashReceiptPurpose));
    }

    public string CashReceiptPreparedBy
    {
        get => _document.CashReceiptPreparedBy;
        set => SetDocumentValue(_document.CashReceiptPreparedBy, value, static (doc, val) => doc.CashReceiptPreparedBy = val, nameof(CashReceiptPreparedBy));
    }

    public string CashReceiptApprovedBy
    {
        get => _document.CashReceiptApprovedBy;
        set => SetDocumentValue(_document.CashReceiptApprovedBy, value, static (doc, val) => doc.CashReceiptApprovedBy = val, nameof(CashReceiptApprovedBy));
    }

    public DateTime CashReceiptAccountingDate
    {
        get => _document.CashReceiptAccountingDate;
        set => SetDocumentValue(_document.CashReceiptAccountingDate, value, static (doc, val) => doc.CashReceiptAccountingDate = val, nameof(CashReceiptAccountingDate));
    }

    public string CashReceiptAccount1
    {
        get => _document.CashReceiptAccount1;
        set => SetDocumentValue(_document.CashReceiptAccount1, value, static (doc, val) => doc.CashReceiptAccount1 = val, nameof(CashReceiptAccount1));
    }

    public string CashReceiptAccount1Text
    {
        get => _document.CashReceiptAccount1Text;
        set => SetDocumentValue(_document.CashReceiptAccount1Text, value, static (doc, val) => doc.CashReceiptAccount1Text = val, nameof(CashReceiptAccount1Text));
    }

    public decimal CashReceiptAccount1Amount
    {
        get => _document.CashReceiptAccount1Amount;
        set => SetDocumentValue(_document.CashReceiptAccount1Amount, value, static (doc, val) => doc.CashReceiptAccount1Amount = val, nameof(CashReceiptAccount1Amount));
    }

    public string CashReceiptAccount2
    {
        get => _document.CashReceiptAccount2;
        set => SetDocumentValue(_document.CashReceiptAccount2, value, static (doc, val) => doc.CashReceiptAccount2 = val, nameof(CashReceiptAccount2));
    }

    public string CashReceiptAccount2Text
    {
        get => _document.CashReceiptAccount2Text;
        set => SetDocumentValue(_document.CashReceiptAccount2Text, value, static (doc, val) => doc.CashReceiptAccount2Text = val, nameof(CashReceiptAccount2Text));
    }

    public decimal CashReceiptAccount2Amount
    {
        get => _document.CashReceiptAccount2Amount;
        set => SetDocumentValue(_document.CashReceiptAccount2Amount, value, static (doc, val) => doc.CashReceiptAccount2Amount = val, nameof(CashReceiptAccount2Amount));
    }

    public string CashReceiptAccount3
    {
        get => _document.CashReceiptAccount3;
        set => SetDocumentValue(_document.CashReceiptAccount3, value, static (doc, val) => doc.CashReceiptAccount3 = val, nameof(CashReceiptAccount3));
    }

    public string CashReceiptAccount3Text
    {
        get => _document.CashReceiptAccount3Text;
        set => SetDocumentValue(_document.CashReceiptAccount3Text, value, static (doc, val) => doc.CashReceiptAccount3Text = val, nameof(CashReceiptAccount3Text));
    }

    public decimal CashReceiptAccount3Amount
    {
        get => _document.CashReceiptAccount3Amount;
        set => SetDocumentValue(_document.CashReceiptAccount3Amount, value, static (doc, val) => doc.CashReceiptAccount3Amount = val, nameof(CashReceiptAccount3Amount));
    }

    public string CashReceiptAccount4
    {
        get => _document.CashReceiptAccount4;
        set => SetDocumentValue(_document.CashReceiptAccount4, value, static (doc, val) => doc.CashReceiptAccount4 = val, nameof(CashReceiptAccount4));
    }

    public string CashReceiptAccount4Text
    {
        get => _document.CashReceiptAccount4Text;
        set => SetDocumentValue(_document.CashReceiptAccount4Text, value, static (doc, val) => doc.CashReceiptAccount4Text = val, nameof(CashReceiptAccount4Text));
    }

    public decimal CashReceiptAccount4Amount
    {
        get => _document.CashReceiptAccount4Amount;
        set => SetDocumentValue(_document.CashReceiptAccount4Amount, value, static (doc, val) => doc.CashReceiptAccount4Amount = val, nameof(CashReceiptAccount4Amount));
    }

    public string CashReceiptAccount5
    {
        get => _document.CashReceiptAccount5;
        set => SetDocumentValue(_document.CashReceiptAccount5, value, static (doc, val) => doc.CashReceiptAccount5 = val, nameof(CashReceiptAccount5));
    }

    public string CashReceiptAccount5Text
    {
        get => _document.CashReceiptAccount5Text;
        set => SetDocumentValue(_document.CashReceiptAccount5Text, value, static (doc, val) => doc.CashReceiptAccount5Text = val, nameof(CashReceiptAccount5Text));
    }

    public decimal CashReceiptAccount5Amount
    {
        get => _document.CashReceiptAccount5Amount;
        set => SetDocumentValue(_document.CashReceiptAccount5Amount, value, static (doc, val) => doc.CashReceiptAccount5Amount = val, nameof(CashReceiptAccount5Amount));
    }

    public string CashReceiptAccount6
    {
        get => _document.CashReceiptAccount6;
        set => SetDocumentValue(_document.CashReceiptAccount6, value, static (doc, val) => doc.CashReceiptAccount6 = val, nameof(CashReceiptAccount6));
    }

    public string CashReceiptAccount6Text
    {
        get => _document.CashReceiptAccount6Text;
        set => SetDocumentValue(_document.CashReceiptAccount6Text, value, static (doc, val) => doc.CashReceiptAccount6Text = val, nameof(CashReceiptAccount6Text));
    }

    public decimal CashReceiptAccount6Amount
    {
        get => _document.CashReceiptAccount6Amount;
        set => SetDocumentValue(_document.CashReceiptAccount6Amount, value, static (doc, val) => doc.CashReceiptAccount6Amount = val, nameof(CashReceiptAccount6Amount));
    }

    public string SelectedCashReceiptRoundingMode
    {
        get => _document.RoundingMode;
        set
        {
            if (_document.RoundingMode == value)
                return;

            _document.RoundingMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RoundingMode));
            RefreshTotals();
        }
    }

    public bool CashReceiptShowVatRows => CashReceiptVatPayer;
    public decimal CashReceiptVatAmount => Math.Max(0m, CashReceiptComputedTotalWithVat - CashReceiptBaseAmount);
    public decimal CashReceiptComputedTotalWithVat => GetCashReceiptComputedTotalWithVat();
    public decimal CashReceiptNonTaxableWithRounding => Math.Max(0m, CashReceiptNonTaxableAmount) + CashReceiptRoundingAdjustment;
    public decimal CashReceiptRoundingAdjustment => CashReceiptAmount - (CashReceiptComputedTotalWithVat + Math.Max(0m, CashReceiptNonTaxableAmount));
    public string CashReceiptSubtotalLabel => $"{CashReceiptBaseAmount:0.00} {Currency}";
    public string CashReceiptVatAmountLabel => $"{CashReceiptVatAmount:0.00} {Currency}";
    public string CashReceiptTotalWithVatLabel => $"{CashReceiptComputedTotalWithVat:0.00} {Currency}";
    public string CashReceiptNonTaxableWithRoundingLabel => $"{CashReceiptNonTaxableWithRounding:0.00} {Currency}";
    public string CashReceiptGrandTotalLabel => $"{CashReceiptAmount:0.00} {Currency}";

    public string SupplierName
    {
        get => _document.Supplier.Name;
        set => SetPartyValue(_document.Supplier, party => party.Name = value, nameof(SupplierName));
    }

    public string SupplierIco
    {
        get => _document.Supplier.Ico;
        set => SetPartyValue(_document.Supplier, party => party.Ico = value, nameof(SupplierIco));
    }

    public string SupplierDic
    {
        get => _document.Supplier.Dic;
        set => SetPartyValue(_document.Supplier, party => party.Dic = value, nameof(SupplierDic));
    }

    public string SupplierVatId
    {
        get => _document.Supplier.VatId;
        set => SetPartyValue(_document.Supplier, party => party.VatId = value, nameof(SupplierVatId));
    }

    public string SupplierStreet
    {
        get => _document.Supplier.Street;
        set => SetPartyValue(_document.Supplier, party => party.Street = value, nameof(SupplierStreet));
    }

    public string SupplierPostalCode
    {
        get => _document.Supplier.PostalCode;
        set => SetPartyValue(_document.Supplier, party => party.PostalCode = value, nameof(SupplierPostalCode));
    }

    public string SupplierCity
    {
        get => _document.Supplier.City;
        set => SetPartyValue(_document.Supplier, party => party.City = value, nameof(SupplierCity));
    }

    public string SupplierCountry
    {
        get => _document.Supplier.Country;
        set => SetPartyValue(_document.Supplier, party => party.Country = value, nameof(SupplierCountry));
    }

    public bool SupplierIsVatPayer
    {
        get => _document.Supplier.IsVatPayer;
        set
        {
            if (_document.Supplier.IsVatPayer == value)
                return;

            _document.Supplier.IsVatPayer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CashReceiptVatPayer));
            RefreshTotals();
        }
    }

    public bool SupplierShowVatIdOnDocument
    {
        get => _document.Supplier.ShowVatIdOnDocument;
        set => SetPartyValue(_document.Supplier, party => party.ShowVatIdOnDocument = value, nameof(SupplierShowVatIdOnDocument));
    }

    public string SupplierInfoNote
    {
        get => _document.Supplier.InfoNote;
        set => SetPartyValue(_document.Supplier, party => party.InfoNote = value, nameof(SupplierInfoNote));
    }

    public string SupplierPhone
    {
        get => _document.Supplier.Phone;
        set => SetPartyValue(_document.Supplier, party => party.Phone = value, nameof(SupplierPhone));
    }

    public string SupplierEmail
    {
        get => _document.Supplier.Email;
        set => SetPartyValue(_document.Supplier, party => party.Email = value, nameof(SupplierEmail));
    }

    public string SupplierWeb
    {
        get => _document.Supplier.Web;
        set => SetPartyValue(_document.Supplier, party => party.Web = value, nameof(SupplierWeb));
    }

    public string SupplierBankIban
    {
        get => _document.Supplier.BankIban;
        set => SetPartyValue(_document.Supplier, party => party.BankIban = value, nameof(SupplierBankIban));
    }

    public string SupplierBankSwift
    {
        get => _document.Supplier.BankSwift;
        set => SetPartyValue(_document.Supplier, party => party.BankSwift = value, nameof(SupplierBankSwift));
    }

    public string SupplierBankName
    {
        get => _document.Supplier.BankName;
        set => SetPartyValue(_document.Supplier, party => party.BankName = value, nameof(SupplierBankName));
    }

    public string SupplierLegacyAccountNumber
    {
        get => _document.Supplier.LegacyAccountNumber;
        set => SetPartyValue(_document.Supplier, party => party.LegacyAccountNumber = value, nameof(SupplierLegacyAccountNumber));
    }

    public string CustomerName
    {
        get => _document.Customer.Name;
        set => SetPartyValue(_document.Customer, party => party.Name = value, nameof(CustomerName));
    }

    public string CustomerIco
    {
        get => _document.Customer.Ico;
        set => SetPartyValue(_document.Customer, party => party.Ico = value, nameof(CustomerIco));
    }

    public string CustomerDic
    {
        get => _document.Customer.Dic;
        set => SetPartyValue(_document.Customer, party => party.Dic = value, nameof(CustomerDic));
    }

    public string CustomerVatId
    {
        get => _document.Customer.VatId;
        set => SetPartyValue(_document.Customer, party => party.VatId = value, nameof(CustomerVatId));
    }

    public string CustomerStreet
    {
        get => _document.Customer.Street;
        set => SetPartyValue(_document.Customer, party => party.Street = value, nameof(CustomerStreet));
    }

    public string CustomerPostalCode
    {
        get => _document.Customer.PostalCode;
        set => SetPartyValue(_document.Customer, party => party.PostalCode = value, nameof(CustomerPostalCode));
    }

    public string CustomerCity
    {
        get => _document.Customer.City;
        set => SetPartyValue(_document.Customer, party => party.City = value, nameof(CustomerCity));
    }

    public string CustomerCountry
    {
        get => _document.Customer.Country;
        set => SetPartyValue(_document.Customer, party => party.Country = value, nameof(CustomerCountry));
    }

    public string CustomerEmail
    {
        get => _document.Customer.Email;
        set => SetPartyValue(_document.Customer, party => party.Email = value, nameof(CustomerEmail));
    }

    public string CustomerInfoNote
    {
        get => _document.Customer.InfoNote;
        set => SetPartyValue(_document.Customer, party => party.InfoNote = value, nameof(CustomerInfoNote));
    }

    public string CustomerDeliveryName
    {
        get => _document.Customer.DeliveryName;
        set => SetPartyValue(_document.Customer, party => party.DeliveryName = value, nameof(CustomerDeliveryName));
    }

    public string CustomerDeliveryStreet
    {
        get => _document.Customer.DeliveryStreet;
        set => SetPartyValue(_document.Customer, party => party.DeliveryStreet = value, nameof(CustomerDeliveryStreet));
    }

    public string CustomerDeliveryCity
    {
        get => _document.Customer.DeliveryCity;
        set => SetPartyValue(_document.Customer, party => party.DeliveryCity = value, nameof(CustomerDeliveryCity));
    }

    public string CustomerDeliveryPostalCode
    {
        get => _document.Customer.DeliveryPostalCode;
        set => SetPartyValue(_document.Customer, party => party.DeliveryPostalCode = value, nameof(CustomerDeliveryPostalCode));
    }

    public string CustomerDeliveryCountry
    {
        get => _document.Customer.DeliveryCountry;
        set => SetPartyValue(_document.Customer, party => party.DeliveryCountry = value, nameof(CustomerDeliveryCountry));
    }

    public decimal TotalAmount => _document.TotalAmount;
    public decimal DiscountAmount => _document.DiscountAmount;
    public decimal AmountToPay => _document.AmountToPay;
    public string TotalAmountLabel => $"{TotalAmount:0.00} {Currency}";
    public string DiscountAmountLabel => $"{DiscountAmount:0.00} {Currency}";
    public string AmountToPayLabel => $"{AmountToPay:0.00} {Currency}";
    public bool HasSupplierCatalog => AvailableCompanies.Count > 0;
    public bool HasCustomerCatalog => AvailableCustomers.Count > 0;
    public bool HasItemCatalog => AvailableItems.Count > 0;

    private void LoadDocument()
    {
        var loaded = _storageService.LoadDocument(_documentId);
        if (loaded == null)
        {
            ToastService.Instance.Warning(Res("InvoicesLoadDocumentFailed"));
            App.NavigationService?.NavigateTo(new InvoicesViewModel(_storageService, InvoiceModuleSection.Documents));
            return;
        }

        _document = loaded;
        if (string.IsNullOrWhiteSpace(_document.QrPaymentFormat))
            _document.QrPaymentFormat = "spayd";
        _document.SelectedTemplateId = NormalizeTemplateId(_document.SelectedTemplateId, _document.Type);
        _document.CashReceiptDocumentVariant = InvoiceCashReceiptHelper.NormalizeVariant(_document.CashReceiptDocumentVariant, _document.Type, _document.CashReceiptTitle);
        _document.CashReceiptTitle = InvoiceCashReceiptHelper.GetTitle(_document.Type, _document.CashReceiptDocumentVariant, _document.CashReceiptTitle, Res);
        Items.Clear();
        foreach (var item in _document.Items)
        {
            SubscribeLineItem(item);
            Items.Add(item);
        }
        if (Items.Count == 0 && !_document.IsCashReceiptDocument)
            AddLineItem();

        Tags.Clear();
        foreach (var tag in _document.Tags)
            Tags.Add(tag);

        if (!string.IsNullOrWhiteSpace(_document.LogoAssetName) && !AssetOptions.Contains(_document.LogoAssetName))
            AssetOptions.Add(_document.LogoAssetName);
        if (!string.IsNullOrWhiteSpace(_document.StampAssetName) && !AssetOptions.Contains(_document.StampAssetName))
            AssetOptions.Add(_document.StampAssetName);

        SelectedSupplier = AvailableCompanies.FirstOrDefault(company => company.Ico == _document.SupplierCatalogId);
        SelectedCustomer = AvailableCustomers.FirstOrDefault(customer => customer.Ico == _document.CustomerCatalogId);
        SelectedCatalogItem = null;

        _isLoaded = true;
        _numberEditedByUser = false;
        RaiseAllProperties();
        CommandManager.InvalidateRequerySuggested();
    }

    private void Save(bool openPreviewAfterSave)
    {
        if (!PolicyService.EnsureWriteAllowed(Res("InvoicesSaveDocumentPolicyName")))
            return;

        if (!PrepareInvoiceNumberForSave())
            return;

        EnsureLinkedReceiptDefaults();
        _document.Items = Items.ToList();
        _document.Tags = Tags.ToList();
        _document.UpdatedAtUtc = DateTime.UtcNow;
        _document.SelectedTemplateId = NormalizeTemplateId(_document.SelectedTemplateId, _document.Type);
        _document.CashReceiptDocumentVariant = InvoiceCashReceiptHelper.NormalizeVariant(_document.CashReceiptDocumentVariant, _document.Type, _document.CashReceiptTitle);
        _document.CashReceiptTitle = InvoiceCashReceiptHelper.GetTitle(_document.Type, _document.CashReceiptDocumentVariant, _document.CashReceiptTitle, Res);

        _storageService.SaveDocument(_document);
        _storageService.SaveModuleLanguage(_document.Language);
        CreateOrUpdateLinkedReceipts();
        _storageService.SaveDocument(_document);

        try
        {
            var pdfPath = _pdfRenderService.RenderPdf(_document);

            if (openPreviewAfterSave)
            {
                ToastService.Instance.Success(string.Format(Res("InvoicesPdfPreviewReady"), Number));
                var previewWindow = new InvoicePdfPreviewWindow(pdfPath);
                if (Application.Current?.MainWindow != null)
                    previewWindow.Owner = Application.Current.MainWindow;
                previewWindow.ShowDialog();
            }
            else
            {
                ToastService.Instance.Success(string.Format(Res("InvoicesSaveSuccess"), Number));
            }
        }
        catch (Exception ex)
        {
            ErrorHandler.Report("InvoiceEditorViewModel.SavePreviewPdf", ex, ErrorSeverity.Error, showUser: false);
            ToastService.Instance.Warning(string.Format(Res("InvoicesPdfPreviewFailedSaved"), Number));
            if (openPreviewAfterSave)
                return;
        }

        App.NavigationService?.NavigateTo(new InvoicesViewModel(_storageService, InvoiceModuleSection.Documents));
    }

    private bool PrepareInvoiceNumberForSave()
    {
        if (_document.Type != InvoiceDocumentType.Invoice)
            return true;

        if (_storageService.IsPendingDocument(_documentId) && (!_numberEditedByUser || string.IsNullOrWhiteSpace(_document.Number)))
            SetDocumentNumberWithoutMarkingEdit(_storageService.AssignNextInvoiceNumber(_document));

        var duplicate = _storageService.FindDuplicateInvoiceNumber(_document);
        if (duplicate == null)
            return true;

        var message = string.Format(Res("InvoicesDuplicateNumberConfirm"), _document.Number);
        var title = Res("InvoicesTitle");
        return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void EnsureLinkedReceiptDefaults()
    {
        if (_document.Type is not InvoiceDocumentType.Invoice)
            return;

        if (_document.CreateIncomeReceipt)
            _document.LinkedIncomeReceiptPurpose = NormalizeLinkedReceiptPurpose(_document.LinkedIncomeReceiptPurpose, _document.Number);

        if (_document.CreateExpenseReceipt)
            _document.LinkedExpenseReceiptPurpose = NormalizeLinkedReceiptPurpose(_document.LinkedExpenseReceiptPurpose, _document.Number);
    }

    private void CreateOrUpdateLinkedReceipts()
    {
        if (_document.Type is not InvoiceDocumentType.Invoice)
            return;

        if (_document.CreateIncomeReceipt)
        {
            var linked = CreateOrUpdateLinkedReceipt(
                _document.LinkedIncomeReceiptId,
                InvoiceDocumentType.CashReceiptIncome,
                LinkedIncomeReceiptPurpose);

            if (linked != null)
                _document.LinkedIncomeReceiptId = linked.Id;
        }

        if (_document.CreateExpenseReceipt)
        {
            var linked = CreateOrUpdateLinkedReceipt(
                _document.LinkedExpenseReceiptId,
                InvoiceDocumentType.CashReceiptExpense,
                LinkedExpenseReceiptPurpose);

            if (linked != null)
                _document.LinkedExpenseReceiptId = linked.Id;
        }

        OnPropertyChanged(nameof(LinkedIncomeReceiptNumberText));
        OnPropertyChanged(nameof(LinkedExpenseReceiptNumberText));
    }

    private InvoiceDocument? CreateOrUpdateLinkedReceipt(string existingId, InvoiceDocumentType receiptType, string purpose)
    {
        var linked = string.IsNullOrWhiteSpace(existingId)
            ? null
            : _storageService.LoadDocument(existingId);

        linked ??= _storageService.CreateDocument(receiptType);
        linked.Type = receiptType;
        linked.Status = InvoiceDocumentStatus.Issued;
        var manualNumber = receiptType == InvoiceDocumentType.CashReceiptIncome
            ? (_document.LinkedIncomeReceiptNumber ?? string.Empty).Trim()
            : (_document.LinkedExpenseReceiptNumber ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(manualNumber))
            linked.Number = manualNumber;
        linked.Language = _document.Language;
        linked.Currency = _document.Currency;
        linked.IssueDate = _document.IssueDate;
        linked.CashReceiptPaymentDate = _document.IssueDate;
        linked.CashReceiptAccountingDate = _document.IssueDate;
        linked.CashReceiptDocumentVariant = "simple";
        linked.CashReceiptTitle = InvoiceCashReceiptHelper.GetTitle(receiptType, "simple", resourceResolver: Res);
        linked.CashReceiptPurpose = purpose;
        linked.SelectedTemplateId = NormalizeTemplateId(linked.SelectedTemplateId, linked.Type);
        linked.SupplierCatalogId = _document.SupplierCatalogId;
        linked.CustomerCatalogId = _document.CustomerCatalogId;
        linked.Supplier = CloneParty(_document.Supplier);
        linked.Customer = CloneParty(_document.Customer);
        linked.Supplier.IsVatPayer = false;
        linked.CashReceiptBaseAmount = _document.AmountToPay;
        linked.CashReceiptTaxRate = 0m;
        linked.CashReceiptTotalWithVat = 0m;
        linked.CashReceiptNonTaxableAmount = 0m;
        linked.CashReceiptAmount = _document.AmountToPay;
        linked.CashReceiptAmountText = _document.AmountToPay > 0m ? _document.AmountToPay.ToString("0.00") : string.Empty;
        linked.CashReceiptPreparedBy = string.IsNullOrWhiteSpace(linked.CashReceiptPreparedBy) ? _document.IssuedBy : linked.CashReceiptPreparedBy;
        linked.NotesAbove = string.Empty;
        linked.NotesBelow = string.Empty;
        linked.InternalNote = string.Empty;
        linked.UpdatedAtUtc = DateTime.UtcNow;

        _storageService.SaveDocument(linked);
        try
        {
            _pdfRenderService.RenderPdf(linked);
        }
        catch (Exception ex)
        {
            ErrorHandler.Report("InvoiceEditorViewModel.CreateOrUpdateLinkedReceiptPdf", ex, ErrorSeverity.Warning, showUser: false);
        }

        if (receiptType == InvoiceDocumentType.CashReceiptIncome)
            _document.LinkedIncomeReceiptNumber = linked.Number;
        else
            _document.LinkedExpenseReceiptNumber = linked.Number;

        return linked;
    }

    private void AddLineItem()
    {
        var item = new InvoiceLineItem();
        SubscribeLineItem(item);
        Items.Add(item);
        SelectedLineItem = item;
        RefreshTotals();
    }

    private void AddTextItem()
    {
        var item = new InvoiceLineItem { IsTextItem = true };
        SubscribeLineItem(item);
        Items.Add(item);
        SelectedLineItem = item;
        RefreshTotals();
    }

    private void InsertSelectedCatalogItem()
    {
        if (SelectedCatalogItem == null)
            return;

        var item = new InvoiceLineItem
        {
            Description = string.IsNullOrWhiteSpace(SelectedCatalogItem.Description) ? SelectedCatalogItem.Name : SelectedCatalogItem.Description,
            Unit = SelectedCatalogItem.Unit,
            UnitPrice = SelectedCatalogItem.UnitPrice
        };
        SubscribeLineItem(item);
        Items.Add(item);
        SelectedLineItem = item;
        RefreshTotals();
    }

    private void RemoveLineItem(object? parameter)
    {
        var itemToRemove = parameter as InvoiceLineItem ?? SelectedLineItem;
        if (itemToRemove == null)
            return;

        UnsubscribeLineItem(itemToRemove);
        Items.Remove(itemToRemove);
        SelectedLineItem = Items.LastOrDefault();
        RefreshTotals();
    }

    private void AddTag()
    {
        var value = NewTag.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!Tags.Any(tag => string.Equals(tag, value, StringComparison.OrdinalIgnoreCase)))
            Tags.Add(value);

        NewTag = string.Empty;
    }

    private string BuildDefaultLinkedReceiptPurpose(string? invoiceNumber)
        => string.IsNullOrWhiteSpace(invoiceNumber)
            ? Res("InvoicesLinkedReceiptPurposeDefaultNoNumber")
            : string.Format(Res("InvoicesLinkedReceiptPurposeDefaultWithNumber"), invoiceNumber.Trim());

    private string NormalizeLinkedReceiptPurpose(string? currentPurpose, string? invoiceNumber)
    {
        var value = (currentPurpose ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return BuildDefaultLinkedReceiptPurpose(invoiceNumber);
        return value;
    }

    private void SyncLinkedReceiptPurposes(string? previousNumber)
    {
        var oldDefault = BuildDefaultLinkedReceiptPurpose(previousNumber);
        var newDefault = BuildDefaultLinkedReceiptPurpose(_document.Number);

        if (string.IsNullOrWhiteSpace(_document.LinkedIncomeReceiptPurpose) || string.Equals(_document.LinkedIncomeReceiptPurpose, oldDefault, StringComparison.Ordinal))
        {
            _document.LinkedIncomeReceiptPurpose = newDefault;
            OnPropertyChanged(nameof(LinkedIncomeReceiptPurpose));
        }

        if (string.IsNullOrWhiteSpace(_document.LinkedExpenseReceiptPurpose) || string.Equals(_document.LinkedExpenseReceiptPurpose, oldDefault, StringComparison.Ordinal))
        {
            _document.LinkedExpenseReceiptPurpose = newDefault;
            OnPropertyChanged(nameof(LinkedExpenseReceiptPurpose));
        }
    }

    private void RemoveTag(object? parameter)
    {
        if (parameter is string tag)
            Tags.Remove(tag);
    }

    private void SetDocumentValue<T>(T currentValue, T newValue, Action<InvoiceDocument, T> apply, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
            return;

        apply(_document, newValue);
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(CashReceiptTitle) or nameof(SelectedCashReceiptDocumentVariant))
        {
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(Breadcrumb));
        }
        if (QrRelevantProperties.Contains(propertyName))
            RefreshQrPreview();
    }

    private void SetPartyValue(InvoiceParty party, Action<InvoiceParty> setter, string propertyName)
    {
        setter(party);
        if (ReferenceEquals(party, _document.Supplier) &&
            propertyName is nameof(SupplierName) or nameof(SupplierIco))
        {
            TryUpdateInvoiceNumberSuggestion();
        }
        OnPropertyChanged(propertyName);
        CommandManager.InvalidateRequerySuggested();
        if (QrRelevantProperties.Contains(propertyName))
            RefreshQrPreview();
    }

    private void TryUpdateInvoiceNumberSuggestion()
    {
        if (!_isLoaded || _numberEditedByUser || _document.Type != InvoiceDocumentType.Invoice)
            return;

        SetDocumentNumberWithoutMarkingEdit(_storageService.SuggestDocumentNumber(_document));
    }

    private void SetDocumentNumberWithoutMarkingEdit(string value)
    {
        if (string.Equals(_document.Number, value, StringComparison.Ordinal))
            return;

        _suppressNumberEditedTracking = true;
        try
        {
            Number = value;
        }
        finally
        {
            _suppressNumberEditedTracking = false;
        }
    }

    private static void ApplyParty(InvoiceParty target, InvoiceParty source)
    {
        target.Name = source.Name;
        target.Ico = source.Ico;
        target.Dic = source.Dic;
        target.VatId = source.VatId;
        target.IsVatPayer = source.IsVatPayer;
        target.ShowVatIdOnDocument = source.ShowVatIdOnDocument;
        target.Street = source.Street;
        target.City = source.City;
        target.PostalCode = source.PostalCode;
        target.Country = source.Country;
        target.InfoNote = source.InfoNote;
        target.Email = source.Email;
        target.Phone = source.Phone;
        target.Web = source.Web;
        target.BankIban = source.BankIban;
        target.BankSwift = source.BankSwift;
        target.BankName = source.BankName;
        target.LegacyAccountNumber = source.LegacyAccountNumber;
        target.DeliveryName = source.DeliveryName;
        target.DeliveryStreet = source.DeliveryStreet;
        target.DeliveryCity = source.DeliveryCity;
        target.DeliveryPostalCode = source.DeliveryPostalCode;
        target.DeliveryCountry = source.DeliveryCountry;
    }

    private static InvoiceParty CloneParty(InvoiceParty source)
    {
        var clone = new InvoiceParty();
        ApplyParty(clone, source);
        return clone;
    }

    private async Task LookupSupplierAresAsync()
    {
        await LookupAresAsync(_document.Supplier, SupplierIco, RaiseSupplierProperties, nameof(SelectedSupplier)).ConfigureAwait(false);
    }

    private async Task LookupCustomerAresAsync()
    {
        await LookupAresAsync(_document.Customer, CustomerIco, RaiseCustomerProperties, nameof(SelectedCustomer)).ConfigureAwait(false);
    }

    private async Task LookupAresAsync(InvoiceParty targetParty, string ico, Action refreshPartyProperties, string selectionPropertyName)
    {
        var normalizedIco = NormalizeIco(ico);
        if (string.IsNullOrWhiteSpace(normalizedIco))
        {
            ToastService.Instance.Warning(Res("InvoicesAresLookupInvalidIco"));
            return;
        }

        try
        {
            var result = await _aresLookupService.LookupByIcoAsync(normalizedIco).ConfigureAwait(false);
            if (result == null)
            {
                ToastService.Instance.Warning(string.Format(Res("InvoicesAresLookupNotFound"), normalizedIco));
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _aresLookupService.ApplyToParty(targetParty, result);
                OnPropertyChanged(selectionPropertyName);
                refreshPartyProperties();
                ToastService.Instance.Success(string.Format(Res("InvoicesAresLookupSuccess"), normalizedIco));
            });
        }
        catch (HttpRequestException)
        {
            ToastService.Instance.Error(Res("InvoicesAresLookupFailed"));
        }
        catch (TaskCanceledException)
        {
            ToastService.Instance.Error(Res("InvoicesAresLookupFailed"));
        }
        catch (Exception ex)
        {
            ErrorHandler.Report("InvoiceEditorViewModel.LookupAresAsync", ex, ErrorSeverity.Error, showUser: false);
            ToastService.Instance.Error(Res("InvoicesAresLookupFailed"));
        }
    }

    private static bool CanLookupAres(string ico) => NormalizeIco(ico).Length >= 6;

    private static string NormalizeIco(string? ico)
        => new string((ico ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string NormalizeTemplateId(string? templateId, InvoiceDocumentType documentType = InvoiceDocumentType.Invoice)
    {
        var isCashReceipt = documentType is InvoiceDocumentType.CashReceiptIncome or InvoiceDocumentType.CashReceiptExpense;
        if (isCashReceipt)
        {
            return (templateId ?? string.Empty).ToLowerInvariant() switch
            {
                "cash1" or "style1" or "default" => "cash1",
                "cash2" or "style2" or "compact" => "cash2",
                _ => "cash1"
            };
        }

        return (templateId ?? string.Empty).ToLowerInvariant() switch
        {
            "default" => "style1",
            "compact" => "style2",
            "minimal" => "style3",
            "formal" => "style4",
            "detail" => "style5",
            "accent" => "style6",
            "style1" or "style2" or "style3" or "style4" or "style5" or "style6" => templateId!.ToLowerInvariant(),
            _ => "style1"
        };
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<InvoiceLineItem>())
                UnsubscribeLineItem(item);
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<InvoiceLineItem>())
                SubscribeLineItem(item);
        }

        RefreshTotals();
    }

    private void OnTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Tags));
    }

    private void SubscribeLineItem(InvoiceLineItem item)
    {
        item.PropertyChanged -= OnLineItemPropertyChanged;
        item.PropertyChanged += OnLineItemPropertyChanged;
    }

    private void UnsubscribeLineItem(InvoiceLineItem item)
    {
        item.PropertyChanged -= OnLineItemPropertyChanged;
    }

    private void OnLineItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InvoiceLineItem.Quantity) or nameof(InvoiceLineItem.UnitPrice) or nameof(InvoiceLineItem.TotalAmount))
            RefreshTotals();
    }

    private decimal GetCashReceiptComputedTotalWithVat()
    {
        var baseAmount = Math.Max(0m, _document.CashReceiptBaseAmount);
        if (!_document.Supplier.IsVatPayer)
            return Math.Round(baseAmount, 2);

        var explicitTotalWithVat = Math.Max(0m, _document.CashReceiptTotalWithVat);
        if (explicitTotalWithVat > 0m)
            return Math.Round(explicitTotalWithVat, 2);

        var taxRate = Math.Max(0m, _document.CashReceiptTaxRate);
        return Math.Round(baseAmount * (1m + (taxRate / 100m)), 2);
    }

    private static decimal ApplyCashReceiptRounding(decimal amount, string? mode)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        return mode switch
        {
            "cash_5" => Math.Round(rounded * 20m, 0, MidpointRounding.AwayFromZero) / 20m,
            "cash_up" => Math.Ceiling(rounded),
            "cash_down" => Math.Floor(rounded),
            _ => rounded
        };
    }

    private void SyncCashReceiptTotals()
    {
        if (!_document.IsCashReceiptDocument)
            return;

        var totalWithVat = GetCashReceiptComputedTotalWithVat();
        var nonTaxable = Math.Max(0m, _document.CashReceiptNonTaxableAmount);
        var grandTotal = ApplyCashReceiptRounding(totalWithVat + nonTaxable, _document.RoundingMode);
        _document.CashReceiptAmount = Math.Round(grandTotal, 2, MidpointRounding.AwayFromZero);
    }

    private void RefreshTotals()
    {
        SyncCashReceiptTotals();
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(TotalAmountLabel));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(DiscountAmountLabel));
        OnPropertyChanged(nameof(AmountToPay));
        OnPropertyChanged(nameof(AmountToPayLabel));
        OnPropertyChanged(nameof(CashReceiptAmount));
        OnPropertyChanged(nameof(CashReceiptShowVatRows));
        OnPropertyChanged(nameof(CashReceiptVatAmount));
        OnPropertyChanged(nameof(CashReceiptComputedTotalWithVat));
        OnPropertyChanged(nameof(CashReceiptNonTaxableWithRounding));
        OnPropertyChanged(nameof(CashReceiptRoundingAdjustment));
        OnPropertyChanged(nameof(CashReceiptSubtotalLabel));
        OnPropertyChanged(nameof(CashReceiptVatAmountLabel));
        OnPropertyChanged(nameof(CashReceiptTotalWithVatLabel));
        OnPropertyChanged(nameof(CashReceiptNonTaxableWithRoundingLabel));
        OnPropertyChanged(nameof(CashReceiptGrandTotalLabel));
        RefreshQrPreview();
    }

    private void RaiseSupplierProperties()
    {
        OnPropertyChanged(nameof(SupplierName));
        OnPropertyChanged(nameof(SupplierIco));
        OnPropertyChanged(nameof(SupplierDic));
        OnPropertyChanged(nameof(SupplierVatId));
        OnPropertyChanged(nameof(CashReceiptVatPayer));
        OnPropertyChanged(nameof(SupplierStreet));
        OnPropertyChanged(nameof(SupplierPostalCode));
        OnPropertyChanged(nameof(SupplierCity));
        OnPropertyChanged(nameof(SupplierCountry));
        OnPropertyChanged(nameof(SupplierIsVatPayer));
        OnPropertyChanged(nameof(SupplierShowVatIdOnDocument));
        OnPropertyChanged(nameof(SupplierInfoNote));
        OnPropertyChanged(nameof(SupplierPhone));
        OnPropertyChanged(nameof(SupplierEmail));
        OnPropertyChanged(nameof(SupplierWeb));
        OnPropertyChanged(nameof(SupplierBankIban));
        OnPropertyChanged(nameof(SupplierBankSwift));
        OnPropertyChanged(nameof(SupplierBankName));
        OnPropertyChanged(nameof(SupplierLegacyAccountNumber));
        RefreshQrPreview();
    }

    private void RaiseCustomerProperties()
    {
        OnPropertyChanged(nameof(CustomerName));
        OnPropertyChanged(nameof(CustomerIco));
        OnPropertyChanged(nameof(CustomerDic));
        OnPropertyChanged(nameof(CustomerVatId));
        OnPropertyChanged(nameof(CustomerStreet));
        OnPropertyChanged(nameof(CustomerPostalCode));
        OnPropertyChanged(nameof(CustomerCity));
        OnPropertyChanged(nameof(CustomerCountry));
        OnPropertyChanged(nameof(CustomerEmail));
        OnPropertyChanged(nameof(CustomerInfoNote));
        OnPropertyChanged(nameof(CustomerDeliveryName));
        OnPropertyChanged(nameof(CustomerDeliveryStreet));
        OnPropertyChanged(nameof(CustomerDeliveryCity));
        OnPropertyChanged(nameof(CustomerDeliveryPostalCode));
        OnPropertyChanged(nameof(CustomerDeliveryCountry));
    }

    private void RefreshQrPreview()
    {
        var preview = _qrPaymentService.CreatePreview(_document);
        QrPreviewStatusText = preview.Message;
        QrPreviewImage = preview.Image;
        OnPropertyChanged(nameof(HasQrPreview));
        RaisePaymentPreviewProperties();
    }

    private IEnumerable<InvoicePaymentPreviewRow> BuildPaymentPreviewRows()
    {
        var payment = _qrPaymentService.DescribePayment(_document);
        var rows = new List<InvoicePaymentPreviewRow>();
        AddPaymentPreviewRow(rows, Res("InvoicesBankName"), payment.BankName);
        AddPaymentPreviewRow(rows, Res("InvoicesAccountNumber"), payment.AccountNumber);
        AddPaymentPreviewRow(rows, Res("InvoicesBankCode"), payment.BankCode);
        AddPaymentPreviewRow(rows, payment.UsesCalculatedIban ? Res("InvoicesCalculatedIban") : Res("InvoicesIban"), payment.Iban);
        AddPaymentPreviewRow(rows, Res("InvoicesSwift"), payment.Swift);
        AddPaymentPreviewRow(rows, Res("InvoicesVariableSymbol"), payment.VariableSymbol);
        AddPaymentPreviewRow(rows, Res("InvoicesNumber"), payment.InvoiceNumber);
        return rows;
    }

    private static void AddPaymentPreviewRow(ICollection<InvoicePaymentPreviewRow> rows, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        rows.Add(new InvoicePaymentPreviewRow
        {
            Label = label,
            Value = value.Trim()
        });
    }

    private void RaisePaymentPreviewProperties()
    {
        OnPropertyChanged(nameof(HasQrPreviewStatus));
        OnPropertyChanged(nameof(PaymentPreviewRows));
        OnPropertyChanged(nameof(HasPaymentPreviewSection));
    }

    private void RaiseAllProperties()
    {
        OnPropertyChanged(nameof(Breadcrumb));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(IsCashReceiptMode));
        OnPropertyChanged(nameof(IsStandardInvoiceEditorMode));
        OnPropertyChanged(nameof(AvailableTemplateOptions));
        OnPropertyChanged(nameof(AvailableCashReceiptDocumentVariantOptions));
        OnPropertyChanged(nameof(CanEditThemeSelection));
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(SelectedLanguageCode));
        OnPropertyChanged(nameof(Currency));
        OnPropertyChanged(nameof(IssueDate));
        OnPropertyChanged(nameof(DeliveryDate));
        OnPropertyChanged(nameof(DueDate));
        OnPropertyChanged(nameof(PaymentMethod));
        OnPropertyChanged(nameof(SelectedPaymentMethod));
        OnPropertyChanged(nameof(RoundingMode));
        OnPropertyChanged(nameof(SelectedRoundingMode));
        OnPropertyChanged(nameof(SelectedQrPaymentFormat));
        OnPropertyChanged(nameof(VariableSymbol));
        OnPropertyChanged(nameof(ConstantSymbol));
        OnPropertyChanged(nameof(OrderNumber));
        OnPropertyChanged(nameof(AlreadyPaidAmount));
        OnPropertyChanged(nameof(DiscountPercent));
        OnPropertyChanged(nameof(ShowPaidStamp));
        OnPropertyChanged(nameof(ShowQrCode));
        OnPropertyChanged(nameof(ShowPaidMessage));
        OnPropertyChanged(nameof(QrPreviewStatusText));
        OnPropertyChanged(nameof(QrPreviewImage));
        OnPropertyChanged(nameof(HasQrPreview));
        RaisePaymentPreviewProperties();
        OnPropertyChanged(nameof(NotesAbove));
        OnPropertyChanged(nameof(NotesBelow));
        OnPropertyChanged(nameof(InternalNote));
        OnPropertyChanged(nameof(IssuedBy));
        OnPropertyChanged(nameof(SelectedLogoAssetName));
        OnPropertyChanged(nameof(SelectedStampAssetName));
        OnPropertyChanged(nameof(SelectedTemplateId));
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(CreateIncomeReceipt));
        OnPropertyChanged(nameof(CreateExpenseReceipt));
        OnPropertyChanged(nameof(LinkedIncomeReceiptNumber));
        OnPropertyChanged(nameof(LinkedIncomeReceiptPurpose));
        OnPropertyChanged(nameof(LinkedExpenseReceiptNumber));
        OnPropertyChanged(nameof(LinkedExpenseReceiptPurpose));
        OnPropertyChanged(nameof(LinkedIncomeReceiptNumberText));
        OnPropertyChanged(nameof(LinkedExpenseReceiptNumberText));
        OnPropertyChanged(nameof(SelectedCashReceiptDocumentVariant));
        OnPropertyChanged(nameof(CashReceiptTitle));
        OnPropertyChanged(nameof(CashReceiptPaymentDate));
        OnPropertyChanged(nameof(CashReceiptPlace));
        OnPropertyChanged(nameof(CashReceiptLedgerNumber));
        OnPropertyChanged(nameof(CashReceiptBaseAmount));
        OnPropertyChanged(nameof(CashReceiptVatPayer));
        OnPropertyChanged(nameof(CashReceiptTaxRate));
        OnPropertyChanged(nameof(CashReceiptTotalWithVat));
        OnPropertyChanged(nameof(CashReceiptNonTaxableAmount));
        OnPropertyChanged(nameof(CashReceiptAmount));
        OnPropertyChanged(nameof(CashReceiptAmountText));
        OnPropertyChanged(nameof(CashReceiptPurpose));
        OnPropertyChanged(nameof(CashReceiptPreparedBy));
        OnPropertyChanged(nameof(CashReceiptApprovedBy));
        OnPropertyChanged(nameof(CashReceiptAccountingDate));
        OnPropertyChanged(nameof(CashReceiptAccount1));
        OnPropertyChanged(nameof(CashReceiptAccount1Text));
        OnPropertyChanged(nameof(CashReceiptAccount1Amount));
        OnPropertyChanged(nameof(CashReceiptAccount2));
        OnPropertyChanged(nameof(CashReceiptAccount2Text));
        OnPropertyChanged(nameof(CashReceiptAccount2Amount));
        OnPropertyChanged(nameof(CashReceiptAccount3));
        OnPropertyChanged(nameof(CashReceiptAccount3Text));
        OnPropertyChanged(nameof(CashReceiptAccount3Amount));
        OnPropertyChanged(nameof(CashReceiptAccount4));
        OnPropertyChanged(nameof(CashReceiptAccount4Text));
        OnPropertyChanged(nameof(CashReceiptAccount4Amount));
        OnPropertyChanged(nameof(CashReceiptAccount5));
        OnPropertyChanged(nameof(CashReceiptAccount5Text));
        OnPropertyChanged(nameof(CashReceiptAccount5Amount));
        OnPropertyChanged(nameof(CashReceiptAccount6));
        OnPropertyChanged(nameof(CashReceiptAccount6Text));
        OnPropertyChanged(nameof(CashReceiptAccount6Amount));
        OnPropertyChanged(nameof(SelectedCashReceiptRoundingMode));
        OnPropertyChanged(nameof(SelectedSupplier));
        OnPropertyChanged(nameof(SelectedCustomer));
        RaiseSupplierProperties();
        RaiseCustomerProperties();
        RefreshTotals();
        RefreshQrPreview();
    }

    private static readonly HashSet<string> QrRelevantProperties =
    [
        nameof(Currency),
        nameof(PaymentMethod),
        nameof(SelectedPaymentMethod),
        nameof(AlreadyPaidAmount),
        nameof(DiscountPercent),
        nameof(ShowQrCode),
        nameof(SelectedQrPaymentFormat),
        nameof(VariableSymbol),
        nameof(ConstantSymbol),
        nameof(Number),
        nameof(DueDate),
        nameof(SupplierName),
        nameof(SupplierBankName),
        nameof(SupplierBankIban),
        nameof(SupplierLegacyAccountNumber),
        nameof(SupplierBankSwift)
    ];

    public void Cleanup()
    {
        Items.CollectionChanged -= OnItemsCollectionChanged;
        Tags.CollectionChanged -= OnTagsCollectionChanged;
        foreach (var item in Items)
            UnsubscribeLineItem(item);
    }

    private static string GetDocumentTypeLabel(InvoiceDocumentType type)
        => Res(type switch
        {
            InvoiceDocumentType.Invoice => "InvoicesTypeInvoice",
            InvoiceDocumentType.Quote => "InvoicesTypeQuote",
            InvoiceDocumentType.Order => "InvoicesTypeOrder",
            InvoiceDocumentType.DeliveryNote => "InvoicesTypeDeliveryNote",
            InvoiceDocumentType.CashReceiptIncome => "InvoicesTypeCashReceiptIncome",
            InvoiceDocumentType.CashReceiptExpense => "InvoicesTypeCashReceiptExpense",
            _ => "InvoicesTypeInvoice"
        });
}
