using System;
using System.Collections.Generic;
using System.IO;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class FinanceSalaryHistoryServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly CompanyService _companyService;
        private readonly FolderService _folderService;

        public FinanceSalaryHistoryServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorSalaryHistoryTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            var appSettings = new AppSettingsService();
            appSettings.Settings.RootFolderPath = _testRootPath;
            var localization = new DocumentLocalizationService();
            var tags = new TagCatalogService(localization);
            _folderService = new FolderService(appSettings);
            var persistence = new PersistenceService(appSettings, _folderService);
            _companyService = new CompanyService(tags, appSettings, persistence, _folderService);
        }

        [Fact]
        public void TrySaveSalaryHistoryRecord_WhenFolderIsMissing_ShouldReturnFalse()
        {
            var service = CreateFileService();
            var missingFolder = Path.Combine(_testRootPath, "missing-employee");

            var saved = service.TrySaveSalaryHistoryRecord(missingFolder, CreateRecord());

            Assert.False(saved);
        }

        [Fact]
        public void TrySaveThenRemoveSalaryHistoryRecord_ShouldRoundTripOnDisk()
        {
            var service = CreateFileService();
            var folder = Path.Combine(_testRootPath, "employee-a");
            Directory.CreateDirectory(folder);
            var record = CreateRecord();

            Assert.True(service.TrySaveSalaryHistoryRecord(folder, record));
            Assert.Single(service.LoadSalaryHistory(folder));

            Assert.True(service.TryRemoveSalaryHistoryRecord(folder, record.Year, record.Month, record.FirmName));
            Assert.Empty(service.LoadSalaryHistory(folder));
        }

        [Fact]
        public void TrySaveSalaryHistoryRecord_WhenStorageThrows_ShouldReturnFalse()
        {
            var service = CreateService(new ThrowingSalaryHistoryStorage());
            var folder = Path.Combine(_testRootPath, "employee-b");
            Directory.CreateDirectory(folder);

            var saved = service.TrySaveSalaryHistoryRecord(folder, CreateRecord());

            Assert.False(saved);
        }

        [Fact]
        public void TryRemoveSalaryHistoryRecord_WhenStorageThrows_ShouldReturnFalse()
        {
            var service = CreateService(new ThrowingSalaryHistoryStorage());
            var folder = Path.Combine(_testRootPath, "employee-c");
            Directory.CreateDirectory(folder);

            var removed = service.TryRemoveSalaryHistoryRecord(folder, 2026, 8, "Firm A");

            Assert.False(removed);
        }

        private FinanceSalaryHistoryService CreateFileService()
            => CreateService(null);

        private FinanceSalaryHistoryService CreateService(IFinanceSalaryHistoryStorage? storage)
        {
            return new FinanceSalaryHistoryService(
                _folderService,
                storage,
                _companyService,
                _ => "emp-test",
                (folder, _) => folder);
        }

        private static SalaryHistoryRecord CreateRecord()
        {
            return new SalaryHistoryRecord
            {
                Year = 2026,
                Month = 8,
                FirmName = "Firm A",
                FullName = "Employee A",
                HoursWorked = 10m,
                HourlyRate = 100m,
                GrossSalary = 1000m,
                NetSalary = 1000m
            };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRootPath))
                    Directory.Delete(_testRootPath, true);
            }
            catch (IOException)
            {
            }
        }

        private sealed class ThrowingSalaryHistoryStorage : IFinanceSalaryHistoryStorage
        {
            public void UpsertSalaryHistoryRecord(string employeeId, string employeeFolder, SalaryHistoryRecord record)
                => throw new IOException("disk full");

            public void DeleteSalaryHistoryRecord(string employeeId, string employeeFolder, int year, int month, string firmName)
                => throw new IOException("disk full");

            public int DeleteSalaryHistoryForEmployee(string? employeeId, string? originalFolder, string? deletedFolder)
                => throw new IOException("disk full");

            public int RemapEmployeeFolder(string? employeeId, string? fromFolderA, string? fromFolderB, string toFolder)
                => throw new IOException("disk full");

            public List<SalaryHistoryRecord> GetSalaryHistory(string employeeId, string employeeFolder)
                => throw new IOException("disk full");

            public int RemoveDuplicateSalaryHistoryRecords()
                => throw new IOException("disk full");
        }
    }
}
