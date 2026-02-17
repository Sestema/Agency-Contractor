using System;
using System.IO;
using System.Threading.Tasks;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class TemplateEditorViewModelTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly TemplateService _templateService;
        private readonly TagCatalogService _tagCatalogService;

        public TemplateEditorViewModelTests()
        {
            // Setup
            _testRootPath = Path.Combine(Path.GetTempPath(), "EditorTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _appSettingsService.Settings.LanguageCode = "en"; // Use English folder names in tests

            var folderService = new FolderService(_appSettingsService);
            _templateService = new TemplateService(_appSettingsService, folderService);
            _tagCatalogService = new TagCatalogService();
        }

        [Fact]
        public async Task OnWebViewReady_ShouldLoadDocx()
        {
            // Arrange
            var firmName = "TestFirm";
            var fileName = "test.docx";
            var filePath = SetupTemplateFile(firmName, fileName, "dummy content");
            
            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };
            
            var vm = new TemplateEditorViewModel(firmName, template, _tagCatalogService, _templateService);
            
            string? loadedType = null;
            vm.SendMessageToWebView += (type, data) => loadedType = type;

            // Act
            await vm.OnWebViewReady();

            // Assert
            Assert.Equal("loadDocx", loadedType);
        }

        [Fact]
        public async Task OnWebViewReady_ShouldLoadXlsx()
        {
            // Arrange
            var firmName = "TestFirm";
            var fileName = "test.xlsx";
            var filePath = SetupTemplateFile(firmName, fileName, "dummy content");
            
            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };
            
            var vm = new TemplateEditorViewModel(firmName, template, _tagCatalogService, _templateService);
            
            string? loadedType = null;
            vm.SendMessageToWebView += (type, data) => loadedType = type;

            // Act
            await vm.OnWebViewReady();

            // Assert
            Assert.Equal("loadXlsx", loadedType);
        }

        [Fact]
        public async Task OnWebViewReady_ShouldLoadPdf()
        {
             // Arrange
            var firmName = "TestFirm";
            var fileName = "test.pdf";
            var filePath = SetupTemplateFile(firmName, fileName, "pdf content");
            
            var template = new TemplateEntry { FilePath = $"Templates/Test_Template/{fileName}" };
            
            var vm = new TemplateEditorViewModel(firmName, template, _tagCatalogService, _templateService);
            
            string? loadedType = null;
            vm.SendMessageToWebView += (type, data) => loadedType = type;

            // Act
            await vm.OnWebViewReady();

            // Assert
            Assert.Equal("loadPdf", loadedType);
        }

        [Fact]
        public async Task OnWebViewReady_ShouldReportError_WhenFileMissing()
        {
             // Arrange
            var firmName = "TestFirm";
            var template = new TemplateEntry { FilePath = "Templates/Missing/missing.docx" };
            
            var vm = new TemplateEditorViewModel(firmName, template, _tagCatalogService, _templateService);
            
            // Act
            await vm.OnWebViewReady();

            // Assert
            Assert.Contains("File not found", vm.EditorStatus);
        }

        private string SetupTemplateFile(string firmName, string fileName, string content)
        {
            var folder = Path.Combine(_testRootPath, firmName, "Templates", "Test_Template");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            var ext = Path.GetExtension(fileName).ToLower();
            if (ext == ".docx" || ext == ".xlsx")
            {
                using var stream = File.Create(path);
                using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, false);
                var entry = archive.CreateEntry("dummy.txt");
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(content);
            }
            else if (ext == ".pdf")
            {
                File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4"));
            }
            else
            {
                File.WriteAllText(path, content);
            }
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_testRootPath, true); } catch { }
        }
    }
}
