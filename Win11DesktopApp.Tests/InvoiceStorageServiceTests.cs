using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Tests;

public class InvoiceStorageServiceTests : IDisposable
{
    private readonly string _testRootPath;
    private readonly InvoiceStorageService _storageService;

    public InvoiceStorageServiceTests()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "InvoiceStorageTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRootPath);

        var settings = new AppSettingsService(suppressStartupNotifications: true);
        settings.Settings.RootFolderPath = _testRootPath;
        settings.Settings.LanguageCode = "en";
        var folderService = new FolderService(settings);
        _storageService = new InvoiceStorageService(folderService, settings);
    }

    [Fact]
    public void SaveDocument_ShouldPopulateSummaryAndUpsertCatalogs()
    {
        var first = CreateInvoice("27111111", "Alpha s.r.o.", "111");
        first.Supplier.Street = "Old street 1";
        first.Supplier.BankIban = "CZ6508000000192000145399";
        first.Customer.Ico = "99111111";
        first.Customer.Name = "Customer One";
        first.Customer.City = "Brno";
        _storageService.SaveDocument(first);

        var second = CreateInvoice("27111111", "Alpha s.r.o.", "222");
        second.Supplier.Street = "New street 25";
        second.Supplier.BankIban = "CZ6508000000192000145400";
        second.Supplier.Email = "office@alpha.test";
        second.Customer.Ico = "99111111";
        second.Customer.Name = "Customer One";
        second.Customer.City = "Praha";
        _storageService.SaveDocument(second);

        var summary = _storageService.GetSummaries().First(entry => entry.Id == second.Id);
        Assert.Equal("Alpha s.r.o.", summary.SupplierName);
        Assert.Equal("Customer One", summary.CustomerName);

        var companies = _storageService.GetCompanies();
        var customers = _storageService.GetCustomers();

        Assert.Single(companies);
        Assert.Single(customers);
        Assert.Equal("New street 25", companies[0].Street);
        Assert.Equal("CZ6508000000192000145400", companies[0].BankIban);
        Assert.Equal("office@alpha.test", companies[0].Email);
        Assert.Equal("Praha", customers[0].City);
    }

    [Fact]
    public void AssignNextInvoiceNumber_ShouldIncrementPerFirmPerYear()
    {
        var first = CreateInvoice("12345678", "Firm A", "100");
        _storageService.SaveDocument(first);

        var second = _storageService.CreateDocument(InvoiceDocumentType.Invoice);
        second.Supplier.Ico = "12345678";
        second.Supplier.Name = "Firm A";

        var number = _storageService.AssignNextInvoiceNumber(second);
        Assert.EndsWith("002", number);
    }

    [Fact]
    public void FindDuplicateInvoiceNumber_ShouldMatchOnlySameFirm()
    {
        var existing = CreateInvoice("11111111", "Firm A", "100");
        existing.Number = "2026001";
        _storageService.SaveDocument(existing);

        var otherFirm = CreateInvoice("22222222", "Firm B", "100");
        otherFirm.Number = "2026001";
        _storageService.SaveDocument(otherFirm);

        var candidate = _storageService.CreateDocument(InvoiceDocumentType.Invoice);
        candidate.Supplier.Ico = "11111111";
        candidate.Supplier.Name = "Firm A";
        candidate.Number = "2026001";

        var duplicate = _storageService.FindDuplicateInvoiceNumber(candidate);
        Assert.NotNull(duplicate);
        Assert.Equal(existing.Id, duplicate!.Id);

        candidate.Supplier.Ico = "33333333";
        candidate.Supplier.Name = "Firm C";
        Assert.Null(_storageService.FindDuplicateInvoiceNumber(candidate));
    }

    [Fact]
    public void RepairSummaryParties_ShouldPersistMissingNames()
    {
        var document = CreateInvoice("55555555", "Repair Firm", "100");
        document.Customer.Name = "Repair Customer";
        _storageService.SaveDocument(document);

        var indexPath = Path.Combine(_storageService.ModulePath, "Data", "index.json");
        var jsonOptions = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var summaries = SafeFileService.ReadJsonOrDefault(indexPath, new List<InvoiceDocumentSummary>(), jsonOptions);
        var target = summaries.Single(entry => entry.Id == document.Id);
        target.SupplierName = string.Empty;
        target.CustomerName = string.Empty;
        SafeFileService.WriteJsonAtomic(indexPath, summaries, jsonOptions);

        _storageService.RepairSummaryParties(document.Id, document.Supplier.Name, document.Customer.Name);

        var repaired = _storageService.GetSummaries().Single(entry => entry.Id == document.Id);
        Assert.Equal("Repair Firm", repaired.SupplierName);
        Assert.Equal("Repair Customer", repaired.CustomerName);
    }

    private InvoiceDocument CreateInvoice(string supplierIco, string supplierName, string customerIco)
    {
        var document = _storageService.CreateDocument(InvoiceDocumentType.Invoice);
        document.Supplier.Ico = supplierIco;
        document.Supplier.Name = supplierName;
        document.Customer.Ico = customerIco;
        document.Customer.Name = $"Customer {customerIco}";
        document.Items =
        [
            new InvoiceLineItem
            {
                Description = "Service",
                Quantity = 1,
                UnitPrice = 100
            }
        ];
        return document;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRootPath))
                Directory.Delete(_testRootPath, true);
        }
        catch
        {
        }
    }
}
