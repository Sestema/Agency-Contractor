using System;
using System.IO;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public sealed class AppStatisticsServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;

        public AppStatisticsServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorStatsTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _appSettingsService.Settings.LanguageCode = "en";
            _folderService = new FolderService(_appSettingsService);
        }

        [Fact]
        public void RecordDocumentGenerated_ShouldStoreStatsInMachineSpecificSqliteFile()
        {
            var pc1 = new AppStatisticsService(_folderService, "PC-1", "UserA");
            var pc2 = new AppStatisticsService(_folderService, "PC-2", "UserA");

            pc1.RecordDocumentGenerated();
            pc2.RecordDocumentGenerated();
            pc2.RecordDocumentGenerated();

            Assert.True(File.Exists(pc1.StatisticsDbPath));
            Assert.True(File.Exists(pc2.StatisticsDbPath));
            Assert.NotEqual(pc1.StatisticsDbPath, pc2.StatisticsDbPath);
            Assert.Equal(1, pc1.GetSnapshot().GeneratedDocumentsCount);
            Assert.Equal(2, pc2.GetSnapshot().GeneratedDocumentsCount);
        }

        [Fact]
        public void GetSnapshot_ShouldMigrateLegacyJsonToCurrentMachineDbAndDeleteJson()
        {
            var legacyPath = Path.Combine(_testRootPath, "app_statistics.json");
            SafeFileService.WriteJsonAtomic(legacyPath, new AppStatisticsSnapshot
            {
                TotalEmployeesCreated = 3,
                GeneratedDocumentsCount = 7,
                TotalProgramRunMinutes = 42
            });

            var service = new AppStatisticsService(_folderService, "Office PC", "AgroP");

            var snapshot = service.GetSnapshot();

            Assert.Equal(3, snapshot.TotalEmployeesCreated);
            Assert.Equal(7, snapshot.GeneratedDocumentsCount);
            Assert.Equal(42, snapshot.TotalProgramRunMinutes);
            Assert.True(File.Exists(service.StatisticsDbPath));
            Assert.False(File.Exists(legacyPath));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRootPath))
                    Directory.Delete(_testRootPath, true);
            }
            catch
            {
            }
        }
    }
}
