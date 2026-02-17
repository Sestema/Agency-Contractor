using System;
using System.Collections.Generic;
using System.IO;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class PersistenceServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly PersistenceService _persistenceService;

        public PersistenceServiceTests()
        {
            // Setup temporary test directory
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            // Mock AppSettingsService
            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _appSettingsService.Settings.LanguageCode = "en"; // Use English folder names in tests

            var folderService = new FolderService(_appSettingsService);
            _persistenceService = new PersistenceService(_appSettingsService, folderService);
        }

        [Fact]
        public void SaveCompanies_ShouldCreateFile()
        {
            var companies = new List<EmployerCompany>
            {
                new EmployerCompany { Name = "Test Company 1" }
            };

            _persistenceService.SaveCompanies(companies);

            var filePath = Path.Combine(_testRootPath, "database.json");
            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public void LoadCompanies_ShouldReturnSavedData()
        {
            var companies = new List<EmployerCompany>
            {
                new EmployerCompany { Name = "Test Company 1", ICO = "12345678" }
            };

            _persistenceService.SaveCompanies(companies);
            var loaded = _persistenceService.LoadCompanies();

            Assert.Single(loaded);
            Assert.Equal("Test Company 1", loaded[0].Name);
            Assert.Equal("12345678", loaded[0].ICO);
        }

        [Fact]
        public void SaveCompanies_ShouldCreateBackup_WhenFileExists()
        {
            var companies1 = new List<EmployerCompany> { new EmployerCompany { Name = "V1" } };
            _persistenceService.SaveCompanies(companies1);

            // Wait a bit or ensure backup logic works
            var companies2 = new List<EmployerCompany> { new EmployerCompany { Name = "V2" } };
            _persistenceService.SaveCompanies(companies2);

            var backupDir = Path.Combine(_testRootPath, "backups");
            Assert.True(Directory.Exists(backupDir));
            Assert.NotEmpty(Directory.GetFiles(backupDir, "*.bak"));
        }

        [Fact]
        public void LoadCompanies_ShouldVerifyIntegrity()
        {
            var companies = new List<EmployerCompany> { new EmployerCompany { Name = "Integrity Test" } };
            _persistenceService.SaveCompanies(companies);

            var filePath = Path.Combine(_testRootPath, "database.json");
            var content = File.ReadAllBytes(filePath);
            
            // Corrupt the data (flip last byte)
            content[content.Length - 1] = (byte)(content[content.Length - 1] ^ 0xFF);
            File.WriteAllBytes(filePath, content);

            // Should throw or return empty list/log error depending on implementation
            // Current implementation catches exception and returns empty list
            var loaded = _persistenceService.LoadCompanies();
            Assert.Empty(loaded);
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
