using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class TemplateServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly TemplateService _templateService;

        public TemplateServiceTests()
        {
            // Setup temporary test directory
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorTemplatesTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            // Mock AppSettingsService
            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _appSettingsService.Settings.LanguageCode = "en";
            _appSettingsService.Settings.DocumentLanguage = "en";

            var folderService = new FolderService(_appSettingsService);
            _templateService = new TemplateService(_appSettingsService, folderService);
        }

        [Fact]
        public void AddTemplate_ShouldCreateStructureAndFiles()
        {
            var firmName = "TestFirm";
            var templateName = "Test Template";
            var description = "Test Description";
            var format = "DOCX";
            
            // Create a dummy source file
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy content");

            _templateService.AddTemplate(firmName, templateName, description, format, sourceFile);

            // Verify Firm Folder
            var firmFolder = Path.Combine(_testRootPath, "TestFirm"); // "TestFirm" sanitized is "TestFirm"
            Assert.True(Directory.Exists(firmFolder));

            // Verify Templates Root
            var templatesRoot = Path.Combine(firmFolder, "Templates");
            Assert.True(Directory.Exists(templatesRoot));

            // Verify Template Folder
            var templateFolder = Path.Combine(templatesRoot, "Test_Template"); // "Test Template" -> "Test_Template"
            Assert.True(Directory.Exists(templateFolder));

            // Verify Files
            Assert.True(File.Exists(Path.Combine(templateFolder, "template.docx")));
            Assert.True(File.Exists(Path.Combine(templateFolder, "metadata.json")));
            Assert.False(Directory.Exists(Path.Combine(templateFolder, "versions")));
            Assert.False(Directory.Exists(Path.Combine(templateFolder, "preview")));

            // Verify Index
            var indexFile = Path.Combine(templatesRoot, "index.json");
            Assert.True(File.Exists(indexFile));
            var indexContent = File.ReadAllText(indexFile);
            Assert.Contains("Test Template", indexContent);
        }

        [Fact]
        public void AddTemplateWithoutFile_CreatesNativeDocxWithoutLegacyFiles()
        {
            var created = _templateService.AddTemplateWithoutFile("TestFirm", "Blank Contract", "Desc", "DOCX");

            Assert.NotNull(created);
            Assert.EndsWith("template.docx", created!.FilePath);

            var templateFolder = Path.Combine(_testRootPath, "TestFirm", "Templates", "Blank_Contract");
            var docxPath = Path.Combine(templateFolder, "template.docx");
            Assert.True(DocumentGenerationService.CanOpenAsTemplateDocx(docxPath));
            Assert.False(File.Exists(Path.Combine(templateFolder, "content.rtf")));
            Assert.False(File.Exists(Path.Combine(templateFolder, "content.xamlpackage")));
            Assert.False(File.Exists(Path.Combine(templateFolder, "editor-layout.json")));
            Assert.False(Directory.Exists(Path.Combine(templateFolder, "versions")));
            Assert.False(Directory.Exists(Path.Combine(templateFolder, "preview")));

            var metadata = SafeFileService.ReadJson<TemplateMetadata>(Path.Combine(templateFolder, "metadata.json"));
            Assert.Equal(TemplateEditorFormat.Docx, metadata?.EditorFormat);

            var index = File.ReadAllText(Path.Combine(_testRootPath, "TestFirm", "Templates", "index.json"));
            Assert.Contains("Blank Contract", index);
            Assert.Contains("template.docx", index);
        }

        [Fact]
        public void GetTemplates_ShouldReturnAddedTemplates()
        {
            var firmName = "TestFirm";
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy");

            _templateService.AddTemplate(firmName, "Template 1", "Desc 1", "DOCX", sourceFile);
            _templateService.AddTemplate(firmName, "Template 2", "Desc 2", "DOCX", sourceFile);

            var templates = _templateService.GetTemplates(firmName);

            Assert.Equal(2, templates.Count);
            Assert.Contains(templates, t => t.Name == "Template 1");
            Assert.Contains(templates, t => t.Name == "Template 2");
        }

        [Fact]
        public async Task DeleteTemplate_ShouldRemoveFilesAndIndexEntry()
        {
            var firmName = "TestFirm";
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy");

            _templateService.AddTemplate(firmName, "Template To Delete", "Desc", "DOCX", sourceFile);
            
            var templates = _templateService.GetTemplates(firmName);
            var templateToDelete = templates[0];

            // Act
            await _templateService.DeleteTemplateAsync(firmName, templateToDelete);

            // Assert
            var remainingTemplates = _templateService.GetTemplates(firmName);
            Assert.Empty(remainingTemplates);

            var firmFolder = Path.Combine(_testRootPath, "TestFirm");
            var templatesRoot = Path.Combine(firmFolder, "Templates");
            var templateFolder = Path.Combine(templatesRoot, "Template_To_Delete");

            Assert.False(Directory.Exists(templateFolder));
        }

        [Fact]
        public async Task DeleteTemplate_ShouldRemoveReadOnlyFilesAndFolder()
        {
            var firmName = "TestFirm";
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy");

            _templateService.AddTemplate(firmName, "ReadOnly Template", "Desc", "DOCX", sourceFile);

            var template = _templateService.GetTemplates(firmName).Single();
            var templateFolder = Path.Combine(_testRootPath, "TestFirm", "Templates", "ReadOnly_Template");
            var templateFile = Path.Combine(templateFolder, "template.docx");

            File.SetAttributes(templateFile, FileAttributes.ReadOnly);

            await _templateService.DeleteTemplateAsync(firmName, template);

            Assert.False(Directory.Exists(templateFolder));
            Assert.Empty(_templateService.GetTemplates(firmName));
        }

        [Fact]
        public void CopyTemplateToCompany_ShouldCreateCopiedTemplateInTargetCompany()
        {
            var sourceFirm = "SourceFirm";
            var targetFirm = "TargetFirm";
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy");

            _templateService.AddTemplate(sourceFirm, "Original Template", "Desc", "DOCX", sourceFile);
            var sourceTemplate = _templateService.GetTemplates(sourceFirm).Single();

            var copiedTemplate = _templateService.CopyTemplateToCompany(sourceFirm, sourceTemplate, targetFirm, "Copied Template");

            var targetTemplates = _templateService.GetTemplates(targetFirm);

            Assert.Single(targetTemplates);
            Assert.Equal("Copied Template", targetTemplates[0].Name);
            Assert.Equal("Copied Template", copiedTemplate.Name);

            var copiedFullPath = Path.Combine(_testRootPath, FolderService.NormalizeFolderName(targetFirm), "Templates", "Copied_Template", "template.docx");
            Assert.True(File.Exists(copiedFullPath));

            var targetIndexFile = Path.Combine(_testRootPath, FolderService.NormalizeFolderName(targetFirm), "Templates", "index.json");
            Assert.True(File.Exists(targetIndexFile));
            Assert.Contains("Copied Template", File.ReadAllText(targetIndexFile));
        }

        [Fact]
        public void RenameTemplate_MovesFolderAndUpdatesIndexPath()
        {
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy");
            _templateService.AddTemplate("TestFirm", "Old Contract", "Desc", "DOCX", sourceFile);

            var template = _templateService.GetTemplates("TestFirm").Single();
            var result = _templateService.RenameTemplate("TestFirm", template, "New Contract");

            Assert.Equal(TemplateRenameResult.Renamed, result);
            var templatesRoot = Path.Combine(_testRootPath, "TestFirm", "Templates");
            Assert.False(Directory.Exists(Path.Combine(templatesRoot, "Old_Contract")));
            Assert.True(File.Exists(Path.Combine(templatesRoot, "New_Contract", "template.docx")));

            var renamed = _templateService.GetTemplates("TestFirm").Single();
            Assert.Equal("New Contract", renamed.Name);
            Assert.Contains("New_Contract", renamed.FilePath);

            var metadata = SafeFileService.ReadJson<TemplateMetadata>(Path.Combine(templatesRoot, "New_Contract", "metadata.json"));
            Assert.Equal("New Contract", metadata?.Name);
        }

        [Fact]
        public void RenameTemplate_RejectsExistingNameWithoutReplacingFolder()
        {
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy");
            _templateService.AddTemplate("TestFirm", "Alpha", "Desc", "DOCX", sourceFile);
            _templateService.AddTemplate("TestFirm", "Beta", "Desc", "DOCX", sourceFile);

            var alpha = _templateService.GetTemplates("TestFirm").Single(t => t.Name == "Alpha");
            File.WriteAllText(Path.Combine(_testRootPath, "TestFirm", "Templates", "Alpha", "template.docx"), "alpha-bytes");

            var result = _templateService.RenameTemplate("TestFirm", alpha, "Beta");

            Assert.Equal(TemplateRenameResult.NameTaken, result);
            Assert.True(File.Exists(Path.Combine(_testRootPath, "TestFirm", "Templates", "Alpha", "template.docx")));
            Assert.Equal("alpha-bytes", File.ReadAllText(Path.Combine(_testRootPath, "TestFirm", "Templates", "Alpha", "template.docx")));
            Assert.Equal("Alpha", _templateService.GetTemplates("TestFirm").Single(t => t.FilePath.Contains("Alpha")).Name);
        }

        [Fact]
        public void RenameTemplate_SameFolderUpdatesLabelOnly()
        {
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy");
            _templateService.AddTemplate("TestFirm", "Test Template", "Desc", "DOCX", sourceFile);

            var template = _templateService.GetTemplates("TestFirm").Single();
            var result = _templateService.RenameTemplate("TestFirm", template, "Test_Template");

            Assert.Equal(TemplateRenameResult.Renamed, result);
            var folder = Path.Combine(_testRootPath, "TestFirm", "Templates", "Test_Template");
            Assert.True(Directory.Exists(folder));
            Assert.Equal("Test_Template", SafeFileService.ReadJson<TemplateMetadata>(Path.Combine(folder, "metadata.json"))?.Name);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRootPath))
                    Directory.Delete(_testRootPath, true);
            }
            catch { }
        }
    }
}
