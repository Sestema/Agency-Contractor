using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services.Scanning
{
    public sealed class ScanDocumentAssemblyService : IScanDocumentAssemblyService
    {
        public Task<string> ExportAsync(
            IReadOnlyList<string> pagePaths,
            string outputFolder,
            ScanExportOptions options,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => ExportInternal(pagePaths, outputFolder), cancellationToken);
        }

        private static string ExportInternal(IReadOnlyList<string> pagePaths, string outputFolder)
        {
            if (pagePaths == null || pagePaths.Count == 0)
                throw new InvalidOperationException("No pages to export.");

            Directory.CreateDirectory(outputFolder);

            if (pagePaths.Count == 1)
            {
                var jpgPath = Path.Combine(outputFolder, $"scan-{Guid.NewGuid():N}.jpg");
                SafeFileService.CopyFile(pagePaths[0], jpgPath);
                return jpgPath;
            }

            var pdfPath = Path.Combine(outputFolder, $"scan-{Guid.NewGuid():N}.pdf");
            using var document = new PdfDocument();
            document.Info.Title = "Scanned document";

            const double margin = 28;

            foreach (var pagePath in pagePaths)
            {
                if (!File.Exists(pagePath))
                    continue;

                using var image = XImage.FromFile(pagePath);
                var page = document.AddPage();
                page.Width = XUnit.FromMillimeter(210);
                page.Height = XUnit.FromMillimeter(297);

                using var gfx = XGraphics.FromPdfPage(page);
                var maxWidth = page.Width.Point - margin * 2;
                var maxHeight = page.Height.Point - margin * 2;
                var imageWidthPt = image.PixelWidth * 72.0 / image.HorizontalResolution;
                var imageHeightPt = image.PixelHeight * 72.0 / image.VerticalResolution;
                if (imageWidthPt <= 0)
                    imageWidthPt = image.PixelWidth * 72.0 / 96.0;
                if (imageHeightPt <= 0)
                    imageHeightPt = image.PixelHeight * 72.0 / 96.0;

                var scale = Math.Min(maxWidth / imageWidthPt, maxHeight / imageHeightPt);
                var drawWidth = imageWidthPt * scale;
                var drawHeight = imageHeightPt * scale;
                var x = margin + (maxWidth - drawWidth) / 2;
                var y = margin + (maxHeight - drawHeight) / 2;
                gfx.DrawImage(image, x, y, drawWidth, drawHeight);
            }

            document.Save(pdfPath);
            return pdfPath;
        }
    }
}
