using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace Win11DesktopApp.Invoices.Models;

public enum InvoiceDocumentType
{
    Invoice,
    Quote,
    Order,
    DeliveryNote,
    CashReceiptIncome,
    CashReceiptExpense
}

public enum InvoiceDocumentStatus
{
    Draft,
    Issued,
    Paid,
    Overdue,
    Cancelled
}

public sealed class InvoiceParty
{
    public string Name { get; set; } = string.Empty;
    public string Ico { get; set; } = string.Empty;
    public string Dic { get; set; } = string.Empty;
    public string VatId { get; set; } = string.Empty;
    public bool IsVatPayer { get; set; }
    public bool ShowVatIdOnDocument { get; set; } = true;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "CZ";
    public string InfoNote { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Web { get; set; } = string.Empty;
    public string BankIban { get; set; } = string.Empty;
    public string BankSwift { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string LegacyAccountNumber { get; set; } = string.Empty;
    public string DeliveryName { get; set; } = string.Empty;
    public string DeliveryStreet { get; set; } = string.Empty;
    public string DeliveryCity { get; set; } = string.Empty;
    public string DeliveryPostalCode { get; set; } = string.Empty;
    public string DeliveryCountry { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayAddress
    {
        get
        {
            var parts = new[] { Street, PostalCode, City, Country }
                .Where(static part => !string.IsNullOrWhiteSpace(part));
            return string.Join(", ", parts);
        }
    }
}

public sealed class InvoiceLineItem : INotifyPropertyChanged
{
    private string _description = string.Empty;
    private decimal _quantity = 1m;
    private string _unit = "pcs";
    private decimal _unitPrice = 0m;
    private bool _isTextItem;

    public string Description
    {
        get => _description;
        set
        {
            if (SetField(ref _description, value))
                OnPropertyChanged(nameof(TotalAmount));
        }
    }

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (SetField(ref _quantity, value))
                OnPropertyChanged(nameof(TotalAmount));
        }
    }

    public string Unit
    {
        get => _unit;
        set => SetField(ref _unit, value);
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (SetField(ref _unitPrice, value))
                OnPropertyChanged(nameof(TotalAmount));
        }
    }

    public bool IsTextItem
    {
        get => _isTextItem;
        set => SetField(ref _isTextItem, value);
    }

    [JsonIgnore]
    public decimal TotalAmount => Math.Round(Quantity * UnitPrice, 2);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class InvoiceDocument
{
    public string Id { get; set; } = string.Empty;
    public InvoiceDocumentType Type { get; set; } = InvoiceDocumentType.Invoice;
    public string Number { get; set; } = string.Empty;
    public InvoiceDocumentStatus Status { get; set; } = InvoiceDocumentStatus.Draft;
    public string Language { get; set; } = "uk";
    public string Currency { get; set; } = "CZK";
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public DateTime DeliveryDate { get; set; } = DateTime.Today;
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(14);
    public string PaymentMethod { get; set; } = string.Empty;
    public string VariableSymbol { get; set; } = string.Empty;
    public string ConstantSymbol { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public decimal AlreadyPaidAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public string RoundingMode { get; set; } = "none";
    public bool ShowPaidStamp { get; set; }
    public bool ShowQrCode { get; set; } = true;
    public bool ShowPaidMessage { get; set; }
    public string QrPaymentFormat { get; set; } = "spayd";
    public string NotesAbove { get; set; } = string.Empty;
    public string NotesBelow { get; set; } = string.Empty;
    public string InternalNote { get; set; } = string.Empty;
    public string IssuedBy { get; set; } = string.Empty;
    public string SupplierCatalogId { get; set; } = string.Empty;
    public string CustomerCatalogId { get; set; } = string.Empty;
    public string SelectedTemplateId { get; set; } = "style1";
    public string SelectedTheme { get; set; } = "skyblue";
    public string LogoAssetName { get; set; } = string.Empty;
    public string StampAssetName { get; set; } = string.Empty;
    public bool CreateIncomeReceipt { get; set; }
    public bool CreateExpenseReceipt { get; set; }
    public bool CreateDeliveryNote { get; set; }
    public string LinkedIncomeReceiptId { get; set; } = string.Empty;
    public string LinkedIncomeReceiptNumber { get; set; } = string.Empty;
    public string LinkedIncomeReceiptPurpose { get; set; } = string.Empty;
    public string LinkedExpenseReceiptId { get; set; } = string.Empty;
    public string LinkedExpenseReceiptNumber { get; set; } = string.Empty;
    public string LinkedExpenseReceiptPurpose { get; set; } = string.Empty;
    public string CashReceiptDocumentVariant { get; set; } = "cashdesk";
    public string CashReceiptTitle { get; set; } = string.Empty;
    public DateTime CashReceiptPaymentDate { get; set; } = DateTime.Today;
    public string CashReceiptPlace { get; set; } = string.Empty;
    public string CashReceiptLedgerNumber { get; set; } = string.Empty;
    public decimal CashReceiptBaseAmount { get; set; }
    public decimal CashReceiptTaxRate { get; set; } = 23m;
    public decimal CashReceiptTotalWithVat { get; set; }
    public decimal CashReceiptNonTaxableAmount { get; set; }
    public decimal CashReceiptAmount { get; set; }
    public string CashReceiptAmountText { get; set; } = string.Empty;
    public string CashReceiptPurpose { get; set; } = string.Empty;
    public string CashReceiptPreparedBy { get; set; } = string.Empty;
    public string CashReceiptApprovedBy { get; set; } = string.Empty;
    public DateTime CashReceiptAccountingDate { get; set; } = DateTime.Today;
    public string CashReceiptAccount1 { get; set; } = string.Empty;
    public string CashReceiptAccount1Text { get; set; } = string.Empty;
    public decimal CashReceiptAccount1Amount { get; set; }
    public string CashReceiptAccount2 { get; set; } = string.Empty;
    public string CashReceiptAccount2Text { get; set; } = string.Empty;
    public decimal CashReceiptAccount2Amount { get; set; }
    public string CashReceiptAccount3 { get; set; } = string.Empty;
    public string CashReceiptAccount3Text { get; set; } = string.Empty;
    public decimal CashReceiptAccount3Amount { get; set; }
    public string CashReceiptAccount4 { get; set; } = string.Empty;
    public string CashReceiptAccount4Text { get; set; } = string.Empty;
    public decimal CashReceiptAccount4Amount { get; set; }
    public string CashReceiptAccount5 { get; set; } = string.Empty;
    public string CashReceiptAccount5Text { get; set; } = string.Empty;
    public decimal CashReceiptAccount5Amount { get; set; }
    public string CashReceiptAccount6 { get; set; } = string.Empty;
    public string CashReceiptAccount6Text { get; set; } = string.Empty;
    public decimal CashReceiptAccount6Amount { get; set; }
    public InvoiceParty Supplier { get; set; } = new();
    public InvoiceParty Customer { get; set; } = new();
    public List<InvoiceLineItem> Items { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public decimal TotalAmount => IsCashReceiptDocument
        ? Math.Round(CashReceiptAmount, 2)
        : Math.Round(Items.Sum(static item => item.TotalAmount), 2);

    [JsonIgnore]
    public decimal DiscountAmount => IsCashReceiptDocument
        ? 0m
        : Math.Round(TotalAmount * (DiscountPercent / 100m), 2);

    [JsonIgnore]
    public decimal AmountToPay => IsCashReceiptDocument
        ? Math.Round(TotalAmount, 2)
        : Math.Max(0m, Math.Round(TotalAmount - DiscountAmount - AlreadyPaidAmount, 2));

    [JsonIgnore]
    public bool IsCashReceiptDocument => Type is InvoiceDocumentType.CashReceiptIncome or InvoiceDocumentType.CashReceiptExpense;
}

public sealed class InvoiceDocumentSummary
{
    public string Id { get; set; } = string.Empty;
    public InvoiceDocumentType Type { get; set; }
    public InvoiceDocumentStatus Status { get; set; }
    public string Number { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Language { get; set; } = "uk";
    public string Currency { get; set; } = "CZK";
    public decimal TotalAmount { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string RelativeJsonPath { get; set; } = string.Empty;
    public string RelativePdfPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string TypeResourceKey => Type switch
    {
        InvoiceDocumentType.Invoice => "InvoicesTypeInvoice",
        InvoiceDocumentType.Quote => "InvoicesTypeQuote",
        InvoiceDocumentType.Order => "InvoicesTypeOrder",
        InvoiceDocumentType.DeliveryNote => "InvoicesTypeDeliveryNote",
        InvoiceDocumentType.CashReceiptIncome => "InvoicesTypeCashReceiptIncome",
        InvoiceDocumentType.CashReceiptExpense => "InvoicesTypeCashReceiptExpense",
        _ => Type.ToString()
    };

    [JsonIgnore]
    public string StatusResourceKey => Status switch
    {
        InvoiceDocumentStatus.Draft => "InvoicesStatusDraft",
        InvoiceDocumentStatus.Issued => "InvoicesStatusIssued",
        InvoiceDocumentStatus.Paid => "InvoicesStatusPaid",
        InvoiceDocumentStatus.Overdue => "InvoicesStatusOverdue",
        InvoiceDocumentStatus.Cancelled => "InvoicesStatusCancelled",
        _ => Status.ToString()
    };

    [JsonIgnore]
    public string TypeLabel => Application.Current?.TryFindResource(TypeResourceKey) as string ?? Type.ToString();

    [JsonIgnore]
    public string StatusLabel => Application.Current?.TryFindResource(StatusResourceKey) as string ?? Status.ToString();

    [JsonIgnore]
    public string TotalLabel => $"{TotalAmount:0.00} {Currency}";
}

public sealed class InvoiceModuleSettings
{
    public string Language { get; set; } = "cs";
    public string DefaultCurrency { get; set; } = "CZK";
    public string DefaultPaymentMethod { get; set; } = string.Empty;
    public string DefaultSupplierId { get; set; } = string.Empty;
    public string DefaultTemplateId { get; set; } = "style1";
    public string DefaultTheme { get; set; } = "skyblue";
}

public sealed class InvoiceNumberingSettings
{
    public List<InvoiceSequenceCounter> Sequences { get; set; } = new();
}

public sealed class InvoiceSequenceCounter
{
    public InvoiceDocumentType Type { get; set; }
    public int Year { get; set; }
    public string FirmKey { get; set; } = string.Empty;
    public int LastValue { get; set; }
}

public sealed class InvoiceTrashEntry
{
    public string Id { get; set; } = string.Empty;
    public string OriginalJsonRelativePath { get; set; } = string.Empty;
    public string OriginalPdfRelativePath { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; }
}

public sealed class InvoiceTrashSummary
{
    public string Id { get; set; } = string.Empty;
    public InvoiceDocumentType Type { get; set; }
    public InvoiceDocumentStatus Status { get; set; }
    public string Number { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Currency { get; set; } = "CZK";
    public decimal TotalAmount { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime DeletedAtUtc { get; set; }
    public string OriginalJsonRelativePath { get; set; } = string.Empty;
    public string OriginalPdfRelativePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string TypeResourceKey => Type switch
    {
        InvoiceDocumentType.Invoice => "InvoicesTypeInvoice",
        InvoiceDocumentType.Quote => "InvoicesTypeQuote",
        InvoiceDocumentType.Order => "InvoicesTypeOrder",
        InvoiceDocumentType.DeliveryNote => "InvoicesTypeDeliveryNote",
        InvoiceDocumentType.CashReceiptIncome => "InvoicesTypeCashReceiptIncome",
        InvoiceDocumentType.CashReceiptExpense => "InvoicesTypeCashReceiptExpense",
        _ => Type.ToString()
    };

    [JsonIgnore]
    public string StatusResourceKey => Status switch
    {
        InvoiceDocumentStatus.Draft => "InvoicesStatusDraft",
        InvoiceDocumentStatus.Issued => "InvoicesStatusIssued",
        InvoiceDocumentStatus.Paid => "InvoicesStatusPaid",
        InvoiceDocumentStatus.Overdue => "InvoicesStatusOverdue",
        InvoiceDocumentStatus.Cancelled => "InvoicesStatusCancelled",
        _ => Status.ToString()
    };

    [JsonIgnore]
    public string TypeLabel => Application.Current?.TryFindResource(TypeResourceKey) as string ?? Type.ToString();

    [JsonIgnore]
    public string StatusLabel => Application.Current?.TryFindResource(StatusResourceKey) as string ?? Status.ToString();

    [JsonIgnore]
    public string TotalLabel => $"{TotalAmount:0.00} {Currency}";
}

public sealed class InvoiceCatalogStore
{
    public ObservableCollection<InvoiceParty> Companies { get; set; } = new();
    public ObservableCollection<InvoiceParty> Customers { get; set; } = new();
}

public sealed class InvoiceCatalogItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = "ks";
    public decimal UnitPrice { get; set; }
}
