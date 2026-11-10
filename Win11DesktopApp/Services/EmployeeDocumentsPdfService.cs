using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Win11DesktopApp.Services
{
    internal sealed record EmployeeDocumentPdfSource(string Name, string FilePath);

    internal sealed record EmployeeDocumentsPdfResult(int IncludedCount, IReadOnlyList<string> SkippedNames);

    /// <summary>
    /// Joins an employee's document scans into one PDF. Only reads the source files;
    /// touches no UI, so it can run on a background thread.
    /// </summary>
    internal static class EmployeeDocumentsPdfService
    {
        private const double A4ShortSide = 595.28;
        private const double A4LongSide = 841.89;
        private const double PageMargin = 20;
        private const int MaxImageSide = 2200;
        private const int JpegQuality = 85;

        public static EmployeeDocumentsPdfResult Build(IReadOnlyList<EmployeeDocumentPdfSource> sources, string outputPath)
        {
            var skipped = new List<string>();
            var included = 0;
            var imageStreams = new List<MemoryStream>();

            try
            {
                using var output = new PdfDocument();
                foreach (var source in sources)
                {
                    try
                    {
                        if (string.Equals(Path.GetExtension(source.FilePath), ".pdf", StringComparison.OrdinalIgnoreCase))
                            AppendPdf(output, source.FilePath);
                        else
                            AppendImage(output, source.FilePath, imageStreams);

                        included++;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("EmployeeDocumentsPdfService.Build", $"{source.Name}: {ex.Message}");
                        skipped.Add(source.Name);
                    }
                }

                if (included > 0)
                {
                    using var buffer = new MemoryStream();
                    output.Save(buffer, false);
                    SafeFileService.WriteBytesAtomic(outputPath, buffer.ToArray());
                }
            }
            finally
            {
                foreach (var stream in imageStreams)
                    stream.Dispose();
            }

            return new EmployeeDocumentsPdfResult(included, skipped);
        }

        private static void AppendPdf(PdfDocument output, string filePath)
        {
            using var input = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            if (input.PageCount == 0)
                throw new InvalidDataException("PDF has no pages.");

            for (var index = 0; index < input.PageCount; index++)
                output.AddPage(input.Pages[index]);
        }

        // The stream must stay open until the output document is saved.
        private static void AppendImage(PdfDocument output, string filePath, List<MemoryStream> imageStreams)
        {
            var stream = new MemoryStream(PrepareImage(filePath));
            imageStreams.Add(stream);
            using var image = XImage.FromStream(stream);

            var isLandscape = image.PixelWidth > image.PixelHeight;
            var page = output.AddPage();
            page.Width = XUnit.FromPoint(isLandscape ? A4LongSide : A4ShortSide);
            page.Height = XUnit.FromPoint(isLandscape ? A4ShortSide : A4LongSide);

            var pageWidth = page.Width.Point;
            var pageHeight = page.Height.Point;
            var scale = Math.Min(
                (pageWidth - 2 * PageMargin) / image.PixelWidth,
                (pageHeight - 2 * PageMargin) / image.PixelHeight);
            var width = image.PixelWidth * scale;
            var height = image.PixelHeight * scale;

            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawImage(image, (pageWidth - width) / 2, (pageHeight - height) / 2, width, height);
        }

        private static byte[] PrepareImage(string filePath)
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            BitmapSource bitmap = frame;
            var longestSide = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
            if (longestSide > MaxImageSide)
            {
                var scale = (double)MaxImageSide / longestSide;
                bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
            }

            var angle = GetRotationAngle(frame);
            if (angle != 0)
                bitmap = new TransformedBitmap(bitmap, new RotateTransform(angle));

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0)));

            using var buffer = new MemoryStream();
            encoder.Save(buffer);
            return buffer.ToArray();
        }

        // Phone photos often store the turn in EXIF instead of rotating the pixels.
        private static double GetRotationAngle(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata metadata
                    && metadata.ContainsQuery("System.Photo.Orientation")
                    && metadata.GetQuery("System.Photo.Orientation") is ushort orientation)
                {
                    return orientation switch
                    {
                        3 => 180,
                        6 => 90,
                        8 => 270,
                        _ => 0
                    };
                }
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                LoggingService.LogWarning("EmployeeDocumentsPdfService.GetRotationAngle", ex.Message);
            }

            return 0;
        }
    }
}
