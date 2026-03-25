using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Invoices.Services;

public sealed class InvoicePdfRenderService
{
    private readonly InvoiceStorageService _storageService;
    private readonly InvoiceQrPaymentService _qrPaymentService;
    private static readonly AsyncLocal<DocumentLocalizationService?> CurrentDocumentLocalizer = new();

    public InvoicePdfRenderService(InvoiceStorageService storageService, InvoiceQrPaymentService qrPaymentService)
    {
        _storageService = storageService;
        _qrPaymentService = qrPaymentService;
    }

    public string RenderPdf(InvoiceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var outputPath = _storageService.GetPdfOutputPath(document);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var previousLocalizer = CurrentDocumentLocalizer.Value;
        CurrentDocumentLocalizer.Value = CreateDocumentLocalizer(document.Language);

        try
        {
            using var pdf = new PdfDocument();
            var page = pdf.AddPage();
            page.Size = PdfSharp.PageSize.A4;

            using var gfx = XGraphics.FromPdfPage(page);
            if (document.Type is InvoiceDocumentType.CashReceiptIncome or InvoiceDocumentType.CashReceiptExpense)
            {
                DrawCashReceiptPdf(gfx, page.Width.Point, page.Height.Point, document);
                pdf.Save(outputPath);
                return outputPath;
            }

            var style = ResolveStyle(document.SelectedTemplateId);
            var palette = ThemePalette.FromTheme(document.SelectedTheme);
            var layout = CreateLayout(style, page.Width.Point, page.Height.Point);
            var ctx = new RenderContext(gfx, document, palette, layout, _qrPaymentService.CreatePreview(document), _qrPaymentService.DescribePayment(document));

            if (style == InvoiceStyle.Style1)
            {
                DrawStyle1Pdf(ctx, page.Width.Point, page.Height.Point);
                pdf.Save(outputPath);
                return outputPath;
            }

            if (style == InvoiceStyle.Style2)
            {
                DrawStyle2Pdf(ctx, page.Width.Point, page.Height.Point);
                pdf.Save(outputPath);
                return outputPath;
            }

            if (style == InvoiceStyle.Style3)
            {
                DrawStyle3Pdf(ctx, page.Width.Point, page.Height.Point);
                pdf.Save(outputPath);
                return outputPath;
            }

            if (style == InvoiceStyle.Style4)
            {
                DrawStyle4Pdf(ctx, page.Width.Point, page.Height.Point);
                pdf.Save(outputPath);
                return outputPath;
            }

            if (style == InvoiceStyle.Style5)
            {
                DrawStyle5Pdf(ctx, page.Width.Point, page.Height.Point);
                pdf.Save(outputPath);
                return outputPath;
            }

            if (style == InvoiceStyle.Style6)
            {
                DrawStyle6Pdf(ctx, page.Width.Point, page.Height.Point);
                pdf.Save(outputPath);
                return outputPath;
            }

            DrawDecorations(ctx);
            DrawBarcode(ctx);
            DrawHeader(ctx);
            DrawPartyCard(ctx, layout.SupplierRect, SupplierTitle(document), document.Supplier, layout.SupplierBox);
            DrawPartyCard(ctx, layout.CustomerRect, CustomerTitle(document), document.Customer, layout.CustomerBox);
            if (layout.BankRect.HasValue && HasBankData(document.Supplier))
                DrawTextCard(ctx, layout.BankRect.Value, Res("InvoicesPaymentBankTransfer"), BuildPaymentLines(ctx.PaymentDetails), layout.BankBox);
            DrawMeta(ctx);
            DrawItems(ctx);
            DrawQr(ctx);
            DrawTotals(ctx);
            DrawFooter(ctx);

            pdf.Save(outputPath);
            return outputPath;
        }
        finally
        {
            CurrentDocumentLocalizer.Value = previousLocalizer;
        }
    }

    private static void DrawStyle1Pdf(RenderContext ctx, double pageWidth, double pageHeight)
    {
        ctx.Gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);

        var outerRect = new XRect(38, 32, pageWidth - 76, pageHeight - 64);
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), outerRect);

        var barcodeRect = new XRect(52, 38, 104, 18);
        DrawStyle1Barcode(ctx, barcodeRect);

        var titleRect = new XRect(316, 34, 236, 24);
        var title = $"{DocumentTitle(ctx.Document)} {ctx.Document.Number}".Trim();
        ctx.Gfx.DrawString(title, FitFont(ctx, title, 16, titleRect.Width, true), XBrushes.Black, titleRect, XStringFormats.TopRight);

        var supplierRect = new XRect(52, 62, 214, 154);
        var customerRect = new XRect(290, 58, 262, 120);
        var metaRect = new XRect(290, 178, 262, 92);
        var noteRect = new XRect(52, 286, 500, 28);
        var itemsRect = new XRect(52, 330, 500, GetStyle1ItemsHeight(ctx.Document.Items.Count));
        var qrRect = new XRect(52, 626, 88, 98);
        var bottomRect = new XRect(52, 730, 500, 80);

        DrawStyle1Supplier(ctx, supplierRect);
        DrawStyle1Customer(ctx, customerRect);
        DrawStyle1MetaTable(ctx, metaRect);
        DrawStyle1Note(ctx, noteRect);
        DrawStyle1Items(ctx, itemsRect);
        DrawStyle1Qr(ctx, qrRect);
        DrawStyle1BottomArea(ctx, bottomRect);
    }

    private static void DrawStyle2Pdf(RenderContext ctx, double pageWidth, double pageHeight)
    {
        ctx.Gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);

        var outerRect = new XRect(36, 34, pageWidth - 72, pageHeight - 68);
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(1.1), outerRect);

        var barcodeRect = new XRect(50, 40, 104, 18);
        DrawStyle1Barcode(ctx, barcodeRect);

        var titleRect = new XRect(332, 40, 220, 24);
        var title = $"{DocumentTitle(ctx.Document)} {ctx.Document.Number}".Trim();
        ctx.Gfx.DrawString(title, FitFont(ctx, title, 18, titleRect.Width, true), XBrushes.Black, titleRect, XStringFormats.TopRight);

        var supplierRect = new XRect(50, 74, 222, 150);
        var customerRect = new XRect(286, 74, 258, 98);
        var metaRect = new XRect(286, 178, 258, 82);
        var noteRect = new XRect(50, 286, 494, 28);
        var itemsRect = new XRect(50, 332, 494, GetStyle2ItemsHeight(ctx.Document.Items.Count));
        var qrRect = new XRect(50, 646, 92, 100);
        var totalsRect = new XRect(358, 648, 186, 82);
        var footerRect = new XRect(50, 762, 494, 42);

        DrawStyle2Supplier(ctx, supplierRect);
        DrawStyle2Customer(ctx, customerRect);
        DrawStyle2Meta(ctx, metaRect);
        DrawStyle2Note(ctx, noteRect);
        DrawStyle2Items(ctx, itemsRect);
        DrawStyle2Qr(ctx, qrRect);
        DrawStyle2Totals(ctx, totalsRect);
        DrawStyle2Footer(ctx, footerRect);
    }

    private static void DrawStyle3Pdf(RenderContext ctx, double pageWidth, double pageHeight)
    {
        ctx.Gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);

        var outerRect = new XRect(36, 28, pageWidth - 72, pageHeight - 56);
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(1.0), outerRect);

        var barcodeRect = new XRect(52, 42, 104, 18);
        DrawStyle1Barcode(ctx, barcodeRect);

        var titleRect = new XRect(52, 76, 312, 30);
        var title = $"{DocumentTitle(ctx.Document)} {ctx.Document.Number}".Trim();
        ctx.Gfx.DrawString(title, FitFont(ctx, title, 20, titleRect.Width, true), XBrushes.Black, titleRect, XStringFormats.TopLeft);

        var supplierRect = new XRect(52, 134, 158, 122);
        var customerRect = new XRect(226, 134, 158, 122);
        var bankRect = new XRect(400, 134, 152, 122);
        var metaRect = new XRect(52, 282, 242, 44);
        var noteRect = new XRect(52, 334, 500, 24);
        var itemsRect = new XRect(52, 366, 500, GetStyle3ItemsHeight(ctx.Document.Items.Count));
        var qrRect = new XRect(52, 654, 88, 108);
        var totalsRect = new XRect(366, 660, 186, 82);
        var footerRect = new XRect(52, 766, 500, 42);

        DrawStyle3Party(ctx, supplierRect, SupplierTitle(ctx.Document), ctx.Document.Supplier);
        DrawStyle3Party(ctx, customerRect, CustomerTitle(ctx.Document), ctx.Document.Customer, includePaymentInfo: false);
        DrawStyle3Bank(ctx, bankRect);
        DrawStyle3Meta(ctx, metaRect);
        DrawStyle2Note(ctx, noteRect);
        DrawStyle3Items(ctx, itemsRect);
        DrawStyle3Qr(ctx, qrRect);
        DrawStyle3Totals(ctx, totalsRect);
        DrawStyle3Footer(ctx, footerRect);
    }

    private static void DrawStyle4Pdf(RenderContext ctx, double pageWidth, double pageHeight)
    {
        ctx.Gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);

        var outerRect = new XRect(38, 34, pageWidth - 76, pageHeight - 68);
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(1.0), outerRect);

        var ribbonRect = new XRect(42, 76, pageWidth - 84, 18);
        ctx.Gfx.DrawRectangle(ctx.Palette.MutedBrush, ribbonRect);

        var barcodeRect = new XRect(50, 42, 104, 18);
        DrawStyle1Barcode(ctx, barcodeRect);

        var titleRect = new XRect(52, 74, 340, 24);
        var title = $"{DocumentTitle(ctx.Document)} {ctx.Document.Number}".Trim();
        ctx.Gfx.DrawString(title, FitFont(ctx, title, 18.5, titleRect.Width, true), XBrushes.Black, titleRect, XStringFormats.CenterLeft);

        var supplierRect = new XRect(54, 122, 208, 96);
        var customerRect = new XRect(304, 122, 208, 96);
        var metaRect = new XRect(54, 234, 208, 66);
        var paymentRect = new XRect(304, 234, 248, 78);
        var noteRect = new XRect(54, 320, 498, 24);
        var itemsRect = new XRect(54, 360, 498, GetStyle4ItemsHeight(ctx.Document.Items.Count));
        var qrRect = new XRect(54, 646, 86, 106);
        var totalsRect = new XRect(360, 658, 192, 84);
        var footerRect = new XRect(54, 754, 498, 48);

        DrawStyle4Party(ctx, supplierRect, SupplierTitle(ctx.Document), ctx.Document.Supplier);
        DrawStyle4Party(ctx, customerRect, CustomerTitle(ctx.Document), ctx.Document.Customer);
        DrawStyle4Meta(ctx, metaRect);
        DrawStyle4Payment(ctx, paymentRect);
        DrawStyle2Note(ctx, noteRect);
        DrawStyle4Items(ctx, itemsRect);
        DrawStyle4Qr(ctx, qrRect);
        DrawStyle4Totals(ctx, totalsRect);
        DrawStyle4Footer(ctx, footerRect);
    }

    private static void DrawStyle5Pdf(RenderContext ctx, double pageWidth, double pageHeight)
    {
        ctx.Gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);

        var outerRect = new XRect(34, 34, pageWidth - 68, pageHeight - 68);
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(1.0), outerRect);

        var barcodeRect = new XRect(42, 42, 104, 18);
        DrawStyle1Barcode(ctx, barcodeRect);

        var titleRect = new XRect(308, 42, 236, 24);
        var title = $"{DocumentTitle(ctx.Document)} {ctx.Document.Number}".Trim();
        ctx.Gfx.DrawString(title, FitFont(ctx, title, 18, titleRect.Width, true), XBrushes.Black, titleRect, XStringFormats.TopRight);

        var supplierRect = new XRect(42, 78, 224, 92);
        var customerRect = new XRect(292, 78, 252, 92);
        var paymentRect = new XRect(42, 178, 224, 68);
        var metaRect = new XRect(292, 178, 252, 82);
        var noteRect = new XRect(42, 270, 502, 20);
        var itemsRect = new XRect(42, 300, 502, GetStyle5ItemsHeight(ctx.Document.Items.Count));
        var qrRect = new XRect(42, 634, 90, 108);
        var totalsRect = new XRect(358, 658, 186, 88);
        var footerRect = new XRect(42, 754, 502, 48);

        DrawStyle5Party(ctx, supplierRect, SupplierTitle(ctx.Document), ctx.Document.Supplier, boxed: false);
        DrawStyle5Party(ctx, customerRect, CustomerTitle(ctx.Document), ctx.Document.Customer, boxed: true);
        DrawStyle5Payment(ctx, paymentRect);
        DrawStyle2Meta(ctx, metaRect);
        DrawStyle2Note(ctx, noteRect);
        DrawStyle5Items(ctx, itemsRect);
        DrawStyle5Qr(ctx, qrRect);
        DrawStyle5Totals(ctx, totalsRect);
        DrawStyle5Footer(ctx, footerRect);
    }

    private static void DrawStyle6Pdf(RenderContext ctx, double pageWidth, double pageHeight)
    {
        ctx.Gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);

        var barcodeRect = new XRect(42, 34, 120, 18);
        DrawStyle1Barcode(ctx, barcodeRect);

        var headerBandRect = new XRect(34, 56, pageWidth - 68, 30);
        ctx.Gfx.DrawRectangle(ctx.Palette.AccentBrush, headerBandRect);
        ctx.Gfx.DrawString(DocumentTitle(ctx.Document).ToUpperInvariant(), ctx.Font(18, true), XBrushes.White, new XRect(headerBandRect.X + 8, headerBandRect.Y + 2, 220, 24), XStringFormats.CenterLeft);
        ctx.Gfx.DrawString(ctx.Document.Number, FitFont(ctx, ctx.Document.Number, 15, 170, true), XBrushes.White, new XRect(headerBandRect.Right - 178, headerBandRect.Y + 2, 170, 24), XStringFormats.CenterRight);

        var supplierRect = new XRect(44, 112, 220, 114);
        var customerRect = new XRect(292, 150, 220, 128);
        var metaRect = new XRect(348, 100, 196, 64);
        var bankRect = new XRect(44, 486, 236, 66);
        var noteRect = new XRect(42, 306, 510, 20);
        var itemsRect = new XRect(42, 344, 510, GetStyle6ItemsHeight(ctx.Document.Items.Count));
        var qrRect = new XRect(42, 662, 92, 112);
        var totalsRect = new XRect(362, 658, 190, 98);
        var footerRect = new XRect(42, 784, 510, 34);

        DrawStyle6Party(ctx, supplierRect, SupplierTitle(ctx.Document), ctx.Document.Supplier);
        DrawStyle6Meta(ctx, metaRect);
        DrawStyle6Party(ctx, customerRect, CustomerTitle(ctx.Document), ctx.Document.Customer);
        DrawStyle2Note(ctx, noteRect);
        DrawStyle6Items(ctx, itemsRect);
        DrawStyle6Payment(ctx, bankRect);
        DrawStyle6Qr(ctx, qrRect);
        DrawStyle6Totals(ctx, totalsRect);
        DrawStyle6Footer(ctx, footerRect);
    }

    private static void DrawStyle1Barcode(RenderContext ctx, XRect rect)
    {
        var bars = (ctx.Document.Number ?? string.Empty).PadRight(16, '0');
        var x = rect.X;
        for (var i = 0; i < bars.Length * 3; i++)
        {
            var width = i % 2 == 0 ? 1.15 : 0.55;
            ctx.Gfx.DrawRectangle(XBrushes.Black, x + (i * 1.9), rect.Y, width, rect.Height);
        }
    }

    private static void DrawStyle2Supplier(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawString(SupplierTitle(ctx.Document), ctx.Font(8.6, true), XBrushes.Black, new XRect(rect.X, rect.Y, rect.Width, 12), XStringFormats.TopLeft);

        var nameFont = ctx.Font(9.6, true);
        var bodyFont = ctx.Font(8.6);
        var y = rect.Y + 18;
        var nameLines = WrapText(ctx, ctx.Document.Supplier.Name, nameFont, rect.Width, 2);
        foreach (var line in nameLines)
        {
            ctx.Gfx.DrawString(line, nameFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 12), XStringFormats.TopLeft);
            y += 12;
        }

        y += 4;
        foreach (var line in BuildStyle1AddressLines(ctx.Document.Supplier, includeCountry: true))
        {
            ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 11), XStringFormats.TopLeft);
            y += 11;
        }

        y += 8;
        DrawStyle1LabelValue(ctx, rect.X, y, 34, "IČO:", ctx.Document.Supplier.Ico);
        y += 12;

        var vatLine = BuildStyle1VatLine(ctx.Document.Supplier);
        if (!string.IsNullOrWhiteSpace(vatLine))
        {
            ctx.Gfx.DrawString(vatLine, bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 11), XStringFormats.TopLeft);
            y += 12;
        }

        if (!string.IsNullOrWhiteSpace(ctx.PaymentDetails.Iban))
        {
            DrawStyle1LabelValue(ctx, rect.X, y, 34, "IBAN:", ctx.PaymentDetails.Iban);
            y += 12;
        }

        if (!string.IsNullOrWhiteSpace(ctx.PaymentDetails.Swift))
            DrawStyle1LabelValue(ctx, rect.X, y, 40, "SWIFT:", ctx.PaymentDetails.Swift);
    }

    private static void DrawStyle3Party(RenderContext ctx, XRect rect, string title, InvoiceParty party, bool includePaymentInfo = false)
    {
        ctx.Gfx.DrawString(title, ctx.Font(9.0, true), XBrushes.Black, new XRect(rect.X, rect.Y, rect.Width, 12), XStringFormats.TopLeft);

        var nameFont = ctx.Font(9.2, true);
        var bodyFont = ctx.Font(8.4);
        var y = rect.Y + 18;
        var nameLines = WrapText(ctx, party.Name, nameFont, rect.Width, 2);
        foreach (var line in nameLines)
        {
            ctx.Gfx.DrawString(line, nameFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 12), XStringFormats.TopLeft);
            y += 11;
        }

        y += 4;
        foreach (var line in BuildStyle1AddressLines(party, includeCountry: true))
        {
            ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 10), XStringFormats.TopLeft);
            y += 10;
        }

        y += 6;
        if (!string.IsNullOrWhiteSpace(party.Ico))
        {
            DrawStyle1LabelValue(ctx, rect.X, y, 30, "IČO:", party.Ico);
            y += 11;
        }

        if (!string.IsNullOrWhiteSpace(party.Dic))
        {
            DrawStyle1LabelValue(ctx, rect.X, y, 30, "DIČ:", party.Dic);
            y += 11;
        }
        else if (!string.IsNullOrWhiteSpace(party.VatId))
        {
            DrawStyle1LabelValue(ctx, rect.X, y, 46, "IČ DPH:", party.VatId);
            y += 11;
        }

        if (!includePaymentInfo)
            return;

        if (!string.IsNullOrWhiteSpace(ctx.PaymentDetails.Iban))
        {
            DrawStyle1LabelValue(ctx, rect.X, y, 34, "IBAN:", ctx.PaymentDetails.Iban);
            y += 11;
        }

        if (!string.IsNullOrWhiteSpace(ctx.PaymentDetails.Swift))
            DrawStyle1LabelValue(ctx, rect.X, y, 40, "SWIFT:", ctx.PaymentDetails.Swift);
    }

    private static void DrawStyle4Party(RenderContext ctx, XRect rect, string title, InvoiceParty party)
    {
        ctx.Gfx.DrawString(title, ctx.Font(8.8, true), XBrushes.Black, new XRect(rect.X, rect.Y, rect.Width, 12), XStringFormats.TopLeft);

        var nameFont = ctx.Font(9.0, true);
        var bodyFont = ctx.Font(8.2);
        var y = rect.Y + 18;
        var nameLines = WrapText(ctx, party.Name, nameFont, rect.Width, 2);
        foreach (var line in nameLines)
        {
            ctx.Gfx.DrawString(line, nameFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 11), XStringFormats.TopLeft);
            y += 10.5;
        }

        y += 2;
        foreach (var line in BuildStyle1AddressLines(party, includeCountry: true))
        {
            ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 10), XStringFormats.TopLeft);
            y += 10;
        }

        y += 6;
        if (!string.IsNullOrWhiteSpace(party.Ico))
        {
            DrawStyle1LabelValue(ctx, rect.X, y, 30, "IČO:", party.Ico);
            y += 11;
        }

        if (!string.IsNullOrWhiteSpace(party.Dic))
            DrawStyle1LabelValue(ctx, rect.X, y, 30, "DIČ:", party.Dic);
        else if (!string.IsNullOrWhiteSpace(party.VatId))
            DrawStyle1LabelValue(ctx, rect.X, y, 46, "IČ DPH:", party.VatId);
    }

    private static void DrawStyle5Party(RenderContext ctx, XRect rect, string title, InvoiceParty party, bool boxed)
    {
        if (boxed)
            ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);

        var insetX = boxed ? rect.X + 8 : rect.X;
        var width = boxed ? rect.Width - 16 : rect.Width;
        ctx.Gfx.DrawString(title, ctx.Font(8.8, true), XBrushes.Black, new XRect(insetX, rect.Y + 4, width, 12), XStringFormats.TopLeft);

        var nameFont = ctx.Font(9.0, true);
        var bodyFont = ctx.Font(8.2);
        var y = rect.Y + 22;
        var nameLines = WrapText(ctx, party.Name, nameFont, width, 2);
        foreach (var line in nameLines)
        {
            ctx.Gfx.DrawString(line, nameFont, XBrushes.Black, new XRect(insetX, y, width, 11), XStringFormats.TopLeft);
            y += 10.5;
        }

        y += 2;
        foreach (var line in BuildStyle1AddressLines(party, includeCountry: true))
        {
            ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(insetX, y, width, 10), XStringFormats.TopLeft);
            y += 10;
        }

        y += 6;
        if (!string.IsNullOrWhiteSpace(party.Ico))
        {
            DrawStyle1LabelValue(ctx, insetX, y, 30, "IČO:", party.Ico);
            y += 11;
        }

        if (!string.IsNullOrWhiteSpace(party.Dic))
            DrawStyle1LabelValue(ctx, insetX, y, 30, "DIČ:", party.Dic);
        else if (!string.IsNullOrWhiteSpace(party.VatId))
            DrawStyle1LabelValue(ctx, insetX, y, 46, "IČ DPH:", party.VatId);
    }

    private static void DrawStyle6Party(RenderContext ctx, XRect rect, string title, InvoiceParty party)
    {
        ctx.Gfx.DrawString(title, ctx.Font(8.8, true), XBrushes.Black, new XRect(rect.X, rect.Y, rect.Width, 12), XStringFormats.TopLeft);

        var nameFont = ctx.Font(9.0, true);
        var bodyFont = ctx.Font(8.2);
        var y = rect.Y + 18;
        var nameLines = WrapText(ctx, party.Name, nameFont, rect.Width, 2);
        foreach (var line in nameLines)
        {
            ctx.Gfx.DrawString(line, nameFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 11), XStringFormats.TopLeft);
            y += 10.5;
        }

        y += 2;
        foreach (var line in BuildStyle1AddressLines(party, includeCountry: true))
        {
            ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 10), XStringFormats.TopLeft);
            y += 10;
        }

        y += 6;
        if (!string.IsNullOrWhiteSpace(party.Ico))
        {
            DrawStyle1LabelValue(ctx, rect.X, y, 30, "IČO:", party.Ico);
            y += 11;
        }

        if (!string.IsNullOrWhiteSpace(party.Dic))
            DrawStyle1LabelValue(ctx, rect.X, y, 30, "DIČ:", party.Dic);
        else if (!string.IsNullOrWhiteSpace(party.VatId))
            DrawStyle1LabelValue(ctx, rect.X, y, 46, "IČ DPH:", party.VatId);
    }

    private static void DrawStyle6Meta(RenderContext ctx, XRect rect)
    {
        var rows = BuildMetaRows(ctx.Document);
        if (rows.Count == 0)
            return;

        var font = ctx.Font(7.7);
        var y = rect.Y;
        foreach (var row in rows.Take(4))
        {
            ctx.Gfx.DrawString(row.Label, ctx.Font(7.7, true), XBrushes.Black, new XRect(rect.X, y, 88, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Ellipsize(ctx, row.Value, font, rect.Width - 92), font, XBrushes.Black, new XRect(rect.X + 92, y, rect.Width - 92, 10), XStringFormats.TopLeft);
            y += 11;
        }
    }

    private static void DrawStyle6Payment(RenderContext ctx, XRect rect)
    {
        var lines = BuildStyle6PaymentLines(ctx.PaymentDetails);
        if (lines.Count == 0)
            return;

        var titleFont = ctx.Font(9.0, true);
        var bodyFont = ctx.Font(8.0);
        ctx.Gfx.DrawString(Res("InvoicesPaymentBankTransfer"), titleFont, XBrushes.Black, new XRect(rect.X, rect.Y, rect.Width, 12), XStringFormats.TopLeft);
        var y = rect.Y + 18;
        foreach (var line in lines)
        {
            var wrapped = WrapText(ctx, line, bodyFont, rect.Width, line.StartsWith("IBAN:", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
            foreach (var wrappedLine in wrapped)
            {
                if (y + 9 > rect.Bottom)
                    return;

                ctx.Gfx.DrawString(Ellipsize(ctx, wrappedLine, bodyFont, rect.Width), bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 9), XStringFormats.TopLeft);
                y += 9;
            }
        }
    }

    private static void DrawStyle5Payment(RenderContext ctx, XRect rect)
    {
        var lines = BuildStyle5PaymentLines(ctx.PaymentDetails);
        if (lines.Count == 0)
            return;

        var titleFont = ctx.Font(8.8, true);
        var bodyFont = ctx.Font(7.9);
        ctx.Gfx.DrawString(Res("InvoicesPaymentBankTransfer"), titleFont, XBrushes.Black, new XRect(rect.X, rect.Y, rect.Width, 12), XStringFormats.TopLeft);
        var y = rect.Y + 18;
        foreach (var line in lines)
        {
            var wrapped = WrapText(ctx, line, bodyFont, rect.Width, line.StartsWith("IBAN:", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
            foreach (var wrappedLine in wrapped)
            {
                if (y + 9 > rect.Bottom)
                    return;

                ctx.Gfx.DrawString(Ellipsize(ctx, wrappedLine, bodyFont, rect.Width), bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 9), XStringFormats.TopLeft);
                y += 9;
            }
        }
    }

    private static void DrawStyle4Meta(RenderContext ctx, XRect rect)
    {
        var rows = BuildMetaRows(ctx.Document);
        if (rows.Count == 0)
            return;

        var font = ctx.Font(7.9);
        var y = rect.Y;
        foreach (var row in rows.Take(4))
        {
            ctx.Gfx.DrawString(row.Label, font, XBrushes.Black, new XRect(rect.X, y, 112, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Ellipsize(ctx, row.Value, font, rect.Width - 116), font, XBrushes.Black, new XRect(rect.X + 116, y, rect.Width - 116, 10), XStringFormats.TopLeft);
            y += 11;
        }
    }

    private static void DrawStyle4Payment(RenderContext ctx, XRect rect)
    {
        var lines = BuildStyle4PaymentLines(ctx.PaymentDetails);
        if (lines.Count == 0)
            return;

        var font = ctx.Font(7.8);
        var y = rect.Y;
        foreach (var line in lines)
        {
            var wrapped = WrapText(ctx, line, font, rect.Width, line.StartsWith("IBAN:", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
            foreach (var wrappedLine in wrapped)
            {
                if (y + 9 > rect.Bottom)
                    return;

                ctx.Gfx.DrawString(Ellipsize(ctx, wrappedLine, font, rect.Width), font, XBrushes.Black, new XRect(rect.X, y, rect.Width, 9), XStringFormats.TopLeft);
                y += 9;
            }
        }
    }

    private static void DrawStyle4Items(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var headerHeight = 18d;
        var columns = GetItemColumns(rect);
        var bodyFont = ctx.Font(8);
        ctx.Gfx.DrawRectangle(ctx.Palette.MutedBrush, rect.X, rect.Y, rect.Width, headerHeight);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rect.Y + headerHeight, rect.Right, rect.Y + headerHeight);
        for (var i = 1; i < columns.Count - 1; i++)
            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), columns[i], rect.Y, columns[i], rect.Bottom);

        DrawItemTableHeader(ctx, columns, rect, headerHeight, white: false, combineQuantityUnit: true);

        var rows = ctx.Document.Items.Take(5).ToList();
        if (rows.Count == 0)
            rows.Add(new InvoiceLineItem());

        var currentY = rect.Y + headerHeight;
        const double rowHeight = 18d;
        foreach (var item in rows)
        {
            if (currentY + rowHeight > rect.Bottom - 18)
                break;

            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, currentY + rowHeight, rect.Right, currentY + rowHeight);
            ctx.Gfx.DrawString(Ellipsize(ctx, item.Description ?? string.Empty, bodyFont, columns[1] - columns[0] - 8), bodyFont, XBrushes.Black, new XRect(columns[0] + 4, currentY + 4, columns[1] - columns[0] - 8, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Style2Decimal(item.Quantity), bodyFont, XBrushes.Black, new XRect(columns[1] + 3, currentY + 4, columns[2] - columns[1] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(item.Unit, bodyFont, XBrushes.Black, new XRect(columns[2] + 3, currentY + 4, columns[3] - columns[2] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(Style2Decimal(item.UnitPrice), bodyFont, XBrushes.Black, new XRect(columns[3] + 4, currentY + 4, columns[4] - columns[3] - 8, 10), XStringFormats.TopRight);
            ctx.Gfx.DrawString(Style2Decimal(item.TotalAmount), bodyFont, XBrushes.Black, new XRect(columns[4] + 4, currentY + 4, columns[5] - columns[4] - 8, 10), XStringFormats.TopRight);
            currentY += rowHeight;
        }
    }

    private static void DrawStyle5Items(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var headerHeight = 18d;
        var columns = GetItemColumns(rect);
        var bodyFont = ctx.Font(8);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rect.Y + headerHeight, rect.Right, rect.Y + headerHeight);
        for (var i = 1; i < columns.Count - 1; i++)
            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), columns[i], rect.Y, columns[i], rect.Bottom);

        DrawItemTableHeader(ctx, columns, rect, headerHeight, white: false);

        var rows = ctx.Document.Items.Take(5).ToList();
        if (rows.Count == 0)
            rows.Add(new InvoiceLineItem());

        var currentY = rect.Y + headerHeight;
        const double rowHeight = 18d;
        foreach (var item in rows)
        {
            if (currentY + rowHeight > rect.Bottom - 18)
                break;

            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, currentY + rowHeight, rect.Right, currentY + rowHeight);
            ctx.Gfx.DrawString(Ellipsize(ctx, item.Description ?? string.Empty, bodyFont, columns[1] - columns[0] - 8), bodyFont, XBrushes.Black, new XRect(columns[0] + 4, currentY + 4, columns[1] - columns[0] - 8, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Style2Decimal(item.Quantity), bodyFont, XBrushes.Black, new XRect(columns[1] + 3, currentY + 4, columns[2] - columns[1] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(item.Unit, bodyFont, XBrushes.Black, new XRect(columns[2] + 3, currentY + 4, columns[3] - columns[2] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(Style2Decimal(item.UnitPrice), bodyFont, XBrushes.Black, new XRect(columns[3] + 4, currentY + 4, columns[4] - columns[3] - 8, 10), XStringFormats.TopRight);
            ctx.Gfx.DrawString(Style2Decimal(item.TotalAmount), bodyFont, XBrushes.Black, new XRect(columns[4] + 4, currentY + 4, columns[5] - columns[4] - 8, 10), XStringFormats.TopRight);
            currentY += rowHeight;
        }

        ctx.Gfx.DrawString("Spolu:", ctx.Font(8, true), XBrushes.Black, new XRect(rect.X + 4, rect.Bottom - 14, rect.Width - 88, 10), XStringFormats.CenterRight);
        ctx.Gfx.DrawString(Style2Money(ctx.Document.TotalAmount, ctx.Document.Currency), ctx.Font(8, true), XBrushes.Black, new XRect(rect.Right - 84, rect.Bottom - 14, 80, 10), XStringFormats.TopRight);
    }

    private static void DrawStyle6Items(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var headerHeight = 18d;
        var columns = GetItemColumns(rect);
        var bodyFont = ctx.Font(8);
        ctx.Gfx.DrawRectangle(ctx.Palette.AccentBrush, rect.X, rect.Y, rect.Width, headerHeight);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rect.Y + headerHeight, rect.Right, rect.Y + headerHeight);
        for (var i = 1; i < columns.Count - 1; i++)
            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), columns[i], rect.Y, columns[i], rect.Bottom);

        DrawItemTableHeader(ctx, columns, rect, headerHeight, white: true);

        var rows = ctx.Document.Items.Take(5).ToList();
        if (rows.Count == 0)
            rows.Add(new InvoiceLineItem());

        var currentY = rect.Y + headerHeight;
        const double rowHeight = 18d;
        foreach (var item in rows)
        {
            if (currentY + rowHeight > rect.Bottom - 18)
                break;

            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, currentY + rowHeight, rect.Right, currentY + rowHeight);
            ctx.Gfx.DrawString(Ellipsize(ctx, item.Description ?? string.Empty, bodyFont, columns[1] - columns[0] - 8), bodyFont, XBrushes.Black, new XRect(columns[0] + 4, currentY + 4, columns[1] - columns[0] - 8, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Style2Decimal(item.Quantity), bodyFont, XBrushes.Black, new XRect(columns[1] + 3, currentY + 4, columns[2] - columns[1] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(item.Unit, bodyFont, XBrushes.Black, new XRect(columns[2] + 3, currentY + 4, columns[3] - columns[2] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(Style2Decimal(item.UnitPrice), bodyFont, XBrushes.Black, new XRect(columns[3] + 4, currentY + 4, columns[4] - columns[3] - 8, 10), XStringFormats.TopRight);
            ctx.Gfx.DrawString(Style2Decimal(item.TotalAmount), bodyFont, XBrushes.Black, new XRect(columns[4] + 4, currentY + 4, columns[5] - columns[4] - 8, 10), XStringFormats.TopRight);
            currentY += rowHeight;
        }

        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(0.6), rect.X, rect.Bottom - 18, rect.Right, rect.Bottom - 18);
        ctx.Gfx.DrawString("Spolu:", ctx.Font(8, true), XBrushes.Black, new XRect(rect.X + 4, rect.Bottom - 14, rect.Width - 88, 10), XStringFormats.CenterRight);
        ctx.Gfx.DrawString(Style2Money(ctx.Document.TotalAmount, ctx.Document.Currency), ctx.Font(8, true), XBrushes.Black, new XRect(rect.Right - 84, rect.Bottom - 14, 80, 10), XStringFormats.TopRight);
    }

    private static void DrawStyle4Qr(RenderContext ctx, XRect rect)
    {
        if (ctx.QrPreview.PngBytes == null)
            return;

        var tempBmpPath = Path.Combine(Path.GetTempPath(), $"agency-contractor-style4-qr-{Guid.NewGuid():N}.bmp");
        try
        {
            WriteBitmapCompatibleQr(ctx.QrPreview.PngBytes, tempBmpPath);
            using var image = XImage.FromFile(tempBmpPath);
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentPen, rect);
            var footerHeight = 20d;
            var imageSize = Math.Min(rect.Width - 16, rect.Height - footerHeight - 10);
            ctx.Gfx.DrawImage(image, rect.X + 8, rect.Y + 8, imageSize, imageSize);
            ctx.Gfx.DrawString(Res("InvoicesQrPaymentLabel"), ctx.Font(7.0, true), ctx.Palette.AccentBrush, new XRect(rect.X + 4, rect.Bottom - 20, rect.Width - 8, 8), XStringFormats.TopLeft);
        }
        finally
        {
            try
            {
                if (File.Exists(tempBmpPath))
                    File.Delete(tempBmpPath);
            }
            catch
            {
            }
        }
    }

    private static void DrawStyle5Qr(RenderContext ctx, XRect rect)
    {
        if (ctx.QrPreview.PngBytes == null)
            return;

        var tempBmpPath = Path.Combine(Path.GetTempPath(), $"agency-contractor-style5-qr-{Guid.NewGuid():N}.bmp");
        try
        {
            WriteBitmapCompatibleQr(ctx.QrPreview.PngBytes, tempBmpPath);
            using var image = XImage.FromFile(tempBmpPath);
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentPen, rect);
            var footerHeight = 22d;
            var imageSize = Math.Min(rect.Width - 16, rect.Height - footerHeight - 10);
            ctx.Gfx.DrawImage(image, rect.X + 8, rect.Y + 8, imageSize, imageSize);
            ctx.Gfx.DrawString(Res("InvoicesQrPaymentLabel"), ctx.Font(7.0, true), ctx.Palette.AccentBrush, new XRect(rect.X + 4, rect.Bottom - 20, rect.Width - 8, 8), XStringFormats.TopLeft);
            var footerLines = BuildQrFooterLines(ctx.PaymentDetails);
            var lineY = rect.Bottom - 12;
            foreach (var line in footerLines.Take(2))
            {
                ctx.Gfx.DrawString(Ellipsize(ctx, line, ctx.Font(6.2), rect.Width - 8), ctx.Font(6.2), XBrushes.Black, new XRect(rect.X + 4, lineY, rect.Width - 8, 7), XStringFormats.TopLeft);
                lineY += 7;
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempBmpPath))
                    File.Delete(tempBmpPath);
            }
            catch
            {
            }
        }
    }

    private static void DrawStyle6Qr(RenderContext ctx, XRect rect)
    {
        if (ctx.QrPreview.PngBytes == null)
            return;

        var tempBmpPath = Path.Combine(Path.GetTempPath(), $"agency-contractor-style6-qr-{Guid.NewGuid():N}.bmp");
        try
        {
            WriteBitmapCompatibleQr(ctx.QrPreview.PngBytes, tempBmpPath);
            using var image = XImage.FromFile(tempBmpPath);
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentPen, rect);
            var footerHeight = 24d;
            var imageSize = Math.Min(rect.Width - 16, rect.Height - footerHeight - 10);
            ctx.Gfx.DrawImage(image, rect.X + 8, rect.Y + 8, imageSize, imageSize);
            ctx.Gfx.DrawString(Res("InvoicesQrPaymentLabel"), ctx.Font(7.2, true), ctx.Palette.AccentBrush, new XRect(rect.X + 4, rect.Bottom - 22, rect.Width - 8, 8), XStringFormats.TopLeft);
            var footerLines = BuildQrFooterLines(ctx.PaymentDetails);
            var lineY = rect.Bottom - 14;
            foreach (var line in footerLines.Take(2))
            {
                ctx.Gfx.DrawString(Ellipsize(ctx, line, ctx.Font(6.2), rect.Width - 8), ctx.Font(6.2), XBrushes.Black, new XRect(rect.X + 4, lineY, rect.Width - 8, 7), XStringFormats.TopLeft);
                lineY += 7;
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempBmpPath))
                    File.Delete(tempBmpPath);
            }
            catch
            {
            }
        }
    }

    private static void DrawStyle4Totals(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var summaryRect = new XRect(rect.X + 8, rect.Y + 8, rect.Width - 16, 28);
        var dueRect = new XRect(rect.X + 8, rect.Bottom - 36, rect.Width - 16, 26);

        var labels = new[]
        {
            ("Celková částka:", Style2Money(ctx.Document.TotalAmount, ctx.Document.Currency)),
            ("Uhrazeno zálohami:", Style2Money(ctx.Document.AlreadyPaidAmount, ctx.Document.Currency)),
            ("Zbývá uhradit:", Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency))
        };

        var y = summaryRect.Y;
        foreach (var (label, value) in labels)
        {
            ctx.Gfx.DrawString(label, ctx.Font(7.8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width * 0.58, 8), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(value, ctx.Font(7.8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width, 8), XStringFormats.TopRight);
            y += 8;
        }

        ctx.Gfx.DrawString("K úhradě:", ctx.Font(9.2, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y, dueRect.Width, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency), FitFont(ctx, Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency), 15.5, dueRect.Width, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y + 9, dueRect.Width, 14), XStringFormats.TopRight);
    }

    private static void DrawStyle5Totals(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var summaryRect = new XRect(rect.X + 8, rect.Y + 8, rect.Width - 16, 28);
        var dueRect = new XRect(rect.X + 8, rect.Bottom - 36, rect.Width - 16, 26);

        var labels = new[]
        {
            ("Celková částka:", Style2Money(ctx.Document.TotalAmount, ctx.Document.Currency)),
            ("Uhrazeno zálohami:", Style2Money(ctx.Document.AlreadyPaidAmount, ctx.Document.Currency)),
            ("Zbývá uhradit:", Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency))
        };

        var y = summaryRect.Y;
        foreach (var (label, value) in labels)
        {
            ctx.Gfx.DrawString(label, ctx.Font(7.8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width * 0.58, 8), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(value, ctx.Font(7.8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width, 8), XStringFormats.TopRight);
            y += 8;
        }

        ctx.Gfx.DrawString("K úhradě:", ctx.Font(9.2, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y, dueRect.Width, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency), FitFont(ctx, Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency), 15.5, dueRect.Width, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y + 9, dueRect.Width, 14), XStringFormats.TopRight);
    }

    private static void DrawStyle6Totals(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var summaryRect = new XRect(rect.X + 8, rect.Y + 8, rect.Width - 16, 34);
        var dueBandRect = new XRect(rect.X, rect.Bottom - 34, rect.Width, 34);

        var labels = new[]
        {
            ("Celková částka:", Style2Money(ctx.Document.TotalAmount, ctx.Document.Currency)),
            ("Uhrazeno zálohami:", Style2Money(ctx.Document.AlreadyPaidAmount, ctx.Document.Currency)),
            ("Zbývá uhradit:", Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency))
        };

        var y = summaryRect.Y;
        foreach (var (label, value) in labels)
        {
            ctx.Gfx.DrawString(label, ctx.Font(7.8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width * 0.58, 8), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(value, ctx.Font(7.8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width, 8), XStringFormats.TopRight);
            y += 8;
        }

        ctx.Gfx.DrawRectangle(ctx.Palette.AccentBrush, dueBandRect);
        ctx.Gfx.DrawString("K úhradě:", ctx.Font(9.2, true), XBrushes.White, new XRect(dueBandRect.X + 8, dueBandRect.Y + 4, dueBandRect.Width - 16, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style1Money(ctx.Document.AmountToPay, ctx.Document.Currency), FitFont(ctx, Style1Money(ctx.Document.AmountToPay, ctx.Document.Currency), 14.5, dueBandRect.Width - 16, true), XBrushes.White, new XRect(dueBandRect.X + 8, dueBandRect.Y + 12, dueBandRect.Width - 16, 16), XStringFormats.TopRight);
    }

    private static void DrawStyle4Footer(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var columnWidth = rect.Width / 2d;
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X + columnWidth, rect.Y, rect.X + columnWidth, rect.Bottom);
        ctx.Gfx.DrawString("Vyhotovil:", ctx.Font(7.9), XBrushes.Black, new XRect(rect.X + 8, rect.Y + 8, columnWidth - 16, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString("Převzal:", ctx.Font(7.9), XBrushes.Black, new XRect(rect.X + columnWidth + 8, rect.Y + 8, columnWidth - 16, 10), XStringFormats.TopLeft);
    }

    private static void DrawStyle5Footer(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var columnWidth = rect.Width / 2d;
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X + columnWidth, rect.Y, rect.X + columnWidth, rect.Bottom);
        ctx.Gfx.DrawString("Vyhotovil:", ctx.Font(7.9), XBrushes.Black, new XRect(rect.X + 8, rect.Y + 8, columnWidth - 16, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString("Převzal:", ctx.Font(7.9), XBrushes.Black, new XRect(rect.X + columnWidth + 8, rect.Y + 8, columnWidth - 16, 10), XStringFormats.TopLeft);
    }

    private static void DrawStyle6Footer(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rect.Y, rect.Right, rect.Y);
        var columnWidth = rect.Width / 3d;
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(0.6), rect.X + columnWidth, rect.Y, rect.X + columnWidth, rect.Bottom);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(0.6), rect.X + (columnWidth * 2d), rect.Y, rect.X + (columnWidth * 2d), rect.Bottom);
        ctx.Gfx.DrawString("Vyhotovil:", ctx.Font(7.8), XBrushes.Black, new XRect(rect.X + 8, rect.Y + 4, columnWidth - 16, 9), XStringFormats.TopLeft);
        ctx.Gfx.DrawString("Převzal:", ctx.Font(7.8), XBrushes.Black, new XRect(rect.X + columnWidth + 8, rect.Y + 4, columnWidth - 16, 9), XStringFormats.TopLeft);
    }

    private static void DrawStyle3Bank(RenderContext ctx, XRect rect)
    {
        var bodyFont = ctx.Font(7.8);
        var lines = BuildStyle3PaymentLines(ctx.PaymentDetails);
        if (lines.Count == 0)
            return;

        ctx.Gfx.DrawString(Res("InvoicesPaymentBankTransfer"), ctx.Font(9.0, true), XBrushes.Black, new XRect(rect.X, rect.Y, rect.Width, 12), XStringFormats.TopLeft);
        var y = rect.Y + 18;
        foreach (var line in lines)
        {
            var wrapped = WrapText(ctx, line, bodyFont, rect.Width, line.StartsWith("IBAN:", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
            foreach (var wrappedLine in wrapped)
            {
                if (y + 9 > rect.Bottom)
                    return;

                ctx.Gfx.DrawString(Ellipsize(ctx, wrappedLine, bodyFont, rect.Width), bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 9), XStringFormats.TopLeft);
                y += 9;
            }
        }
    }

    private static void DrawStyle3Meta(RenderContext ctx, XRect rect)
    {
        var rows = BuildMetaRows(ctx.Document);
        if (rows.Count == 0)
            return;

        var font = ctx.Font(8.0);
        var y = rect.Y;
        foreach (var row in rows.Take(3))
        {
            ctx.Gfx.DrawString(row.Label, font, XBrushes.Black, new XRect(rect.X, y, 106, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Ellipsize(ctx, row.Value, font, rect.Width - 110), font, XBrushes.Black, new XRect(rect.X + 110, y, rect.Width - 110, 10), XStringFormats.TopLeft);
            y += 12;
        }
    }

    private static void DrawStyle3Items(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var headerHeight = 18d;
        var columns = GetItemColumns(rect);
        var bodyFont = ctx.Font(8);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rect.Y + headerHeight, rect.Right, rect.Y + headerHeight);
        for (var i = 1; i < columns.Count - 1; i++)
            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), columns[i], rect.Y, columns[i], rect.Bottom);

        DrawItemTableHeader(ctx, columns, rect, headerHeight, white: false);

        var rows = ctx.Document.Items.Take(5).ToList();
        if (rows.Count == 0)
            rows.Add(new InvoiceLineItem());

        var currentY = rect.Y + headerHeight;
        const double rowHeight = 18d;
        foreach (var item in rows)
        {
            if (currentY + rowHeight > rect.Bottom)
                break;

            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, currentY + rowHeight, rect.Right, currentY + rowHeight);
            ctx.Gfx.DrawString(Ellipsize(ctx, item.Description ?? string.Empty, bodyFont, columns[1] - columns[0] - 8), bodyFont, XBrushes.Black, new XRect(columns[0] + 4, currentY + 4, columns[1] - columns[0] - 8, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Style2Decimal(item.Quantity), bodyFont, XBrushes.Black, new XRect(columns[1] + 3, currentY + 4, columns[2] - columns[1] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(item.Unit, bodyFont, XBrushes.Black, new XRect(columns[2] + 3, currentY + 4, columns[3] - columns[2] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(Style2Decimal(item.UnitPrice), bodyFont, XBrushes.Black, new XRect(columns[3] + 4, currentY + 4, columns[4] - columns[3] - 8, 10), XStringFormats.TopRight);
            ctx.Gfx.DrawString(Style2Decimal(item.TotalAmount), bodyFont, XBrushes.Black, new XRect(columns[4] + 4, currentY + 4, columns[5] - columns[4] - 8, 10), XStringFormats.TopRight);
            currentY += rowHeight;
        }
    }

    private static void DrawStyle3Qr(RenderContext ctx, XRect rect)
    {
        if (ctx.QrPreview.PngBytes == null)
            return;

        var tempBmpPath = Path.Combine(Path.GetTempPath(), $"agency-contractor-style3-qr-{Guid.NewGuid():N}.bmp");
        try
        {
            WriteBitmapCompatibleQr(ctx.QrPreview.PngBytes, tempBmpPath);
            using var image = XImage.FromFile(tempBmpPath);
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentPen, rect);
            var footerHeight = 24d;
            var imageSize = Math.Min(rect.Width - 14, rect.Height - footerHeight - 10);
            ctx.Gfx.DrawImage(image, rect.X + 7, rect.Y + 7, imageSize, imageSize);
            ctx.Gfx.DrawString(Res("InvoicesQrPaymentLabel"), ctx.Font(7.2, true), ctx.Palette.AccentBrush, new XRect(rect.X + 4, rect.Bottom - 22, rect.Width - 8, 8), XStringFormats.TopLeft);
            var footerLines = BuildQrFooterLines(ctx.PaymentDetails);
            var lineY = rect.Bottom - 14;
            foreach (var line in footerLines.Take(2))
            {
                ctx.Gfx.DrawString(Ellipsize(ctx, line, ctx.Font(6.4), rect.Width - 8), ctx.Font(6.4), XBrushes.Black, new XRect(rect.X + 4, lineY, rect.Width - 8, 7), XStringFormats.TopLeft);
                lineY += 7;
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempBmpPath))
                    File.Delete(tempBmpPath);
            }
            catch
            {
            }
        }
    }

    private static void DrawStyle3Totals(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var summaryRect = new XRect(rect.X + 8, rect.Y + 7, rect.Width - 16, 28);
        var dueRect = new XRect(rect.X + 8, rect.Bottom - 34, rect.Width - 16, 24);

        var labels = new[]
        {
            ("Celková částka:", Style2Money(ctx.Document.TotalAmount, ctx.Document.Currency)),
            ("Uhrazeno zálohami:", Style2Money(ctx.Document.AlreadyPaidAmount, ctx.Document.Currency)),
            ("Zbývá uhradit:", Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency))
        };

        var y = summaryRect.Y;
        foreach (var (label, value) in labels)
        {
            ctx.Gfx.DrawString(label, ctx.Font(7.8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width * 0.58, 8), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(value, ctx.Font(7.8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width, 8), XStringFormats.TopRight);
            y += 8;
        }

        ctx.Gfx.DrawString("K úhradě:", ctx.Font(9.0, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y, dueRect.Width, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency), FitFont(ctx, Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency), 14.5, dueRect.Width, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y + 9, dueRect.Width, 13), XStringFormats.TopRight);
    }

    private static void DrawStyle3Footer(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var columnWidth = rect.Width / 2d;
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X + columnWidth, rect.Y, rect.X + columnWidth, rect.Bottom);
        ctx.Gfx.DrawString("Vyhotovil:", ctx.Font(7.8), XBrushes.Black, new XRect(rect.X + 8, rect.Y + 8, columnWidth - 16, 9), XStringFormats.TopLeft);
        ctx.Gfx.DrawString("Převzal:", ctx.Font(7.8), XBrushes.Black, new XRect(rect.X + columnWidth + 8, rect.Y + 8, columnWidth - 16, 9), XStringFormats.TopLeft);
    }

    private static void DrawStyle2Customer(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        ctx.Gfx.DrawString(CustomerTitle(ctx.Document), ctx.Font(8.6, true), XBrushes.Black, new XRect(rect.X + 8, rect.Y + 4, rect.Width - 16, 11), XStringFormats.TopLeft);

        var nameFont = ctx.Font(9.4, true);
        var bodyFont = ctx.Font(8.5);
        var y = rect.Y + 22;
        var nameLines = WrapText(ctx, ctx.Document.Customer.Name, nameFont, rect.Width - 16, 2);
        foreach (var line in nameLines)
        {
            ctx.Gfx.DrawString(line, nameFont, XBrushes.Black, new XRect(rect.X + 10, y, rect.Width - 20, 12), XStringFormats.TopLeft);
            y += 12;
        }

        y += 2;
        foreach (var line in BuildStyle1AddressLines(ctx.Document.Customer, includeCountry: true))
        {
            ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(rect.X + 10, y, rect.Width - 20, 11), XStringFormats.TopLeft);
            y += 11;
        }

        y += 8;
        if (!string.IsNullOrWhiteSpace(ctx.Document.Customer.Ico))
        {
            DrawStyle1LabelValue(ctx, rect.X + 10, y, 34, "IČO:", ctx.Document.Customer.Ico);
            y += 11;
        }

        if (!string.IsNullOrWhiteSpace(ctx.Document.Customer.Dic))
            DrawStyle1LabelValue(ctx, rect.X + 10, y, 34, "DIČ:", ctx.Document.Customer.Dic);
        else if (!string.IsNullOrWhiteSpace(ctx.Document.Customer.VatId))
            DrawStyle1LabelValue(ctx, rect.X + 10, y, 50, "IČ DPH:", ctx.Document.Customer.VatId);
    }

    private static void DrawStyle2Meta(RenderContext ctx, XRect rect)
    {
        var rows = BuildMetaRows(ctx.Document);
        if (rows.Count == 0)
            return;

        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var rowHeight = rect.Height / rows.Count;
        var splitX = rect.X + (rect.Width * 0.56);
        var font = ctx.Font(7.4);

        for (var i = 0; i < rows.Count; i++)
        {
            var rowY = rect.Y + (i * rowHeight);
            if (i > 0)
                ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rowY, rect.Right, rowY);

            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), splitX, rowY, splitX, rowY + rowHeight);
            ctx.Gfx.DrawString(Ellipsize(ctx, rows[i].Label, font, splitX - rect.X - 8), font, XBrushes.Black, new XRect(rect.X + 4, rowY + 2, splitX - rect.X - 8, rowHeight - 4), XStringFormats.CenterLeft);
            ctx.Gfx.DrawString(Ellipsize(ctx, rows[i].Value, font, rect.Right - splitX - 8), font, XBrushes.Black, new XRect(splitX + 4, rowY + 2, rect.Right - splitX - 8, rowHeight - 4), XStringFormats.CenterLeft);
        }
    }

    private static void DrawStyle2Note(RenderContext ctx, XRect rect)
    {
        var note = !string.IsNullOrWhiteSpace(ctx.Document.NotesAbove)
            ? ctx.Document.NotesAbove.Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(note))
            return;

        var lines = WrapText(ctx, note, ctx.Font(8), rect.Width, 2);
        var y = rect.Y;
        foreach (var line in lines)
        {
            ctx.Gfx.DrawString(line, ctx.Font(8), XBrushes.Black, new XRect(rect.X, y, rect.Width, 10), XStringFormats.TopLeft);
            y += 10;
        }
    }

    private static void DrawStyle2Items(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var headerHeight = 18d;
        var columns = GetItemColumns(rect);
        var bodyFont = ctx.Font(8);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rect.Y + headerHeight, rect.Right, rect.Y + headerHeight);
        for (var i = 1; i < columns.Count - 1; i++)
            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), columns[i], rect.Y, columns[i], rect.Bottom);

        DrawItemTableHeader(ctx, columns, rect, headerHeight, white: false);

        var rows = ctx.Document.Items.Take(5).ToList();
        if (rows.Count == 0)
            rows.Add(new InvoiceLineItem());

        var currentY = rect.Y + headerHeight;
        const double rowHeight = 18d;
        foreach (var item in rows)
        {
            if (currentY + rowHeight > rect.Bottom)
                break;

            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, currentY + rowHeight, rect.Right, currentY + rowHeight);
            ctx.Gfx.DrawString(Ellipsize(ctx, item.Description ?? string.Empty, bodyFont, columns[1] - columns[0] - 8), bodyFont, XBrushes.Black, new XRect(columns[0] + 4, currentY + 4, columns[1] - columns[0] - 8, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Style2Decimal(item.Quantity), bodyFont, XBrushes.Black, new XRect(columns[1] + 3, currentY + 4, columns[2] - columns[1] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(item.Unit, bodyFont, XBrushes.Black, new XRect(columns[2] + 3, currentY + 4, columns[3] - columns[2] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(Style2Decimal(item.UnitPrice), bodyFont, XBrushes.Black, new XRect(columns[3] + 4, currentY + 4, columns[4] - columns[3] - 8, 10), XStringFormats.TopRight);
            ctx.Gfx.DrawString(Style2Decimal(item.TotalAmount), bodyFont, XBrushes.Black, new XRect(columns[4] + 4, currentY + 4, columns[5] - columns[4] - 8, 10), XStringFormats.TopRight);
            currentY += rowHeight;
        }
    }

    private static void DrawStyle2Qr(RenderContext ctx, XRect rect)
    {
        if (ctx.QrPreview.PngBytes == null)
            return;

        var tempBmpPath = Path.Combine(Path.GetTempPath(), $"agency-contractor-style2-qr-{Guid.NewGuid():N}.bmp");
        try
        {
            WriteBitmapCompatibleQr(ctx.QrPreview.PngBytes, tempBmpPath);
            using var image = XImage.FromFile(tempBmpPath);
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentPen, rect);
            var footerHeight = 18d;
            var imageSize = Math.Min(rect.Width - 16, rect.Height - footerHeight - 12);
            ctx.Gfx.DrawImage(image, rect.X + 8, rect.Y + 8, imageSize, imageSize);
            var footer = $"{Res("InvoicesQrPaymentLabel")} {Style1CurrencySymbol(ctx.Document.Currency)}".Trim();
            ctx.Gfx.DrawString(footer, ctx.Font(7.8, true), ctx.Palette.AccentBrush, new XRect(rect.X + 4, rect.Bottom - 18, rect.Width - 8, 10), XStringFormats.TopLeft);
        }
        finally
        {
            try
            {
                if (File.Exists(tempBmpPath))
                    File.Delete(tempBmpPath);
            }
            catch
            {
            }
        }
    }

    private static void DrawStyle2Totals(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var summaryRect = new XRect(rect.X + 8, rect.Y + 6, rect.Width - 16, 34);
        var dueRect = new XRect(rect.X + 8, rect.Bottom - 32, rect.Width - 16, 24);

        var labels = new[]
        {
            ("Celková částka:", Style2Money(ctx.Document.TotalAmount, ctx.Document.Currency)),
            ("Uhrazeno zálohami:", Style2Money(ctx.Document.AlreadyPaidAmount, ctx.Document.Currency)),
            ("Zbývá uhradit:", Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency))
        };

        var y = summaryRect.Y;
        foreach (var (label, value) in labels)
        {
            ctx.Gfx.DrawString(label, ctx.Font(8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width * 0.58, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(value, ctx.Font(8), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width, 10), XStringFormats.TopRight);
            y += 10;
        }

        ctx.Gfx.DrawString("K úhradě:", ctx.Font(9.2, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y, dueRect.Width, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency), FitFont(ctx, Style2Money(ctx.Document.AmountToPay, ctx.Document.Currency), 16, dueRect.Width, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y + 8, dueRect.Width, 14), XStringFormats.TopRight);
    }

    private static void DrawStyle2Footer(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var columnWidth = rect.Width / 2d;
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X + columnWidth, rect.Y, rect.X + columnWidth, rect.Bottom);
        ctx.Gfx.DrawString("Vyhotovil:", ctx.Font(8.5), XBrushes.Black, new XRect(rect.X + 8, rect.Y + 6, columnWidth - 16, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString("Převzal:", ctx.Font(8.5), XBrushes.Black, new XRect(rect.X + columnWidth + 8, rect.Y + 6, columnWidth - 16, 10), XStringFormats.TopLeft);
    }

    private static void DrawStyle1Supplier(RenderContext ctx, XRect rect)
    {
        var nameFont = ctx.Font(10, true);
        var bodyFont = ctx.Font(8.5);
        var labelFont = ctx.Font(8.3);
        var y = rect.Y;

        DrawWrappedText(ctx.Gfx, ctx.Document.Supplier.Name, nameFont, new XRect(rect.X, y, rect.Width, 16), 2);
        y += 24;

        foreach (var line in BuildStyle1AddressLines(ctx.Document.Supplier, includeCountry: true))
        {
            ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 11), XStringFormats.TopLeft);
            y += 11;
        }

        y += 10;
        DrawStyle1LabelValue(ctx, rect.X, y, 74, "IČO:", ctx.Document.Supplier.Ico);
        y += 14;

        var vatLine = BuildStyle1VatLine(ctx.Document.Supplier);
        if (!string.IsNullOrWhiteSpace(vatLine))
        {
            ctx.Gfx.DrawString(vatLine, bodyFont, XBrushes.Black, new XRect(rect.X, y, rect.Width, 11), XStringFormats.TopLeft);
            y += 14;
        }

        if (!string.IsNullOrWhiteSpace(ctx.PaymentDetails.Iban))
        {
            ctx.Gfx.DrawString("IBAN:", labelFont, XBrushes.Black, new XRect(rect.X, y, 74, 11), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(ctx.PaymentDetails.Iban, bodyFont, XBrushes.Black, new XRect(rect.X + 44, y, rect.Width - 44, 11), XStringFormats.TopLeft);
            y += 12;
        }

        if (!string.IsNullOrWhiteSpace(ctx.PaymentDetails.Swift))
        {
            ctx.Gfx.DrawString("SWIFT:", labelFont, XBrushes.Black, new XRect(rect.X, y, 74, 11), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(ctx.PaymentDetails.Swift, bodyFont, XBrushes.Black, new XRect(rect.X + 44, y, rect.Width - 44, 11), XStringFormats.TopLeft);
        }
    }

    private static void DrawStyle1Customer(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        ctx.Gfx.DrawString(CustomerTitle(ctx.Document), ctx.Font(8.2), XBrushes.Black, new XRect(rect.X + 6, rect.Y + 4, rect.Width - 12, 11), XStringFormats.TopLeft);

        var nameFont = ctx.Font(9.8, true);
        var bodyFont = ctx.Font(8.7);
        var y = rect.Y + 24;
        var nameLines = WrapText(ctx, ctx.Document.Customer.Name, nameFont, rect.Width - 28, 2);
        foreach (var line in nameLines)
        {
            ctx.Gfx.DrawString(line, nameFont, XBrushes.Black, new XRect(rect.X + 32, y, rect.Width - 38, 14), XStringFormats.TopLeft);
            y += 13;
        }

        y += 2;
        foreach (var line in BuildStyle1AddressLines(ctx.Document.Customer, includeCountry: true))
        {
            ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(rect.X + 32, y, rect.Width - 38, 11), XStringFormats.TopLeft);
            y += 11;
        }

        y += 8;
        DrawStyle1LabelValue(ctx, rect.X + 32, y, 34, "IČO:", ctx.Document.Customer.Ico);
        y += 12;
        if (!string.IsNullOrWhiteSpace(ctx.Document.Customer.Dic))
        {
            DrawStyle1LabelValue(ctx, rect.X + 32, y, 34, "DIČ:", ctx.Document.Customer.Dic);
            y += 12;
        }
        else if (!string.IsNullOrWhiteSpace(ctx.Document.Customer.VatId))
        {
            DrawStyle1LabelValue(ctx, rect.X + 32, y, 50, "IČ DPH:", ctx.Document.Customer.VatId);
        }
    }

    private static void DrawStyle1MetaTable(RenderContext ctx, XRect rect)
    {
        var rows = BuildMetaRows(ctx.Document);
        if (rows.Count == 0)
            return;

        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var rowHeight = rect.Height / rows.Count;
        var splitX = rect.X + (rect.Width * 0.56);
        var labelFont = ctx.Font(7.5);
        var valueFont = ctx.Font(7.5);

        for (var i = 0; i < rows.Count; i++)
        {
            var rowY = rect.Y + (i * rowHeight);
            if (i > 0)
                ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rowY, rect.Right, rowY);

            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), splitX, rowY, splitX, rowY + rowHeight);

            var labelRect = new XRect(rect.X + 4, rowY + 2, splitX - rect.X - 8, rowHeight - 4);
            var valueRect = new XRect(splitX + 4, rowY + 2, rect.Right - splitX - 8, rowHeight - 4);

            ctx.Gfx.DrawString(Ellipsize(ctx, rows[i].Label, labelFont, labelRect.Width), labelFont, XBrushes.Black, labelRect, XStringFormats.CenterLeft);
            ctx.Gfx.DrawString(Ellipsize(ctx, rows[i].Value, valueFont, valueRect.Width), valueFont, XBrushes.Black, valueRect, XStringFormats.CenterLeft);
        }
    }

    private static void DrawStyle1Note(RenderContext ctx, XRect rect)
    {
        var note = !string.IsNullOrWhiteSpace(ctx.Document.NotesAbove)
            ? ctx.Document.NotesAbove.Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(note))
            return;

        var lines = WrapText(ctx, note, ctx.Font(8), rect.Width, 2);
        var y = rect.Y;
        foreach (var line in lines)
        {
            ctx.Gfx.DrawString(line, ctx.Font(8), XBrushes.Black, new XRect(rect.X, y, rect.Width, 10), XStringFormats.TopLeft);
            y += 10;
        }
    }

    private static void DrawStyle1Items(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var headerHeight = 18d;
        var columns = GetItemColumns(rect);
        var bodyFont = ctx.Font(8);

        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, rect.Y + headerHeight, rect.Right, rect.Y + headerHeight);
        for (var i = 1; i < columns.Count - 1; i++)
            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), columns[i], rect.Y, columns[i], rect.Bottom);

        DrawItemTableHeader(ctx, columns, rect, headerHeight, white: false);

        var rows = ctx.Document.Items.Take(5).ToList();
        if (rows.Count == 0)
            rows.Add(new InvoiceLineItem());

        var currentY = rect.Y + headerHeight;
        var rowHeight = 18d;
        foreach (var item in rows)
        {
            if (currentY + rowHeight + rowHeight > rect.Bottom)
                break;

            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, currentY + rowHeight, rect.Right, currentY + rowHeight);
            var description = Ellipsize(ctx, item.Description ?? string.Empty, bodyFont, columns[1] - columns[0] - 8);
            ctx.Gfx.DrawString(description, bodyFont, XBrushes.Black, new XRect(columns[0] + 4, currentY + 4, columns[1] - columns[0] - 8, 10), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(Style1Decimal(item.Quantity), bodyFont, XBrushes.Black, new XRect(columns[1] + 3, currentY + 4, columns[2] - columns[1] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(item.Unit, bodyFont, XBrushes.Black, new XRect(columns[2] + 3, currentY + 4, columns[3] - columns[2] - 6, 10), XStringFormats.TopCenter);
            ctx.Gfx.DrawString(Style1Decimal(item.UnitPrice), bodyFont, XBrushes.Black, new XRect(columns[3] + 4, currentY + 4, columns[4] - columns[3] - 8, 10), XStringFormats.TopRight);
            ctx.Gfx.DrawString(Style1Decimal(item.TotalAmount), bodyFont, XBrushes.Black, new XRect(columns[4] + 4, currentY + 4, columns[5] - columns[4] - 8, 10), XStringFormats.TopRight);
            currentY += rowHeight;
        }

        if (currentY + rowHeight <= rect.Bottom)
        {
            ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, currentY, rect.Right, currentY);
            ctx.Gfx.DrawString("Spolu:", ctx.Font(8, true), XBrushes.Black, new XRect(columns[3], currentY + 4, columns[4] - columns[3] - 6, 10), XStringFormats.TopRight);
            ctx.Gfx.DrawString(Style1Money(ctx.Document.TotalAmount, ctx.Document.Currency), ctx.Font(8), XBrushes.Black, new XRect(columns[4] + 4, currentY + 4, columns[5] - columns[4] - 8, 10), XStringFormats.TopRight);
        }
    }

    private static void DrawStyle1Qr(RenderContext ctx, XRect rect)
    {
        if (ctx.QrPreview.PngBytes == null)
            return;

        var tempBmpPath = Path.Combine(Path.GetTempPath(), $"agency-contractor-style1-qr-{Guid.NewGuid():N}.bmp");
        try
        {
            WriteBitmapCompatibleQr(ctx.QrPreview.PngBytes, tempBmpPath);
            using var image = XImage.FromFile(tempBmpPath);
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentPen, rect);
            var footerHeight = 18d;
            var imageSize = Math.Min(rect.Width - 16, rect.Height - footerHeight - 12);
            ctx.Gfx.DrawImage(image, rect.X + 8, rect.Y + 8, imageSize, imageSize);

            var footer = $"{Res("InvoicesQrPaymentLabel")} {Style1CurrencySymbol(ctx.Document.Currency)}".Trim();
            ctx.Gfx.DrawString(footer, ctx.Font(7.8, true), ctx.Palette.AccentBrush, new XRect(rect.X + 4, rect.Bottom - 18, rect.Width - 8, 10), XStringFormats.TopLeft);
        }
        finally
        {
            try
            {
                if (File.Exists(tempBmpPath))
                    File.Delete(tempBmpPath);
            }
            catch
            {
            }
        }
    }

    private static void DrawStyle1BottomArea(RenderContext ctx, XRect rect)
    {
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);

        var rightWidth = 188d;
        var signatureHeight = 24d;
        var rightX = rect.Right - rightWidth;
        var footerY = rect.Bottom - signatureHeight;
        var summarySplitY = rect.Y + 30;

        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rightX, rect.Y, rightX, rect.Bottom);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, footerY, rect.Right, footerY);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rightX, summarySplitY, rect.Right, summarySplitY);
        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X + ((rightX - rect.X) / 2d), footerY, rect.X + ((rightX - rect.X) / 2d), rect.Bottom);

        ctx.Gfx.DrawString("Celková částka:", ctx.Font(8), XBrushes.Black, new XRect(rightX + 6, rect.Y + 4, 82, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style1Money(ctx.Document.TotalAmount, ctx.Document.Currency), ctx.Font(8), XBrushes.Black, new XRect(rightX + 88, rect.Y + 4, rightWidth - 94, 10), XStringFormats.TopRight);
        ctx.Gfx.DrawString("Uhrazeno zálohami:", ctx.Font(8), XBrushes.Black, new XRect(rightX + 6, rect.Y + 15, 92, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style1Money(ctx.Document.AlreadyPaidAmount, ctx.Document.Currency), ctx.Font(8), XBrushes.Black, new XRect(rightX + 98, rect.Y + 15, rightWidth - 104, 10), XStringFormats.TopRight);

        ctx.Gfx.DrawString("Vyhotovil:", ctx.Font(8.5), XBrushes.Black, new XRect(rect.X + 8, footerY + 5, (rightX - rect.X) / 2d - 16, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString("Převzal:", ctx.Font(8.5), XBrushes.Black, new XRect(rect.X + ((rightX - rect.X) / 2d) + 8, footerY + 5, (rightX - rect.X) / 2d - 16, 10), XStringFormats.TopLeft);

        ctx.Gfx.DrawString("Zbývá uhradit:", ctx.Font(8), XBrushes.Black, new XRect(rightX + 6, summarySplitY + 5, 88, 10), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style1Money(ctx.Document.AmountToPay, ctx.Document.Currency), ctx.Font(8), XBrushes.Black, new XRect(rightX + 92, summarySplitY + 5, rightWidth - 98, 10), XStringFormats.TopRight);
        ctx.Gfx.DrawString("K úhradě:", ctx.Font(9.2, true), XBrushes.Black, new XRect(rightX + 8, summarySplitY + 26, rightWidth - 16, 12), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(Style1Money(ctx.Document.AmountToPay, ctx.Document.Currency), FitFont(ctx, Style1Money(ctx.Document.AmountToPay, ctx.Document.Currency), 16.5, rightWidth - 18, true), XBrushes.Black, new XRect(rightX + 8, summarySplitY + 30, rightWidth - 16, 18), XStringFormats.TopRight);

        var footerNote = !string.IsNullOrWhiteSpace(ctx.Document.NotesBelow)
            ? ctx.Document.NotesBelow.Trim()
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(footerNote))
        {
            ctx.Gfx.DrawString(Ellipsize(ctx, footerNote, ctx.Font(6.4), rightWidth - 16), ctx.Font(6.4), XBrushes.Black, new XRect(rightX + 8, summarySplitY + 58, rightWidth - 16, 8), XStringFormats.TopRight);
        }
    }

    private static double GetStyle1ItemsHeight(int itemCount)
    {
        var visibleRows = Math.Max(1, Math.Min(5, itemCount));
        const double headerHeight = 18d;
        const double rowHeight = 18d;
        const double totalRowHeight = 18d;
        return headerHeight + (visibleRows * rowHeight) + totalRowHeight;
    }

    private static double GetStyle2ItemsHeight(int itemCount)
    {
        var visibleRows = Math.Max(1, Math.Min(5, itemCount));
        const double headerHeight = 18d;
        const double rowHeight = 18d;
        return headerHeight + (visibleRows * rowHeight);
    }

    private static double GetStyle3ItemsHeight(int itemCount)
    {
        var visibleRows = Math.Max(1, Math.Min(5, itemCount));
        const double headerHeight = 18d;
        const double rowHeight = 18d;
        return headerHeight + (visibleRows * rowHeight);
    }

    private static double GetStyle4ItemsHeight(int itemCount)
    {
        var visibleRows = Math.Max(1, Math.Min(5, itemCount));
        const double headerHeight = 18d;
        const double rowHeight = 18d;
        const double totalRowReserve = 18d;
        return headerHeight + (visibleRows * rowHeight) + totalRowReserve;
    }

    private static double GetStyle5ItemsHeight(int itemCount)
    {
        var visibleRows = Math.Max(1, Math.Min(5, itemCount));
        const double headerHeight = 18d;
        const double rowHeight = 18d;
        const double totalRowReserve = 18d;
        return headerHeight + (visibleRows * rowHeight) + totalRowReserve;
    }

    private static double GetStyle6ItemsHeight(int itemCount)
    {
        var visibleRows = Math.Max(1, Math.Min(5, itemCount));
        const double headerHeight = 18d;
        const double rowHeight = 18d;
        const double totalRowReserve = 18d;
        return headerHeight + (visibleRows * rowHeight) + totalRowReserve;
    }

    private static IEnumerable<string> BuildStyle1AddressLines(InvoiceParty party, bool includeCountry)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(party.Street))
            lines.Add(party.Street.Trim());

        var cityLine = string.Join(" ", new[] { party.PostalCode, party.City }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(cityLine))
            lines.Add(cityLine);

        if (includeCountry && !string.IsNullOrWhiteSpace(party.Country))
            lines.Add(party.Country.Trim());

        return lines;
    }

    private static string BuildStyle1VatLine(InvoiceParty party)
    {
        if (!party.IsVatPayer)
            return Res("InvoicesBoolNo");

        if (party.ShowVatIdOnDocument && !string.IsNullOrWhiteSpace(party.VatId))
            return $"{Res("InvoicesVatId")} {party.VatId}";
        if (!string.IsNullOrWhiteSpace(party.Dic))
            return $"{Res("InvoicesDic")} {party.Dic}";
        return Res("InvoicesBoolYes");
    }

    private static void DrawStyle1LabelValue(RenderContext ctx, double x, double y, double labelWidth, string label, string? value)
    {
        ctx.Gfx.DrawString(label, ctx.Font(8.3), XBrushes.Black, new XRect(x, y, labelWidth, 11), XStringFormats.TopLeft);
        ctx.Gfx.DrawString(value ?? string.Empty, ctx.Font(8.5), XBrushes.Black, new XRect(x + labelWidth, y, 180, 11), XStringFormats.TopLeft);
    }

    private static string Style1Money(decimal value, string currency)
        => $"{Style1Decimal(value)} {currency}";

    private static string Style2Money(decimal value, string currency)
        => $"{Style2Decimal(value)} {currency}";

    private static string Style1Decimal(decimal value)
        => value.ToString("N2", CultureInfo.GetCultureInfo("cs-CZ")).Replace('\u00A0', ' ');

    private static string Style2Decimal(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Style1CurrencySymbol(string currency)
        => currency.ToUpperInvariant() switch
        {
            "EUR" => "€",
            "CZK" => "Kč",
            "USD" => "$",
            _ => currency
        };

    private static void DrawCashReceiptPdf(XGraphics gfx, double pageWidth, double pageHeight, InvoiceDocument document)
    {
        var variant = InvoiceCashReceiptHelper.NormalizeVariant(document.CashReceiptDocumentVariant, document.Type, document.CashReceiptTitle);
        if (variant == "simple")
        {
            DrawSimpleCashReceiptPdf(gfx, pageWidth, pageHeight, document);
            return;
        }

        var secondTemplate = string.Equals(document.SelectedTemplateId, "cash2", StringComparison.OrdinalIgnoreCase);
        var accent = secondTemplate ? XColor.FromArgb(0x21, 0x8F, 0x64) : XColor.FromArgb(0x33, 0x5C, 0xB3);
        var light = secondTemplate ? XColor.FromArgb(0xE9, 0xF6, 0xF0) : XColor.FromArgb(0xEE, 0xF3, 0xFE);
        var titleFont = SafeFont(20, true);
        var sectionFont = SafeFont(10, true);
        var labelFont = SafeFont(8.5, true);
        var bodyFont = SafeFont(8.6);
        var amountFont = SafeFont(18, true);
        var borderPen = new XPen(XColors.Black, 1);
        var accentPen = new XPen(accent, 1.2);
        var accentBrush = new XSolidBrush(accent);
        var lightBrush = new XSolidBrush(light);
        var totalWithVat = CalculateCashReceiptTotalWithVat(document);
        var nonTaxable = Math.Max(0m, document.CashReceiptNonTaxableAmount);
        var grandTotal = CalculateCashReceiptGrandTotal(document);
        var roundingAdjustment = grandTotal - (totalWithVat + nonTaxable);

        gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);
        gfx.DrawRectangle(lightBrush, 30, 28, pageWidth - 60, secondTemplate ? 48 : 36);
        gfx.DrawRectangle(accentBrush, 30, 28, secondTemplate ? 160 : 138, secondTemplate ? 48 : 36);

        var documentTitle = GetCashReceiptTitle(document).ToUpperInvariant();
        gfx.DrawString(documentTitle, titleFont, XBrushes.Black, new XRect(42, 86, pageWidth - 84, 28), XStringFormats.TopLeft);
        gfx.DrawString(document.Number, SafeFont(12, true), accentBrush, new XRect(42, 114, 260, 16), XStringFormats.TopLeft);

        DrawCashReceiptBox(gfx, borderPen, accentBrush, sectionFont, labelFont, bodyFont, new XRect(42, 146, 242, 106), Res("InvoicesCompanyDataSection"), BuildCashReceiptPartyLines(document.Supplier));
        DrawCashReceiptBox(gfx, borderPen, accentBrush, sectionFont, labelFont, bodyFont, new XRect(310, 146, 242, 106), Res("InvoicesReceivedFromSection"), BuildCashReceiptPartyLines(document.Customer));

        var detailsRect = new XRect(42, 264, 510, 72);
        gfx.DrawRectangle(borderPen, detailsRect);
        gfx.DrawRectangle(lightBrush, detailsRect.X, detailsRect.Y, detailsRect.Width, 18);
        gfx.DrawString(Res("InvoicesReceiptDataSection"), sectionFont, accentBrush, new XRect(detailsRect.X + 8, detailsRect.Y + 3, detailsRect.Width - 16, 12), XStringFormats.TopLeft);
        DrawPair(gfx, labelFont, bodyFont, detailsRect.X + 10, detailsRect.Y + 28, 116, Res("InvoicesReceiptNumber"), document.Number);
        DrawPair(gfx, labelFont, bodyFont, detailsRect.X + 260, detailsRect.Y + 28, 96, Res("InvoicesPaymentDateLabel"), document.CashReceiptPaymentDate.ToString("d.M.yyyy", CultureInfo.InvariantCulture));
        DrawPair(gfx, labelFont, bodyFont, detailsRect.X + 10, detailsRect.Y + 48, 116, Res("InvoicesReceiptTitleLabel"), GetCashReceiptTitle(document));

        var amountRect = new XRect(42, 350, 510, 112);
        gfx.DrawRectangle(borderPen, amountRect);
        gfx.DrawRectangle(lightBrush, amountRect.X, amountRect.Y, amountRect.Width, 18);
        gfx.DrawString(Res("InvoicesMoneyDataSection"), sectionFont, accentBrush, new XRect(amountRect.X + 8, amountRect.Y + 3, amountRect.Width - 16, 12), XStringFormats.TopLeft);
        DrawPair(gfx, labelFont, bodyFont, amountRect.X + 10, amountRect.Y + 28, 116, Res("InvoicesCashReceiptBaseAmount"), Money(document.CashReceiptBaseAmount, document.Currency));
        DrawPair(gfx, labelFont, bodyFont, amountRect.X + 260, amountRect.Y + 28, 92, Res("InvoicesCashReceiptTaxRate"), document.Supplier.IsVatPayer ? $"{document.CashReceiptTaxRate:0.##} %" : Res("InvoicesBoolNo"));
        DrawPair(gfx, labelFont, bodyFont, amountRect.X + 10, amountRect.Y + 48, 116, Res("InvoicesCashReceiptAmountWithVat"), Money(totalWithVat, document.Currency));
        DrawPair(gfx, labelFont, bodyFont, amountRect.X + 260, amountRect.Y + 48, 92, Res("InvoicesCashReceiptNonTaxable"), Money(nonTaxable, document.Currency));
        DrawPair(gfx, labelFont, bodyFont, amountRect.X + 10, amountRect.Y + 68, 116, Res("InvoicesCashReceiptRounding"), Money(roundingAdjustment, document.Currency));
        DrawPair(gfx, labelFont, bodyFont, amountRect.X + 260, amountRect.Y + 68, 92, Res("InvoicesGrandTotal"), Money(grandTotal, document.Currency));
        DrawWrappedText(gfx, document.CashReceiptAmountText, bodyFont, new XRect(amountRect.X + 10, amountRect.Y + 88, amountRect.Width - 20, 14), 1);

        var executionRect = new XRect(42, 474, 510, 84);
        gfx.DrawRectangle(borderPen, executionRect);
        gfx.DrawRectangle(lightBrush, executionRect.X, executionRect.Y, executionRect.Width, 18);
        gfx.DrawString(Res("InvoicesCashReceiptIssuanceSection"), sectionFont, accentBrush, new XRect(executionRect.X + 8, executionRect.Y + 3, executionRect.Width - 16, 12), XStringFormats.TopLeft);
        DrawPair(gfx, labelFont, bodyFont, executionRect.X + 10, executionRect.Y + 28, 80, Res("InvoicesPurpose"), document.CashReceiptPurpose);
        DrawPair(gfx, labelFont, bodyFont, executionRect.X + 260, executionRect.Y + 28, 88, Res("InvoicesPreparedBy"), document.CashReceiptPreparedBy);
        DrawPair(gfx, labelFont, bodyFont, executionRect.X + 10, executionRect.Y + 48, 80, Res("InvoicesApprovedBy"), document.CashReceiptApprovedBy);
        DrawPair(gfx, labelFont, bodyFont, executionRect.X + 260, executionRect.Y + 48, 88, Res("InvoicesJournalEntryNumber"), document.CashReceiptLedgerNumber);
        DrawWrappedText(gfx, document.InternalNote, bodyFont, new XRect(executionRect.X + 10, executionRect.Y + 66, executionRect.Width - 20, 12), 1);

        var tableRect = new XRect(42, 570, 510, 172);
        gfx.DrawRectangle(borderPen, tableRect);
        gfx.DrawRectangle(lightBrush, tableRect.X, tableRect.Y, tableRect.Width, 18);
        gfx.DrawString(Res("InvoicesAccountingPrescriptionSection"), sectionFont, accentBrush, new XRect(tableRect.X + 8, tableRect.Y + 3, tableRect.Width - 16, 12), XStringFormats.TopLeft);
        DrawAccountingHeader(gfx, labelFont, tableRect);
        DrawAccountingRow(gfx, bodyFont, tableRect, 0, document.CashReceiptAccount1, document.CashReceiptAccount1Amount, document.Currency);
        DrawAccountingRow(gfx, bodyFont, tableRect, 1, document.CashReceiptAccount2, document.CashReceiptAccount2Amount, document.Currency);
        DrawAccountingRow(gfx, bodyFont, tableRect, 2, document.CashReceiptAccount3, document.CashReceiptAccount3Amount, document.Currency);
        DrawAccountingRow(gfx, bodyFont, tableRect, 3, document.CashReceiptAccount4, document.CashReceiptAccount4Amount, document.Currency);
        DrawAccountingRow(gfx, bodyFont, tableRect, 4, document.CashReceiptAccount5, document.CashReceiptAccount5Amount, document.Currency);
        DrawAccountingRow(gfx, bodyFont, tableRect, 5, document.CashReceiptAccount6, document.CashReceiptAccount6Amount, document.Currency);
        DrawPair(gfx, labelFont, bodyFont, tableRect.X + 10, tableRect.Bottom - 18, 70, Res("InvoicesDateLabel"), document.CashReceiptAccountingDate.ToString("d.M.yyyy", CultureInfo.InvariantCulture));

        var signTop = 760d;
        DrawSignatureBlock(gfx, borderPen, labelFont, bodyFont, 42, signTop, 150, StripTrailingColon(Res("InvoicesIssuedBy")), document.CashReceiptPreparedBy);
        DrawSignatureBlock(gfx, borderPen, labelFont, bodyFont, 222, signTop, 150, StripTrailingColon(Res("InvoicesApprovedBy")), document.CashReceiptApprovedBy);
        DrawSignatureBlock(gfx, borderPen, labelFont, bodyFont, 402, signTop, 150, Res("InvoicesReceivedBy"), document.Customer.Name);

        if (secondTemplate)
            gfx.DrawRectangle(accentPen, 30, 28, pageWidth - 60, pageHeight - 56);
    }

    private static void DrawSimpleCashReceiptPdf(XGraphics gfx, double pageWidth, double pageHeight, InvoiceDocument document)
    {
        var secondTemplate = string.Equals(document.SelectedTemplateId, "cash2", StringComparison.OrdinalIgnoreCase);
        var accent = secondTemplate ? XColors.Black : XColor.FromArgb(0xC6, 0x2A, 0xD1);
        var accentBrush = new XSolidBrush(accent);
        var borderPen = new XPen(accent, secondTemplate ? 0.8 : 1);
        var titleFont = SafeFont(secondTemplate ? 18 : 19, true);
        var headerFont = SafeFont(9.2, true);
        var labelFont = SafeFont(8.4, true);
        var bodyFont = SafeFont(8.5);
        var amountFont = SafeFont(15, true);
        var totalWithVat = CalculateCashReceiptTotalWithVat(document);
        var grandTotal = CalculateCashReceiptGrandTotal(document);

        gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);
        gfx.DrawString(GetCashReceiptTitle(document).ToUpperInvariant(), titleFont, XBrushes.Black, new XRect(42, 46, 360, 24), XStringFormats.TopLeft);
        gfx.DrawString(document.Number, SafeFont(14, true), accentBrush, new XRect(pageWidth - 120, 46, 78, 24), XStringFormats.TopRight);
        gfx.DrawString($"ze dne: {document.CashReceiptPaymentDate:d.M.yyyy}", bodyFont, XBrushes.Black, new XRect(42, 72, 220, 14), XStringFormats.TopLeft);
        gfx.DrawLine(borderPen, 42, 92, pageWidth - 42, 92);

        var organizationLines = BuildCashReceiptPartyLines(document.Supplier);
        var receivedLines = BuildCashReceiptPartyLines(document.Customer);

        gfx.DrawString("Organizace:", labelFont, accentBrush, new XRect(42, 104, 90, 12), XStringFormats.TopLeft);
        DrawPartyTextLines(gfx, bodyFont, 130, 104, organizationLines, 4, 210);
        gfx.DrawString("Prijato od:", labelFont, accentBrush, new XRect(42, 188, 90, 12), XStringFormats.TopLeft);
        DrawPartyTextLines(gfx, bodyFont, 130, 188, receivedLines, 3, 360);

        var tableTop = 248d;
        gfx.DrawLine(borderPen, 42, tableTop, pageWidth - 42, tableTop);
        gfx.DrawLine(borderPen, 42, tableTop + 18, pageWidth - 42, tableTop + 18);
        gfx.DrawString(StripTrailingColon(Res("InvoicesCashReceiptBaseAmount")), headerFont, XBrushes.Black, new XRect(52, tableTop + 4, 130, 12), XStringFormats.TopLeft);
        gfx.DrawString(StripTrailingColon(Res("InvoicesVatLabel")), headerFont, XBrushes.Black, new XRect(220, tableTop + 4, 60, 12), XStringFormats.TopLeft);
        gfx.DrawString(StripTrailingColon(Res("InvoicesGrandTotal")), headerFont, XBrushes.Black, new XRect(pageWidth - 200, tableTop + 4, 140, 12), XStringFormats.TopLeft);
        gfx.DrawString(Money(document.CashReceiptBaseAmount, document.Currency), amountFont, XBrushes.Black, new XRect(52, tableTop + 24, 150, 16), XStringFormats.TopLeft);
        gfx.DrawString(document.Supplier.IsVatPayer ? $"{document.CashReceiptTaxRate:0.##} %" : "-", amountFont, XBrushes.Black, new XRect(220, tableTop + 24, 70, 16), XStringFormats.TopLeft);
        gfx.DrawString(Money(grandTotal, document.Currency), amountFont, XBrushes.Black, new XRect(pageWidth - 200, tableTop + 24, 150, 16), XStringFormats.TopLeft);
        gfx.DrawLine(borderPen, 42, tableTop + 48, pageWidth - 42, tableTop + 48);
        gfx.DrawString(Res("InvoicesInWords"), headerFont, XBrushes.Black, new XRect(52, tableTop + 54, 70, 12), XStringFormats.TopLeft);
        DrawWrappedText(gfx, document.CashReceiptAmountText, bodyFont, new XRect(130, tableTop + 54, pageWidth - 180, 14), 1);
        gfx.DrawLine(borderPen, 42, tableTop + 70, pageWidth - 42, tableTop + 70);

        var leftTop = tableTop + 96;
        gfx.DrawString("Účel platby:", labelFont, accentBrush, new XRect(52, leftTop, 90, 12), XStringFormats.TopLeft);
        DrawWrappedText(gfx, document.CashReceiptPurpose, bodyFont, new XRect(150, leftTop, 210, 24), 2);
        gfx.DrawString("Vyhotovil:", labelFont, accentBrush, new XRect(52, leftTop + 28, 90, 12), XStringFormats.TopLeft);
        gfx.DrawString(document.CashReceiptPreparedBy, bodyFont, XBrushes.Black, new XRect(150, leftTop + 28, 190, 12), XStringFormats.TopLeft);
        gfx.DrawString("Schválil:", labelFont, accentBrush, new XRect(52, leftTop + 50, 90, 12), XStringFormats.TopLeft);
        gfx.DrawString(document.CashReceiptApprovedBy, bodyFont, XBrushes.Black, new XRect(150, leftTop + 50, 190, 12), XStringFormats.TopLeft);
        gfx.DrawString("Podpis příjemce:", labelFont, accentBrush, new XRect(52, leftTop + 126, 100, 12), XStringFormats.TopLeft);
        gfx.DrawLine(borderPen, 152, leftTop + 136, 294, leftTop + 136);
        gfx.DrawString(Res("InvoicesJournalEntryNumber"), labelFont, accentBrush, new XRect(52, leftTop + 146, 180, 12), XStringFormats.TopLeft);
        gfx.DrawString(document.CashReceiptLedgerNumber, bodyFont, XBrushes.Black, new XRect(232, leftTop + 146, 90, 12), XStringFormats.TopLeft);

        var rightRect = new XRect(360, leftTop - 2, pageWidth - 402, 188);
        gfx.DrawRectangle(borderPen, rightRect);
        gfx.DrawString(Res("InvoicesAccountingPrescriptionSection"), headerFont, XBrushes.Black, new XRect(rightRect.X, rightRect.Y + 6, rightRect.Width, 12), XStringFormats.TopCenter);
        gfx.DrawLine(borderPen, rightRect.X, rightRect.Y + 22, rightRect.Right, rightRect.Y + 22);
        gfx.DrawLine(borderPen, rightRect.X + (rightRect.Width * 0.42), rightRect.Y + 22, rightRect.X + (rightRect.Width * 0.42), rightRect.Bottom - 26);
        gfx.DrawString(StripTrailingColon(Res("InvoicesCreditAccount")), labelFont, XBrushes.Black, new XRect(rightRect.X + 6, rightRect.Y + 28, 80, 12), XStringFormats.TopLeft);
        gfx.DrawString(StripTrailingColon(Res("InvoicesCashReceiptBaseAmount")), labelFont, XBrushes.Black, new XRect(rightRect.Right - 110, rightRect.Y + 28, 100, 12), XStringFormats.TopRight);
        DrawSimpleAccountingRow(gfx, borderPen, bodyFont, rightRect, 0, document.CashReceiptAccount1, document.CashReceiptAccount1Amount, document.Currency);
        DrawSimpleAccountingRow(gfx, borderPen, bodyFont, rightRect, 1, document.CashReceiptAccount2, document.CashReceiptAccount2Amount, document.Currency);
        DrawSimpleAccountingRow(gfx, borderPen, bodyFont, rightRect, 2, document.CashReceiptAccount3, document.CashReceiptAccount3Amount, document.Currency);
        DrawSimpleAccountingRow(gfx, borderPen, bodyFont, rightRect, 3, document.CashReceiptAccount4, document.CashReceiptAccount4Amount, document.Currency);
        DrawSimpleAccountingRow(gfx, borderPen, bodyFont, rightRect, 4, document.CashReceiptAccount5, document.CashReceiptAccount5Amount, document.Currency);
        DrawSimpleAccountingRow(gfx, borderPen, bodyFont, rightRect, 5, document.CashReceiptAccount6, document.CashReceiptAccount6Amount, document.Currency);
        gfx.DrawLine(borderPen, rightRect.X, rightRect.Bottom - 26, rightRect.Right, rightRect.Bottom - 26);
        gfx.DrawString("Datum:", labelFont, accentBrush, new XRect(rightRect.X + 6, rightRect.Bottom - 20, 50, 12), XStringFormats.TopLeft);
        gfx.DrawString(document.CashReceiptAccountingDate.ToString("d.M.yyyy", CultureInfo.InvariantCulture), bodyFont, XBrushes.Black, new XRect(rightRect.X + 48, rightRect.Bottom - 20, 88, 12), XStringFormats.TopLeft);
        gfx.DrawString("Podpis:", labelFont, accentBrush, new XRect(rightRect.Right - 88, rightRect.Bottom - 20, 72, 12), XStringFormats.TopLeft);

        if (!secondTemplate)
            gfx.DrawRectangle(borderPen, 36, 36, pageWidth - 72, pageHeight - 72);
    }

    private static void DrawDecorations(RenderContext ctx)
    {
        var layout = ctx.Layout;
        if (layout.OuterFrameRect.HasValue)
            ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(layout.FrameThickness), layout.OuterFrameRect.Value);

        if (layout.HeaderBandRect.HasValue)
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentBrush, layout.HeaderBandRect.Value);

        if (layout.HeaderRibbonRect.HasValue)
            ctx.Gfx.DrawRectangle(ctx.Palette.MutedBrush, layout.HeaderRibbonRect.Value);
    }

    private static void DrawBarcode(RenderContext ctx)
    {
        var rect = ctx.Layout.BarcodeRect;
        var bars = (ctx.Document.Number ?? string.Empty).PadRight(18, '0');
        var x = rect.X;
        for (var i = 0; i < bars.Length * 3; i++)
        {
            var width = i % 2 == 0 ? 1.3 : 0.6;
            ctx.Gfx.DrawRectangle(XBrushes.Black, x + (i * 2.1), rect.Y, width, rect.Height);
        }
    }

    private static void DrawHeader(RenderContext ctx)
    {
        var rect = ctx.Layout.TitleRect;
        var brush = ctx.Layout.TitleWhite ? XBrushes.White : XBrushes.Black;
        var text = $"{DocumentTitle(ctx.Document)} {ctx.Document.Number}".Trim();
        var font = FitFont(ctx, text, ctx.Layout.TitleBaseFontSize, rect.Width, true);
        var format = ctx.Layout.TitleAlignRight ? XStringFormats.CenterRight : XStringFormats.CenterLeft;
        ctx.Gfx.DrawString(text, font, brush, rect, format);
    }

    private static void DrawPartyCard(RenderContext ctx, XRect rect, string title, InvoiceParty party, bool boxed)
    {
        if (boxed)
            ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);

        var titleFont = ctx.Font(9.3, true);
        var bodyFont = ctx.Font(8.8);
        var titleRect = new XRect(rect.X + 8, rect.Y + 6, rect.Width - 16, 14);
        var contentY = rect.Y + (string.IsNullOrWhiteSpace(title) ? 8 : 22);

        if (!string.IsNullOrWhiteSpace(title))
            ctx.Gfx.DrawString(title, titleFont, XBrushes.Black, titleRect, XStringFormats.TopLeft);

        var lines = BuildPartyLines(party);
        foreach (var line in lines)
        {
            if (contentY + 10 > rect.Bottom - 6)
                break;

            var wrapped = WrapText(ctx, line, bodyFont, rect.Width - 16, 2);
            foreach (var wrappedLine in wrapped)
            {
                if (contentY + 10 > rect.Bottom - 6)
                    break;

                ctx.Gfx.DrawString(wrappedLine, bodyFont, XBrushes.Black, new XRect(rect.X + 8, contentY, rect.Width - 16, 11), XStringFormats.TopLeft);
                contentY += 11;
            }

            contentY += 1;
        }
    }

    private static void DrawTextCard(RenderContext ctx, XRect rect, string title, IReadOnlyList<string> lines, bool boxed)
    {
        if (boxed)
            ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);

        var titleFont = ctx.Font(9.3, true);
        var bodyFont = ctx.Font(8.3);
        var titleRect = new XRect(rect.X + 8, rect.Y + 6, rect.Width - 16, 14);
        var contentY = rect.Y + (string.IsNullOrWhiteSpace(title) ? 8 : 22);

        if (!string.IsNullOrWhiteSpace(title))
            ctx.Gfx.DrawString(title, titleFont, XBrushes.Black, titleRect, XStringFormats.TopLeft);

        foreach (var line in lines.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            if (contentY + 10 > rect.Bottom - 6)
                break;

            var wrapped = WrapText(ctx, line, bodyFont, rect.Width - 16, 1);
            var rendered = Ellipsize(ctx, wrapped.FirstOrDefault() ?? string.Empty, bodyFont, rect.Width - 16);
            ctx.Gfx.DrawString(rendered, bodyFont, XBrushes.Black, new XRect(rect.X + 8, contentY, rect.Width - 16, 10), XStringFormats.TopLeft);
            contentY += 10.5;
        }
    }

    private static void DrawMeta(RenderContext ctx)
    {
        var rows = BuildMetaRows(ctx.Document);
        if (rows.Count == 0)
        {
            ctx.MetaBottom = ctx.Layout.MetaRect.Y;
            return;
        }

        var rect = ctx.Layout.MetaRect;
        if (ctx.Layout.MetaAsTable)
        {
            var rowHeight = 12d;
            var actualHeight = Math.Min(rect.Height, rows.Count * rowHeight);
            var actualRect = new XRect(rect.X, rect.Y, rect.Width, actualHeight);
            ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), actualRect);
            var splitX = rect.X + (rect.Width * ctx.Layout.MetaSplitRatio);
            for (var i = 0; i < rows.Count; i++)
            {
                var rowRect = new XRect(actualRect.X, actualRect.Y + (i * rowHeight), actualRect.Width, rowHeight);
                if (rowRect.Bottom > actualRect.Bottom + 0.1)
                    break;
                var labelRect = new XRect(rowRect.X + 4, rowRect.Y + 2, splitX - rowRect.X - 8, rowRect.Height - 4);
                var valueRect = new XRect(splitX + 4, rowRect.Y + 2, rowRect.Right - splitX - 8, rowRect.Height - 4);
                var labelFont = ctx.Font(7.3);
                var valueFont = ctx.Font(7.3);
                ctx.Gfx.DrawString(Ellipsize(ctx, rows[i].Label, labelFont, labelRect.Width), labelFont, XBrushes.Black, labelRect, XStringFormats.CenterLeft);
                ctx.Gfx.DrawString(Ellipsize(ctx, rows[i].Value, valueFont, valueRect.Width), valueFont, XBrushes.Black, valueRect, XStringFormats.CenterLeft);
            }
            ctx.MetaBottom = actualRect.Bottom;
            return;
        }

        var labelWidth = Math.Min(rect.Width * 0.44, 86);
        var y = rect.Y;
        foreach (var row in rows)
        {
            if (y + 11 > rect.Bottom)
                break;

            ctx.Gfx.DrawString(row.Label, ctx.Font(8), XBrushes.Black, new XRect(rect.X, y, labelWidth, 11), XStringFormats.TopLeft);
            var valueLines = WrapText(ctx, row.Value, ctx.Font(8), rect.Width - labelWidth - 4, 1);
            ctx.Gfx.DrawString(valueLines.FirstOrDefault() ?? string.Empty, ctx.Font(8), XBrushes.Black, new XRect(rect.X + labelWidth + 4, y, rect.Width - labelWidth - 4, 11), XStringFormats.TopLeft);
            y += 11.5;
        }
        ctx.MetaBottom = y;
    }

    private static void DrawItems(RenderContext ctx)
    {
        var headerHeight = 18d;
        var baseRect = ctx.Layout.ItemsRect;
        var rows = ctx.Document.Items.Take(8).ToList();
        if (rows.Count == 0)
            rows.Add(new InvoiceLineItem());

        var estimatedHeight = headerHeight + rows.Sum(item =>
        {
            var descriptionLines = WrapText(ctx, item.Description, ctx.Font(8), (baseRect.Width * 0.54) - 8, 2);
            return Math.Max(22d, 10d + (descriptionLines.Count * 10d));
        });
        var actualHeight = Math.Max(headerHeight + 28, Math.Min(baseRect.Height, estimatedHeight + 8));
        var rect = new XRect(baseRect.X, Math.Max(baseRect.Y, ctx.MetaBottom + 18), baseRect.Width, actualHeight);
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);

        var columns = GetItemColumns(rect);
        var bodyTop = rect.Y + headerHeight;

        if (ctx.Layout.HighlightItemHeader)
            ctx.Gfx.DrawRectangle(ctx.Palette.MutedBrush, rect.X, rect.Y, rect.Width, headerHeight);

        ctx.Gfx.DrawLine(ctx.Palette.BorderPen(), rect.X, bodyTop, rect.Right, bodyTop);

        DrawItemTableHeader(ctx, columns, rect, headerHeight, white: false);

        var currentY = bodyTop;
        var bodyFont = ctx.Font(8);
        foreach (var item in rows)
        {
            var description = item.Description ?? string.Empty;
            var descriptionLines = WrapText(ctx, description, bodyFont, columns[1] - columns[0] - 8, 2);
            var rowHeight = Math.Max(22d, 10d + (descriptionLines.Count * 10d));
            if (currentY + rowHeight > rect.Bottom)
                break;

            var textY = currentY + 4;
            foreach (var line in descriptionLines)
            {
                ctx.Gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(columns[0] + 4, textY, columns[1] - columns[0] - 8, 10), XStringFormats.TopLeft);
                textY += 10;
            }

            DrawBodyCell(ctx, item.Quantity.ToString("0.##", CultureInfo.InvariantCulture), new XRect(columns[1] + 3, currentY + 3, columns[2] - columns[1] - 6, rowHeight - 6), XStringFormats.TopCenter);
            DrawBodyCell(ctx, item.Unit, new XRect(columns[2] + 3, currentY + 3, columns[3] - columns[2] - 6, rowHeight - 6), XStringFormats.TopCenter);
            DrawBodyCell(ctx, item.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture), new XRect(columns[3] + 4, currentY + 3, columns[4] - columns[3] - 8, rowHeight - 6), XStringFormats.TopRight);
            DrawBodyCell(ctx, item.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture), new XRect(columns[4] + 4, currentY + 3, columns[5] - columns[4] - 8, rowHeight - 6), XStringFormats.TopRight);

            currentY += rowHeight;
        }

        ctx.ItemsBottom = currentY;
    }

    private static void DrawQr(RenderContext ctx)
    {
        if (ctx.QrPreview.PngBytes == null || !ctx.Layout.QrRect.HasValue)
            return;

        var baseRect = ctx.Layout.QrRect.Value;
        var rect = new XRect(baseRect.X, Math.Max(baseRect.Y, ctx.ItemsBottom + 18), baseRect.Width, baseRect.Height);
        var footerLines = BuildQrFooterLines(ctx.PaymentDetails);
        var footerHeight = footerLines.Count == 0 ? 18d : 18d + (footerLines.Count * 9d);
        var imageHeight = Math.Max(40d, rect.Height - footerHeight - 8d);
        var tempBmpPath = Path.Combine(Path.GetTempPath(), $"agency-contractor-qr-{Guid.NewGuid():N}.bmp");

        try
        {
            WriteBitmapCompatibleQr(ctx.QrPreview.PngBytes, tempBmpPath);
            using var image = XImage.FromFile(tempBmpPath);
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentPen, rect);
            ctx.Gfx.DrawImage(image, rect.X + 4, rect.Y + 4, rect.Width - 8, imageHeight);
            var footerTop = rect.Bottom - footerHeight;
            ctx.Gfx.DrawString(Res("InvoicesQrPaymentLabel"), ctx.Font(8.5, true), ctx.Palette.AccentBrush, new XRect(rect.X + 4, footerTop + 2, rect.Width - 8, 10), XStringFormats.CenterLeft);
            var lineY = footerTop + 12;
            foreach (var line in footerLines)
            {
                ctx.Gfx.DrawString(Ellipsize(ctx, line, ctx.Font(7.2), rect.Width - 8), ctx.Font(7.2), XBrushes.Black, new XRect(rect.X + 4, lineY, rect.Width - 8, 9), XStringFormats.CenterLeft);
                lineY += 9;
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempBmpPath))
                    File.Delete(tempBmpPath);
            }
            catch
            {
                // Ignore temp cleanup failures; they should not break PDF rendering.
            }
        }

        ctx.QrBottom = rect.Bottom;
    }

    private static void WriteBitmapCompatibleQr(byte[] pngBytes, string targetPath)
    {
        using var stream = new MemoryStream(pngBytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fileStream = File.Create(targetPath);
        encoder.Save(fileStream);
    }

    private static void DrawTotals(RenderContext ctx)
    {
        var baseRect = ctx.Layout.TotalsRect;
        var topY = Math.Max(baseRect.Y, ctx.ItemsBottom + 22);
        var rect = new XRect(baseRect.X, topY, baseRect.Width, baseRect.Height);
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);

        var summaryRect = new XRect(rect.X + 8, rect.Y + 6, rect.Width - 16, 42);
        var dueRect = new XRect(rect.X + 8, rect.Bottom - 38, rect.Width - 16, 28);
        var labels = new[]
        {
            ("Celková částka:", Money(ctx.Document.TotalAmount, ctx.Document.Currency)),
            ("Uhrazeno zálohami:", Money(ctx.Document.AlreadyPaidAmount, ctx.Document.Currency)),
            ("Zbývá uhradit:", Money(ctx.Document.AmountToPay, ctx.Document.Currency))
        };

        var y = summaryRect.Y;
        foreach (var (label, value) in labels)
        {
            ctx.Gfx.DrawString(label, ctx.Font(8.5), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width * 0.52, 11), XStringFormats.TopLeft);
            ctx.Gfx.DrawString(value, ctx.Font(8.5), XBrushes.Black, new XRect(summaryRect.X, y, summaryRect.Width, 11), XStringFormats.TopRight);
            y += 12;
        }

        if (ctx.Layout.AccentTotalBand)
        {
            ctx.Gfx.DrawRectangle(ctx.Palette.AccentBrush, rect.X, rect.Bottom - 44, rect.Width, 44);
            ctx.Gfx.DrawString("K úhradě:", ctx.Font(10, true), XBrushes.White, new XRect(rect.X + 8, rect.Bottom - 36, rect.Width - 16, 12), XStringFormats.TopLeft);
            var font = FitFont(ctx, Money(ctx.Document.AmountToPay, ctx.Document.Currency), 18, rect.Width - 16, true);
            ctx.Gfx.DrawString(Money(ctx.Document.AmountToPay, ctx.Document.Currency), font, XBrushes.White, new XRect(rect.X + 8, rect.Bottom - 24, rect.Width - 16, 18), XStringFormats.TopRight);
        }
        else
        {
            ctx.Gfx.DrawString("K úhradě:", ctx.Font(10, true), XBrushes.Black, new XRect(dueRect.X, dueRect.Y, dueRect.Width, 12), XStringFormats.TopLeft);
            var font = FitFont(ctx, Money(ctx.Document.AmountToPay, ctx.Document.Currency), 16, dueRect.Width, true);
            ctx.Gfx.DrawString(Money(ctx.Document.AmountToPay, ctx.Document.Currency), font, XBrushes.Black, dueRect, XStringFormats.BottomRight);
        }

        ctx.TotalsBottom = rect.Bottom;
    }

    private static void DrawFooter(RenderContext ctx)
    {
        var baseRect = ctx.Layout.FooterRect;
        var rect = new XRect(baseRect.X, Math.Max(baseRect.Y, ctx.TotalsBottom + 26), baseRect.Width, baseRect.Height);
        ctx.Gfx.DrawRectangle(ctx.Palette.BorderPen(), rect);
        var columnWidth = rect.Width / 2d;

        var issuedBy = string.IsNullOrWhiteSpace(ctx.Document.IssuedBy) ? "Vyhotovil:" : $"Vyhotovil: {ctx.Document.IssuedBy}";
        var receivedBy = !string.IsNullOrWhiteSpace(ctx.Document.Customer.Name) && ctx.Document.Type is InvoiceDocumentType.CashReceiptIncome or InvoiceDocumentType.CashReceiptExpense
            ? $"Převzal: {ctx.Document.Customer.Name}"
            : "Převzal:";

        DrawFooterCaption(ctx, issuedBy, new XRect(rect.X + 8, rect.Y + 6, columnWidth - 16, 11));
        DrawFooterCaption(ctx, receivedBy, new XRect(rect.X + columnWidth + 8, rect.Y + 6, columnWidth - 16, 11));
    }

    private static void DrawFooterCaption(RenderContext ctx, string text, XRect rect)
    {
        ctx.Gfx.DrawString(text, ctx.Font(8.5), XBrushes.Black, rect, XStringFormats.TopLeft);
    }

    private static void DrawHeaderCell(RenderContext ctx, string text, XRect rect, XStringFormat format)
    {
        ctx.Gfx.DrawString(text, ctx.Font(7.7, true), XBrushes.Black, rect, format);
    }

    private static void DrawHeaderCellWhite(RenderContext ctx, string text, XRect rect, XStringFormat format)
    {
        ctx.Gfx.DrawString(text, ctx.Font(7.7, true), XBrushes.White, rect, format);
    }

    private static void DrawItemTableHeader(RenderContext ctx, List<double> columns, XRect rect, double headerHeight, bool white, bool combineQuantityUnit = false)
    {
        DrawItemHeaderCell(ctx, white, StripTrailingColon(Res("InvoicesItemDescription")), new XRect(columns[0] + 4, rect.Y + 2, columns[1] - columns[0] - 8, headerHeight - 4), XStringFormats.CenterLeft);

        if (combineQuantityUnit)
        {
            DrawItemHeaderCell(ctx, white, Res("InvoicesItemQuantityUnit"), new XRect(columns[1] + 2, rect.Y + 2, columns[3] - columns[1] - 4, headerHeight - 4), XStringFormats.Center);
        }
        else
        {
            DrawItemHeaderCell(ctx, white, StripTrailingColon(Res("InvoicesItemQuantity")), new XRect(columns[1] + 2, rect.Y + 2, columns[2] - columns[1] - 4, headerHeight - 4), XStringFormats.Center);
            DrawItemHeaderCell(ctx, white, StripTrailingColon(Res("InvoicesItemUnit")), new XRect(columns[2] + 2, rect.Y + 2, columns[3] - columns[2] - 4, headerHeight - 4), XStringFormats.Center);
        }

        DrawItemHeaderCell(ctx, white, StripTrailingColon(Res("InvoicesItemUnitPrice")), new XRect(columns[3] + 2, rect.Y + 2, columns[4] - columns[3] - 4, headerHeight - 4), XStringFormats.Center);
        DrawItemHeaderCell(ctx, white, StripTrailingColon(Res("InvoicesItemTotal")), new XRect(columns[4] + 2, rect.Y + 2, columns[5] - columns[4] - 4, headerHeight - 4), XStringFormats.Center);
    }

    private static void DrawItemHeaderCell(RenderContext ctx, bool white, string text, XRect rect, XStringFormat format)
    {
        if (white)
            DrawHeaderCellWhite(ctx, text, rect, format);
        else
            DrawHeaderCell(ctx, text, rect, format);
    }

    private static void DrawBodyCell(RenderContext ctx, string text, XRect rect, XStringFormat format)
    {
        ctx.Gfx.DrawString(text, ctx.Font(8), XBrushes.Black, rect, format);
    }

    private static List<double> GetItemColumns(XRect rect)
    {
        var description = rect.Width * 0.54;
        var quantity = rect.Width * 0.12;
        var unit = rect.Width * 0.11;
        var unitPrice = rect.Width * 0.12;
        return
        [
            rect.X,
            rect.X + description,
            rect.X + description + quantity,
            rect.X + description + quantity + unit,
            rect.X + description + quantity + unit + unitPrice,
            rect.Right
        ];
    }

    private static LayoutSpec CreateLayout(InvoiceStyle style, double pageWidth, double pageHeight)
    {
        return style switch
        {
            InvoiceStyle.Style2 => new LayoutSpec
            {
                TitleRect = new XRect(320, 28, 230, 28),
                BarcodeRect = new XRect(42, 36, 108, 18),
                SupplierRect = new XRect(42, 62, 220, 94),
                CustomerRect = new XRect(284, 62, 268, 68),
                MetaRect = new XRect(284, 136, 268, 72),
                ItemsRect = new XRect(42, 250, 510, 150),
                TotalsRect = new XRect(370, 664, 182, 88),
                FooterRect = new XRect(42, 776, 510, 30),
                OuterFrameRect = new XRect(28, 28, pageWidth - 56, pageHeight - 56),
                QrRect = null,
                CustomerBox = true,
                SupplierBox = false,
                BankBox = false,
                MetaAsTable = true,
                HighlightItemHeader = false,
                AccentTotalBand = false,
                TitleAlignRight = true,
                TitleWhite = false,
                TitleBaseFontSize = 18,
                FooterColumns = 3,
                FrameThickness = 1.2
            },
            InvoiceStyle.Style3 => new LayoutSpec
            {
                TitleRect = new XRect(42, 40, 260, 30),
                BarcodeRect = new XRect(42, 18, 108, 18),
                SupplierRect = new XRect(42, 84, 160, 110),
                CustomerRect = new XRect(214, 84, 170, 110),
                BankRect = new XRect(396, 84, 156, 110),
                MetaRect = new XRect(42, 198, 510, 34),
                ItemsRect = new XRect(42, 242, 510, 214),
                TotalsRect = new XRect(372, 642, 180, 98),
                FooterRect = new XRect(42, 776, 510, 28),
                QrRect = new XRect(42, 642, 78, 94),
                CustomerBox = false,
                SupplierBox = false,
                BankBox = false,
                MetaAsTable = false,
                HighlightItemHeader = false,
                AccentTotalBand = false,
                TitleAlignRight = false,
                TitleWhite = false,
                TitleBaseFontSize = 22,
                FooterColumns = 2,
                FrameThickness = 0.8
            },
            InvoiceStyle.Style4 => new LayoutSpec
            {
                TitleRect = new XRect(46, 42, 250, 22),
                BarcodeRect = new XRect(42, 18, 108, 18),
                SupplierRect = new XRect(42, 78, 232, 88),
                CustomerRect = new XRect(286, 78, 266, 88),
                MetaRect = new XRect(42, 170, 230, 68),
                ItemsRect = new XRect(42, 248, 510, 182),
                TotalsRect = new XRect(372, 656, 180, 92),
                FooterRect = new XRect(42, 776, 510, 30),
                HeaderRibbonRect = new XRect(36, 42, pageWidth - 72, 20),
                CustomerBox = false,
                SupplierBox = false,
                BankBox = false,
                MetaAsTable = false,
                HighlightItemHeader = true,
                AccentTotalBand = false,
                TitleAlignRight = false,
                TitleWhite = false,
                TitleBaseFontSize = 18,
                FooterColumns = 3,
                FrameThickness = 1.0
            },
            InvoiceStyle.Style5 => new LayoutSpec
            {
                TitleRect = new XRect(316, 38, 236, 24),
                BarcodeRect = new XRect(36, 18, 108, 18),
                SupplierRect = new XRect(36, 70, 244, 88),
                CustomerRect = new XRect(292, 70, 260, 88),
                MetaRect = new XRect(292, 162, 260, 92),
                ItemsRect = new XRect(36, 266, 516, 174),
                TotalsRect = new XRect(370, 658, 182, 92),
                FooterRect = new XRect(36, 776, 516, 30),
                CustomerBox = true,
                SupplierBox = false,
                BankBox = false,
                MetaAsTable = true,
                HighlightItemHeader = false,
                AccentTotalBand = false,
                TitleAlignRight = true,
                TitleWhite = false,
                TitleBaseFontSize = 18,
                FooterColumns = 3,
                FrameThickness = 1.0
            },
            InvoiceStyle.Style6 => new LayoutSpec
            {
                TitleRect = new XRect(38, 38, 514, 28),
                BarcodeRect = new XRect(42, 18, 108, 18),
                SupplierRect = new XRect(42, 110, 226, 88),
                CustomerRect = new XRect(282, 110, 270, 88),
                MetaRect = new XRect(356, 74, 196, 48),
                ItemsRect = new XRect(42, 220, 510, 182),
                BankRect = new XRect(42, 432, 248, 58),
                TotalsRect = new XRect(362, 646, 190, 104),
                FooterRect = new XRect(42, 776, 510, 30),
                HeaderBandRect = new XRect(30, 36, pageWidth - 60, 32),
                QrRect = new XRect(42, 646, 96, 104),
                CustomerBox = false,
                SupplierBox = false,
                BankBox = false,
                MetaAsTable = false,
                HighlightItemHeader = true,
                AccentTotalBand = true,
                TitleAlignRight = false,
                TitleWhite = true,
                TitleBaseFontSize = 18,
                FooterColumns = 3,
                FrameThickness = 1.0
            },
            _ => new LayoutSpec
            {
                TitleRect = new XRect(style == InvoiceStyle.Style1 ? 302 : 42, 40, 250, 26),
                BarcodeRect = new XRect(42, 18, 108, 18),
                SupplierRect = style == InvoiceStyle.Style1 ? new XRect(42, 84, 168, 106) : new XRect(42, 84, 236, 92),
                CustomerRect = style == InvoiceStyle.Style1 ? new XRect(220, 84, 148, 106) : new XRect(292, 84, 260, 92),
                MetaRect = style == InvoiceStyle.Style1 ? new XRect(378, 84, 174, 74) : new XRect(42, 180, 230, 68),
                BankRect = style == InvoiceStyle.Style1 ? new XRect(378, 166, 174, 70) : null,
                ItemsRect = style == InvoiceStyle.Style1 ? new XRect(42, 258, 510, 152) : new XRect(42, 230, 510, 182),
                TotalsRect = style == InvoiceStyle.Style1 ? new XRect(370, 646, 182, 92) : new XRect(370, 658, 182, 92),
                QrRect = style == InvoiceStyle.Style1 ? new XRect(42, 638, 112, 104) : null,
                OuterFrameRect = style == InvoiceStyle.Style1 ? new XRect(28, 28, pageWidth - 56, pageHeight - 56) : null,
                HeaderRibbonRect = null,
                HeaderBandRect = null,
                FooterRect = new XRect(42, 776, 510, 30),
                CustomerBox = false,
                SupplierBox = false,
                BankBox = style == InvoiceStyle.Style1,
                MetaAsTable = style == InvoiceStyle.Style1,
                MetaSplitRatio = style == InvoiceStyle.Style1 ? 0.62 : 0.48,
                HighlightItemHeader = false,
                AccentTotalBand = false,
                TitleAlignRight = style == InvoiceStyle.Style1,
                TitleWhite = false,
                TitleBaseFontSize = 18,
                FooterColumns = style == InvoiceStyle.Style1 ? 2 : 3,
                FrameThickness = 1.0
            }
        };
    }

    private static InvoiceStyle ResolveStyle(string? templateId)
    {
        return (templateId ?? string.Empty).ToLowerInvariant() switch
        {
            "style1" or "default" => InvoiceStyle.Style1,
            "style2" or "compact" => InvoiceStyle.Style2,
            "style3" or "minimal" => InvoiceStyle.Style3,
            "style4" or "formal" => InvoiceStyle.Style4,
            "style5" or "detail" => InvoiceStyle.Style5,
            "style6" or "accent" => InvoiceStyle.Style6,
            _ => InvoiceStyle.Style1
        };
    }

    private static string DocumentTitle(InvoiceDocument document)
    {
        return document.Type switch
        {
            InvoiceDocumentType.Quote => Res("InvoicesTypeQuote").ToUpperInvariant(),
            InvoiceDocumentType.Order => Res("InvoicesTypeOrder").ToUpperInvariant(),
            InvoiceDocumentType.DeliveryNote => Res("InvoicesTypeDeliveryNote").ToUpperInvariant(),
            InvoiceDocumentType.CashReceiptIncome => Res("InvoicesTypeCashReceiptIncome").ToUpperInvariant(),
            InvoiceDocumentType.CashReceiptExpense => Res("InvoicesTypeCashReceiptExpense").ToUpperInvariant(),
            _ => Res("InvoicesTypeInvoice").ToUpperInvariant()
        };
    }

    private static string SupplierTitle(InvoiceDocument document)
        => Res("InvoicesSupplier");

    private static string CustomerTitle(InvoiceDocument document)
        => Res("InvoicesCustomer");

    private static InvoiceParty CreateBankParty(InvoiceParty supplier)
    {
        return new InvoiceParty
        {
            Name = string.IsNullOrWhiteSpace(supplier.BankName) ? string.Empty : supplier.BankName,
            Street = string.IsNullOrWhiteSpace(supplier.LegacyAccountNumber) ? string.Empty : $"{Res("InvoicesAccountNumber")}: {supplier.LegacyAccountNumber}",
            PostalCode = string.IsNullOrWhiteSpace(supplier.BankIban) ? string.Empty : $"{Res("InvoicesIban")}: {supplier.BankIban}",
            City = string.IsNullOrWhiteSpace(supplier.BankSwift) ? string.Empty : $"{Res("InvoicesSwift")}: {supplier.BankSwift}",
            Country = string.Empty
        };
    }

    private static bool HasBankData(InvoiceParty supplier)
        => !string.IsNullOrWhiteSpace(supplier.BankName)
           || !string.IsNullOrWhiteSpace(supplier.BankIban)
           || !string.IsNullOrWhiteSpace(supplier.BankSwift)
           || !string.IsNullOrWhiteSpace(supplier.LegacyAccountNumber);

    private static List<string> BuildPaymentLines(InvoicePaymentDetailsPreview payment)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(payment.BankName))
            lines.Add(payment.BankName);
        if (!string.IsNullOrWhiteSpace(payment.AccountNumber))
            lines.Add($"{Res("InvoicesAccountNumber")}: {payment.AccountNumber}");
        if (!string.IsNullOrWhiteSpace(payment.BankCode))
            lines.Add($"{Res("InvoicesBankCode")}: {payment.BankCode}");
        if (!string.IsNullOrWhiteSpace(payment.Iban))
            lines.Add($"{Res("InvoicesIban")}: {payment.Iban}");
        return lines;
    }

    private static List<string> BuildStyle3PaymentLines(InvoicePaymentDetailsPreview payment)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(payment.BankName))
            lines.Add(payment.BankName);
        if (!string.IsNullOrWhiteSpace(payment.BankCode))
            lines.Add($"{Res("InvoicesBankCode")}: {payment.BankCode}");
        if (!string.IsNullOrWhiteSpace(payment.Iban))
            lines.Add($"{Res("InvoicesIban")}: {payment.Iban}");
        else if (!string.IsNullOrWhiteSpace(payment.AccountNumber))
            lines.Add($"{Res("InvoicesAccountNumber")}: {payment.AccountNumber}");
        if (!string.IsNullOrWhiteSpace(payment.Swift))
            lines.Add($"{Res("InvoicesSwift")}: {payment.Swift}");
        return lines;
    }

    private static List<string> BuildStyle4PaymentLines(InvoicePaymentDetailsPreview payment)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(payment.BankName))
            lines.Add(payment.BankName);
        if (!string.IsNullOrWhiteSpace(payment.AccountNumber))
            lines.Add($"{Res("InvoicesAccountNumber")}: {payment.AccountNumber}");
        if (!string.IsNullOrWhiteSpace(payment.BankCode))
            lines.Add($"{Res("InvoicesBankCode")}: {payment.BankCode}");
        if (!string.IsNullOrWhiteSpace(payment.Iban))
            lines.Add($"{Res("InvoicesIban")}: {payment.Iban}");
        if (!string.IsNullOrWhiteSpace(payment.Swift))
            lines.Add($"{Res("InvoicesSwift")}: {payment.Swift}");
        return lines;
    }

    private static List<string> BuildStyle5PaymentLines(InvoicePaymentDetailsPreview payment)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(payment.BankName))
            lines.Add(payment.BankName);
        if (!string.IsNullOrWhiteSpace(payment.AccountNumber))
            lines.Add($"{Res("InvoicesAccountNumber")}: {payment.AccountNumber}");
        if (!string.IsNullOrWhiteSpace(payment.BankCode))
            lines.Add($"{Res("InvoicesBankCode")}: {payment.BankCode}");
        if (!string.IsNullOrWhiteSpace(payment.Iban))
            lines.Add($"{Res("InvoicesIban")}: {payment.Iban}");
        if (!string.IsNullOrWhiteSpace(payment.Swift))
            lines.Add($"{Res("InvoicesSwift")}: {payment.Swift}");
        return lines;
    }

    private static List<string> BuildStyle6PaymentLines(InvoicePaymentDetailsPreview payment)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(payment.BankName))
            lines.Add(payment.BankName);
        if (!string.IsNullOrWhiteSpace(payment.AccountNumber))
            lines.Add($"{Res("InvoicesAccountNumber")}: {payment.AccountNumber}");
        if (!string.IsNullOrWhiteSpace(payment.BankCode))
            lines.Add($"{Res("InvoicesBankCode")}: {payment.BankCode}");
        if (!string.IsNullOrWhiteSpace(payment.Iban))
            lines.Add($"{Res("InvoicesIban")}: {payment.Iban}");
        if (!string.IsNullOrWhiteSpace(payment.Swift))
            lines.Add($"{Res("InvoicesSwift")}: {payment.Swift}");
        return lines;
    }

    private static List<string> BuildQrFooterLines(InvoicePaymentDetailsPreview payment)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(payment.VariableSymbol))
            lines.Add($"{Res("InvoicesVariableSymbol")}: {payment.VariableSymbol}");
        if (!string.IsNullOrWhiteSpace(payment.InvoiceNumber))
            lines.Add(payment.InvoiceNumber);
        return lines;
    }

    private static List<(string Label, string Value)> BuildMetaRows(InvoiceDocument document)
    {
        var rows = new List<(string Label, string Value)>();
        AddIfNotEmpty(rows, Res("InvoicesPaymentForm"), HumanizePaymentMethod(document.PaymentMethod));
        AddIfNotEmpty(rows, Res("InvoicesVariableSymbol"), document.VariableSymbol);
        AddIfNotEmpty(rows, Res("InvoicesConstantSymbol"), document.ConstantSymbol);
        AddIfNotEmpty(rows, Res("InvoicesIssueDate"), document.IssueDate.ToString("d.M.yyyy", CultureInfo.InvariantCulture));
        AddIfNotEmpty(rows, Res("InvoicesDeliveryDate"), document.DeliveryDate.ToString("d.M.yyyy", CultureInfo.InvariantCulture));
        AddIfNotEmpty(rows, Res("InvoicesDueDate"), document.DueDate.ToString("d.M.yyyy", CultureInfo.InvariantCulture));
        return rows;
    }

    private static void AddIfNotEmpty(ICollection<(string Label, string Value)> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            rows.Add((label, value.Trim()));
    }

    private static List<string> BuildPartyLines(InvoiceParty party)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(party.Name))
            lines.Add(party.Name.Trim());
        if (!string.IsNullOrWhiteSpace(party.Street))
            lines.Add(party.Street.Trim());

        var postalCodeLooksLikeLabel = LooksLikeStandaloneLine(party.PostalCode);
        var cityLooksLikeLabel = LooksLikeStandaloneLine(party.City);
        if (!postalCodeLooksLikeLabel && !cityLooksLikeLabel)
        {
            var cityLine = string.Join(" ", new[] { party.PostalCode, party.City }.Where(static x => !string.IsNullOrWhiteSpace(x)));
            if (!string.IsNullOrWhiteSpace(cityLine))
                lines.Add(cityLine);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(party.PostalCode))
                lines.Add(party.PostalCode.Trim());
            if (!string.IsNullOrWhiteSpace(party.City))
                lines.Add(party.City.Trim());
        }
        if (!string.IsNullOrWhiteSpace(party.Country))
            lines.Add(party.Country.Trim());
        if (!string.IsNullOrWhiteSpace(party.Ico))
            lines.Add($"{Res("InvoicesIco")} {party.Ico}");
        if (!string.IsNullOrWhiteSpace(party.Dic))
            lines.Add($"{Res("InvoicesDic")} {party.Dic}");
        if (party.ShowVatIdOnDocument && !string.IsNullOrWhiteSpace(party.VatId))
            lines.Add($"{Res("InvoicesVatId")} {party.VatId}");
        if (!string.IsNullOrWhiteSpace(party.BankIban))
            lines.Add($"{Res("InvoicesIban")}: {party.BankIban}");
        if (!string.IsNullOrWhiteSpace(party.BankSwift))
            lines.Add($"{Res("InvoicesSwift")}: {party.BankSwift}");
        return lines;
    }

    private static bool LooksLikeStandaloneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.TrimStart();
        return trimmed.StartsWith($"{Res("InvoicesAccountNumber")}:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith($"{Res("InvoicesIban")}:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith($"{Res("InvoicesSwift")}:", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> WrapText(RenderContext ctx, string? text, XFont font, double maxWidth, int maxLines)
    {
        var value = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return [string.Empty];

        var result = new List<string>();
        foreach (var paragraph in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = string.Empty;
            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
                if (ctx.Gfx.MeasureString(candidate, font).Width <= maxWidth || string.IsNullOrEmpty(current))
                {
                    current = candidate;
                    continue;
                }

                result.Add(current);
                current = word;
                if (result.Count >= maxLines)
                    return TrimWrappedLines(ctx, result, font, maxWidth, maxLines);
            }

            if (!string.IsNullOrWhiteSpace(current))
                result.Add(current);
            if (result.Count >= maxLines)
                return TrimWrappedLines(ctx, result, font, maxWidth, maxLines);
        }

        return TrimWrappedLines(ctx, result, font, maxWidth, maxLines);
    }

    private static List<string> TrimWrappedLines(RenderContext ctx, List<string> lines, XFont font, double maxWidth, int maxLines)
    {
        if (lines.Count == 0)
            return [string.Empty];

        if (lines.Count <= maxLines)
            return lines;

        var trimmed = lines.Take(maxLines).ToList();
        trimmed[^1] = Ellipsize(ctx, trimmed[^1], font, maxWidth);
        return trimmed;
    }

    private static string Ellipsize(RenderContext ctx, string text, XFont font, double maxWidth)
    {
        if (ctx.Gfx.MeasureString(text, font).Width <= maxWidth)
            return text;

        var value = text;
        while (value.Length > 1 && ctx.Gfx.MeasureString(value + "...", font).Width > maxWidth)
            value = value[..^1];

        return value + "...";
    }

    private static XFont FitFont(RenderContext ctx, string text, double size, double maxWidth, bool bold)
    {
        var current = size;
        while (current >= 8)
        {
            var font = ctx.Font(current, bold);
            if (ctx.Gfx.MeasureString(text, font).Width <= maxWidth)
                return font;
            current -= 0.5;
        }

        return ctx.Font(8, bold);
    }

    private static string HumanizePaymentMethod(string? method)
    {
        return (method ?? string.Empty).ToLowerInvariant() switch
        {
            "bank_transfer" => Res("InvoicesPaymentBankTransfer"),
            "cash" => Res("InvoicesPaymentCash"),
            "card" => Res("InvoicesPaymentCard"),
            _ => method ?? string.Empty
        };
    }

    private static string Money(decimal value, string currency)
        => $"{value.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    private static string GetCashReceiptTitle(InvoiceDocument document)
        => InvoiceCashReceiptHelper.GetTitle(document.Type, document.CashReceiptDocumentVariant, document.CashReceiptTitle);

    private static decimal CalculateCashReceiptTotalWithVat(InvoiceDocument document)
    {
        var baseAmount = Math.Max(0m, document.CashReceiptBaseAmount);
        if (!document.Supplier.IsVatPayer)
            return Math.Round(baseAmount, 2);

        var explicitTotalWithVat = Math.Max(0m, document.CashReceiptTotalWithVat);
        if (explicitTotalWithVat > 0m)
            return Math.Round(explicitTotalWithVat, 2);

        return Math.Round(baseAmount * (1m + (Math.Max(0m, document.CashReceiptTaxRate) / 100m)), 2);
    }

    private static decimal ApplyCashReceiptRounding(decimal amount, string? mode)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        return mode switch
        {
            "cash_5" => Math.Round(rounded * 20m, 0, MidpointRounding.AwayFromZero) / 20m,
            "cash_up" => Math.Ceiling(rounded),
            "cash_down" => Math.Floor(rounded),
            _ => rounded
        };
    }

    private static decimal CalculateCashReceiptGrandTotal(InvoiceDocument document)
    {
        var totalWithVat = CalculateCashReceiptTotalWithVat(document);
        var nonTaxable = Math.Max(0m, document.CashReceiptNonTaxableAmount);
        return ApplyCashReceiptRounding(totalWithVat + nonTaxable, document.RoundingMode);
    }

    private static List<string> BuildCashReceiptPartyLines(InvoiceParty party)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(party.Name))
            lines.Add(party.Name.Trim());
        if (!string.IsNullOrWhiteSpace(party.Street))
            lines.Add(party.Street.Trim());

        var cityLine = string.Join(" ", new[] { party.PostalCode, party.City }.Where(static x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(cityLine))
            lines.Add(cityLine);
        if (!string.IsNullOrWhiteSpace(party.Ico))
            lines.Add($"{Res("InvoicesIco")} {party.Ico}");
        var taxId = string.IsNullOrWhiteSpace(party.VatId) ? party.Dic : party.VatId;
        if (!string.IsNullOrWhiteSpace(taxId))
            lines.Add($"{Res("InvoicesDicVatIdCombined")} {taxId}");
        return lines;
    }

    private static void DrawPartyTextLines(XGraphics gfx, XFont font, double x, double y, IReadOnlyList<string> lines, int maxLines, double width)
    {
        var currentY = y;
        foreach (var line in lines.Take(maxLines))
        {
            DrawWrappedText(gfx, line, font, new XRect(x, currentY, width, 12), 1);
            currentY += 15;
        }
    }

    private static void DrawCashReceiptBox(XGraphics gfx, XPen pen, XBrush accentBrush, XFont sectionFont, XFont labelFont, XFont bodyFont, XRect rect, string title, IReadOnlyList<string> lines)
    {
        gfx.DrawRectangle(pen, rect);
        gfx.DrawString(title, sectionFont, accentBrush, new XRect(rect.X + 8, rect.Y + 6, rect.Width - 16, 12), XStringFormats.TopLeft);
        var y = rect.Y + 26;
        foreach (var line in lines.Take(6))
        {
            DrawWrappedText(gfx, line, bodyFont, new XRect(rect.X + 8, y, rect.Width - 16, 12), 1);
            y += 13;
        }
    }

    private static void DrawPair(XGraphics gfx, XFont labelFont, XFont bodyFont, double x, double y, double labelWidth, string label, string? value)
    {
        gfx.DrawString(label, labelFont, XBrushes.Black, new XRect(x, y, labelWidth, 12), XStringFormats.TopLeft);
        gfx.DrawString(value ?? string.Empty, bodyFont, XBrushes.Black, new XRect(x + labelWidth, y, 120, 12), XStringFormats.TopLeft);
    }

    private static void DrawWrappedText(XGraphics gfx, string? text, XFont font, XRect rect, int maxLines)
    {
        var lines = WrapSimpleText(gfx, text, font, rect.Width, maxLines);
        var y = rect.Y;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XRect(rect.X, y, rect.Width, 11), XStringFormats.TopLeft);
            y += 11;
        }
    }

    private static List<string> WrapSimpleText(XGraphics gfx, string? text, XFont font, double maxWidth, int maxLines)
    {
        var value = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return [string.Empty];

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
            if (gfx.MeasureString(candidate, font).Width <= maxWidth || string.IsNullOrWhiteSpace(current))
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
            if (lines.Count == maxLines)
                break;
        }

        if (lines.Count < maxLines && !string.IsNullOrWhiteSpace(current))
            lines.Add(current);

        if (lines.Count > maxLines)
            lines = lines.Take(maxLines).ToList();
        return lines;
    }

    private static void DrawAccountingHeader(XGraphics gfx, XFont font, XRect tableRect)
    {
        gfx.DrawString(StripTrailingColon(Res("InvoicesCreditAccount")), font, XBrushes.Black, new XRect(tableRect.X + 10, tableRect.Y + 24, 180, 12), XStringFormats.TopLeft);
        gfx.DrawString(StripTrailingColon(Res("InvoicesAmountLabel")), font, XBrushes.Black, new XRect(tableRect.Right - 110, tableRect.Y + 24, 90, 12), XStringFormats.TopRight);
    }

    private static void DrawSimpleAccountingRow(XGraphics gfx, XPen pen, XFont font, XRect tableRect, int rowIndex, string? account, decimal amount, string currency)
    {
        var y = tableRect.Y + 48 + (rowIndex * 20);
        gfx.DrawLine(pen, tableRect.X, y, tableRect.Right, y);
        gfx.DrawString(account ?? string.Empty, font, XBrushes.Black, new XRect(tableRect.X + 6, y + 4, 96, 12), XStringFormats.TopLeft);
        gfx.DrawString(amount == 0m ? string.Empty : Money(amount, currency), font, XBrushes.Black, new XRect(tableRect.Right - 94, y + 4, 86, 12), XStringFormats.TopRight);
    }

    private static void DrawAccountingRow(XGraphics gfx, XFont font, XRect tableRect, int rowIndex, string? account, decimal amount, string currency)
    {
        var y = tableRect.Y + 42 + (rowIndex * 20);
        gfx.DrawLine(XPens.Black, tableRect.X + 8, y - 4, tableRect.Right - 8, y - 4);
        gfx.DrawString(account ?? string.Empty, font, XBrushes.Black, new XRect(tableRect.X + 10, y, 320, 12), XStringFormats.TopLeft);
        gfx.DrawString(amount == 0m ? string.Empty : Money(amount, currency), font, XBrushes.Black, new XRect(tableRect.Right - 120, y, 100, 12), XStringFormats.TopRight);
    }

    private static void DrawSignatureBlock(XGraphics gfx, XPen pen, XFont labelFont, XFont bodyFont, double x, double y, double width, string label, string? value)
    {
        gfx.DrawString(label, labelFont, XBrushes.Black, new XRect(x, y, width, 12), XStringFormats.TopLeft);
        gfx.DrawLine(pen, x, y + 34, x + width, y + 34);
        gfx.DrawString(value ?? string.Empty, bodyFont, XBrushes.Black, new XRect(x, y + 18, width, 12), XStringFormats.Center);
    }

    private static XFont SafeFont(double size, bool bold = false, string family = "Arial")
    {
        try
        {
            return new XFont(family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        }
        catch
        {
            return new XFont("Arial", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        }
    }

    private sealed class LayoutSpec
    {
        public required XRect TitleRect { get; init; }
        public required XRect BarcodeRect { get; init; }
        public required XRect SupplierRect { get; init; }
        public required XRect CustomerRect { get; init; }
        public required XRect MetaRect { get; init; }
        public required XRect ItemsRect { get; init; }
        public required XRect TotalsRect { get; init; }
        public required XRect FooterRect { get; init; }
        public XRect? OuterFrameRect { get; init; }
        public XRect? HeaderBandRect { get; init; }
        public XRect? HeaderRibbonRect { get; init; }
        public XRect? QrRect { get; init; }
        public XRect? BankRect { get; init; }
        public bool SupplierBox { get; init; }
        public bool CustomerBox { get; init; }
        public bool BankBox { get; init; }
        public bool MetaAsTable { get; init; }
        public bool HighlightItemHeader { get; init; }
        public bool AccentTotalBand { get; init; }
        public double MetaSplitRatio { get; init; } = 0.48;
        public bool TitleAlignRight { get; init; }
        public bool TitleWhite { get; init; }
        public double TitleBaseFontSize { get; init; }
        public int FooterColumns { get; init; }
        public double FrameThickness { get; init; }
    }

    private enum InvoiceStyle
    {
        Style1,
        Style2,
        Style3,
        Style4,
        Style5,
        Style6
    }

    private sealed class RenderContext
    {
        public RenderContext(XGraphics gfx, InvoiceDocument document, ThemePalette palette, LayoutSpec layout, InvoiceQrPaymentPreview qrPreview, InvoicePaymentDetailsPreview paymentDetails)
        {
            Gfx = gfx;
            Document = document;
            Palette = palette;
            Layout = layout;
            QrPreview = qrPreview;
            PaymentDetails = paymentDetails;
        }

        public XGraphics Gfx { get; }
        public InvoiceDocument Document { get; }
        public ThemePalette Palette { get; }
        public LayoutSpec Layout { get; }
        public InvoiceQrPaymentPreview QrPreview { get; }
        public InvoicePaymentDetailsPreview PaymentDetails { get; }
        public double MetaBottom { get; set; }
        public double ItemsBottom { get; set; }
        public double QrBottom { get; set; }
        public double TotalsBottom { get; set; }

        public XFont Font(double size, bool bold = false, string family = "Arial")
        {
            var style = bold ? XFontStyleEx.Bold : XFontStyleEx.Regular;
            try
            {
                return new XFont(family, size, style);
            }
            catch
            {
                return new XFont("Arial", size, style);
            }
        }
    }

    private sealed class ThemePalette
    {
        public required XColor Accent { get; init; }
        public required XColor Border { get; init; }
        public required XColor Light { get; init; }
        public required XColor Muted { get; init; }

        public XSolidBrush AccentBrush => new(Accent);
        public XSolidBrush MutedBrush => new(Muted);
        public XPen AccentPen => new(Accent, 1.2);
        public XPen LightPen => new(Light, 0.7);
        public XPen BorderPen(double width = 1.0) => new(Border, width);

        public static ThemePalette FromTheme(string? theme)
        {
            return (theme ?? string.Empty).ToLowerInvariant() switch
            {
                "emerald" => new ThemePalette { Accent = XColor.FromArgb(0x2E, 0xA0, 0x75), Border = XColors.Black, Light = XColor.FromArgb(0xB9, 0xC8, 0xC2), Muted = XColor.FromArgb(0xE7, 0xF4, 0xEE) },
                "graphite" => new ThemePalette { Accent = XColor.FromArgb(0x52, 0x63, 0x7A), Border = XColors.Black, Light = XColor.FromArgb(0xC7, 0xCE, 0xD6), Muted = XColor.FromArgb(0xEC, 0xEF, 0xF2) },
                "violet" => new ThemePalette { Accent = XColor.FromArgb(0x7A, 0x60, 0xC6), Border = XColors.Black, Light = XColor.FromArgb(0xD8, 0xD0, 0xEE), Muted = XColor.FromArgb(0xF1, 0xEE, 0xFB) },
                _ => new ThemePalette { Accent = XColor.FromArgb(0x5B, 0x8F, 0xD1), Border = XColors.Black, Light = XColor.FromArgb(0xC9, 0xD9, 0xEE), Muted = XColor.FromArgb(0xE8, 0xF0, 0xFA) }
            };
        }
    }

    private static string Res(string key)
        => CurrentDocumentLocalizer.Value?.Get(key) ?? App.Current?.TryFindResource(key) as string ?? key;

    private static string StripTrailingColon(string value)
        => value.TrimEnd().TrimEnd(':');

    private static DocumentLocalizationService CreateDocumentLocalizer(string? languageCode)
    {
        var localizer = new DocumentLocalizationService();
        localizer.LoadLanguage((languageCode ?? string.Empty).Trim().ToLowerInvariant());
        return localizer;
    }
}
