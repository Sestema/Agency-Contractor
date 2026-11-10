using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class EmployeeDocumentsPdfServiceTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "pdf-bundle-" + Guid.NewGuid().ToString("N"));
        private readonly string _output;

        public EmployeeDocumentsPdfServiceTests()
        {
            Directory.CreateDirectory(_folder);
            _output = Path.Combine(_folder, "out", "result.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        }

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private string CreateJpeg(string name, int width, int height)
        {
            var path = Path.Combine(_folder, name);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null,
                new byte[width * height * 3], width * 3);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }

        private string CreatePdf(string name, int pages)
        {
            var path = Path.Combine(_folder, name);
            using var document = new PdfDocument();
            for (var index = 0; index < pages; index++)
                document.AddPage();
            document.Save(path);
            return path;
        }

        private int OutputPageCount()
        {
            using var document = PdfReader.Open(_output, PdfDocumentOpenMode.Import);
            return document.PageCount;
        }

        private static EmployeeDocumentPdfSource Source(string name, string path) => new(name, path);

        [Fact]
        public void JoinsImagesAndAllPdfPages()
        {
            var result = EmployeeDocumentsPdfService.Build(new[]
            {
                Source("Passport", CreateJpeg("a.jpg", 300, 400)),
                Source("Visa", CreatePdf("b.pdf", 2)),
                Source("Insurance", CreateJpeg("c.jpg", 300, 400))
            }, _output);

            Assert.Equal(3, result.IncludedCount);
            Assert.Empty(result.SkippedNames);
            Assert.Equal(4, OutputPageCount());
        }

        [Fact]
        public void KeepsDocumentOrder()
        {
            EmployeeDocumentsPdfService.Build(new[]
            {
                Source("Passport", CreatePdf("a.pdf", 1)),
                Source("Landscape", CreateJpeg("b.jpg", 800, 300))
            }, _output);

            using var document = PdfReader.Open(_output, PdfDocumentOpenMode.Import);
            Assert.True(document.Pages[0].Height.Point > document.Pages[0].Width.Point);
            Assert.True(document.Pages[1].Width.Point > document.Pages[1].Height.Point);
        }

        [Fact]
        public void SkipsDamagedFileAndKeepsTheRest()
        {
            var broken = Path.Combine(_folder, "broken.pdf");
            File.WriteAllText(broken, "not a pdf");
            var brokenImage = Path.Combine(_folder, "broken.jpg");
            File.WriteAllText(brokenImage, "not an image");

            var result = EmployeeDocumentsPdfService.Build(new[]
            {
                Source("Passport", CreateJpeg("a.jpg", 300, 400)),
                Source("Visa", broken),
                Source("Insurance", brokenImage)
            }, _output);

            Assert.Equal(1, result.IncludedCount);
            Assert.Equal(new[] { "Visa", "Insurance" }, result.SkippedNames);
            Assert.Equal(1, OutputPageCount());
        }

        [Fact]
        public void NothingReadable_DoesNotCreateFile()
        {
            var missing = Path.Combine(_folder, "missing.jpg");

            var result = EmployeeDocumentsPdfService.Build(new[] { Source("Passport", missing) }, _output);

            Assert.Equal(0, result.IncludedCount);
            Assert.False(File.Exists(_output));
        }

        [Fact]
        public void DoesNotChangeSourceFolder()
        {
            var path = CreateJpeg("a.jpg", 300, 400);
            var before = File.ReadAllBytes(path);
            var filesBefore = Directory.GetFiles(_folder).Length;

            EmployeeDocumentsPdfService.Build(new[] { Source("Passport", path) }, _output);

            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(filesBefore, Directory.GetFiles(_folder).Length);
        }

        [Fact]
        public void HugeImage_IsReducedInFile()
        {
            var result = EmployeeDocumentsPdfService.Build(new[]
            {
                Source("Passport", CreateJpeg("big.jpg", 5000, 3000))
            }, _output);

            Assert.Equal(1, result.IncludedCount);
            Assert.Equal(1, OutputPageCount());
            Assert.True(new FileInfo(_output).Length > 0);
        }
    }
}
