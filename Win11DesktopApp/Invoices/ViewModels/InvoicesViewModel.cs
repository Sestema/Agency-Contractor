using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Invoices.ViewModels;

public enum InvoiceModuleSection
{
    Dashboard = 0,
    Documents = 1,
    Companies = 2,
    Customers = 3,
    Items = 4,
    BankAccounts = 5,
    Trash = 6
}

public sealed class InvoicesViewModel : ViewModelBase
{
    private readonly NavigationService _navigationService;
    private readonly InvoiceStorageService _storageService;
    private readonly InvoiceViewModelFactory _invoiceViewModelFactory;
    private InvoiceModuleSection _selectedSection;

    public InvoicesViewModel(
        InvoiceStorageService storageService,
        NavigationService navigationService,
        InvoiceViewModelFactory invoiceViewModelFactory,
        InvoiceModuleSection initialSection = InvoiceModuleSection.Dashboard)
    {
        _storageService = storageService ?? throw new InvalidOperationException("InvoiceStorageService is not initialized.");
        _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
        _invoiceViewModelFactory = invoiceViewModelFactory ?? throw new InvalidOperationException("InvoiceViewModelFactory is not initialized.");

        GoBackCommand = new RelayCommand(_ => _navigationService.NavigateTo<MainViewModel>());
        OpenDashboardCommand = new RelayCommand(_ => OpenSection(InvoiceModuleSection.Dashboard));

        DashboardSection = new InvoicesDashboardViewModel(_storageService, OpenSection, OpenEditor, OpenDocument);
        DocumentsSection = new InvoicesDocumentsViewModel(_storageService, _invoiceViewModelFactory.PdfRenderService, _navigationService, _invoiceViewModelFactory, RefreshSections);
        CompaniesSection = new InvoicesPlaceholderSectionViewModel("InvoicesSectionCompanies", "InvoicesPlaceholderCompanies", "InvoicesPlaceholderCompaniesHint");
        CustomersSection = new InvoicesPlaceholderSectionViewModel("InvoicesSectionCustomers", "InvoicesPlaceholderCustomers", "InvoicesPlaceholderCustomersHint");
        ItemsSection = new InvoicesPlaceholderSectionViewModel("InvoicesSectionItems", "InvoicesPlaceholderItems", "InvoicesPlaceholderItemsHint");
        BankAccountsSection = new InvoicesPlaceholderSectionViewModel("InvoicesSectionBankAccounts", "InvoicesPlaceholderBankAccounts", "InvoicesPlaceholderBankAccountsHint");
        TrashSection = new InvoicesTrashViewModel(_storageService, RefreshSections);

        OpenSection(initialSection);
    }

    public ICommand GoBackCommand { get; }
    public ICommand OpenDashboardCommand { get; }
    public string ModulePath => _storageService.ModulePath;
    public InvoiceModuleSection SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(CurrentSection));
                OnPropertyChanged(nameof(IsDashboardActive));
                OnPropertyChanged(nameof(ShowSectionShortcut));
                OnPropertyChanged(nameof(CurrentSectionTitle));
            }
        }
    }

    public InvoicesDashboardViewModel DashboardSection { get; }
    public InvoicesDocumentsViewModel DocumentsSection { get; }
    public InvoicesPlaceholderSectionViewModel CompaniesSection { get; }
    public InvoicesPlaceholderSectionViewModel CustomersSection { get; }
    public InvoicesPlaceholderSectionViewModel ItemsSection { get; }
    public InvoicesPlaceholderSectionViewModel BankAccountsSection { get; }
    public InvoicesTrashViewModel TrashSection { get; }
    public object CurrentSection => SelectedSection switch
    {
        InvoiceModuleSection.Dashboard => DashboardSection,
        InvoiceModuleSection.Documents => DocumentsSection,
        InvoiceModuleSection.Companies => CompaniesSection,
        InvoiceModuleSection.Customers => CustomersSection,
        InvoiceModuleSection.Items => ItemsSection,
        InvoiceModuleSection.BankAccounts => BankAccountsSection,
        InvoiceModuleSection.Trash => TrashSection,
        _ => DashboardSection
    };
    public bool IsDashboardActive => SelectedSection == InvoiceModuleSection.Dashboard;
    public bool ShowSectionShortcut => !IsDashboardActive;
    public string CurrentSectionTitle => SelectedSection switch
    {
        InvoiceModuleSection.Dashboard => Res("InvoicesSectionHome"),
        InvoiceModuleSection.Documents => Res("InvoicesSectionDocuments"),
        InvoiceModuleSection.Companies => Res("InvoicesSectionCompanies"),
        InvoiceModuleSection.Customers => Res("InvoicesSectionCustomers"),
        InvoiceModuleSection.Items => Res("InvoicesSectionItems"),
        InvoiceModuleSection.BankAccounts => Res("InvoicesSectionBankAccounts"),
        InvoiceModuleSection.Trash => Res("InvoicesSectionTrash"),
        _ => Res("InvoicesTitle")
    };

    private void OpenSection(InvoiceModuleSection section)
    {
        SelectedSection = section;
        switch (section)
        {
            case InvoiceModuleSection.Dashboard:
                DashboardSection.Refresh();
                break;
            case InvoiceModuleSection.Documents:
                DocumentsSection.Refresh();
                break;
            case InvoiceModuleSection.Trash:
                TrashSection.Refresh();
                break;
        }
    }

    private void OpenEditor(Models.InvoiceDocumentType type)
    {
        var document = _storageService.CreateDocument(type);
        OpenDocument(document.Id);
    }

    private void OpenDocument(string documentId)
    {
        _navigationService.NavigateTo(_invoiceViewModelFactory.CreateInvoiceEditor(documentId));
    }

    private void RefreshSections()
    {
        DashboardSection?.Refresh();
        DocumentsSection?.Refresh();
        TrashSection?.Refresh();
    }
}
