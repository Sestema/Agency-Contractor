using System.Collections.ObjectModel;
using System.Windows.Input;
using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Invoices.ViewModels;

public sealed class InvoicesDashboardViewModel : ViewModelBase
{
    private readonly InvoiceStorageService _storageService;
    private readonly Action<InvoiceModuleSection> _openSection;
    private readonly Action<InvoiceDocumentType> _createDocument;
    private ObservableCollection<InvoiceDocumentSummary> _recentDocuments = new();

    public InvoicesDashboardViewModel(
        InvoiceStorageService storageService,
        Action<InvoiceModuleSection> openSection,
        Action<InvoiceDocumentType> createDocument)
    {
        _storageService = storageService;
        _openSection = openSection;
        _createDocument = createDocument;

        OpenDocumentsCommand = new RelayCommand(_ => _openSection(InvoiceModuleSection.Documents));
        OpenCompaniesCommand = new RelayCommand(_ => _openSection(InvoiceModuleSection.Companies));
        OpenCustomersCommand = new RelayCommand(_ => _openSection(InvoiceModuleSection.Customers));
        OpenItemsCommand = new RelayCommand(_ => _openSection(InvoiceModuleSection.Items));
        OpenBankAccountsCommand = new RelayCommand(_ => _openSection(InvoiceModuleSection.BankAccounts));
        OpenTrashCommand = new RelayCommand(_ => _openSection(InvoiceModuleSection.Trash));
        OpenRecentDocumentCommand = new RelayCommand(OpenRecentDocument, static parameter => parameter is InvoiceDocumentSummary);
        CreateInvoiceCommand = new RelayCommand(_ => _createDocument(InvoiceDocumentType.Invoice));
        CreateCashReceiptCommand = new RelayCommand(_ => _createDocument(InvoiceDocumentType.CashReceiptIncome));

        Refresh();
    }

    public ICommand OpenDocumentsCommand { get; }
    public ICommand OpenCompaniesCommand { get; }
    public ICommand OpenCustomersCommand { get; }
    public ICommand OpenItemsCommand { get; }
    public ICommand OpenBankAccountsCommand { get; }
    public ICommand OpenTrashCommand { get; }
    public ICommand OpenRecentDocumentCommand { get; }
    public ICommand CreateInvoiceCommand { get; }
    public ICommand CreateCashReceiptCommand { get; }

    public int IssuedInvoicesCount { get; private set; }
    public int QuotesCount { get; private set; }
    public int DeliveryNotesCount { get; private set; }
    public int IncomeCashReceiptsCount { get; private set; }
    public int OrdersCount { get; private set; }
    public int DraftCount { get; private set; }
    public int TotalDocumentsCount { get; private set; }
    public string ModulePath => _storageService.ModulePath;
    public ObservableCollection<InvoiceDocumentSummary> RecentDocuments
    {
        get => _recentDocuments;
        private set => SetProperty(ref _recentDocuments, value);
    }

    public bool HasRecentDocuments => RecentDocuments.Count > 0;

    public void Refresh()
    {
        var summaries = _storageService.GetSummaries();
        TotalDocumentsCount = summaries.Count;
        IssuedInvoicesCount = summaries.Count(document =>
            document.Type == InvoiceDocumentType.Invoice &&
            document.Status == InvoiceDocumentStatus.Issued);
        QuotesCount = summaries.Count(document => document.Type == InvoiceDocumentType.Quote);
        DeliveryNotesCount = summaries.Count(document => document.Type == InvoiceDocumentType.DeliveryNote);
        IncomeCashReceiptsCount = summaries.Count(document => document.Type == InvoiceDocumentType.CashReceiptIncome);
        OrdersCount = summaries.Count(document => document.Type == InvoiceDocumentType.Order);
        DraftCount = summaries.Count(document => document.Status == InvoiceDocumentStatus.Draft);
        RecentDocuments = new ObservableCollection<InvoiceDocumentSummary>(
            summaries
                .OrderByDescending(document => document.UpdatedAtUtc)
                .Take(6));

        OnPropertyChanged(nameof(TotalDocumentsCount));
        OnPropertyChanged(nameof(IssuedInvoicesCount));
        OnPropertyChanged(nameof(QuotesCount));
        OnPropertyChanged(nameof(DeliveryNotesCount));
        OnPropertyChanged(nameof(IncomeCashReceiptsCount));
        OnPropertyChanged(nameof(OrdersCount));
        OnPropertyChanged(nameof(DraftCount));
        OnPropertyChanged(nameof(HasRecentDocuments));
    }

    private void OpenRecentDocument(object? parameter)
    {
        if (parameter is not InvoiceDocumentSummary summary)
            return;

        App.NavigationService?.NavigateTo(new InvoiceEditorViewModel(summary.Id, _storageService));
    }
}
