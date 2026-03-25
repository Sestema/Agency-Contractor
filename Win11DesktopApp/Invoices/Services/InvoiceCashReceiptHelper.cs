using Win11DesktopApp.Invoices.Models;

namespace Win11DesktopApp.Invoices.Services;

public static class InvoiceCashReceiptHelper
{
    public static string NormalizeVariant(string? variant, InvoiceDocumentType type, string? title)
    {
        if (type is not InvoiceDocumentType.CashReceiptIncome and not InvoiceDocumentType.CashReceiptExpense)
            return "cashdesk";

        if (string.Equals(variant, "simple", StringComparison.OrdinalIgnoreCase))
            return "simple";
        if (string.Equals(variant, "cashdesk", StringComparison.OrdinalIgnoreCase))
            return "cashdesk";

        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Contains("poklad", StringComparison.OrdinalIgnoreCase))
            return "cashdesk";
        if (normalizedTitle.Contains("doklad", StringComparison.OrdinalIgnoreCase))
            return "simple";
        return "cashdesk";
    }

    public static string GetTitle(InvoiceDocumentType type, string? variant, string? fallbackTitle = null)
    {
        var normalizedVariant = NormalizeVariant(variant, type, fallbackTitle);
        return type switch
        {
            InvoiceDocumentType.CashReceiptExpense => normalizedVariant == "simple"
                ? "Výdajový doklad"
                : "Výdajový pokladní doklad",
            _ => normalizedVariant == "simple"
                ? "Príjmový doklad"
                : "Príjmový pokladničný doklad"
        };
    }

    public static void NormalizeDocument(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.IsCashReceiptDocument)
            return;

        document.CashReceiptDocumentVariant = NormalizeVariant(document.CashReceiptDocumentVariant, document.Type, document.CashReceiptTitle);
        document.CashReceiptTitle = GetTitle(document.Type, document.CashReceiptDocumentVariant, document.CashReceiptTitle);
        if (document.CashReceiptAccountingDate == default)
            document.CashReceiptAccountingDate = document.CashReceiptPaymentDate == default ? document.IssueDate : document.CashReceiptPaymentDate;
    }
}
