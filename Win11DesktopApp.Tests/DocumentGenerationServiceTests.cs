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

        [Fact]
        public void GenerateDocx_DropsWordCommentsAndKeepsTheCommentedText()
        {
            var templatePath = Path.Combine(_root, "commented.docx");
            WriteCommentedTemplate(templatePath);

            var outputPath = Path.Combine(_root, "employee.docx");
            new DocumentGenerationService().GenerateDocx(templatePath, outputPath, new Dictionary<string, string>
            {
                ["EMPLOYEE_FullName"] = "Maksym Yashniuk"
            });

            using (var template = WordprocessingDocument.Open(templatePath, false))
            {
                Assert.NotNull(template.MainDocumentPart!.WordprocessingCommentsPart);
            }

            using var generated = WordprocessingDocument.Open(outputPath, false);
            var main = generated.MainDocumentPart!;
            Assert.Null(main.WordprocessingCommentsPart);
            Assert.Null(main.WordprocessingCommentsExPart);
            Assert.Empty(main.Document.Descendants<CommentRangeStart>());
            Assert.Empty(main.Document.Descendants<CommentReference>());
            Assert.Empty(main.HeaderParts.SelectMany(header => header.Header.Descendants<CommentReference>()));

            var text = string.Concat(main.Document.Body!.Descendants<Text>().Select(item => item.Text));
            var headerText = string.Concat(main.HeaderParts.SelectMany(header => header.Header.Descendants<Text>().Select(item => item.Text)));
            Assert.Contains("Smlouva Maksym Yashniuk", text);
            Assert.Contains("Hlavicka", headerText);
            Assert.DoesNotContain("skryta poznamka", text);
            Assert.DoesNotContain("${EMPLOYEE_FullName}", text);

            var stripped = DocumentGenerationService.WithoutWordComments(File.ReadAllBytes(templatePath));
            using var strippedDoc = WordprocessingDocument.Open(new MemoryStream(stripped), false);
            Assert.Null(strippedDoc.MainDocumentPart!.WordprocessingCommentsPart);
            var strippedText = string.Concat(strippedDoc.MainDocumentPart.Document.Body!.Descendants<Text>().Select(item => item.Text));
            Assert.Contains("Smlouva ${EMPLOYEE_FullName}", strippedText);

            var plain = DocumentGenerationService.WithoutWordComments(stripped);
            Assert.Same(stripped, plain);
        }

        private static void WriteCommentedTemplate(string path)
        {
            using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
            var main = document.AddMainDocumentPart();
            var headerPart = main.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(
                new Paragraph(
                    new CommentRangeStart { Id = "1" },
                    new Run(new Text("Hlavicka")),
                    new CommentRangeEnd { Id = "1" },
                    new Run(new CommentReference { Id = "1" })));
            headerPart.Header.Save();

            var body = new Body(
                new Paragraph(
                    new CommentRangeStart { Id = "0" },
                    new Run(new Text("Smlouva ") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }),
                    new CommentRangeEnd { Id = "0" },
                    new Run(new CommentReference { Id = "0" }),
                    new Run(new Text("${EMPLOYEE_FullName}"))),
                new SectionProperties(
                    new HeaderReference
                    {
                        Type = HeaderFooterValues.Default,
                        Id = main.GetIdOfPart(headerPart)
                    }));
            main.Document = new Document(body);

            var commentsPart = main.AddNewPart<WordprocessingCommentsPart>();
            commentsPart.Comments = new Comments(
                new Comment(
                    new Paragraph(new Run(new Text("skryta poznamka"))))
                {
                    Id = "0",
                    Author = "Agency",
                    Initials = "A",
                    Date = DateTime.UtcNow
                },
                new Comment(
                    new Paragraph(new Run(new Text("poznamka v hlavicce"))))
                {
                    Id = "1",
                    Author = "Agency",
                    Initials = "A",
                    Date = DateTime.UtcNow
                });
            commentsPart.Comments.Save();
            main.Document.Save();
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
