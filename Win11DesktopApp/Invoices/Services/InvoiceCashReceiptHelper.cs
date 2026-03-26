using Win11DesktopApp.Invoices.Models;
using System.Windows;

namespace Win11DesktopApp.Invoices.Services;

public static class InvoiceCashReceiptHelper
{
    private static readonly string[] CashdeskMarkers =
    {
        "poklad",
        "cash",
        "касов"
    };

    private static readonly string[] SimpleMarkers =
    {
        "doklad",
        "document",
        "receipt",
        "документ"
    };

    public static string NormalizeVariant(string? variant, InvoiceDocumentType type, string? title)
    {
        if (type is not InvoiceDocumentType.CashReceiptIncome and not InvoiceDocumentType.CashReceiptExpense)
            return "cashdesk";

        if (string.Equals(variant, "simple", StringComparison.OrdinalIgnoreCase))
            return "simple";
        if (string.Equals(variant, "cashdesk", StringComparison.OrdinalIgnoreCase))
            return "cashdesk";

        var normalizedTitle = (title ?? string.Empty).Trim();
        if (ContainsAny(normalizedTitle, CashdeskMarkers))
            return "cashdesk";
        if (ContainsAny(normalizedTitle, SimpleMarkers))
            return "simple";
        return "cashdesk";
    }

    public static string GetTitle(InvoiceDocumentType type, string? variant, string? fallbackTitle = null, Func<string, string>? resourceResolver = null)
    {
        var normalizedVariant = NormalizeVariant(variant, type, fallbackTitle);
        var resourceKey = type switch
        {
            InvoiceDocumentType.CashReceiptExpense => normalizedVariant == "simple"
                ? "InvoicesCashReceiptExpenseTitleSimple"
                : "InvoicesCashReceiptExpenseTitleCashdesk",
            _ => normalizedVariant == "simple"
                ? "InvoicesCashReceiptIncomeTitleSimple"
                : "InvoicesCashReceiptIncomeTitleCashdesk"
        };

        return ResolveResource(resourceResolver, resourceKey);
    }

    public static void NormalizeDocument(InvoiceDocument document, Func<string, string>? resourceResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.IsCashReceiptDocument)
            return;

        document.CashReceiptDocumentVariant = NormalizeVariant(document.CashReceiptDocumentVariant, document.Type, document.CashReceiptTitle);
        document.CashReceiptTitle = GetTitle(document.Type, document.CashReceiptDocumentVariant, document.CashReceiptTitle, resourceResolver);
        if (document.CashReceiptAccountingDate == default)
            document.CashReceiptAccountingDate = document.CashReceiptPaymentDate == default ? document.IssueDate : document.CashReceiptPaymentDate;
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers)
        => markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string ResolveResource(Func<string, string>? resourceResolver, string key)
        => resourceResolver?.Invoke(key)
           ?? Application.Current?.TryFindResource(key) as string
           ?? key;
}
