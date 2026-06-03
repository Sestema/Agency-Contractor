using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using OpenCvSharp;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services.Scanning
{
    public sealed class ScanSessionStore : IScanSessionStore
    {
        private const int ThumbnailMaxDim = 180;
        private readonly ImageEnhancementService _imageService;
        private bool _disposed;

        public ScanSessionStore(ImageEnhancementService imageService)
        {
            _imageService = imageService;
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgencyContractor",
                "ScanSessions",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            SessionFolder = baseDir;
        }

        public string SessionFolder { get; }

        public ScanPage AddPageFromFile(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Source file not found.", sourcePath);

            var order = NextOrder();
            var pagePath = Path.Combine(SessionFolder, $"page-{order:D3}.jpg");
            NormalizeToJpeg(sourcePath, pagePath);

            var thumbPath = Path.Combine(SessionFolder, $"thumb-{order:D3}.jpg");
            CreateThumbnail(pagePath, thumbPath);

            return new ScanPage
            {
                SourcePath = pagePath,
                ThumbnailPath = thumbPath,
                Order = order
            };
        }

        public IReadOnlyList<ScanPage> AddPagesFromFile(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Source file not found.", sourcePath);

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext == ".pdf")
                return AddPagesFromPdf(sourcePath);

            return new List<ScanPage> { AddPageFromFile(sourcePath) };
        }

        public void ReplacePageFile(ScanPage page, string newPath)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            if (string.IsNullOrWhiteSpace(newPath) || !File.Exists(newPath))
                throw new FileNotFoundException("Edited file not found.", newPath);

            NormalizeToJpeg(newPath, page.SourcePath);
            if (!string.IsNullOrEmpty(page.ThumbnailPath))
                CreateThumbnail(page.SourcePath, page.ThumbnailPath);
            page.IsEdited = true;
        }

        public void RemovePage(ScanPage page)
        {
            if (page == null)
                return;

            TryDelete(page.SourcePath);
            TryDelete(page.ThumbnailPath);
        }

        public void Reorder(IReadOnlyList<ScanPage> orderedPages)
        {
            for (var i = 0; i < orderedPages.Count; i++)
                orderedPages[i].Order = i + 1;
        }

        public void Cleanup()
        {
            if (_disposed)
                return;

            try
            {
                if (Directory.Exists(SessionFolder))
                    Directory.Delete(SessionFolder, recursive: true);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ScanSessionStore.Cleanup", ex.Message);
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Cleanup();
        }

        private int NextOrder()
        {
            var max = 0;
            foreach (var file in Directory.GetFiles(SessionFolder, "page-*.jpg"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length >= 5 && int.TryParse(name.AsSpan(5), out var n))
                    max = Math.Max(max, n);
            }

            return max + 1;
        }

        private IReadOnlyList<ScanPage> AddPagesFromPdf(string pdfPath)
        {
            var pages = new List<ScanPage>();
            try
            {
                using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(2));
                var pageCount = docReader.GetPageCount();
                for (var i = 0; i < pageCount; i++)
                {
                    using var pageReader = docReader.GetPageReader(i);
                    var rawBytes = pageReader.GetImage();
                    var width = pageReader.GetPageWidth();
                    var height = pageReader.GetPageHeight();
                    if (width <= 0 || height <= 0)
                        continue;

                    var order = NextOrder();
                    var pagePath = Path.Combine(SessionFolder, $"page-{order:D3}.jpg");
                    var stride = width * 4;
                    var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rawBytes, stride);
                    bmp.Freeze();
                    SaveJpeg(bmp, pagePath);

                    var thumbPath = Path.Combine(SessionFolder, $"thumb-{order:D3}.jpg");
                    CreateThumbnail(pagePath, thumbPath);

                    pages.Add(new ScanPage
                    {
                        SourcePath = pagePath,
                        ThumbnailPath = thumbPath,
                        Order = order
                    });
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ScanSessionStore.AddPagesFromPdf", ex);
                throw;
            }

            return pages;
        }

        private void NormalizeToJpeg(string sourcePath, string destPath)
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" && string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                return;

            if (ext is ".jpg" or ".jpeg")
            {
                SafeFileService.CopyFile(sourcePath, destPath);
                return;
            }

            using var mat = _imageService.LoadImage(sourcePath);
            _imageService.SaveImage(mat, destPath);
        }

        private void CreateThumbnail(string sourcePath, string thumbPath)
        {
            using var mat = _imageService.LoadImage(sourcePath);
            using var resized = new Mat();
            var maxSide = Math.Max(mat.Width, mat.Height);
            if (maxSide > ThumbnailMaxDim)
            {
                var scale = ThumbnailMaxDim / (double)maxSide;
                Cv2.Resize(mat, resized, new OpenCvSharp.Size((int)(mat.Width * scale), (int)(mat.Height * scale)));
            }
            else
            {
                mat.CopyTo(resized);
            }

            _imageService.SaveImage(resized, thumbPath);
        }

        private static void SaveJpeg(BitmapSource source, string outputPath)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write);
            encoder.Save(stream);
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
