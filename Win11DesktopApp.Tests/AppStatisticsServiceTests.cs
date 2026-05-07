using System;
using System.IO;
using Microsoft.Data.Sqlite;
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
        public void RecordDocumentGenerated_ShouldStoreStatsForMultipleMachinesInSharedSqliteFile()
        {
            var pc1 = new AppStatisticsService(_folderService, "PC-1", "UserA");
            var pc2 = new AppStatisticsService(_folderService, "PC-2", "UserA");

            pc1.RecordDocumentGenerated();
            pc2.RecordDocumentGenerated();
            pc2.RecordDocumentGenerated();

            Assert.True(File.Exists(pc1.StatisticsDbPath));
            Assert.True(File.Exists(pc2.StatisticsDbPath));
            Assert.Equal(pc1.StatisticsDbPath, pc2.StatisticsDbPath);
            Assert.Equal("app_statistics.db", Path.GetFileName(pc1.StatisticsDbPath));
            Assert.Equal(1, pc1.GetSnapshot().GeneratedDocumentsCount);
            Assert.Equal(2, pc2.GetSnapshot().GeneratedDocumentsCount);
        }

        [Fact]
        public void GetSnapshot_ShouldMigrateLegacyJsonToSharedDbAndDeleteJson()
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

        [Fact]
        public void GetSnapshot_ShouldMigrateMachineSpecificDbToSharedDbAndDeleteOldDb()
        {
            var statisticsFolder = Path.Combine(_testRootPath, "SQLite", "Statistics");
            Directory.CreateDirectory(statisticsFolder);
            var oldDbPath = Path.Combine(statisticsFolder, "app_statistics_Office_PC_AgroP.db");
            CreateOldStatisticsDb(oldDbPath, "Office PC|AgroP", "Office PC", "AgroP", 5, 9, 77);

            var service = new AppStatisticsService(_folderService, "Office PC", "AgroP");

            var snapshot = service.GetSnapshot();

            Assert.Equal(5, snapshot.TotalEmployeesCreated);
            Assert.Equal(9, snapshot.GeneratedDocumentsCount);
            Assert.Equal(77, snapshot.TotalProgramRunMinutes);
            Assert.True(File.Exists(service.StatisticsDbPath));
            Assert.Equal("app_statistics.db", Path.GetFileName(service.StatisticsDbPath));
            Assert.False(File.Exists(oldDbPath));
        }

        private static void CreateOldStatisticsDb(
            string path,
            string machineKey,
            string machineName,
            string userName,
            int employeesCreated,
            int documentsGenerated,
            int runMinutes)
        {
            using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE app_statistics (
    machine_key TEXT PRIMARY KEY,
    machine_name TEXT NOT NULL,
    user_name TEXT NOT NULL,
    total_employees_created INTEGER NOT NULL DEFAULT 0,
    generated_documents_count INTEGER NOT NULL DEFAULT 0,
    total_program_run_minutes INTEGER NOT NULL DEFAULT 0,
    updated_at_utc TEXT NOT NULL
);

INSERT INTO app_statistics (
    machine_key, machine_name, user_name,
    total_employees_created, generated_documents_count, total_program_run_minutes, updated_at_utc
) VALUES (
    $machine_key, $machine_name, $user_name,
    $total_employees_created, $generated_documents_count, $total_program_run_minutes, $updated_at_utc
);";
            command.Parameters.AddWithValue("$machine_key", machineKey);
            command.Parameters.AddWithValue("$machine_name", machineName);
            command.Parameters.AddWithValue("$user_name", userName);
            command.Parameters.AddWithValue("$total_employees_created", employeesCreated);
            command.Parameters.AddWithValue("$generated_documents_count", documentsGenerated);
            command.Parameters.AddWithValue("$total_program_run_minutes", runMinutes);
            command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
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
