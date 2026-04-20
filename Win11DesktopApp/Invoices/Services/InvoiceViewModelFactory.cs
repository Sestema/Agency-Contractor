using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Invoices.ViewModels;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Invoices.Services;

public sealed class InvoiceViewModelFactory
{
    private readonly InvoiceStorageService _storageService;
    private readonly AresLookupService _aresLookupService;
    private readonly InvoiceQrPaymentService _qrPaymentService;
    private readonly InvoicePdfRenderService _pdfRenderService;
    private readonly NavigationService _navigationService;

    public InvoiceViewModelFactory(
        InvoiceStorageService storageService,
        AresLookupService aresLookupService,
        InvoiceQrPaymentService qrPaymentService,
        InvoicePdfRenderService pdfRenderService,
        NavigationService navigationService)
    {
        _storageService = storageService;
        _aresLookupService = aresLookupService;
        _qrPaymentService = qrPaymentService;
        _pdfRenderService = pdfRenderService;
        _navigationService = navigationService;
    }

    internal InvoicePdfRenderService PdfRenderService => _pdfRenderService;

    public InvoicesViewModel CreateInvoices(InvoiceModuleSection initialSection = InvoiceModuleSection.Dashboard)
    {
        return new InvoicesViewModel(_storageService, _navigationService, this, initialSection);
    }

    public InvoiceEditorViewModel CreateInvoiceEditor(string documentId)
    {
        return new InvoiceEditorViewModel(documentId, _storageService, _aresLookupService, _qrPaymentService, _pdfRenderService, _navigationService, this);
    }

    public InvoiceEditorViewModel CreateInvoiceEditor(InvoiceDocumentSummary summary)
    {
        return CreateInvoiceEditor(summary.Id);
    }
}
