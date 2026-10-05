using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Tests
{
    public class RtfToNativeDocxServiceTests
    {
        [Fact]
        public void ConvertRtf_ProducesExpandedNativeDocx()
        {
            const string rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs24\pard Hello ${COMPANY_Name}\par}";
            var bytes = RtfToNativeDocxService.ConvertRtf(rtf);

            var path = Path.Combine(Path.GetTempPath(), "rtf-docx-" + Guid.NewGuid() + ".docx");
            try
            {
                File.WriteAllBytes(path, bytes);
                Assert.True(DocumentGenerationService.IsExpandedNativeDocx(path));
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void ExpandAltChunk_TurnsEmbeddedRtfIntoParagraphs()
        {
            var directory = Path.Combine(Path.GetTempPath(), "altchunk-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var rtfPath = Path.Combine(directory, "content.rtf");
            var docxPath = Path.Combine(directory, "template.docx");
            File.WriteAllText(rtfPath, @"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs24\pard Pracovni smlouva\par}");
            SafeFileService.WriteJsonAtomic(Path.Combine(directory, "editor-layout.json"), new TemplateEditorLayoutSettings
            {
                PageSizeKey = "letter",
                OrientationKey = "portrait",
                MarginKey = "narrow"
            });

            try
            {
                new DocumentGenerationService().GenerateDocxFromRtf(rtfPath, docxPath, new Dictionary<string, string>());
                Assert.False(DocumentGenerationService.CanOpenAsTemplateDocx(docxPath));

                var expanded = RtfToNativeDocxService.TryExpandAltChunkDocx(docxPath);
                Assert.NotNull(expanded);

                var expandedPath = Path.Combine(directory, "expanded.docx");
                File.WriteAllBytes(expandedPath, expanded!);
                Assert.True(DocumentGenerationService.IsExpandedNativeDocx(expandedPath));

                using var doc = WordprocessingDocument.Open(expandedPath, false);
                var text = string.Concat(doc.MainDocumentPart!.Document.Body!.Descendants<Text>().Select(item => item.Text));
                Assert.Contains("Pracovni smlouva", text);

                var page = doc.MainDocumentPart.Document.Body.Elements<SectionProperties>().Last().GetFirstChild<PageSize>();
                Assert.Equal(12240u, page!.Width?.Value);
                Assert.Equal(15840u, page.Height?.Value);
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
    }
}
