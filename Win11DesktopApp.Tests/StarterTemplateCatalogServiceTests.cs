using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class StarterTemplateCatalogServiceTests : IDisposable
    {
        private readonly string _catalogRoot;

        public StarterTemplateCatalogServiceTests()
        {
            _catalogRoot = Path.Combine(Path.GetTempPath(), "AgencyContractorStarterCatalogTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_catalogRoot);
            Directory.CreateDirectory(Path.Combine(_catalogRoot, "contracts"));

            var catalog = new StarterTemplateCatalogFile
            {
                Templates =
                {
                    new StarterTemplateCatalogEntry
                    {
                        Id = "sample-docx",
                        Category = "contract",
                        Title = "Sample DOCX",
                        Format = "DOCX",
                        RelativeContentPath = "contracts/sample-docx.rtf"
                    },
                    new StarterTemplateCatalogEntry
                    {
                        Id = "sample-pdf",
                        Category = "contract",
                        Title = "Ignored PDF",
                        Format = "PDF",
                        RelativeContentPath = "contracts/sample-pdf.txt"
                    }
                }
            };

            File.WriteAllText(
                Path.Combine(_catalogRoot, "catalog.json"),
                JsonSerializer.Serialize(catalog),
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(_catalogRoot, "contracts", "sample-docx.rtf"),
                "{\\rtf1\\ansi Sample}",
                Encoding.UTF8);
        }

        [Fact]
        public void GetContractTemplates_ShouldReturnOnlyDocxContractEntries()
        {
            var service = new StarterTemplateCatalogService(_catalogRoot);

            var templates = service.GetContractTemplates();

            Assert.Single(templates);
            Assert.Equal("sample-docx", templates.Single().Id);
        }

        [Fact]
        public void LoadTemplateRtf_ShouldReturnBundledRtfContent()
        {
            var service = new StarterTemplateCatalogService(_catalogRoot);
            var entry = service.GetContractTemplates().Single();

            var content = service.LoadTemplateRtf(entry);

            Assert.Equal("{\\rtf1\\ansi Sample}", content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_catalogRoot))
                    Directory.Delete(_catalogRoot, true);
            }
            catch
            {
            }
        }
    }
}
