using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Invoices.Services;

public sealed class InvoiceStorageService
{
    private const string ModuleFolderName = "Faktury";
    private const string DefaultModuleLanguage = "uk";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase) { "uk", "cs", "en", "ru" };
    private readonly Dictionary<string, InvoiceDocument> _pendingDocuments = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _basePath;
    private readonly string _dataPath;
    private readonly string _documentsPath;
    private readonly string _pdfPath;
    private readonly string _catalogsPath;
    private readonly string _templatesPath;
    private readonly string _trashPath;
    private readonly string _trashDocumentsPath;
    private readonly string _trashPdfPath;
    private readonly string _trashMetaPath;
    private readonly string _cachePath;
    private readonly string _cacheAresPath;
    private readonly string _tempPath;

    private readonly string _indexFilePath;
    private readonly string _settingsFilePath;
    private readonly string _numberingFilePath;
    private readonly string _trashIndexFilePath;
    private readonly string _companiesCatalogFilePath;
    private readonly string _customersCatalogFilePath;
    private readonly string _itemsCatalogFilePath;
    private readonly string _bankAccountsCatalogFilePath;
    private readonly string _tagsCatalogFilePath;
    private readonly AppSettingsService _appSettingsService;

    public InvoiceStorageService(FolderService folderService, AppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
        var rootPath = folderService.RootPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            rootPath = Path.Combine(appData, "AgencyContractor");
        }

        Directory.CreateDirectory(rootPath);

        _basePath = Path.Combine(rootPath, ModuleFolderName);
        _dataPath = Path.Combine(_basePath, "Data");
        _documentsPath = Path.Combine(_basePath, "Documents");
        _pdfPath = Path.Combine(_basePath, "Pdf");
        _catalogsPath = Path.Combine(_basePath, "Catalogs");
        _templatesPath = Path.Combine(_basePath, "Templates");
        _trashPath = Path.Combine(_basePath, "Trash");
        _trashDocumentsPath = Path.Combine(_trashPath, "Documents");
        _trashPdfPath = Path.Combine(_trashPath, "Pdf");
        _trashMetaPath = Path.Combine(_trashPath, "Meta");
        _cachePath = Path.Combine(_basePath, "Cache");
        _cacheAresPath = Path.Combine(_cachePath, "Ares");
        _tempPath = Path.Combine(_basePath, "Temp");

        _indexFilePath = Path.Combine(_dataPath, "index.json");
        _settingsFilePath = Path.Combine(_dataPath, "module_settings.json");
        _numberingFilePath = Path.Combine(_dataPath, "numbering.json");
        _trashIndexFilePath = Path.Combine(_dataPath, "trash_index.json");
        _companiesCatalogFilePath = Path.Combine(_catalogsPath, "companies.json");
        _customersCatalogFilePath = Path.Combine(_catalogsPath, "customers.json");
        _itemsCatalogFilePath = Path.Combine(_catalogsPath, "items.json");
        _bankAccountsCatalogFilePath = Path.Combine(_catalogsPath, "bank_accounts.json");
        _tagsCatalogFilePath = Path.Combine(_catalogsPath, "tags.json");

        EnsureStructure();
    }

    public string ModulePath => _basePath;

    public InvoiceModuleSettings GetSettings()
    {
        var fallback = new InvoiceModuleSettings
        {
            Language = NormalizeLanguage(_appSettingsService.Settings.LanguageCode)
        };
        var settings = SafeFileService.ReadJsonOrDefault(_settingsFilePath, fallback, JsonOptions);
        settings.Language = NormalizeLanguage(settings.Language);
        return settings;
    }

    public IReadOnlyList<InvoiceDocumentSummary> GetSummaries()
    {
        var entries = SafeFileService.ReadJsonOrDefault(_indexFilePath, new List<InvoiceDocumentSummary>(), JsonOptions);
        return entries
            .OrderByDescending(static entry => entry.UpdatedAtUtc)
            .ThenByDescending(static entry => entry.IssueDate)
            .ToList();
    }

    public IReadOnlyList<InvoiceParty> GetCompanies()
        => SafeFileService.ReadJsonOrDefault(_companiesCatalogFilePath, new List<InvoiceParty>(), JsonOptions);

    public IReadOnlyList<InvoiceParty> GetCustomers()
        => SafeFileService.ReadJsonOrDefault(_customersCatalogFilePath, new List<InvoiceParty>(), JsonOptions);

    public IReadOnlyList<InvoiceCatalogItem> GetItems()
        => SafeFileService.ReadJsonOrDefault(_itemsCatalogFilePath, new List<InvoiceCatalogItem>(), JsonOptions);

    public IReadOnlyList<string> GetTags()
        => SafeFileService.ReadJsonOrDefault(_tagsCatalogFilePath, new List<string>(), JsonOptions);

    public T? ReadAresCache<T>(string ico)
    {
        var normalizedIco = NormalizeIco(ico);
        if (string.IsNullOrWhiteSpace(normalizedIco))
            return default;

        var path = GetAresCacheFilePath(normalizedIco);
        if (!File.Exists(path))
            return default;

        return SafeFileService.ReadJsonOrDefault<T?>(path, default, JsonOptions);
    }

    public void WriteAresCache<T>(string ico, T data)
    {
        var normalizedIco = NormalizeIco(ico);
        if (string.IsNullOrWhiteSpace(normalizedIco))
            return;

        var path = GetAresCacheFilePath(normalizedIco);
        SafeFileService.WriteJsonAtomic(path, data, JsonOptions);
    }

    public InvoiceDocument CreateDocument(InvoiceDocumentType type)
    {
        var settings = GetSettings();
        var issueDate = DateTime.Today;
        var document = new InvoiceDocument
        {
            Id = $"{GetTypeFolderName(type).ToLowerInvariant()}-{Guid.NewGuid():N}",
            Type = type,
            Number = GetNextDocumentNumber(type, issueDate.Year),
            Language = NormalizeLanguage(settings.Language),
            Currency = settings.DefaultCurrency,
            PaymentMethod = settings.DefaultPaymentMethod,
            SelectedTemplateId = type is InvoiceDocumentType.CashReceiptIncome or InvoiceDocumentType.CashReceiptExpense
                ? "cash1"
                : string.IsNullOrWhiteSpace(settings.DefaultTemplateId) ? "style1" : settings.DefaultTemplateId,
            SelectedTheme = settings.DefaultTheme,
            IssueDate = issueDate,
            DeliveryDate = issueDate,
            DueDate = issueDate.AddDays(14),
            CashReceiptDocumentVariant = "cashdesk",
            CashReceiptTitle = InvoiceCashReceiptHelper.GetTitle(type, "cashdesk", resourceResolver: CreateLanguageResolver(settings.Language)),
            CashReceiptPaymentDate = issueDate,
            Items = type is InvoiceDocumentType.CashReceiptIncome or InvoiceDocumentType.CashReceiptExpense
                ? new List<InvoiceLineItem>()
                : new List<InvoiceLineItem> { new() }
        };

        _pendingDocuments[document.Id] = document;
        return document;
    }

    public InvoiceDocument? LoadDocument(string id)
    {
        if (_pendingDocuments.TryGetValue(id, out var pending))
        {
            NormalizeCashReceiptDocument(pending);
            return pending;
        }

        var summary = GetSummaries().FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (summary == null)
            return null;

        var path = Path.Combine(_basePath, summary.RelativeJsonPath);
        var document = SafeFileService.ReadJsonOrDefault<InvoiceDocument?>(path, null, JsonOptions);
        if (document != null)
            NormalizeCashReceiptDocument(document);
        return document;
    }

    public IReadOnlyList<InvoiceTrashSummary> GetTrashSummaries()
    {
        var entries = SafeFileService.ReadJsonOrDefault(_trashIndexFilePath, new List<InvoiceTrashEntry>(), JsonOptions);
        return entries
            .Select(BuildTrashSummary)
            .OrderByDescending(static entry => entry.DeletedAtUtc)
            .ThenByDescending(static entry => entry.UpdatedAtUtc)
            .ToList();
    }

    public InvoiceDocument? DuplicateDocumentAsNew(string id)
    {
        var source = LoadDocument(id);
        if (source == null)
            return null;

        var clone = JsonSerializer.Deserialize<InvoiceDocument>(
            JsonSerializer.Serialize(source, JsonOptions),
            JsonOptions);

        if (clone == null)
            return null;

        clone.Id = $"{GetTypeFolderName(source.Type).ToLowerInvariant()}-{Guid.NewGuid():N}";
        clone.Number = source.Type == InvoiceDocumentType.Invoice
            ? AssignNextInvoiceNumber(clone)
            : GetNextDocumentNumber(source.Type, source.IssueDate.Year);
        clone.Status = InvoiceDocumentStatus.Draft;
        clone.CreatedAtUtc = DateTime.UtcNow;
        clone.UpdatedAtUtc = clone.CreatedAtUtc;

        SaveDocument(clone);
        return clone;
    }

    public string GetPdfOutputPath(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureYearFolders(document.Type, document.IssueDate.Year);
        return Path.Combine(_basePath, GetRelativePdfPath(document));
    }

    public void SaveDocument(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EnsureYearFolders(document.Type, document.IssueDate.Year);
        _pendingDocuments.Remove(document.Id);
        document.Language = NormalizeLanguage(document.Language);
        document.UpdatedAtUtc = DateTime.UtcNow;
        if (document.CreatedAtUtc == default)
            document.CreatedAtUtc = document.UpdatedAtUtc;
        document.SelectedTheme = string.IsNullOrWhiteSpace(document.SelectedTheme) ? "skyblue" : document.SelectedTheme;
        document.SelectedTemplateId = string.IsNullOrWhiteSpace(document.SelectedTemplateId)
            ? document.Type is InvoiceDocumentType.CashReceiptIncome or InvoiceDocumentType.CashReceiptExpense ? "cash1" : "style1"
            : document.SelectedTemplateId;
        document.RoundingMode = string.IsNullOrWhiteSpace(document.RoundingMode) ? "none" : document.RoundingMode;
        NormalizeCashReceiptDocument(document);
        document.QrPaymentFormat = string.IsNullOrWhiteSpace(document.QrPaymentFormat) ? "spayd" : document.QrPaymentFormat;
        document.Tags = document.Tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SyncInvoiceSequence(document);
        SaveKnownParties(document);

        var jsonPath = GetDocumentJsonPath(document);
        SafeFileService.WriteJsonAtomic(jsonPath, document, JsonOptions);

        var summaries = GetSummaries().ToList();
        var relativeJsonPath = Path.GetRelativePath(_basePath, jsonPath);
        var relativePdfPath = GetRelativePdfPath(document);
        var summary = summaries.FirstOrDefault(entry => string.Equals(entry.Id, document.Id, StringComparison.OrdinalIgnoreCase));
        if (summary == null)
        {
            summary = new InvoiceDocumentSummary { Id = document.Id };
            summaries.Add(summary);
        }

        summary.Type = document.Type;
        summary.Status = document.Status;
        summary.Number = document.Number;
        summary.SupplierName = document.Supplier.Name;
        summary.CustomerName = document.Customer.Name;
        summary.Language = document.Language;
        summary.Currency = document.Currency;
        summary.TotalAmount = document.TotalAmount;
        summary.IssueDate = document.IssueDate;
        summary.DueDate = document.DueDate;
        summary.UpdatedAtUtc = document.UpdatedAtUtc;
        summary.RelativeJsonPath = relativeJsonPath;
        summary.RelativePdfPath = relativePdfPath;

        SafeFileService.WriteJsonAtomic(_indexFilePath, summaries.OrderByDescending(static entry => entry.UpdatedAtUtc).ToList(), JsonOptions);
        SaveKnownTags(document.Tags);
    }

    public void RepairSummaryParties(string id, string supplierName, string customerName)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        var summaries = GetSummaries().ToList();
        var summary = summaries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (summary == null)
            return;

        var changed = false;
        if (string.IsNullOrWhiteSpace(summary.SupplierName) && !string.IsNullOrWhiteSpace(supplierName))
        {
            summary.SupplierName = supplierName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(summary.CustomerName) && !string.IsNullOrWhiteSpace(customerName))
        {
            summary.CustomerName = customerName;
            changed = true;
        }

        if (changed)
            SafeFileService.WriteJsonAtomic(_indexFilePath, summaries.OrderByDescending(static entry => entry.UpdatedAtUtc).ToList(), JsonOptions);
    }

    public string SuggestDocumentNumber(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Type == InvoiceDocumentType.Invoice
            ? GetNextInvoiceNumber(document, persistSequence: false)
            : GetNextDocumentNumber(document.Type, document.IssueDate.Year);
    }

    public string AssignNextInvoiceNumber(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Type == InvoiceDocumentType.Invoice
            ? GetNextInvoiceNumber(document, persistSequence: true)
            : GetNextDocumentNumber(document.Type, document.IssueDate.Year);
    }

    public InvoiceDocumentSummary? FindDuplicateInvoiceNumber(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Type != InvoiceDocumentType.Invoice)
            return null;

        var normalizedNumber = NormalizeDocumentNumber(document.Number);
        if (string.IsNullOrWhiteSpace(normalizedNumber))
            return null;

        var currentFirmKey = GetFirmKey(document);
        var candidates = GetSummaries()
            .Where(summary =>
                summary.Type == InvoiceDocumentType.Invoice &&
                !string.Equals(summary.Id, document.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeDocumentNumber(summary.Number), normalizedNumber, StringComparison.OrdinalIgnoreCase));

        foreach (var candidate in candidates)
        {
            var existing = LoadDocument(candidate.Id);
            if (existing == null)
                continue;

            if (string.Equals(GetFirmKey(existing), currentFirmKey, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    public void SaveModuleLanguage(string languageCode)
    {
        var settings = GetSettings();
        settings.Language = NormalizeLanguage(languageCode);
        SafeFileService.WriteJsonAtomic(_settingsFilePath, settings, JsonOptions);
    }

    public bool IsPendingDocument(string id)
        => !string.IsNullOrWhiteSpace(id) && _pendingDocuments.ContainsKey(id);

    public void DiscardPendingDocument(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        _pendingDocuments.Remove(id);
    }

    public bool MoveToTrash(string id)
    {
        var summaries = GetSummaries().ToList();
        var summary = summaries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (summary == null)
            return false;

        var sourceJsonPath = Path.Combine(_basePath, summary.RelativeJsonPath);
        var sourcePdfPath = string.IsNullOrWhiteSpace(summary.RelativePdfPath)
            ? string.Empty
            : Path.Combine(_basePath, summary.RelativePdfPath);

        var trashJsonPath = Path.Combine(_trashDocumentsPath, Path.GetFileName(sourceJsonPath));
        if (File.Exists(sourceJsonPath))
            SafeFileService.MoveFile(sourceJsonPath, trashJsonPath);

        var trashPdfPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(sourcePdfPath) && File.Exists(sourcePdfPath))
        {
            trashPdfPath = Path.Combine(_trashPdfPath, Path.GetFileName(sourcePdfPath));
            SafeFileService.MoveFile(sourcePdfPath, trashPdfPath);
        }

        var trashEntries = SafeFileService.ReadJsonOrDefault(_trashIndexFilePath, new List<InvoiceTrashEntry>(), JsonOptions);
        trashEntries.Add(new InvoiceTrashEntry
        {
            Id = summary.Id,
            OriginalJsonRelativePath = summary.RelativeJsonPath,
            OriginalPdfRelativePath = summary.RelativePdfPath,
            DeletedAtUtc = DateTime.UtcNow
        });
        SafeFileService.WriteJsonAtomic(_trashIndexFilePath, trashEntries, JsonOptions);

        summaries.Remove(summary);
        SafeFileService.WriteJsonAtomic(_indexFilePath, summaries, JsonOptions);

        if (!string.IsNullOrWhiteSpace(trashJsonPath))
        {
            var metaPath = Path.Combine(_trashMetaPath, $"{summary.Id}.json");
            SafeFileService.WriteJsonAtomic(metaPath, summary, JsonOptions);
        }

        return true;
    }

    public bool RestoreFromTrash(string id)
    {
        var trashEntries = SafeFileService.ReadJsonOrDefault(_trashIndexFilePath, new List<InvoiceTrashEntry>(), JsonOptions);
        var entry = trashEntries.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return false;

        var trashJsonPath = GetTrashJsonPath(entry);
        var trashPdfPath = GetTrashPdfPath(entry);
        var restoredJsonPath = Path.Combine(_basePath, entry.OriginalJsonRelativePath);
        var restoredPdfPath = string.IsNullOrWhiteSpace(entry.OriginalPdfRelativePath)
            ? string.Empty
            : Path.Combine(_basePath, entry.OriginalPdfRelativePath);

        if (File.Exists(trashJsonPath))
            SafeFileService.MoveFile(trashJsonPath, restoredJsonPath);

        if (!string.IsNullOrWhiteSpace(restoredPdfPath) && File.Exists(trashPdfPath))
            SafeFileService.MoveFile(trashPdfPath, restoredPdfPath);

        var meta = LoadTrashMeta(entry.Id);
        var restoredDocument = SafeFileService.ReadJsonOrDefault<InvoiceDocument?>(restoredJsonPath, null, JsonOptions);
        if (restoredDocument != null)
        {
            var summaries = GetSummaries().ToList();
            summaries.RemoveAll(summary => string.Equals(summary.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            summaries.Add(BuildSummaryFromDocument(restoredDocument, entry.OriginalJsonRelativePath, entry.OriginalPdfRelativePath, meta));
            SafeFileService.WriteJsonAtomic(_indexFilePath, summaries.OrderByDescending(static item => item.UpdatedAtUtc).ToList(), JsonOptions);
        }

        trashEntries.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        SafeFileService.WriteJsonAtomic(_trashIndexFilePath, trashEntries, JsonOptions);
        SafeFileService.DeleteFile(GetTrashMetaPath(id));
        return true;
    }

    public bool DeleteDocumentForever(string id)
    {
        var summaries = GetSummaries().ToList();
        var summary = summaries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        if (summary == null)
            return false;

        var jsonPath = Path.Combine(_basePath, summary.RelativeJsonPath);
        var pdfPath = string.IsNullOrWhiteSpace(summary.RelativePdfPath)
            ? string.Empty
            : Path.Combine(_basePath, summary.RelativePdfPath);

        SafeFileService.DeleteFile(jsonPath);
        if (!string.IsNullOrWhiteSpace(pdfPath))
            SafeFileService.DeleteFile(pdfPath);

        summaries.Remove(summary);
        SafeFileService.WriteJsonAtomic(_indexFilePath, summaries.OrderByDescending(static entry => entry.UpdatedAtUtc).ToList(), JsonOptions);
        return true;
    }

    public bool DeleteTrashEntryForever(string id)
    {
        var trashEntries = SafeFileService.ReadJsonOrDefault(_trashIndexFilePath, new List<InvoiceTrashEntry>(), JsonOptions);
        var entry = trashEntries.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return false;

        SafeFileService.DeleteFile(GetTrashJsonPath(entry));
        SafeFileService.DeleteFile(GetTrashPdfPath(entry));
        SafeFileService.DeleteFile(GetTrashMetaPath(id));

        trashEntries.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        SafeFileService.WriteJsonAtomic(_trashIndexFilePath, trashEntries, JsonOptions);
        return true;
    }

    private void EnsureStructure()
    {
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(_dataPath);
        Directory.CreateDirectory(_documentsPath);
        Directory.CreateDirectory(_pdfPath);
        Directory.CreateDirectory(_catalogsPath);
        Directory.CreateDirectory(_templatesPath);
        Directory.CreateDirectory(_trashPath);
        Directory.CreateDirectory(_trashDocumentsPath);
        Directory.CreateDirectory(_trashPdfPath);
        Directory.CreateDirectory(_trashMetaPath);
        Directory.CreateDirectory(_cachePath);
        Directory.CreateDirectory(_cacheAresPath);
        Directory.CreateDirectory(_tempPath);

        foreach (var folder in Enum.GetValues<InvoiceDocumentType>().Select(GetTypeFolderName))
        {
            Directory.CreateDirectory(Path.Combine(_documentsPath, folder));
            Directory.CreateDirectory(Path.Combine(_pdfPath, folder));
            Directory.CreateDirectory(Path.Combine(_templatesPath, folder));
        }

        Directory.CreateDirectory(Path.Combine(_templatesPath, "Assets"));
        Directory.CreateDirectory(Path.Combine(_templatesPath, "Assets", "logos"));
        Directory.CreateDirectory(Path.Combine(_templatesPath, "Assets", "stamps"));
        Directory.CreateDirectory(Path.Combine(_templatesPath, "Assets", "signatures"));

        EnsureJsonFile(_indexFilePath, new List<InvoiceDocumentSummary>());
        EnsureJsonFile(_settingsFilePath, new InvoiceModuleSettings
        {
            Language = DefaultModuleLanguage
        });
        EnsureJsonFile(_numberingFilePath, new InvoiceNumberingSettings());
        EnsureJsonFile(_trashIndexFilePath, new List<InvoiceTrashEntry>());
        EnsureJsonFile(_companiesCatalogFilePath, new List<InvoiceParty>());
        EnsureJsonFile(_customersCatalogFilePath, new List<InvoiceParty>());
        EnsureJsonFile(_itemsCatalogFilePath, new List<InvoiceCatalogItem>());
        EnsureJsonFile(_bankAccountsCatalogFilePath, new List<object>());
        EnsureJsonFile(_tagsCatalogFilePath, new List<string>());
    }

    private void EnsureYearFolders(InvoiceDocumentType type, int year)
    {
        Directory.CreateDirectory(Path.Combine(_documentsPath, GetTypeFolderName(type), year.ToString()));
        Directory.CreateDirectory(Path.Combine(_pdfPath, GetTypeFolderName(type), year.ToString()));
    }

    private string GetDocumentJsonPath(InvoiceDocument document)
    {
        return Path.Combine(_documentsPath, GetTypeFolderName(document.Type), document.IssueDate.Year.ToString(), $"{document.Id}.json");
    }

    private string GetRelativePdfPath(InvoiceDocument document)
    {
        return Path.Combine("Pdf", GetTypeFolderName(document.Type), document.IssueDate.Year.ToString(), $"{document.Id}.pdf");
    }

    private string GetNextDocumentNumber(InvoiceDocumentType type, int year)
    {
        if (type == InvoiceDocumentType.Invoice)
            return $"{year}001";

        var numbering = SafeFileService.ReadJsonOrDefault(_numberingFilePath, new InvoiceNumberingSettings(), JsonOptions);
        var sequence = numbering.Sequences.FirstOrDefault(entry =>
            entry.Type == type &&
            entry.Year == year &&
            string.IsNullOrWhiteSpace(entry.FirmKey));
        if (sequence == null)
        {
            sequence = new InvoiceSequenceCounter { Type = type, Year = year, FirmKey = string.Empty, LastValue = 0 };
            numbering.Sequences.Add(sequence);
        }

        sequence.LastValue++;
        SafeFileService.WriteJsonAtomic(_numberingFilePath, numbering, JsonOptions);

        var prefix = type switch
        {
            InvoiceDocumentType.Invoice => "INV",
            InvoiceDocumentType.Quote => "QTE",
            InvoiceDocumentType.Order => "ORD",
            InvoiceDocumentType.DeliveryNote => "DLV",
            InvoiceDocumentType.CashReceiptIncome => "CRI",
            InvoiceDocumentType.CashReceiptExpense => "CRE",
            _ => "DOC"
        };

        return $"{year}-{prefix}-{sequence.LastValue:0001}";
    }

    private string GetNextInvoiceNumber(InvoiceDocument document, bool persistSequence)
    {
        var year = document.IssueDate.Year;
        var firmKey = GetFirmKey(document);
        var numbering = SafeFileService.ReadJsonOrDefault(_numberingFilePath, new InvoiceNumberingSettings(), JsonOptions);
        var sequence = numbering.Sequences.FirstOrDefault(entry =>
            entry.Type == InvoiceDocumentType.Invoice &&
            entry.Year == year &&
            string.Equals(entry.FirmKey ?? string.Empty, firmKey, StringComparison.OrdinalIgnoreCase));

        if (sequence == null)
        {
            sequence = new InvoiceSequenceCounter
            {
                Type = InvoiceDocumentType.Invoice,
                Year = year,
                FirmKey = firmKey,
                LastValue = 0
            };
            numbering.Sequences.Add(sequence);
        }

        var nextValue = sequence.LastValue + 1;
        if (persistSequence)
        {
            sequence.LastValue = nextValue;
            SafeFileService.WriteJsonAtomic(_numberingFilePath, numbering, JsonOptions);
        }

        return $"{year}{nextValue:000}";
    }

    private void SyncInvoiceSequence(InvoiceDocument document)
    {
        if (document.Type != InvoiceDocumentType.Invoice)
            return;

        if (!TryParseInvoiceSequence(document.Number, document.IssueDate.Year, out var sequenceValue))
            return;

        var numbering = SafeFileService.ReadJsonOrDefault(_numberingFilePath, new InvoiceNumberingSettings(), JsonOptions);
        var firmKey = GetFirmKey(document);
        var sequence = numbering.Sequences.FirstOrDefault(entry =>
            entry.Type == InvoiceDocumentType.Invoice &&
            entry.Year == document.IssueDate.Year &&
            string.Equals(entry.FirmKey ?? string.Empty, firmKey, StringComparison.OrdinalIgnoreCase));

        if (sequence == null)
        {
            sequence = new InvoiceSequenceCounter
            {
                Type = InvoiceDocumentType.Invoice,
                Year = document.IssueDate.Year,
                FirmKey = firmKey,
                LastValue = sequenceValue
            };
            numbering.Sequences.Add(sequence);
            SafeFileService.WriteJsonAtomic(_numberingFilePath, numbering, JsonOptions);
            return;
        }

        if (sequenceValue <= sequence.LastValue)
            return;

        sequence.LastValue = sequenceValue;
        SafeFileService.WriteJsonAtomic(_numberingFilePath, numbering, JsonOptions);
    }

    private static bool TryParseInvoiceSequence(string? number, int year, out int sequenceValue)
    {
        sequenceValue = 0;
        var normalizedNumber = NormalizeDocumentNumber(number);
        var prefix = year.ToString();
        if (!normalizedNumber.StartsWith(prefix, StringComparison.Ordinal) || normalizedNumber.Length <= prefix.Length)
            return false;

        return int.TryParse(normalizedNumber[prefix.Length..], out sequenceValue) && sequenceValue > 0;
    }

    private static string GetTypeFolderName(InvoiceDocumentType type)
    {
        return type switch
        {
            InvoiceDocumentType.Invoice => "Invoices",
            InvoiceDocumentType.Quote => "Quotes",
            InvoiceDocumentType.Order => "Orders",
            InvoiceDocumentType.DeliveryNote => "DeliveryNotes",
            InvoiceDocumentType.CashReceiptIncome => "CashReceiptsIncome",
            InvoiceDocumentType.CashReceiptExpense => "CashReceiptsExpense",
            _ => "Documents"
        };
    }

    private static void EnsureJsonFile<T>(string path, T value)
    {
        if (!File.Exists(path))
            SafeFileService.WriteJsonAtomic(path, value, JsonOptions);
    }

    private InvoiceTrashSummary BuildTrashSummary(InvoiceTrashEntry entry)
    {
        var meta = LoadTrashMeta(entry.Id);
        if (meta != null)
        {
            return new InvoiceTrashSummary
            {
                Id = meta.Id,
                Type = meta.Type,
                Status = meta.Status,
                Number = meta.Number,
                SupplierName = meta.SupplierName,
                CustomerName = meta.CustomerName,
                Currency = meta.Currency,
                TotalAmount = meta.TotalAmount,
                IssueDate = meta.IssueDate,
                DueDate = meta.DueDate,
                UpdatedAtUtc = meta.UpdatedAtUtc,
                DeletedAtUtc = entry.DeletedAtUtc,
                OriginalJsonRelativePath = entry.OriginalJsonRelativePath,
                OriginalPdfRelativePath = entry.OriginalPdfRelativePath
            };
        }

        var trashedDocument = SafeFileService.ReadJsonOrDefault<InvoiceDocument?>(GetTrashJsonPath(entry), null, JsonOptions);
        if (trashedDocument != null)
        {
            var summary = BuildSummaryFromDocument(trashedDocument, entry.OriginalJsonRelativePath, entry.OriginalPdfRelativePath, null);
            return new InvoiceTrashSummary
            {
                Id = summary.Id,
                Type = summary.Type,
                Status = summary.Status,
                Number = summary.Number,
                SupplierName = summary.SupplierName,
                CustomerName = summary.CustomerName,
                Currency = summary.Currency,
                TotalAmount = summary.TotalAmount,
                IssueDate = summary.IssueDate,
                DueDate = summary.DueDate,
                UpdatedAtUtc = summary.UpdatedAtUtc,
                DeletedAtUtc = entry.DeletedAtUtc,
                OriginalJsonRelativePath = entry.OriginalJsonRelativePath,
                OriginalPdfRelativePath = entry.OriginalPdfRelativePath
            };
        }

        return new InvoiceTrashSummary
        {
            Id = entry.Id,
            DeletedAtUtc = entry.DeletedAtUtc,
            OriginalJsonRelativePath = entry.OriginalJsonRelativePath,
            OriginalPdfRelativePath = entry.OriginalPdfRelativePath
        };
    }

    private InvoiceDocumentSummary BuildSummaryFromDocument(InvoiceDocument document, string relativeJsonPath, string relativePdfPath, InvoiceDocumentSummary? existing)
    {
        return new InvoiceDocumentSummary
        {
            Id = document.Id,
            Type = document.Type,
            Status = document.Status,
            Number = document.Number,
            SupplierName = document.Supplier.Name,
            CustomerName = document.Customer.Name,
            Language = document.Language,
            Currency = document.Currency,
            TotalAmount = document.TotalAmount,
            IssueDate = document.IssueDate,
            DueDate = document.DueDate,
            UpdatedAtUtc = existing?.UpdatedAtUtc ?? document.UpdatedAtUtc,
            RelativeJsonPath = relativeJsonPath,
            RelativePdfPath = relativePdfPath
        };
    }

    private InvoiceDocumentSummary? LoadTrashMeta(string id)
        => SafeFileService.ReadJsonOrDefault<InvoiceDocumentSummary?>(GetTrashMetaPath(id), null, JsonOptions);

    private string GetTrashMetaPath(string id)
        => Path.Combine(_trashMetaPath, $"{id}.json");

    private string GetTrashJsonPath(InvoiceTrashEntry entry)
        => Path.Combine(_trashDocumentsPath, Path.GetFileName(entry.OriginalJsonRelativePath));

    private string GetTrashPdfPath(InvoiceTrashEntry entry)
        => string.IsNullOrWhiteSpace(entry.OriginalPdfRelativePath)
            ? string.Empty
            : Path.Combine(_trashPdfPath, Path.GetFileName(entry.OriginalPdfRelativePath));

    private string GetAresCacheFilePath(string ico)
        => Path.Combine(_cacheAresPath, $"{ico}.json");

    private void SaveKnownTags(IEnumerable<string> tags)
    {
        var existing = SafeFileService.ReadJsonOrDefault(_tagsCatalogFilePath, new List<string>(), JsonOptions);
        var merged = existing
            .Concat(tags)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SafeFileService.WriteJsonAtomic(_tagsCatalogFilePath, merged, JsonOptions);
    }

    private void SaveKnownParties(InvoiceDocument document)
    {
        SaveOrUpdatePartyCatalog(_companiesCatalogFilePath, document.Supplier, isSupplier: true, document);
        SaveOrUpdatePartyCatalog(_customersCatalogFilePath, document.Customer, isSupplier: false, document);
    }

    private void SaveOrUpdatePartyCatalog(string catalogPath, InvoiceParty party, bool isSupplier, InvoiceDocument document)
    {
        if (!HasMeaningfulPartyData(party))
            return;

        var existing = SafeFileService.ReadJsonOrDefault(catalogPath, new List<InvoiceParty>(), JsonOptions);
        var match = existing.FirstOrDefault(entry => IsSameCatalogParty(entry, party));
        if (match == null)
        {
            existing.Add(CloneParty(party));
        }
        else
        {
            CopyPartyValues(match, party);
        }

        var ordered = existing
            .Where(HasMeaningfulPartyData)
            .GroupBy(GetPartyIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SafeFileService.WriteJsonAtomic(catalogPath, ordered, JsonOptions);

        if (isSupplier && !string.IsNullOrWhiteSpace(party.Ico))
            document.SupplierCatalogId = NormalizeIco(party.Ico);
        else if (!isSupplier && !string.IsNullOrWhiteSpace(party.Ico))
            document.CustomerCatalogId = NormalizeIco(party.Ico);
    }

    private static bool HasMeaningfulPartyData(InvoiceParty party)
    {
        return !string.IsNullOrWhiteSpace(party.Name)
               || !string.IsNullOrWhiteSpace(NormalizeIco(party.Ico))
               || !string.IsNullOrWhiteSpace(party.BankIban)
               || !string.IsNullOrWhiteSpace(party.LegacyAccountNumber)
               || !string.IsNullOrWhiteSpace(party.Email)
               || !string.IsNullOrWhiteSpace(party.Phone);
    }

    private static bool IsSameCatalogParty(InvoiceParty left, InvoiceParty right)
    {
        var leftIco = NormalizeIco(left.Ico);
        var rightIco = NormalizeIco(right.Ico);
        if (!string.IsNullOrWhiteSpace(leftIco) && !string.IsNullOrWhiteSpace(rightIco))
            return string.Equals(leftIco, rightIco, StringComparison.OrdinalIgnoreCase);

        var leftName = NormalizePartyName(left.Name);
        var rightName = NormalizePartyName(right.Name);
        return !string.IsNullOrWhiteSpace(leftName)
               && !string.IsNullOrWhiteSpace(rightName)
               && string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPartyIdentityKey(InvoiceParty party)
    {
        var ico = NormalizeIco(party.Ico);
        if (!string.IsNullOrWhiteSpace(ico))
            return $"ico:{ico}";

        var name = NormalizePartyName(party.Name);
        return string.IsNullOrWhiteSpace(name) ? Guid.NewGuid().ToString("N") : $"name:{name}";
    }

    private static string NormalizePartyName(string? name)
        => (name ?? string.Empty).Trim().ToLowerInvariant();

    private static InvoiceParty CloneParty(InvoiceParty source)
    {
        var clone = new InvoiceParty();
        CopyPartyValues(clone, source);
        return clone;
    }

    private static void CopyPartyValues(InvoiceParty target, InvoiceParty source)
    {
        target.Name = source.Name;
        target.Ico = NormalizeIco(source.Ico);
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

    private static string NormalizeLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return DefaultModuleLanguage;

        var normalized = languageCode.Trim().ToLowerInvariant();
        return SupportedLanguages.Contains(normalized) ? normalized : DefaultModuleLanguage;
    }

    private static Func<string, string> CreateLanguageResolver(string? languageCode)
    {
        var localizer = new DocumentLocalizationService();
        localizer.LoadLanguage((languageCode ?? string.Empty).Trim().ToLowerInvariant());
        return localizer.Get;
    }

    private static void NormalizeCashReceiptDocument(InvoiceDocument document)
        => InvoiceCashReceiptHelper.NormalizeDocument(document, CreateLanguageResolver(document.Language));

    private static string NormalizeIco(string? ico)
        => new string((ico ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string NormalizeDocumentNumber(string? number)
        => (number ?? string.Empty).Trim();

    private static string GetFirmKey(InvoiceDocument document)
    {
        var supplierIco = NormalizeIco(document.Supplier.Ico);
        if (!string.IsNullOrWhiteSpace(supplierIco))
            return $"ico:{supplierIco}";

        var catalogKey = NormalizeIco(document.SupplierCatalogId);
        if (!string.IsNullOrWhiteSpace(catalogKey))
            return $"catalog:{catalogKey}";

        var supplierName = (document.Supplier.Name ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(supplierName))
            return $"name:{supplierName.ToLowerInvariant()}";

        return "firm:default";
    }
}
