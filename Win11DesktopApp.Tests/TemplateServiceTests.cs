using System;
using System.Collections.Generic;
using System.IO;
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
            Assert.True(Directory.Exists(Path.Combine(templateFolder, "versions")));
            Assert.True(Directory.Exists(Path.Combine(templateFolder, "preview")));

            // Verify Index
            var indexFile = Path.Combine(templatesRoot, "index.json");
            Assert.True(File.Exists(indexFile));
            var indexContent = File.ReadAllText(indexFile);
            Assert.Contains("Test Template", indexContent);
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
        public void DeleteTemplate_ShouldRemoveFilesAndIndexEntry()
        {
            var firmName = "TestFirm";
            var sourceFile = Path.Combine(_testRootPath, "source.docx");
            File.WriteAllText(sourceFile, "dummy");

            _templateService.AddTemplate(firmName, "Template To Delete", "Desc", "DOCX", sourceFile);
            
            var templates = _templateService.GetTemplates(firmName);
            var templateToDelete = templates[0];

            // Act
            _templateService.DeleteTemplate(firmName, templateToDelete);

            // Assert
            var remainingTemplates = _templateService.GetTemplates(firmName);
            Assert.Empty(remainingTemplates);

            var firmFolder = Path.Combine(_testRootPath, "TestFirm");
            var templatesRoot = Path.Combine(firmFolder, "Templates");
            var templateFolder = Path.Combine(templatesRoot, "Template_To_Delete");

            Assert.False(Directory.Exists(templateFolder));
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
