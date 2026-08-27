using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Win11DesktopApp.Services
{
    public sealed class SalaryQrPdfItem
    {
        public string EmployeeName { get; init; } = string.Empty;
        public string FirmName { get; init; } = string.Empty;
        public string AccountNumber { get; init; } = string.Empty;
        public string BankName { get; init; } = string.Empty;
        public string Iban { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string MessageText { get; init; } = string.Empty;
        public byte[]? QrPngBytes { get; init; }
    }

    public sealed class SalaryQrPdfExportLabels
    {
        public string Title { get; init; } = "QR";
        public string Amount { get; init; } = "Amount";
        public string Account { get; init; } = "Account";
        public string Bank { get; init; } = "Bank";
        public string Iban { get; init; } = "IBAN";
        public string Message { get; init; } = "Message";
    }

    public static class SalaryQrPdfExportService
    {
        public static void GenerateToFile(
            string outputPath,
            string monthTitle,
            IReadOnlyList<SalaryQrPdfItem> items,
            SalaryQrPdfExportLabels labels,
            string? currencySymbol = null)
        {
            var symbol = string.IsNullOrWhiteSpace(currencySymbol) ? "Kč" : currencySymbol.Trim();
            var pages = (items ?? Array.Empty<SalaryQrPdfItem>())
                .Where(item => item.QrPngBytes is { Length: > 0 })
                .ToList();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(style => style.FontFamily("Segoe UI").FontSize(9));

                    page.Header().PaddingBottom(6).Column(header =>
                    {
                        header.Item().Text(labels.Title).FontSize(13).Bold();
                        header.Item().Text(monthTitle).FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    // Compact full-width strips: 8 people fit on one A4 portrait page.
                    page.Content().Column(content =>
                    {
                        content.Spacing(5);

                        foreach (var item in pages)
                        {
                            content.Item().Border(1).BorderColor("#D0D5DD")
                                .PaddingVertical(6).PaddingHorizontal(10)
                                .Height(88)
                                .Row(row =>
                            {
                                row.RelativeItem().AlignMiddle().Column(info =>
                                {
                                    info.Spacing(1);
                                    info.Item().Text(item.EmployeeName).FontSize(12).Bold();
                                    if (!string.IsNullOrWhiteSpace(item.FirmName))
                                        info.Item().Text(item.FirmName).FontSize(8).FontColor(Colors.Grey.Darken1);

                                    info.Item().PaddingTop(2).Text($"{labels.Amount}: {item.Amount.ToString("N0", CultureInfo.CurrentCulture)} {symbol}").FontSize(10).Bold();
                                    info.Item().Text($"{labels.Account}: {item.AccountNumber}");
                                    if (!string.IsNullOrWhiteSpace(item.BankName))
                                        info.Item().Text($"{labels.Bank}: {item.BankName}").FontSize(8);
                                    if (!string.IsNullOrWhiteSpace(item.Iban))
                                        info.Item().Text($"{labels.Iban}: {item.Iban}");
                                    if (!string.IsNullOrWhiteSpace(item.MessageText))
                                        info.Item().Text($"{labels.Message}: {item.MessageText}");
                                });

                                if (item.QrPngBytes is { Length: > 0 } png)
                                {
                                    row.ConstantItem(72).AlignMiddle().Height(72).Image(png).FitArea();
                                }
                            });
                        }
                    });
                });
            }).GeneratePdf(outputPath);
        }
    }
}
