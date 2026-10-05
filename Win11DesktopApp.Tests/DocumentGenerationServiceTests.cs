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

        [Fact]
        public void GeneratePreparedDocx_ExpandsAltChunkAndWrapsToTheEditorPage()
        {
            var rtfPath = Path.Combine(_root, "content.rtf");
            File.WriteAllText(rtfPath, @"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs24\pard Pracovni $\{EMPLOYEE_FullName\}\par}");
            SafeFileService.WriteJsonAtomic(Path.Combine(_root, "editor-layout.json"), new TemplateEditorLayoutSettings
            {
                PageSizeKey = "letter",
                OrientationKey = "portrait",
                MarginKey = "narrow"
            });

            var templatePath = Path.Combine(_root, "template.docx");
            new DocumentGenerationService().GenerateDocxFromRtf(rtfPath, templatePath, new Dictionary<string, string>());
            Assert.False(DocumentGenerationService.CanOpenAsTemplateDocx(templatePath));

            var outputPath = Path.Combine(_root, "formed.docx");
            new DocumentGenerationService().GeneratePreparedDocx(_root, templatePath, outputPath, new Dictionary<string, string>
            {
                ["EMPLOYEE_FullName"] = "Maksym Yashniuk"
            });

            Assert.True(DocumentGenerationService.IsExpandedNativeDocx(outputPath));
            using var doc = WordprocessingDocument.Open(outputPath, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            Assert.Empty(body.Descendants<AltChunk>());
            var text = string.Concat(body.Descendants<Text>().Select(item => item.Text));
            Assert.Contains("Maksym Yashniuk", text);
            Assert.DoesNotContain("${EMPLOYEE_FullName}", text);

            var page = body.Elements<SectionProperties>().Last().GetFirstChild<PageSize>();
            Assert.Equal(12240u, page!.Width?.Value);
            Assert.Equal(15840u, page.Height?.Value);
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
