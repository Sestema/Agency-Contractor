using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Invoices.Services;
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
    private readonly InvoiceStorageService _storageService;
    private InvoiceModuleSection _selectedSection;

    public InvoicesViewModel(InvoiceStorageService? storageService = null, InvoiceModuleSection initialSection = InvoiceModuleSection.Dashboard)
    {
        _storageService = storageService ?? App.InvoiceStorageService;

        GoBackCommand = new RelayCommand(_ => App.NavigationService?.NavigateTo(new MainViewModel()));
        OpenDashboardCommand = new RelayCommand(_ => OpenSection(InvoiceModuleSection.Dashboard));

        DashboardSection = new InvoicesDashboardViewModel(_storageService, OpenSection, OpenEditor);
        DocumentsSection = new InvoicesDocumentsViewModel(_storageService, RefreshSections);
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
        App.NavigationService?.NavigateTo(new InvoiceEditorViewModel(document.Id, _storageService));
    }

    private void RefreshSections()
    {
        DashboardSection?.Refresh();
        DocumentsSection?.Refresh();
        TrashSection?.Refresh();
    }
}
