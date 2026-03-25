using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Invoices.Services;

namespace Win11DesktopApp.Tests;

public class InvoiceCashReceiptHelperTests
{
    [Theory]
    [InlineData("simple", InvoiceDocumentType.CashReceiptIncome, "", "simple")]
    [InlineData("cashdesk", InvoiceDocumentType.CashReceiptIncome, "", "cashdesk")]
    [InlineData("", InvoiceDocumentType.CashReceiptIncome, "Príjmový doklad", "simple")]
    [InlineData("", InvoiceDocumentType.CashReceiptIncome, "Príjmový pokladničný doklad", "cashdesk")]
    [InlineData("", InvoiceDocumentType.Invoice, "", "cashdesk")]
    public void NormalizeVariant_ShouldReturnExpectedValue(string variant, InvoiceDocumentType type, string title, string expected)
    {
        var actual = InvoiceCashReceiptHelper.NormalizeVariant(variant, type, title);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(InvoiceDocumentType.CashReceiptIncome, "simple", "Príjmový doklad")]
    [InlineData(InvoiceDocumentType.CashReceiptIncome, "cashdesk", "Príjmový pokladničný doklad")]
    [InlineData(InvoiceDocumentType.CashReceiptExpense, "simple", "Výdajový doklad")]
    [InlineData(InvoiceDocumentType.CashReceiptExpense, "cashdesk", "Výdajový pokladní doklad")]
    public void GetTitle_ShouldReturnExpectedTitle(InvoiceDocumentType type, string variant, string expected)
    {
        var actual = InvoiceCashReceiptHelper.GetTitle(type, variant);
        Assert.Equal(expected, actual);
    }
}
