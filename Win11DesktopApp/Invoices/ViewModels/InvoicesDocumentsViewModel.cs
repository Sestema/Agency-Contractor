using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Invoices.Views;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Invoices.ViewModels;

public sealed class InvoiceDocumentListTabOption
{
    public InvoiceDocumentType? Type { get; init; }
    public string ResourceKey { get; init; } = string.Empty;
    public string Label => Application.Current?.TryFindResource(ResourceKey) as string ?? ResourceKey;
}

public sealed class InvoiceStatusFilterOption
{
    public InvoiceDocumentStatus? Status { get; init; }
    public string ResourceKey { get; init; } = string.Empty;
    public string Label => Application.Current?.TryFindResource(ResourceKey) as string ?? ResourceKey;
}

public sealed class InvoiceTextFilterOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class InvoiceYearFilterOption
{
    public int? Year { get; init; }
    public string Label { get; init; } = string.Empty;
}

public sealed class InvoicesDocumentsViewModel : ViewModelBase
{
    private readonly InvoicePdfRenderService _pdfRenderService;
    private readonly InvoiceStorageService _storageService;
    private readonly Action? _onDocumentsChanged;
    private List<InvoiceDocumentSummary> _allDocuments = new();
    private ObservableCollection<InvoiceDocumentSummary> _documents = new();
    private string _searchQuery = string.Empty;
    private InvoiceDocumentListTabOption? _selectedDocumentTab;
    private InvoiceStatusFilterOption? _selectedStatusFilter;
    private InvoiceTextFilterOption? _selectedSupplierFilter;
    private InvoiceTextFilterOption? _selectedCustomerFilter;
    private InvoiceTextFilterOption? _selectedCurrencyFilter;
    private InvoiceYearFilterOption? _selectedYearFilter;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;

    public InvoicesDocumentsViewModel(InvoiceStorageService storageService, Action? onDocumentsChanged = null)
    {
        _storageService = storageService;
        _onDocumentsChanged = onDocumentsChanged;
        _pdfRenderService = App.InvoicePdfRenderService;

        DocumentTabs = new ObservableCollection<InvoiceDocumentListTabOption>
        {
            new() { ResourceKey = "InvoicesListTabAll" },
            new() { Type = InvoiceDocumentType.Invoice, ResourceKey = "InvoicesTypeInvoice" },
            new() { Type = InvoiceDocumentType.Quote, ResourceKey = "InvoicesTypeQuote" },
            new() { Type = InvoiceDocumentType.Order, ResourceKey = "InvoicesTypeOrder" },
            new() { Type = InvoiceDocumentType.CashReceiptIncome, ResourceKey = "InvoicesTypeCashReceiptIncome" },
            new() { Type = InvoiceDocumentType.CashReceiptExpense, ResourceKey = "InvoicesTypeCashReceiptExpense" }
        };

        StatusFilters = new ObservableCollection<InvoiceStatusFilterOption>
        {
            new() { ResourceKey = "InvoicesFilterStatusAll" },
            new() { Status = InvoiceDocumentStatus.Draft, ResourceKey = "InvoicesStatusDraft" },
            new() { Status = InvoiceDocumentStatus.Issued, ResourceKey = "InvoicesStatusIssued" },
            new() { Status = InvoiceDocumentStatus.Paid, ResourceKey = "InvoicesStatusPaid" },
            new() { Status = InvoiceDocumentStatus.Overdue, ResourceKey = "InvoicesStatusOverdue" },
            new() { Status = InvoiceDocumentStatus.Cancelled, ResourceKey = "InvoicesStatusCancelled" }
        };

        SupplierFilters = new ObservableCollection<InvoiceTextFilterOption>();
        CustomerFilters = new ObservableCollection<InvoiceTextFilterOption>();
        CurrencyFilters = new ObservableCollection<InvoiceTextFilterOption>();
        YearFilters = new ObservableCollection<InvoiceYearFilterOption>();

        _selectedDocumentTab = DocumentTabs[0];
        _selectedStatusFilter = StatusFilters[0];

        RefreshCommand = new RelayCommand(_ => LoadDocuments());
        ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
        CreateDocumentCommand = new RelayCommand(CreateDocument);
        OpenDocumentCommand = new RelayCommand(OpenDocument, static parameter => parameter is InvoiceDocumentSummary);
        PreviewDocumentCommand = new RelayCommand(PreviewDocument, static parameter => parameter is InvoiceDocumentSummary);
        DuplicateDocumentCommand = new RelayCommand(DuplicateDocument, static parameter => parameter is InvoiceDocumentSummary);
        MoveToTrashCommand = new RelayCommand(MoveToTrash, static parameter => parameter is InvoiceDocumentSummary);
        DeleteForeverCommand = new RelayCommand(DeleteForever, static parameter => parameter is InvoiceDocumentSummary);

        LoadDocuments();
    }

    public ICommand RefreshCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand CreateDocumentCommand { get; }
    public ICommand OpenDocumentCommand { get; }
    public ICommand PreviewDocumentCommand { get; }
    public ICommand DuplicateDocumentCommand { get; }
    public ICommand MoveToTrashCommand { get; }
    public ICommand DeleteForeverCommand { get; }

    public ObservableCollection<InvoiceDocumentListTabOption> DocumentTabs { get; }
    public ObservableCollection<InvoiceStatusFilterOption> StatusFilters { get; }
    public ObservableCollection<InvoiceTextFilterOption> SupplierFilters { get; }
    public ObservableCollection<InvoiceTextFilterOption> CustomerFilters { get; }
    public ObservableCollection<InvoiceTextFilterOption> CurrencyFilters { get; }
    public ObservableCollection<InvoiceYearFilterOption> YearFilters { get; }

    public ObservableCollection<InvoiceDocumentSummary> Documents
    {
        get => _documents;
        set => SetProperty(ref _documents, value);
    }

    public InvoiceDocumentListTabOption? SelectedDocumentTab
    {
        get => _selectedDocumentTab;
        set
        {
            if (SetProperty(ref _selectedDocumentTab, value))
                ApplyFilter();
        }
    }

    public InvoiceStatusFilterOption? SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
                ApplyFilter();
        }
    }

    public InvoiceTextFilterOption? SelectedSupplierFilter
    {
        get => _selectedSupplierFilter;
        set
        {
            if (SetProperty(ref _selectedSupplierFilter, value))
                ApplyFilter();
        }
    }

    public InvoiceTextFilterOption? SelectedCustomerFilter
    {
        get => _selectedCustomerFilter;
        set
        {
            if (SetProperty(ref _selectedCustomerFilter, value))
                ApplyFilter();
        }
    }

    public InvoiceTextFilterOption? SelectedCurrencyFilter
    {
        get => _selectedCurrencyFilter;
        set
        {
            if (SetProperty(ref _selectedCurrencyFilter, value))
                ApplyFilter();
        }
    }

    public InvoiceYearFilterOption? SelectedYearFilter
    {
        get => _selectedYearFilter;
        set
        {
            if (SetProperty(ref _selectedYearFilter, value))
                ApplyFilter();
        }
    }

    public DateTime? DateFrom
    {
        get => _dateFrom;
        set
        {
            if (SetProperty(ref _dateFrom, value))
                ApplyFilter();
        }
    }

    public DateTime? DateTo
    {
        get => _dateTo;
        set
        {
            if (SetProperty(ref _dateTo, value))
                ApplyFilter();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                ApplyFilter();
        }
    }

    public int TotalCount => _allDocuments.Count;
    public int DraftCount => _allDocuments.Count(static document => document.Status == InvoiceDocumentStatus.Draft);
    public int OverdueCount => _allDocuments.Count(document =>
        document.Status != InvoiceDocumentStatus.Paid &&
        document.Status != InvoiceDocumentStatus.Cancelled &&
        document.DueDate.Date < DateTime.Today);
    public int FilteredCount => Documents.Count;
    public bool HasDocuments => Documents.Count > 0;

    public void Refresh()
        => LoadDocuments();

    private void LoadDocuments()
    {
        _allDocuments = _storageService.GetSummaries()
            .Select(NormalizeSummary)
            .OrderByDescending(static document => document.IssueDate)
            .ThenByDescending(static document => document.UpdatedAtUtc)
            .ToList();

        RebuildFilterCollections();
        ApplyFilter();

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(DraftCount));
        OnPropertyChanged(nameof(OverdueCount));
    }

    private InvoiceDocumentSummary NormalizeSummary(InvoiceDocumentSummary summary)
    {
        if (!string.IsNullOrWhiteSpace(summary.SupplierName))
            return summary;

        var document = _storageService.LoadDocument(summary.Id);
        if (document == null)
            return summary;

        summary.SupplierName = document.Supplier.Name;
        if (string.IsNullOrWhiteSpace(summary.CustomerName))
            summary.CustomerName = document.Customer.Name;
        _storageService.RepairSummaryParties(summary.Id, summary.SupplierName, summary.CustomerName);
        return summary;
    }

    private void RebuildFilterCollections()
    {
        RebuildTextFilters(
            SupplierFilters,
            _allDocuments.Select(static document => document.SupplierName),
            "InvoicesFilterSupplierAll",
            ref _selectedSupplierFilter);

        RebuildTextFilters(
            CustomerFilters,
            _allDocuments.Select(static document => document.CustomerName),
            "InvoicesFilterCustomerAll",
            ref _selectedCustomerFilter);

        RebuildTextFilters(
            CurrencyFilters,
            _allDocuments.Select(static document => document.Currency),
            "InvoicesFilterCurrencyAll",
            ref _selectedCurrencyFilter);

        var previousYear = _selectedYearFilter?.Year;
        YearFilters.Clear();
        YearFilters.Add(new InvoiceYearFilterOption { Label = Res("InvoicesFilterYearAll") });
        foreach (var year in _allDocuments.Select(static document => document.IssueDate.Year).Distinct().OrderByDescending(static year => year))
            YearFilters.Add(new InvoiceYearFilterOption { Year = year, Label = year.ToString() });
        _selectedYearFilter = YearFilters.FirstOrDefault(option => option.Year == previousYear) ?? YearFilters.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedYearFilter));
    }

    private void RebuildTextFilters(
        ObservableCollection<InvoiceTextFilterOption> target,
        IEnumerable<string> values,
        string allResourceKey,
        ref InvoiceTextFilterOption? selected)
    {
        var previous = selected?.Value ?? string.Empty;
        target.Clear();
        target.Add(new InvoiceTextFilterOption { Label = Res(allResourceKey) });
        foreach (var value in values.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.CurrentCultureIgnoreCase))
            target.Add(new InvoiceTextFilterOption { Value = value, Label = value });
        selected = target.FirstOrDefault(option => string.Equals(option.Value, previous, StringComparison.OrdinalIgnoreCase)) ?? target.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        IEnumerable<InvoiceDocumentSummary> filtered = _allDocuments;

        if (SelectedDocumentTab?.Type != null)
            filtered = filtered.Where(document => document.Type == SelectedDocumentTab.Type.Value);

        if (SelectedStatusFilter?.Status != null)
            filtered = filtered.Where(document => document.Status == SelectedStatusFilter.Status.Value);

        if (!string.IsNullOrWhiteSpace(SelectedSupplierFilter?.Value))
            filtered = filtered.Where(document => string.Equals(document.SupplierName, SelectedSupplierFilter.Value, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SelectedCustomerFilter?.Value))
            filtered = filtered.Where(document => string.Equals(document.CustomerName, SelectedCustomerFilter.Value, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SelectedCurrencyFilter?.Value))
            filtered = filtered.Where(document => string.Equals(document.Currency, SelectedCurrencyFilter.Value, StringComparison.OrdinalIgnoreCase));

        if (SelectedYearFilter?.Year != null)
            filtered = filtered.Where(document => document.IssueDate.Year == SelectedYearFilter.Year.Value);

        if (DateFrom.HasValue)
            filtered = filtered.Where(document => document.IssueDate.Date >= DateFrom.Value.Date);

        if (DateTo.HasValue)
            filtered = filtered.Where(document => document.IssueDate.Date <= DateTo.Value.Date);

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(document =>
                document.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                document.SupplierName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                document.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                document.Currency.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Documents = new ObservableCollection<InvoiceDocumentSummary>(
            filtered.OrderByDescending(static document => document.IssueDate)
                .ThenByDescending(static document => document.UpdatedAtUtc));

        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(HasDocuments));
    }

    private void ClearFilters()
    {
        SearchQuery = string.Empty;
        SelectedDocumentTab = DocumentTabs.FirstOrDefault();
        SelectedStatusFilter = StatusFilters.FirstOrDefault();
        SelectedSupplierFilter = SupplierFilters.FirstOrDefault();
        SelectedCustomerFilter = CustomerFilters.FirstOrDefault();
        SelectedCurrencyFilter = CurrencyFilters.FirstOrDefault();
        SelectedYearFilter = YearFilters.FirstOrDefault();
        DateFrom = null;
        DateTo = null;
        ApplyFilter();
    }

    private void CreateDocument(object? parameter)
    {
        if (!PolicyService.EnsureWriteAllowed("Створити документ Faktury"))
            return;

        var type = parameter is InvoiceDocumentType documentType
            ? documentType
            : InvoiceDocumentType.Invoice;

        var document = _storageService.CreateDocument(type);
        App.NavigationService?.NavigateTo(new InvoiceEditorViewModel(document.Id, _storageService));
    }

    private void OpenDocument(object? parameter)
    {
        if (parameter is not InvoiceDocumentSummary summary)
            return;

        App.NavigationService?.NavigateTo(new InvoiceEditorViewModel(summary.Id, _storageService));
    }

    private void PreviewDocument(object? parameter)
    {
        if (parameter is not InvoiceDocumentSummary summary)
            return;

        if (!PolicyService.EnsureWriteAllowed("Сформувати PDF Faktury"))
            return;

        var document = _storageService.LoadDocument(summary.Id);
        if (document == null)
        {
            ToastService.Instance.Warning(Res("InvoicesLoadDocumentFailed"));
            return;
        }

        try
        {
            var pdfPath = _pdfRenderService.RenderPdf(document);
            var previewWindow = new InvoicePdfPreviewWindow(pdfPath);
            if (Application.Current?.MainWindow != null)
                previewWindow.Owner = Application.Current.MainWindow;
            previewWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            ErrorHandler.Report("InvoicesDocumentsViewModel.PreviewDocument", ex, ErrorSeverity.Error, showUser: false);
            ToastService.Instance.Error(Res("InvoicesPdfPreviewFailed"));
        }
    }

    private void DuplicateDocument(object? parameter)
    {
        if (parameter is not InvoiceDocumentSummary summary)
            return;

        if (!PolicyService.EnsureWriteAllowed("Скопіювати документ Faktury"))
            return;

        var clone = _storageService.DuplicateDocumentAsNew(summary.Id);
        if (clone == null)
        {
            ToastService.Instance.Warning(Res("InvoicesDuplicateFailed"));
            return;
        }

        ToastService.Instance.Success(string.Format(Res("InvoicesDuplicateSuccess"), clone.Number));
        _onDocumentsChanged?.Invoke();
        LoadDocuments();
        App.NavigationService?.NavigateTo(new InvoiceEditorViewModel(clone.Id, _storageService));
    }

    private void MoveToTrash(object? parameter)
    {
        if (parameter is not InvoiceDocumentSummary summary)
            return;

        if (!PolicyService.EnsureWriteAllowed("Перемістити документ Faktury в кошик"))
            return;

        var title = Res("InvoicesTitle");
        var message = string.Format(Res("InvoicesMoveToTrashConfirm"), summary.Number);
        if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (_storageService.MoveToTrash(summary.Id))
        {
            ToastService.Instance.Success(string.Format(Res("InvoicesMoveToTrashSuccess"), summary.Number));
            LoadDocuments();
            _onDocumentsChanged?.Invoke();
        }
        else
        {
            ToastService.Instance.Warning(Res("InvoicesMoveToTrashFailed"));
        }
    }

    private void DeleteForever(object? parameter)
    {
        if (parameter is not InvoiceDocumentSummary summary)
            return;

        if (!PolicyService.EnsureWriteAllowed("Видалити документ Faktury назавжди"))
            return;

        var title = Res("InvoicesTitle");
        var message = string.Format(Res("InvoicesDeleteForeverConfirm"), summary.Number);
        if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (_storageService.DeleteDocumentForever(summary.Id))
        {
            ToastService.Instance.Success(string.Format(Res("InvoicesDeleteForeverSuccess"), summary.Number));
            LoadDocuments();
            _onDocumentsChanged?.Invoke();
        }
        else
        {
            ToastService.Instance.Warning(Res("InvoicesDeleteForeverFailed"));
        }
    }
}
