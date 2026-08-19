using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class DocumentGenerationServiceTests : IDisposable
    {
        private readonly string _root;

        public DocumentGenerationServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "DocGenTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [Fact]
        public void GenerateDocxFromRtf_AppliesLandscapeFromEditorLayout()
        {
            var rtfPath = Path.Combine(_root, "content.rtf");
            File.WriteAllText(rtfPath, @"{\rtf1\ansi Hello}");
            SafeFileService.WriteJsonAtomic(Path.Combine(_root, "editor-layout.json"), new TemplateEditorLayoutSettings
            {
                PageSizeKey = "a4",
                OrientationKey = "landscape",
                MarginKey = "normal"
            });

            var outputPath = Path.Combine(_root, "out.docx");
            new DocumentGenerationService().GenerateDocxFromRtf(rtfPath, outputPath, new Dictionary<string, string>());

            using var doc = WordprocessingDocument.Open(outputPath, false);
            var pageSize = doc.MainDocumentPart?.Document.Body?
                .Elements<SectionProperties>()
                .LastOrDefault()?
                .GetFirstChild<PageSize>();

            Assert.NotNull(pageSize);
            Assert.Equal(PageOrientationValues.Landscape, pageSize!.Orient?.Value);
            Assert.Equal(16838u, pageSize.Width?.Value);
            Assert.Equal(11906u, pageSize.Height?.Value);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, true);
            }
            catch
            {
            }
        }
    }
}
