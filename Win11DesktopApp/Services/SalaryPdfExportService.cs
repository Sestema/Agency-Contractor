using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class SalaryPdfExportLabels
    {
        public string ColFirm { get; init; } = "Firm";
        public string ColName { get; init; } = "Name";
        public string ColHours { get; init; } = "Hours";
        public string ColRate { get; init; } = "Rate";
        public string ColGross { get; init; } = "Gross";
        public string ColAdvance { get; init; } = "Advance";
        public string ColNet { get; init; } = "Net Pay";
        public string ColNote { get; init; } = "Note";
        public string ColPaid { get; init; } = "Paid";
        public string ColAmount { get; init; } = "Amount";
        public string FirmExpenses { get; init; } = "Firm Expenses";
        public string ExpenseTotal { get; init; } = "Total expenses";
        public string GrandTotal { get; init; } = "Grand Total";
        public string FirmBreakdown { get; init; } = "By Firm";
        public string PaidYes { get; init; } = "✓";
    }

    public static class SalaryPdfExportService
    {
        private static readonly string HeaderColor = "#4472C4";
        private static readonly string FirmBandColor = "#D9E2F3";
        private static readonly string FirmBandTextColor = "#2F5496";
        private static readonly string AltRowColor = "#F7F9FC";
        private static readonly string PaidRowColor = "#E8F5E9";
        private static readonly string TotalsColor = "#FFF9C4";
        private static readonly string GrandTotalColor = "#FFD966";
        private static readonly string GridLineColor = "#8A93A3";
        private static float CellFontSize = 8;
        private static float FirmFontSize = 8;

        public static void GenerateToFile(
            string outputPath,
            int year,
            int month,
            IReadOnlyList<SalaryEntry> entries,
            IReadOnlyList<CustomSalaryField> fields,
            IReadOnlyList<FirmExpense> expenses,
            SalaryPdfExportLabels labels,
            string? currencySymbol = null,
            SalaryExportColumnSelection? columnSelection = null,
            int fontStep = SalaryExportFontScale.DefaultStep)
        {
            var symbol = string.IsNullOrWhiteSpace(currencySymbol) ? "Kč" : currencySymbol.Trim();
            var columns = columnSelection ?? SalaryExportColumnSelection.All;
            CellFontSize = SalaryExportFontScale.PdfTable(fontStep);
            FirmFontSize = SalaryExportFontScale.PdfFirm(fontStep);
            var orderedFields = (fields ?? Array.Empty<CustomSalaryField>())
                .Where(field => columns.IsVisible(SalaryExportColumnKeys.Custom(field.Id)))
                .OrderBy(field => field.Order)
                .ThenBy(field => field.Name)
                .ToList();
            var exportEntries = (entries ?? Array.Empty<SalaryEntry>())
                .OrderBy(entry => entry.FirmName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var exportExpenses = (expenses ?? Array.Empty<FirmExpense>())
                .OrderBy(expense => expense.FirmName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(expense => expense.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var columnCount = columns.CountVisibleForPdf(orderedFields);
            var title = $"{month}.{year}";

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.MarginHorizontal(12);
                    page.MarginVertical(16);
                    page.DefaultTextStyle(style => style.FontFamily("Segoe UI").FontSize(7));

                    page.Header().PaddingBottom(8).Column(outer =>
                    {
                        outer.Item()
                            .Background(FirmBandColor)
                            .Border(1)
                            .BorderColor(FirmBandTextColor)
                            .PaddingVertical(8)
                            .PaddingHorizontal(12)
                            .Column(header =>
                            {
                                header.Item().Text(title).FontSize(16).Bold().AlignCenter();
                                header.Item().PaddingTop(2).Text(
                                        $"{labels.ColFirm}: {exportEntries.Select(e => e.FirmName).Distinct(StringComparer.OrdinalIgnoreCase).Count()} | " +
                                        $"{labels.ColName}: {exportEntries.Count}")
                                    .FontSize(8)
                                    .FontColor(Colors.Grey.Darken1)
                                    .AlignCenter();
                            });
                    });

                    page.Content().Column(content =>
                    {
                        content.Item()
                            .Border(1)
                            .BorderColor(HeaderColor)
                            .Table(table =>
                        {
                            table.ColumnsDefinition(defs =>
                            {
                                defs.RelativeColumn(3f);
                                if (columns.IsVisible(SalaryExportColumnKeys.Hours)) defs.RelativeColumn(1f);
                                if (columns.IsVisible(SalaryExportColumnKeys.Rate)) defs.RelativeColumn(1f);
                                if (columns.IsVisible(SalaryExportColumnKeys.Gross)) defs.RelativeColumn(1.2f);
                                if (columns.IsVisible(SalaryExportColumnKeys.Advance)) defs.RelativeColumn(1.1f);
                                foreach (var _ in orderedFields)
                                    defs.RelativeColumn(1f);
                                if (columns.IsVisible(SalaryExportColumnKeys.Net)) defs.RelativeColumn(1.3f);
                                if (columns.IsVisible(SalaryExportColumnKeys.Note)) defs.RelativeColumn(2f);
                                if (columns.IsVisible(SalaryExportColumnKeys.Paid)) defs.RelativeColumn(0.8f);
                            });

                            table.Header(header =>
                            {
                                void HeaderCell(string text) => header.Cell()
                                    .Background(HeaderColor)
                                    .Border(0.6f)
                                    .BorderColor(FirmBandTextColor)
                                    .PaddingVertical(3)
                                    .PaddingHorizontal(2)
                                    .AlignCenter()
                                    .Text(text)
                                    .FontColor(Colors.White)
                                    .Bold()
                                    .FontSize(CellFontSize);

                                HeaderCell(labels.ColName);
                                if (columns.IsVisible(SalaryExportColumnKeys.Hours)) HeaderCell(labels.ColHours);
                                if (columns.IsVisible(SalaryExportColumnKeys.Rate)) HeaderCell(labels.ColRate);
                                if (columns.IsVisible(SalaryExportColumnKeys.Gross)) HeaderCell(labels.ColGross);
                                if (columns.IsVisible(SalaryExportColumnKeys.Advance)) HeaderCell(labels.ColAdvance);
                                foreach (var field in orderedFields)
                                    HeaderCell($"{MapFieldOperation(field.Operation)}{field.Name}");
                                if (columns.IsVisible(SalaryExportColumnKeys.Net)) HeaderCell(labels.ColNet);
                                if (columns.IsVisible(SalaryExportColumnKeys.Note)) HeaderCell(labels.ColNote);
                                if (columns.IsVisible(SalaryExportColumnKeys.Paid)) HeaderCell(labels.ColPaid);
                            });

                            var firmGroups = exportEntries
                                .GroupBy(entry => entry.FirmName, StringComparer.OrdinalIgnoreCase)
                                .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase);

                            var rowIndex = 0;
                            foreach (var group in firmGroups)
                            {
                                table.Cell()
                                    .ColumnSpan((uint)columnCount)
                                    .Background(FirmBandColor)
                                    .Border(0.75f)
                                    .BorderColor(HeaderColor)
                                    .PaddingVertical(4)
                                    .PaddingHorizontal(4)
                                    .AlignCenter()
                                    .Text(group.Key)
                                    .Bold()
                                    .FontSize(FirmFontSize)
                                    .FontColor(FirmBandTextColor);

                                foreach (var entry in group.OrderBy(e => e.FullName, StringComparer.CurrentCultureIgnoreCase))
                                {
                                    var bg = entry.IsPaid
                                        ? PaidRowColor
                                        : rowIndex % 2 == 1
                                            ? AltRowColor
                                            : Colors.White;

                                    DataCell(table, entry.DisplayName, bg, bold: entry.GrossSalary > 0);
                                    if (columns.IsVisible(SalaryExportColumnKeys.Hours))
                                        DataCell(table, FormatHours(entry.HoursWorked), bg, alignCenter: true, bold: true);
                                    if (columns.IsVisible(SalaryExportColumnKeys.Rate))
                                        DataCell(table, FormatMoney(entry.HourlyRate, 0), bg, alignCenter: true, bold: true);
                                    if (columns.IsVisible(SalaryExportColumnKeys.Gross))
                                        DataCell(table, FormatMoney(entry.GrossSalary), bg, alignCenter: true, bold: true);
                                    if (columns.IsVisible(SalaryExportColumnKeys.Advance))
                                        DataCell(table, FormatMoney(entry.Advance), bg, alignCenter: true, bold: true,
                                            textColor: entry.Advance > 0 ? "#C62828" : null);

                                    foreach (var field in orderedFields)
                                    {
                                        var value = entry.CustomValues.TryGetValue(field.Id, out var customValue) ? customValue : 0m;
                                        DataCell(table, FormatMoney(value), bg, alignCenter: true, bold: true);
                                    }

                                    if (columns.IsVisible(SalaryExportColumnKeys.Net))
                                        DataCell(table, FormatMoney(entry.NetSalary), bg, alignCenter: true, bold: true, textColor: "#1565C0");
                                    if (columns.IsVisible(SalaryExportColumnKeys.Note))
                                        DataCell(table, entry.Note ?? string.Empty, bg, alignCenter: false, italic: true, textColor: "#888888");
                                    if (columns.IsVisible(SalaryExportColumnKeys.Paid))
                                        DataCell(table, entry.IsPaid ? labels.PaidYes : string.Empty, bg, alignCenter: true, bold: entry.IsPaid, textColor: "#2E7D32");
                                    rowIndex++;
                                }
                            }

                            void TotalsCell(string text, string? color = null)
                                => table.Cell()
                                    .Background(TotalsColor)
                                    .Border(0.6f)
                                    .BorderColor(GridLineColor)
                                    .PaddingVertical(3)
                                    .PaddingHorizontal(2)
                                    .AlignCenter()
                                    .Text(text)
                                    .Bold()
                                    .FontSize(CellFontSize)
                                    .FontColor(color ?? Colors.Black);

                            void EmptyTotal() =>
                                table.Cell().Background(TotalsColor).Border(0.6f).BorderColor(GridLineColor);

                            EmptyTotal();
                            if (columns.IsVisible(SalaryExportColumnKeys.Hours))
                                TotalsCell(FormatHours(exportEntries.Sum(e => e.HoursWorked)));
                            if (columns.IsVisible(SalaryExportColumnKeys.Rate))
                                EmptyTotal();
                            if (columns.IsVisible(SalaryExportColumnKeys.Gross))
                                TotalsCell(FormatMoney(exportEntries.Sum(e => e.GrossSalary)));
                            if (columns.IsVisible(SalaryExportColumnKeys.Advance))
                                TotalsCell(FormatMoney(exportEntries.Sum(e => e.Advance)), color: "#C62828");
                            foreach (var field in orderedFields)
                            {
                                var total = exportEntries.Sum(e => e.CustomValues.TryGetValue(field.Id, out var v) ? v : 0m);
                                TotalsCell(FormatMoney(total));
                            }

                            if (columns.IsVisible(SalaryExportColumnKeys.Net))
                                TotalsCell(FormatMoney(exportEntries.Sum(e => e.NetSalary)) + " " + symbol, color: "#1565C0");
                            if (columns.IsVisible(SalaryExportColumnKeys.Note))
                                EmptyTotal();
                            if (columns.IsVisible(SalaryExportColumnKeys.Paid))
                                EmptyTotal();
                        });

                        if (exportExpenses.Count > 0)
                        {
                            content.Item().PaddingTop(10)
                                .Border(1)
                                .BorderColor("#E65100")
                                .Table(expenseTable =>
                            {
                                expenseTable.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(2.5f);
                                    c.RelativeColumn(3f);
                                    c.RelativeColumn(1.2f);
                                });

                                expenseTable.Cell().ColumnSpan(3)
                                    .Background("#FFF2CC")
                                    .Padding(4)
                                    .Text(labels.FirmExpenses)
                                    .Bold()
                                    .FontSize(8)
                                    .FontColor("#E65100");

                                foreach (var expense in exportExpenses)
                                {
                                    ExpenseCell(expenseTable, expense.FirmName, "#2F5496");
                                    ExpenseCell(expenseTable, expense.Name, "#4E342E");
                                    ExpenseCell(expenseTable, FormatMoney(expense.Amount, 0) + " " + symbol, "#E65100", alignCenter: true);
                                }

                                expenseTable.Cell().Background("#FFF2CC");
                                expenseTable.Cell().Background("#FFF2CC").AlignRight().Text(labels.ExpenseTotal).Bold().FontColor("#BF360C");
                                expenseTable.Cell().Background("#FFF2CC").AlignCenter()
                                    .Text(FormatMoney(exportExpenses.Sum(e => e.Amount), 0) + " " + symbol)
                                    .Bold()
                                    .FontColor("#BF360C");
                            });
                        }

                        var expensesTotal = exportExpenses.Sum(e => e.Amount);
                        var netTotal = exportEntries.Sum(e => e.NetSalary);
                        content.Item().PaddingTop(8).Background(GrandTotalColor).Border(1).BorderColor("#F9A825").Padding(6)
                            .Row(row =>
                            {
                                row.RelativeItem().Text(labels.GrandTotal).Bold().FontSize(10).FontColor("#3E2723");
                                row.ConstantItem(120).AlignRight().Text(FormatMoney(netTotal + expensesTotal) + " " + symbol)
                                    .Bold()
                                    .FontSize(10)
                                    .FontColor("#3E2723");
                            });

                        var firmSummaries = exportEntries
                            .GroupBy(e => e.FirmName, StringComparer.OrdinalIgnoreCase)
                            .OrderByDescending(g => g.Sum(e => e.GrossSalary))
                            .ToList();

                        if (firmSummaries.Count > 0)
                        {
                            content.Item().PaddingTop(10)
                                .Border(1)
                                .BorderColor(HeaderColor)
                                .Table(summaryTable =>
                                {
                                    summaryTable.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(3.5f);
                                        c.RelativeColumn(1.5f);
                                        c.RelativeColumn(1f);
                                    });

                                    summaryTable.Cell().ColumnSpan(3)
                                        .Background(FirmBandColor)
                                        .BorderBottom(0.75f)
                                        .BorderColor(HeaderColor)
                                        .Padding(4)
                                        .Text(labels.FirmBreakdown)
                                        .Bold()
                                        .FontSize(8)
                                        .FontColor(FirmBandTextColor);

                                    void SummaryHeaderCell(string text) => summaryTable.Cell()
                                        .Background(HeaderColor)
                                        .Border(0.6f)
                                        .BorderColor(GridLineColor)
                                        .PaddingVertical(3)
                                        .PaddingHorizontal(3)
                                        .AlignCenter()
                                        .Text(text)
                                        .FontColor(Colors.White)
                                        .Bold()
                                        .FontSize(CellFontSize);

                                    SummaryHeaderCell(labels.ColFirm);
                                    SummaryHeaderCell(labels.ColAmount);
                                    SummaryHeaderCell(labels.ColHours);

                                    var firmAlt = false;
                                    foreach (var group in firmSummaries)
                                    {
                                        var bg = firmAlt ? AltRowColor : "#FFFFFF";
                                        summaryTable.Cell()
                                            .Background(bg)
                                            .Border(0.6f)
                                            .BorderColor(GridLineColor)
                                            .PaddingVertical(2)
                                            .PaddingHorizontal(3)
                                            .AlignLeft()
                                            .Text(group.Key)
                                            .FontSize(CellFontSize)
                                            .FontColor(FirmBandTextColor);

                                        summaryTable.Cell()
                                            .Background(bg)
                                            .Border(0.6f)
                                            .BorderColor(GridLineColor)
                                            .PaddingVertical(2)
                                            .PaddingHorizontal(3)
                                            .AlignCenter()
                                            .Text(FormatMoney(group.Sum(e => e.NetSalary), 0) + " " + symbol)
                                            .Bold()
                                            .FontSize(CellFontSize);

                                        summaryTable.Cell()
                                            .Background(bg)
                                            .Border(0.6f)
                                            .BorderColor(GridLineColor)
                                            .PaddingVertical(2)
                                            .PaddingHorizontal(3)
                                            .AlignCenter()
                                            .Text(FormatHours(group.Sum(e => e.HoursWorked)))
                                            .FontSize(CellFontSize);

                                        firmAlt = !firmAlt;
                                    }
                                });
                        }
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("— ").FontSize(7).FontColor(Colors.Grey.Medium);
                        text.CurrentPageNumber().FontSize(7);
                        text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Medium);
                        text.TotalPages().FontSize(7);
                        text.Span(" —").FontSize(7).FontColor(Colors.Grey.Medium);
                    });
                });
            }).GeneratePdf(outputPath);
        }

        private static void DataCell(
            TableDescriptor table,
            string text,
            string background,
            bool alignCenter = false,
            bool bold = false,
            bool italic = false,
            string? textColor = null)
        {
            IContainer cell = table.Cell()
                .Background(background)
                .Border(0.6f)
                .BorderColor(GridLineColor)
                .PaddingVertical(2)
                .PaddingHorizontal(2);

            if (alignCenter)
            {
                if (bold && italic && textColor != null)
                    cell.AlignCenter().Text(text).FontSize(CellFontSize).Bold().Italic().FontColor(textColor);
                else if (bold && italic)
                    cell.AlignCenter().Text(text).FontSize(CellFontSize).Bold().Italic();
                else if (bold && textColor != null)
                    cell.AlignCenter().Text(text).FontSize(CellFontSize).Bold().FontColor(textColor);
                else if (bold)
                    cell.AlignCenter().Text(text).FontSize(CellFontSize).Bold();
                else if (italic && textColor != null)
                    cell.AlignCenter().Text(text).FontSize(CellFontSize).Italic().FontColor(textColor);
                else if (italic)
                    cell.AlignCenter().Text(text).FontSize(CellFontSize).Italic();
                else if (textColor != null)
                    cell.AlignCenter().Text(text).FontSize(CellFontSize).FontColor(textColor);
                else
                    cell.AlignCenter().Text(text).FontSize(CellFontSize);
            }
            else
            {
                if (bold && italic && textColor != null)
                    cell.AlignLeft().Text(text).FontSize(CellFontSize).Bold().Italic().FontColor(textColor);
                else if (bold && italic)
                    cell.AlignLeft().Text(text).FontSize(CellFontSize).Bold().Italic();
                else if (bold && textColor != null)
                    cell.AlignLeft().Text(text).FontSize(CellFontSize).Bold().FontColor(textColor);
                else if (bold)
                    cell.AlignLeft().Text(text).FontSize(CellFontSize).Bold();
                else if (italic && textColor != null)
                    cell.AlignLeft().Text(text).FontSize(CellFontSize).Italic().FontColor(textColor);
                else if (italic)
                    cell.AlignLeft().Text(text).FontSize(CellFontSize).Italic();
                else if (textColor != null)
                    cell.AlignLeft().Text(text).FontSize(CellFontSize).FontColor(textColor);
                else
                    cell.AlignLeft().Text(text).FontSize(CellFontSize);
            }
        }

        private static void ExpenseCell(TableDescriptor table, string text, string color, bool alignCenter = false)
        {
            var cell = table.Cell()
                .Border(0.6f)
                .BorderColor(GridLineColor)
                .PaddingVertical(2)
                .PaddingHorizontal(3);
            if (alignCenter)
                cell.AlignCenter().Text(text).FontSize(CellFontSize).FontColor(color);
            else
                cell.AlignLeft().Text(text).FontSize(CellFontSize).FontColor(color);
        }

        private static string FormatHours(decimal hours)
        {
            return SalaryEntry.UseCustomHoursFormat
                ? hours.ToString("0.####################", CultureInfo.CurrentCulture)
                : hours.ToString("N1", CultureInfo.CurrentCulture);
        }

        private static string FormatMoney(decimal value, int decimals = 2)
            => value.ToString(decimals == 0 ? "N0" : "N2", CultureInfo.CurrentCulture);

        private static string MapFieldOperation(FieldOperation operation)
            => operation switch
            {
                FieldOperation.Add => "+",
                FieldOperation.Subtract => "−",
                FieldOperation.Multiply => "×",
                FieldOperation.Divide => "÷",
                _ => string.Empty
            };
    }
}
